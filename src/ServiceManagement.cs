using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ServiceTool
{
    public static class ServiceManagement
    {
        public const int RollbackFailed = 1003; // ERROR_CAN_NOT_COMPLETE; startup rollback needs manual verification.
        private const int InvalidParameter = 87;
        private const int Timeout = 1460;
        private const int WaitMilliseconds = 30000;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal sealed class ManagementRequest
        {
            public string ServiceName, Action, Mode;
        }

        internal sealed class StartupConfiguration
        {
            public uint StartType;
            public bool Delayed;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatus
        {
            public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode;
            public uint ServiceSpecificExitCode, CheckPoint, WaitHint;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatusProcess
        {
            public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode;
            public uint ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct QueryServiceConfigData
        {
            public uint ServiceType, StartType, ErrorControl;
            public IntPtr BinaryPath, LoadOrderGroup;
            public uint TagId;
            public IntPtr Dependencies, StartName, DisplayName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManager(string machine, string database, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenService(IntPtr scm, string name, uint access);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr handle);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool StartService(IntPtr service, uint argumentCount, IntPtr arguments);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ControlService(IntPtr service, uint control, out ServiceStatus status);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatusEx(IntPtr service, int level,
            out ServiceStatusProcess status, uint size, out uint needed);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryServiceConfig(IntPtr service, IntPtr config, uint size, out uint needed);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryServiceConfig2(IntPtr service, uint level, out int info, uint size, out uint needed);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ChangeServiceConfig(IntPtr service, uint serviceType, uint startType,
            uint errorControl, string binaryPath, string group, IntPtr tag, string dependencies,
            string account, string password, string displayName);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ChangeServiceConfig2(IntPtr service, uint level, ref int info);

        public static ProcessStartInfo BuildHelperStartInfo(string helperExe, string serviceName, string action, string mode)
        {
            ValidateRequest(serviceName, action, mode);
            string path = ExecutablePaths.Normalize(helperExe);
            if (!String.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Выбери исполняемый файл утилиты.", "helperExe");
            if (!File.Exists(path)) throw new FileNotFoundException("Исполняемый файл утилиты не найден.", path);
            return new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--manage-service " + action + " " + Convert.ToBase64String(StrictUtf8.GetBytes(serviceName)) + " " + mode,
                WorkingDirectory = Path.GetDirectoryName(path),
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }

        // Program.Main dispatches here only when the first argument is --manage-service.
        // The helper communicates through its exit code; it creates no logs or result files.
        public static int RunHelper(string[] args)
        {
            ManagementRequest request;
            if (!TryParseRequest(args, out request)) return InvalidParameter;
            try
            {
                Execute(request.ServiceName, request.Action, request.Mode);
                return 0;
            }
            catch (Win32Exception ex) { return ex.NativeErrorCode > 0 ? ex.NativeErrorCode : 1; }
            catch (UnauthorizedAccessException) { return 5; }
            catch (System.Security.SecurityException) { return 5; }
            catch (ArgumentException) { return InvalidParameter; }
            catch (Exception) { return 1; }
        }

        internal static bool TryParseRequest(string[] args, out ManagementRequest request)
        {
            request = null;
            if (args == null || args.Length != 4 || args[0] != "--manage-service" ||
                String.IsNullOrEmpty(args[2]) || args[2].Length > 2048) return false;
            try
            {
                byte[] bytes = Convert.FromBase64String(args[2]);
                // Reject whitespace and noncanonical encodings accepted by FromBase64String.
                if (Convert.ToBase64String(bytes) != args[2]) return false;
                string name = StrictUtf8.GetString(bytes);
                ValidateRequest(name, args[1], args[3]);
                request = new ManagementRequest { ServiceName = name, Action = args[1], Mode = args[3] };
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (FormatException) { return false; }
        }

        private static void ValidateRequest(string serviceName, string action, string mode)
        {
            if (String.IsNullOrWhiteSpace(serviceName) || serviceName.Length > 256)
                throw new ArgumentException("Некорректное системное имя службы.", "serviceName");
            foreach (char ch in serviceName)
                if (ch < 32 || ch == '/' || ch == '\\')
                    throw new ArgumentException("Некорректное системное имя службы.", "serviceName");
            StrictUtf8.GetByteCount(serviceName);
            if ((action == "start" || action == "stop") && mode == "none") return;
            if (action == "startup" && (mode == "auto" || mode == "auto-delayed" || mode == "manual" || mode == "disabled")) return;
            throw new ArgumentException("Недопустимое действие или режим запуска службы.");
        }

        // Called only by the explicitly elevated helper, never during catalog reads or UI rendering.
        public static void Execute(string serviceName, string action, string mode)
        {
            ValidateRequest(serviceName, action, mode);
            IntPtr scm = OpenSCManager(null, null, 0x0001);
            if (scm == IntPtr.Zero) throw NativeError();
            IntPtr service = IntPtr.Zero;
            try
            {
                uint access = action == "startup" ? 0x0001u | 0x0002u :
                    0x0004u | (action == "start" ? 0x0010u : 0x0020u);
                service = OpenService(scm, serviceName, access);
                if (service == IntPtr.Zero) throw NativeError();
                if (action == "startup")
                {
                    StartupConfiguration before = ReadStartup(service);
                    var desired = new StartupConfiguration
                    {
                        StartType = mode == "manual" ? 3u : mode == "disabled" ? 4u : 2u,
                        Delayed = mode == "auto-delayed"
                    };
                    ApplyStartupChange(before, desired,
                        delegate(uint value) { WriteStartType(service, value); },
                        delegate(bool value) { WriteDelayed(service, value); },
                        delegate { return ReadStartup(service); });
                }
                else
                {
                    ChangeRunningState(service, action == "start");
                }
            }
            finally
            {
                if (service != IntPtr.Zero) CloseServiceHandle(service);
                CloseServiceHandle(scm);
            }
        }

        internal enum RunningStep { Done, Wait, Start, Stop, Invalid }

        internal static RunningStep NextRunningStep(uint state, bool start, bool commandSent)
        {
            if (state == (start ? 4u : 1u)) return RunningStep.Done;
            if (state == 2 || state == 3 || state == 5 || state == 6) return RunningStep.Wait;
            if (commandSent) return RunningStep.Wait;
            if (start) return state == 1 ? RunningStep.Start : RunningStep.Invalid;
            return state == 4 || state == 7 ? RunningStep.Stop : RunningStep.Invalid;
        }

        private static void ChangeRunningState(IntPtr service, bool start)
        {
            var clock = Stopwatch.StartNew();
            bool commandSent = false;
            while (true)
            {
                ServiceStatusProcess state = ReadStatus(service);
                if (start && commandSent && state.CurrentState == 1)
                    throw new Win32Exception(state.Win32ExitCode != 0 ? checked((int)state.Win32ExitCode) : 1067);
                RunningStep next = NextRunningStep(state.CurrentState, start, commandSent);
                if (next == RunningStep.Done) return;
                if (clock.ElapsedMilliseconds >= WaitMilliseconds) throw new Win32Exception(Timeout);
                if (next == RunningStep.Invalid) throw new Win32Exception(1061);
                if (next == RunningStep.Start)
                {
                    if (!StartService(service, 0, IntPtr.Zero))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != 1056) throw new Win32Exception(error);
                    }
                    commandSent = true;
                }
                else if (next == RunningStep.Stop)
                {
                    ServiceStatus ignored;
                    // No dependent service enumeration or recursive stop: SCM returns 1051 if blocked.
                    if (!ControlService(service, 1, out ignored))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != 1062) throw new Win32Exception(error);
                    }
                    commandSent = true;
                }
                long remaining = WaitMilliseconds - clock.ElapsedMilliseconds;
                if (remaining > 0) Thread.Sleep((int)Math.Min(250, remaining));
            }
        }

        private static ServiceStatusProcess ReadStatus(IntPtr service)
        {
            ServiceStatusProcess status;
            uint needed;
            if (!QueryServiceStatusEx(service, 0, out status,
                (uint)Marshal.SizeOf(typeof(ServiceStatusProcess)), out needed)) throw NativeError();
            return status;
        }

        private static StartupConfiguration ReadStartup(IntPtr service)
        {
            uint needed;
            if (!QueryServiceConfig(service, IntPtr.Zero, 0, out needed) && Marshal.GetLastWin32Error() != 122)
                throw NativeError();
            if (needed == 0 || needed > 8192) throw new Win32Exception(InvalidParameter);
            IntPtr buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!QueryServiceConfig(service, buffer, needed, out needed)) throw NativeError();
                var config = (QueryServiceConfigData)Marshal.PtrToStructure(buffer, typeof(QueryServiceConfigData));
                int delayed;
                if (!QueryServiceConfig2(service, 3, out delayed, 4, out needed)) throw NativeError();
                return new StartupConfiguration { StartType = config.StartType, Delayed = delayed != 0 };
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static void WriteStartType(IntPtr service, uint startType)
        {
            const uint noChange = 0xffffffff;
            if (!ChangeServiceConfig(service, noChange, startType, noChange,
                null, null, IntPtr.Zero, null, null, null, null)) throw NativeError();
        }

        private static void WriteDelayed(IntPtr service, bool delayed)
        {
            int flag = delayed ? 1 : 0;
            if (!ChangeServiceConfig2(service, 3, ref flag)) throw NativeError();
        }

        // Kept independent of native APIs so rollback behavior can be verified without changing Windows services.
        internal static void ApplyStartupChange(StartupConfiguration before, StartupConfiguration desired,
            Action<uint> writeStart, Action<bool> writeDelayed, Func<StartupConfiguration> readCurrent)
        {
            bool typeChanged = before.StartType != desired.StartType;
            if (typeChanged) writeStart(desired.StartType);
            if (before.Delayed == desired.Delayed) return;
            try { writeDelayed(desired.Delayed); }
            catch (Win32Exception)
            {
                if (!typeChanged) throw;
                bool rollbackOk = true;
                try { writeStart(before.StartType); } catch (Win32Exception) { rollbackOk = false; }
                try { writeDelayed(before.Delayed); } catch (Win32Exception) { rollbackOk = false; }
                if (!rollbackOk)
                {
                    try
                    {
                        StartupConfiguration current = readCurrent();
                        rollbackOk = current.StartType == before.StartType && current.Delayed == before.Delayed;
                    }
                    catch (Win32Exception) { rollbackOk = false; }
                }
                if (!rollbackOk) throw new Win32Exception(RollbackFailed);
                throw;
            }
        }

        private static Win32Exception NativeError()
        {
            return new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
