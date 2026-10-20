using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace ServiceTool
{
    // A private job tracks and cancels only this installer tree. Start suspended so fast
    // bootstrapper children cannot escape between Process.Start and AssignProcessToJobObject.
    internal static class LibraryProcess
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Startup
        {
            public int Size; public string Reserved, Desktop, Title;
            public int X, Y, Width, Height, Columns, Rows, Fill, Flags;
            public short Show, Reserved2; public IntPtr ReservedPointer, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint Pid, Tid; }
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimit { public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWorking, MaxWorking; public uint Active; public UIntPtr Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong Read, Write, Other, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimit { public BasicLimit Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        [StructLayout(LayoutKind.Sequential)] private struct JobAccounting
        { public long User, Kernel, ThisUser, ThisKernel; public uint Faults, Total, Active, Terminated; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr security, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(IntPtr job, int information, out JobAccounting accounting, int size, IntPtr length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string name, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref Startup startup, out ProcessInfo process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int info, ref ExtendedLimit limit, int length);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(IntPtr job, uint code);
        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "QueryInformationJobObject")] private static extern bool QueryJobPids(IntPtr job, int info, IntPtr data, int length, IntPtr returned);
        private delegate bool WindowVisitor(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowVisitor visitor, IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
        private static void AskToClose(IntPtr job)
        {
            var pids = new HashSet<uint>();
            for (int length = 4096; length <= 1048576; length *= 2)
            {
                IntPtr buffer = Marshal.AllocHGlobal(length);
                try
                {
                    if (!QueryJobPids(job, 3, buffer, length, IntPtr.Zero)) { if (Marshal.GetLastWin32Error() == 234) continue; return; }
                    int count = Marshal.ReadInt32(buffer, 4);
                    for (int i = 0; i < count && 8 + (i + 1) * IntPtr.Size <= length; i++) pids.Add(unchecked((uint)Marshal.ReadIntPtr(buffer, 8 + i * IntPtr.Size).ToInt64()));
                    break;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            EnumWindows(delegate(IntPtr window, IntPtr ignored) { uint pid; GetWindowThreadProcessId(window, out pid); if (pids.Contains(pid)) PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); return true; }, IntPtr.Zero);
        }
        public static int Run(ProcessStartInfo start, Action<string> heartbeat, Func<bool> cancelled = null)
        {
            if (cancelled == null) cancelled = () => LibraryCancellation.Requested;
            if (cancelled()) throw new OperationCanceledException("Операция отменена до запуска установщика");
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            ProcessInfo process = new ProcessInfo(); bool resumed = false;
            try
            {
                var limit = new ExtendedLimit { Basic = new BasicLimit { Flags = 0x2000 } };
                if (!SetInformationJobObject(job, 9, ref limit, Marshal.SizeOf(typeof(ExtendedLimit)))) throw new Win32Exception(Marshal.GetLastWin32Error());
                Startup startup = new Startup { Size = Marshal.SizeOf(typeof(Startup)) };
                var command = new StringBuilder(LibraryPlan.Quote(start.FileName) + " " + start.Arguments);
                if (!CreateProcess(start.FileName, command, IntPtr.Zero, IntPtr.Zero, false, 0x00000004 | 0x08000000,
                    IntPtr.Zero, String.IsNullOrWhiteSpace(start.WorkingDirectory) ? null : start.WorkingDirectory, ref startup, out process))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "Не удалось запустить " + start.FileName + "; Win32 " + error + ": " + new Win32Exception(error).Message + "; рабочая папка: " + (String.IsNullOrWhiteSpace(start.WorkingDirectory) ? "наследуется от менеджера" : start.WorkingDirectory));
                }
                InstallerRebootGuard.Apply(process.Process);
                if (!AssignProcessToJobObject(job, process.Process)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось организовать контроль дочерних процессов. Установка не запускалась.");
                if (ResumeThread(process.Thread) == UInt32.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
                resumed = true;
                Stopwatch elapsed = Stopwatch.StartNew(); long last = -1, cancelAt = -1; bool forced = false;
                while (true)
                {
                    JobAccounting accounting;
                    if (!QueryInformationJobObject(job, 1, out accounting, Marshal.SizeOf(typeof(JobAccounting)), IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (accounting.Active == 0)
                    {
                        if (cancelAt >= 0) throw new OperationCanceledException("Операция отменена; процессы этой очереди завершены. Проверь состояние компонента: полный откат не гарантирован.");
                        break;
                    }
                    if (cancelAt < 0 && cancelled()) { cancelAt = elapsed.ElapsedMilliseconds; heartbeat("Отмена: запрашиваем закрытие окон установщика"); AskToClose(job); }
                    if (cancelAt >= 0 && elapsed.ElapsedMilliseconds - cancelAt >= 2000 && !forced)
                    {
                        if (!TerminateJobObject(job, 1602)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось завершить процессы текущей очереди");
                        forced = true; heartbeat("Отмена: завершаем оставшиеся процессы текущей очереди");
                    }
                    if (forced && elapsed.ElapsedMilliseconds - cancelAt > 8000) throw new TimeoutException("Windows ещё завершает процессы отменённой операции. Проверь состояние перед повтором.");
                    long second = elapsed.ElapsedMilliseconds / 1000;
                    if (last != second)
                    {
                        last = second;
                        heartbeat((second >= 1200 ? "Дольше ожидаемого; очередь не продолжится до завершения. " : "Операция выполняется. ") +
                            "Прошло " + elapsed.Elapsed.ToString(@"hh\:mm\:ss") + "; процессов: " + accounting.Active);
                    }
                    Thread.Sleep(200);
                }
                uint code;
                if (!GetExitCodeProcess(process.Process, out code)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return unchecked((int)code);
            }
            finally
            {
                // Closing this private kill-on-close job also cleans up its own tree after exceptions.
                // Shared Windows Installer services and unrelated processes are never targeted.
                if (!resumed && process.Process != IntPtr.Zero) TerminateProcess(process.Process, 87);
                if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
                if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
                CloseHandle(job);
            }
        }
    }
    internal sealed class LibraryInstallResult
    {
        public int ExitCode;
        public string Message, LogPath;
        public LibraryState State;
        public bool Reboot { get { return LibraryPlan.NeedsReboot(ExitCode); } }
    }
    internal sealed class LibrarySession : IDisposable
    {
        private readonly NamedPipeServerStream pipe;
        private readonly EventWaitHandle cancelSignal;
        private Process worker;
        private readonly string name;
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle handle, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle handle, out uint pid);
        private const int IoTimeout = 180000;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private LibrarySession(string pipeName)
        {
            name = pipeName; cancelSignal = LibraryCancellation.Create(pipeName);
            pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 32768, 32768, ServiceProfileInstaller.CreatePipeSecurity());
        }
        internal static bool Parse(string[] args, out string pipeName, out int parent)
        {
            pipeName = null; parent = 0; Guid guid;
            if (args == null || args.Length != 3 || args[0] != "--library-worker" || args[1] == null || !args[1].StartsWith("BARS.Library.", StringComparison.Ordinal) ||
                !Guid.TryParseExact(args[1].Substring(13), "N", out guid) || !Int32.TryParse(args[2], out parent) || parent <= 0) return false;
            pipeName = args[1]; return true;
        }
        public static LibrarySession Start(Func<bool> cancelled = null)
        {
            if (cancelled != null && cancelled()) throw new OperationCanceledException("Очередь отменена до запроса прав администратора");
            var session = new LibrarySession("BARS.Library." + Guid.NewGuid().ToString("N"));
            try
            {
                IAsyncResult wait = session.pipe.BeginWaitForConnection(null, null);
                var start = new ProcessStartInfo { FileName = System.Windows.Forms.Application.ExecutablePath,
                    Arguments = "--library-worker " + session.name + " " + Process.GetCurrentProcess().Id,
                    UseShellExecute = true, Verb = "runas", WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory };
                if (cancelled != null && cancelled()) throw new OperationCanceledException("Очередь отменена до запроса прав администратора");
                session.worker = Process.Start(start);
                var connectTime = Stopwatch.StartNew();
                while (!wait.AsyncWaitHandle.WaitOne(100))
                {
                    if (cancelled != null && cancelled()) { session.Cancel(); throw new OperationCanceledException("Подготовка очереди отменена; пакеты не запускались"); }
                    if (connectTime.ElapsedMilliseconds >= 45000) throw new TimeoutException("Установщик не подключился за 45 секунд; ни один пакет не запускался.");
                }
                session.pipe.EndWaitForConnection(wait); wait.AsyncWaitHandle.Close();
                uint client;
                if (session.worker == null || !GetNamedPipeClientProcessId(session.pipe.SafePipeHandle, out client) || client != session.worker.Id)
                    throw new SecurityException("Не удалось проверить процесс установки библиотек.");
                return session;
            }
            catch { session.Dispose(); throw; }
        }
        private static byte[] Message(params string[] values)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Utf8, true)) { foreach (string value in values) { if (value == null || value.Length > 12000) throw new InvalidDataException("Некорректное сообщение."); writer.Write(value); } }
                return stream.ToArray();
            }
        }
        private static string[] ReadMessage(PipeStream stream, int count)
        {
            byte[] frame = ServiceProfileInstaller.ReadFrame(stream, IoTimeout);
            using (var memory = new MemoryStream(frame))
            using (var reader = new BinaryReader(memory, Utf8))
            {
                var fields = new string[count];
                for (int i = 0; i < count; i++) { fields[i] = reader.ReadString(); if (fields[i].Length > 12000) throw new InvalidDataException("Слишком длинное поле."); }
                if (memory.Position != memory.Length) throw new InvalidDataException("Лишние данные в ответе.");
                return fields;
            }
        }
        private static void Send(PipeStream stream, params string[] values) { ServiceProfileInstaller.WriteFrame(stream, Message(values), IoTimeout); }
        public LibraryInstallResult Install(LibraryPackage package, string root, Action<string> progress)
        {
            return Execute(package, root, LibraryOperation.Install, progress);
        }
        public LibraryInstallResult Execute(LibraryPackage package, string root, LibraryOperation operation, Action<string> progress)
        {
            if (!Enum.IsDefined(typeof(LibraryOperation), operation)) throw new ArgumentException("Неизвестная операция");
            if (cancelSignal.WaitOne(0)) throw new OperationCanceledException("Очередь отменена до запуска следующего компонента");
            Send(pipe, operation.ToString().ToLowerInvariant(), LibraryCatalog.Serialize(package), root ?? "");
            return Receive(progress);
        }
        internal LibraryInstallResult ExecuteSystem(string action, string data, string path, Action<string> progress)
        {
            if (!ManagerSystemActions.Known(action)) throw new InvalidDataException("Неизвестное действие");
            if (cancelSignal.WaitOne(0)) throw new OperationCanceledException();
            Send(pipe, action, data ?? "", path ?? "");
            return Receive(progress);
        }
        private LibraryInstallResult Receive(Action<string> progress)
        {
            while (true)
            {
                string[] response = ReadMessage(pipe, 5);
                if (response[0] == "progress") { progress(response[2]); continue; }
                int code, state;
                if (response[0] != "result" || !Int32.TryParse(response[1], out code) || !Int32.TryParse(response[3], out state) || state < 0 || state > 3)
                    throw new InvalidDataException("Некорректный ответ установщика.");
                return new LibraryInstallResult { ExitCode = code, Message = response[2], State = (LibraryState)state, LogPath = response[4] };
            }
        }
        public void Cancel() { try { cancelSignal.Set(); } catch (ObjectDisposedException) { } }
        public void Dispose()
        {
            Cancel();
            try { if (pipe.IsConnected) ServiceProfileInstaller.WriteFrame(pipe, Message("close", "", ""), 2000); } catch (IOException) { } catch (ObjectDisposedException) { } catch (TimeoutException) { }
            pipe.Dispose(); cancelSignal.Dispose();
            if (worker != null) { worker.Dispose(); worker = null; }
        }
        public static int RunWorker(string[] args)
        {
            string pipeName; int parent;
            if (!Parse(args, out pipeName, out parent)) return 87;
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 5;
            try
            {
                using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
                {
                    pipe.Connect(45000); uint server;
                    if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out server) || server != parent) throw new SecurityException("Неверный процесс менеджера.");
                    using (var cancel = EventWaitHandle.OpenExisting(LibraryCancellation.EventName(pipeName), EventWaitHandleRights.Synchronize))
                    using (var owner = Process.GetProcessById(parent))
                    while (true)
                    {
                        string[] request = ReadMessage(pipe, 3);
                        if (request[0] == "close") return 0;
                        LibraryOperation operation = LibraryOperation.Install;
                        bool system = ManagerSystemActions.Known(request[0]);
                        if (!system && (!Enum.TryParse<LibraryOperation>(request[0], true, out operation) || !Enum.IsDefined(typeof(LibraryOperation), operation))) return 87;
                        LibraryPackage package = system ? new LibraryPackage { Id = "system", Name = request[0] } : LibraryCatalog.Deserialize(request[1]);
                        int connected = 1;
                        Action<string> progress = text => { if (Volatile.Read(ref connected) == 1) try { Send(pipe, "progress", "", text, "", ""); } catch (IOException) { Interlocked.Exchange(ref connected, 0); } catch (TimeoutException) { Interlocked.Exchange(ref connected, 0); } };
                        LibraryCancellation.Current = () => cancel.WaitOne(0) || owner.HasExited || Volatile.Read(ref connected) == 0;
                        LibraryInstallResult result;
                        using (var heartbeat = new LibraryHeartbeat(progress, 1000))
                        {
                            try { result = system ? ManagerSystemActions.Execute(request[0], request[1], request[2], heartbeat.SetPhase) : InstallVerified(package, request[2], pipeName.Substring(13), heartbeat.SetPhase, () => Volatile.Read(ref connected) == 1, operation); }
                            catch (Exception ex)
                            {
                                string failure = "";
                                try { string directory = LogDirectory(pipeName.Substring(13)); Directory.CreateDirectory(directory); failure = Path.Combine(directory, package.Id + "-error.txt"); File.WriteAllText(failure, DateTime.Now.ToString("O") + Environment.NewLine + request[0] + " " + package.Name + Environment.NewLine + ex.ToString(), Utf8); } catch { }
                                var native = ex as Win32Exception;
                                result = new LibraryInstallResult { ExitCode = ex is OperationCanceledException ? 1602 : native == null ? -1 : native.NativeErrorCode, State = LibraryState.Unknown, Message = ex.Message, LogPath = failure };
                            }
                        }
                        LibraryCancellation.Current = null;
                        if (Volatile.Read(ref connected) == 0) return 1;
                        Send(pipe, "result", result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture), result.Message,
                            ((int)result.State).ToString(System.Globalization.CultureInfo.InvariantCulture), result.LogPath ?? "");
                    }
                }
            }
            catch { return 1; }
        }
        internal static LibraryInstallResult InstallVerified(LibraryPackage package, string root, string session, Action<string> progress, Func<bool> connected, LibraryOperation operation = LibraryOperation.Install)
        {
            LibraryCancellation.Check();
            using (var gate = new Mutex(false, @"Global\BARS.Library.Installation"))
            {
                bool acquired;
                try { acquired = gate.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) return new LibraryInstallResult { ExitCode = 1618, State = LibraryState.Unknown, Message = "Другая очередь установки библиотек уже выполняется. Дождись её завершения.", LogPath = "" };
                try
                {
                    if (operation == LibraryOperation.Remove) return LibraryMaintenance.Remove(package, LogDirectory(session), progress, connected);
                    return InstallCore(package, root, session, progress, connected, operation);
                }
                finally { gate.ReleaseMutex(); }
            }
        }
        private static string LogDirectory(string session) { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BARS", "Manager", "LibraryLogs", session); }
        private static LibraryInstallResult InstallCore(LibraryPackage package, string root, string session, Action<string> progress, Func<bool> connected, LibraryOperation operation)
        {
            if (package.Kind == LibraryKind.Composite) throw new InvalidOperationException("RuntimePack Full исключён из установки");
            LibraryCancellation.Check();
            LibraryCheck before = LibraryDetection.Check(package);
            if (before.Relation == LibraryRelation.Newer && operation != LibraryOperation.Remove)
                return new LibraryInstallResult { ExitCode = 0, State = LibraryState.Installed, Message = LibraryVersions.Advice(before), LogPath = "" };
            if ((operation == LibraryOperation.Install || operation == LibraryOperation.Update) && before.State == LibraryState.Unknown)
                throw new InvalidOperationException("Перед установкой нужна проверка: " + before.Detail);
            if ((package.Kind == LibraryKind.VcBundle || package.Kind == LibraryKind.VcLegacy)) LibraryVcProfiles.ValidateNoDowngrade(package, LibraryMaintenance.Inventory());
            if (before.State == LibraryState.Installed && (operation == LibraryOperation.Install || operation == LibraryOperation.Update)) return new LibraryInstallResult { ExitCode = 0, State = before.State, Message = "Уже установлено; запуск пропущен", LogPath = "" };
            if (before.State == LibraryState.NotApplicable) throw new InvalidOperationException(before.Detail);
            string logDirectory = LogDirectory(session);
            string stage = Path.Combine(Path.GetTempPath(), "BARS.Package." + Guid.NewGuid().ToString("N"));
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            SecurityIdentifier user = WindowsIdentity.GetCurrent().User; security.SetOwner(user);
            foreach (SecurityIdentifier sid in new[] { user, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(stage, security); Directory.CreateDirectory(logDirectory);
            string verified = Path.Combine(stage, Path.GetFileName(package.RelativePath));
            string log = Path.Combine(logDirectory, package.Id + ".log"), summary = Path.Combine(logDirectory, package.Id + "-result.txt");
            try
            {
                progress("Проверяем SHA-256 и подготавливаем локальную копию " + package.Name);
                string source = LibrarySource.PackagePath(LibrarySource.NormalizeRoot(root), package);
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    LibrarySource.VerifyStream(input, package); input.Position = 0;
                    using (var output = new FileStream(verified, FileMode.CreateNew, FileAccess.Write, FileShare.None)) LibraryCancellation.Copy(input, output);
                }
                using (var locked = new FileStream(verified, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    LibrarySource.VerifyStream(locked, package);
                    LibraryCancellation.Check();
                    if (!connected()) throw new IOException("Менеджер закрыл соединение до запуска пакета; установка не начата.");
                    if (operation == LibraryOperation.Reinstall && LibraryVersions.CanRemove(before))
                    {
                        LibraryMaintenance.ValidateReinstall(package, LibraryMaintenance.Resolve(package));
                        LibraryInstallResult removed = LibraryMaintenance.Remove(package, logDirectory, progress, connected);
                        if (!LibraryPlan.Success(removed.ExitCode) || removed.Reboot || removed.State != LibraryState.Missing) return removed;
                        LibraryCancellation.Check();
                    if (!connected()) throw new IOException("Соединение закрыто после удаления. Повторную установку можно выполнить из менеджера.");
                    }
                    ProcessStartInfo start = LibraryPlan.Command(package, verified, log, operation == LibraryOperation.Update && package.Kind == LibraryKind.Msi && LibraryMaintenance.ExactMsiInstalled(package.ProductCode)); start.WorkingDirectory = stage;
                    int code = -1;
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        LibraryCancellation.Check();
                        code = LibraryProcess.Run(start, progress);
                        if (code != 1618) break;
                        progress("Windows Installer занят другой установкой; повтор через 15 секунд");
                        for (int second = 0; second < 15; second++) { LibraryCancellation.Check(); Thread.Sleep(1000); progress("Ждём освобождения Windows Installer: " + (15 - second) + " с"); }
                    }
                    LibraryCheck after = LibraryDetection.Check(package);
                    string message = !LibraryPlan.Success(code) ? "Установщик сообщил об ошибке, код " + code :
                        after.State == LibraryState.Installed ? "Установка подтверждена повторной проверкой" :
                        LibraryPlan.NeedsReboot(code) ? "Установщик запросил перезагрузку; проверка продолжится после неё" :
                        "Установщик завершился с кодом " + code + ", но наличие всего компонента не подтверждено";
                    File.WriteAllText(summary, DateTime.Now.ToString("O") + Environment.NewLine + package.Name + Environment.NewLine + message + Environment.NewLine + after.Detail, Utf8);
                    return new LibraryInstallResult { ExitCode = code, State = after.State, Message = message, LogPath = summary };
                }
            }
            finally
            {
                // Delete only this exact private package file and empty GUID directory; never recurse.
                try { if (File.Exists(verified)) File.Delete(verified); Directory.Delete(stage, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
