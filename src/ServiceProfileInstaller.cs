using System;
using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using TimeoutException = System.TimeoutException;

namespace ServiceTool
{
    internal sealed class ProfileInstallResult
    {
        public int ExitCode;
        public string Message;
    }

    internal static class ServiceProfileInstaller
    {
        private const int ProtocolVersion = 1;
        private const int MaxMessageBytes = 65536;
        private const int ConnectTimeout = 45000;
        private const int IoTimeout = 30000;
        private const int InstallTimeout = 10 * 60 * 1000;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);

        // Call on a background thread. The password and target path travel only inside the
        // ACL-restricted pipe, not command arguments, environment variables or a temp file.
        public static ProfileInstallResult Execute(string managerExe, ServiceInstallProfile profile, string password, bool uninstall)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            ServiceInstallProfile copy = profile.Clone(); copy.Validate();
            if (copy.Mode != ServiceInstallMode.ManagedDotNet) throw new ArgumentException("Выбери режим .NET с учётной записью.");
            if (!uninstall && copy.Account == InstallAccountKind.User && (password == null || password.Length == 0))
                throw new ArgumentException("Введи пароль учётной записи Windows в окне установки.");
            if (password != null && (password.Length > 4096 || password.IndexOf('\0') >= 0)) throw new ArgumentException("Недопустимый пароль.");
            string helper = ResolveHelperExecutable(managerExe, copy.ExecutablePath);
            string pipeName = "BARS.SM3.Install." + Guid.NewGuid().ToString("N");
            PipeSecurity security = CreatePipeSecurity();
            using (var helperLease = new EmbeddedHelpers.Lease(helper))
            using (NamedPipeServerStream pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, MaxMessageBytes, MaxMessageBytes, security))
            {
                ProcessStartInfo startInfo = BuildHelperStartInfo(helper, pipeName, Process.GetCurrentProcess().Id);
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null) throw new InvalidOperationException("Windows не вернула процесс установки.");
                    IAsyncResult connection = pipe.BeginWaitForConnection(null, null);
                    using (connection.AsyncWaitHandle)
                    {
                        if (!connection.AsyncWaitHandle.WaitOne(ConnectTimeout))
                            throw new TimeoutException("Не удалось подключиться к процессу установки за 45 секунд. Повтори установку.");
                        pipe.EndWaitForConnection(connection);
                    }
                    uint client;
                    if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out client) || client != (uint)process.Id)
                        throw new SecurityException("Подключился посторонний процесс; учётные данные не переданы.");
                    byte[] request = SerializeRequest(copy, uninstall ? null : password, uninstall);
                    try { WriteFrame(pipe, request, IoTimeout); }
                    finally { Array.Clear(request, 0, request.Length); }
                    byte[] response = ReadFrame(pipe, InstallTimeout);
                    try
                    {
                        using (BinaryReader reader = new BinaryReader(new MemoryStream(response, false), StrictUtf8))
                        {
                            if (reader.ReadInt32() != ProtocolVersion) throw new IOException("Неизвестный ответ установщика.");
                            int code = reader.ReadInt32();
                            string message = ReadText(reader, 12000);
                            if (reader.BaseStream.Position != reader.BaseStream.Length) throw new IOException("Некорректный ответ установщика.");
                            if (!process.WaitForExit(IoTimeout)) throw new TimeoutException("Процесс установки не завершился. Обнови статус службы.");
                            return new ProfileInstallResult { ExitCode = code, Message = message };
                        }
                    }
                    finally { Array.Clear(response, 0, response.Length); }
                }
            }
        }

        internal static PipeSecurity CreatePipeSecurity()
        {
            SecurityIdentifier user = WindowsIdentity.GetCurrent().User;
            if (user == null) throw new SecurityException("Не удалось определить текущую учётную запись Windows.");
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(user);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.FullControl, AccessControlType.Allow));
            return security;
        }

        internal static ProcessStartInfo BuildHelperStartInfo(string helperExe, string pipeName, int parentPid)
        {
            string executable = ServiceInstallProfile.NormalizeExecutable(helperExe);
            if (!ValidPipeName(pipeName) || parentPid <= 0) throw new ArgumentException("Некорректный канал установки.");
            return new ProcessStartInfo { FileName = executable,
                Arguments = "--install-profile-helper " + pipeName + " " + parentPid.ToString(CultureInfo.InvariantCulture),
                WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = true,
                Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        }

        internal static bool TryParseHelperArguments(string[] args, out string pipeName, out int parentPid)
        {
            pipeName = null; parentPid = 0;
            if (args == null || args.Length != 3 || args[0] != "--install-profile-helper" || !ValidPipeName(args[1]) ||
                !Int32.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out parentPid) || parentPid <= 0) return false;
            pipeName = args[1]; return true;
        }

        private static bool ValidPipeName(string value)
        {
            const string prefix = "BARS.SM3.Install.";
            if (value == null || !value.StartsWith(prefix, StringComparison.Ordinal) || value.Length != prefix.Length + 32) return false;
            for (int i = prefix.Length; i < value.Length; i++)
                if (!(value[i] >= '0' && value[i] <= '9') && !(value[i] >= 'a' && value[i] <= 'f')) return false;
            return true;
        }

        // Only dispatch after the exact helper command has been recognised by Program.Main.
        // Invalid arguments return before creating a pipe, loading assemblies or accessing SCM.
        public static int RunHelper(string[] args)
        {
            string pipeName; int parentPid;
            if (!TryParseHelperArguments(args, out pipeName, out parentPid)) return 87;
            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
                {
                    pipe.Connect(ConnectTimeout);
                    uint server;
                    if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out server) || server != (uint)parentPid)
                        throw new SecurityException("Не удалось проверить процесс менеджера служб.");
                    byte[] requestBytes = ReadFrame(pipe, IoTimeout);
                    InstallRequest request;
                    try { request = DeserializeRequest(requestBytes); }
                    finally { Array.Clear(requestBytes, 0, requestBytes.Length); }
                    ProfileInstallResult result;
                    try
                    {
                        InstallAssembly(request.Profile, request.Password, request.Uninstall);
                        result = new ProfileInstallResult { ExitCode = 0, Message = request.Uninstall ? "Регистрация удалена." : "Установка .NET завершена." };
                    }
                    catch (Exception ex)
                    {
                        result = new ProfileInstallResult { ExitCode = ErrorCode(ex), Message = SafeError(ex, request.Password) };
                    }
                    finally { request.Password = null; }
                    byte[] response;
                    using (MemoryStream memory = new MemoryStream())
                    {
                        using (BinaryWriter writer = new BinaryWriter(memory, StrictUtf8, true))
                        { writer.Write(ProtocolVersion); writer.Write(result.ExitCode); WriteText(writer, result.Message, 12000); }
                        response = memory.ToArray();
                    }
                    try { WriteFrame(pipe, response, IoTimeout); }
                    finally { Array.Clear(response, 0, response.Length); }
                    return result.ExitCode;
                }
            }
            catch (Exception ex) { return ErrorCode(ex); }
        }

        internal sealed class InstallRequest
        {
            public ServiceInstallProfile Profile;
            public string Password;
            public bool Uninstall;
        }

        internal static byte[] SerializeRequest(ServiceInstallProfile profile, string password, bool uninstall)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(memory, StrictUtf8, true))
                {
                    writer.Write(ProtocolVersion); writer.Write(uninstall);
                    WriteText(writer, profile.ExecutablePath, 32767); writer.Write((int)profile.Account);
                    WriteText(writer, profile.UserName ?? String.Empty, 512);
                    WriteText(writer, password ?? String.Empty, 4096);
                }
                if (memory.Length > MaxMessageBytes) throw new ArgumentException("Слишком большой запрос установки.");
                return memory.ToArray();
            }
        }

        internal static InstallRequest DeserializeRequest(byte[] bytes)
        {
            if (bytes == null || bytes.Length > MaxMessageBytes) throw new InvalidDataException("Недопустимый запрос установки.");
            using (BinaryReader reader = new BinaryReader(new MemoryStream(bytes, false), StrictUtf8))
            {
                if (reader.ReadInt32() != ProtocolVersion) throw new InvalidDataException("Неизвестный протокол установки.");
                bool uninstall = reader.ReadBoolean();
                var profile = new ServiceInstallProfile { Mode = ServiceInstallMode.ManagedDotNet,
                    ExecutablePath = ReadText(reader, 32767), Account = (InstallAccountKind)reader.ReadInt32(), UserName = ReadText(reader, 512) };
                string password = ReadText(reader, 4096);
                profile.Validate();
                if (password.IndexOf('\0') >= 0 || (!uninstall && profile.Account == InstallAccountKind.User && password.Length == 0) ||
                    reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Некорректные параметры установки.");
                return new InstallRequest { Profile = profile, Password = password, Uninstall = uninstall };
            }
        }

        private static void WriteText(BinaryWriter writer, string text, int maxCharacters)
        {
            text = text ?? String.Empty;
            if (text.Length > maxCharacters) throw new InvalidDataException("Превышен размер поля установки.");
            byte[] bytes = StrictUtf8.GetBytes(text);
            try { writer.Write(bytes.Length); writer.Write(bytes); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }

        private static string ReadText(BinaryReader reader, int maxCharacters)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > Math.Min(MaxMessageBytes, maxCharacters * 4) || count > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Недопустимый размер поля установки.");
            byte[] bytes = reader.ReadBytes(count);
            try
            {
                string text = StrictUtf8.GetString(bytes);
                if (text.Length > maxCharacters) throw new InvalidDataException("Превышен размер поля установки.");
                return text;
            }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }

        private static byte[] ReadFrame(PipeStream pipe, int milliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            byte[] size = new byte[4]; ReadExact(pipe, size, deadline);
            int count = BitConverter.ToInt32(size, 0);
            if (count < 0 || count > MaxMessageBytes) throw new InvalidDataException("Недопустимый размер сообщения установщика.");
            byte[] content = new byte[count];
            try { ReadExact(pipe, content, deadline); return content; }
            catch { Array.Clear(content, 0, content.Length); throw; }
        }

        private static void ReadExact(PipeStream pipe, byte[] bytes, DateTime deadline)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                IAsyncResult read = pipe.BeginRead(bytes, offset, bytes.Length - offset, null, null);
                using (read.AsyncWaitHandle)
                {
                    int remaining = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    if (!read.AsyncWaitHandle.WaitOne(remaining)) throw new TimeoutException("Истекло время ожидания установщика. Обнови статус службы.");
                    int count = pipe.EndRead(read);
                    if (count == 0) throw new EndOfStreamException("Установщик закрыл канал связи.");
                    offset += count;
                }
            }
        }

        private static void WriteFrame(PipeStream pipe, byte[] bytes, int milliseconds)
        {
            if (bytes.Length > MaxMessageBytes) throw new InvalidDataException("Слишком большое сообщение установщика.");
            byte[] frame = new byte[bytes.Length + 4];
            Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length), 0, frame, 0, 4); Buffer.BlockCopy(bytes, 0, frame, 4, bytes.Length);
            try
            {
                IAsyncResult write = pipe.BeginWrite(frame, 0, frame.Length, null, null);
                using (write.AsyncWaitHandle)
                {
                    if (!write.AsyncWaitHandle.WaitOne(milliseconds)) throw new TimeoutException("Не удалось передать данные установщику за отведённое время.");
                    pipe.EndWrite(write);
                }
            }
            finally { Array.Clear(frame, 0, frame.Length); }
        }

        internal static string ResolveHelperExecutable(string managerExe, string targetExe)
        {
            ServiceInstallProfile.NormalizeExecutable(managerExe);
            string architecture = ReadManagedArchitecture(targetExe);
            return EmbeddedHelpers.Prepare(architecture == "anycpu" ? (Environment.Is64BitProcess ? "x64" : "x86") : architecture);
        }

        // PE/CLR metadata only: this never loads or executes the selected assembly.
        internal static string ReadManagedArchitecture(string executable)
        {
            string path = ServiceInstallProfile.NormalizeExecutable(executable);
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (stream.Length < 64 || reader.ReadUInt16() != 0x5A4D) throw new BadImageFormatException("Выбранный .exe не является сборкой .NET Framework.");
                stream.Position = 60; int pe = reader.ReadInt32();
                if (pe < 64 || pe > stream.Length - 24) throw new BadImageFormatException("Повреждён заголовок .exe.");
                stream.Position = pe;
                if (reader.ReadUInt32() != 0x00004550) throw new BadImageFormatException("Повреждён заголовок PE.");
                ushort machine = reader.ReadUInt16(); ushort sections = reader.ReadUInt16();
                stream.Position = pe + 20; ushort optionalSize = reader.ReadUInt16();
                long optional = pe + 24;
                if (sections == 0 || sections > 96 || optional + optionalSize + sections * 40L > stream.Length) throw new BadImageFormatException("Повреждены разделы PE.");
                stream.Position = optional; ushort magic = reader.ReadUInt16();
                int directory = magic == 0x10B ? 96 : magic == 0x20B ? 112 : -1;
                if (directory < 0 || optionalSize < directory + 15 * 8) throw new BadImageFormatException("Не найден заголовок CLR.");
                stream.Position = optional + directory + 14 * 8; uint clrRva = reader.ReadUInt32(); uint clrSize = reader.ReadUInt32();
                if (clrRva == 0 || clrSize < 20) throw new BadImageFormatException("Для этого .exe используй обычную установку -i / -u: установщик .NET не найден.");
                long clrOffset = -1;
                for (int i = 0; i < sections; i++)
                {
                    stream.Position = optional + optionalSize + i * 40L + 8;
                    uint virtualSize = reader.ReadUInt32(), virtualAddress = reader.ReadUInt32(), rawSize = reader.ReadUInt32(), raw = reader.ReadUInt32();
                    if (clrRva >= virtualAddress && (ulong)clrRva + 20 <= (ulong)virtualAddress + Math.Max(virtualSize, rawSize))
                        clrOffset = (long)raw + clrRva - virtualAddress;
                }
                if (clrOffset < 0 || clrOffset > stream.Length - 20) throw new BadImageFormatException("Повреждён заголовок CLR.");
                stream.Position = clrOffset + 16; uint flags = reader.ReadUInt32();
                if (machine == 0x8664) return "x64";
                if (machine != 0x14C) throw new BadImageFormatException("Архитектура службы не поддерживается этим установщиком.");
                return (flags & 2) != 0 || (flags & 1) == 0 ? "x86" : "anycpu";
            }
        }

        private static void InstallAssembly(ServiceInstallProfile profile, string password, bool uninstall)
        {
            ReadManagedArchitecture(profile.ExecutablePath);
            Directory.SetCurrentDirectory(Path.GetDirectoryName(profile.ExecutablePath));
            // UseNewContext=false preserves our in-memory credentials and suppresses framework
            // InstallLog output. Normal .InstallState transactions are preserved by AssemblyInstaller.
            var context = new InstallContext(null, new string[] { "/logfile=", "/logtoconsole=false", "/unattended=true" });
            context.Parameters["assemblypath"] = profile.ExecutablePath;
            using (var installer = new AssemblyInstaller(profile.ExecutablePath, new string[0]))
            {
                installer.UseNewContext = false; installer.Context = context;
                installer.BeforeInstall += delegate
                {
                    int count = ApplyAccountTree(installer, profile, password, context, true);
                    if (count == 0) throw new InvalidOperationException("В .exe не найден ServiceProcessInstaller. Используй обычную установку -i / -u.");
                };
                if (uninstall) { installer.Uninstall(null); return; }
                var state = new Hashtable();
                try { installer.Install(state); installer.Commit(state); }
                catch (Exception original)
                {
                    try { installer.Rollback(state); }
                    catch (Exception) { throw new InvalidOperationException("Установка завершилась с ошибкой; полный откат не подтверждён. Обнови статус службы. " + SafeError(original, password), original); }
                    throw;
                }
                finally { context.Parameters.Remove("password"); context.Parameters.Remove("username"); }
            }
        }

        // Also used by pure tests on an inert Installer tree; does not invoke Install.
        internal static int ApplyAccountTree(Installer installer, ServiceInstallProfile profile, string password, InstallContext context, bool addGuards)
        {
            int count = 0;
            installer.Context = context;
            ServiceProcessInstaller process = installer as ServiceProcessInstaller;
            if (process != null)
            {
                process.Account = AccountValue(profile.Account);
                process.Username = profile.Account == InstallAccountKind.User ? profile.UserName : null;
                process.Password = profile.Account == InstallAccountKind.User ? password : null;
                count++;
            }
            if (profile.Account == InstallAccountKind.User)
            { context.Parameters["username"] = profile.UserName; context.Parameters["password"] = password; }
            else { context.Parameters.Remove("username"); context.Parameters.Remove("password"); }
            context.Parameters["unattended"] = "true";
            foreach (Installer child in installer.Installers) count += ApplyAccountTree(child, profile, password, context, addGuards);
            // ProjectInstaller's existing BeforeInstall callbacks run first, so custom hooks
            // (including HttpService's -s assemblypath hook) remain present. Reapply the explicit
            // account after those callbacks and before its child installers are invoked.
            if (addGuards) installer.BeforeInstall += delegate { ApplyAccountTree(installer, profile, password, context, false); };
            return count;
        }

        internal static ServiceAccount AccountValue(InstallAccountKind account)
        {
            switch (account)
            {
                case InstallAccountKind.LocalService: return ServiceAccount.LocalService;
                case InstallAccountKind.NetworkService: return ServiceAccount.NetworkService;
                case InstallAccountKind.LocalSystem: return ServiceAccount.LocalSystem;
                case InstallAccountKind.User: return ServiceAccount.User;
                default: throw new ArgumentException("Неизвестный тип учётной записи.");
            }
        }

        private static int ErrorCode(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                Win32Exception windows = current as Win32Exception;
                if (windows != null && windows.NativeErrorCode > 0) return windows.NativeErrorCode;
                if (current is UnauthorizedAccessException || current is SecurityException) return 5;
                if (current is TimeoutException) return 1460;
            }
            return 1;
        }

        internal static string SafeError(Exception ex, string password)
        {
            StringBuilder text = new StringBuilder();
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                string part = current.Message ?? current.GetType().Name;
                if (!String.IsNullOrEmpty(password)) part = part.Replace(password, "[пароль скрыт]");
                if (text.Length > 0) text.Append(" ");
                text.Append(part);
                if (text.Length > 10000) break;
            }
            return text.ToString().Substring(0, Math.Min(12000, text.Length));
        }
    }
}
