using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ServiceTool
{
    public sealed class ServiceEntry
    {
        public string Name, DisplayName, BinaryPath, State, StartMode;
        public bool DelayedAutoStart;
        public bool CanStop;
        // Preserve synthetic/previous callers; live entries explicitly start unknown below.
        public bool StartupKnown = true;
    }

    public sealed class ServiceSnapshot
    {
        public List<ServiceEntry> Entries;
        public bool Complete;
        public string Warning;
    }

    public static class ServiceCatalog
    {
        private const int MoreData = 234;

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatusProcess
        {
            public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode;
            public uint ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EnumServiceStatusProcess
        {
            public IntPtr Name, DisplayName;
            public ServiceStatusProcess Status;
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
        private static extern IntPtr OpenSCManager(string machineName, string databaseName, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumServicesStatusEx(IntPtr scm, int infoLevel, uint type, uint state,
            IntPtr buffer, uint bufferSize, out uint needed, out uint returned, ref uint resume, string group);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenService(IntPtr scm, string name, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryServiceConfig(IntPtr service, IntPtr config, uint size, out uint needed);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryServiceConfig2(IntPtr service, uint level, out int info, uint size, out uint needed);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        public static List<ServiceEntry> Read()
        {
            ServiceSnapshot snapshot = ReadSnapshot();
            if (!snapshot.Complete) throw new InvalidOperationException(snapshot.Warning);
            return snapshot.Entries;
        }

        public static ServiceSnapshot ReadSnapshot()
        {
            // Only enumerate and query. Never request SERVICE_START, SERVICE_STOP, DELETE or CREATE.
            IntPtr scm = OpenSCManager(null, null, 0x0001 | 0x0004);
            if (scm == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось прочитать диспетчер служб Windows.");
            var snapshot = new ServiceSnapshot
            {
                Entries = new List<ServiceEntry>(),
                Complete = true,
                Warning = String.Empty
            };
            IntPtr buffer = IntPtr.Zero;
            int unreadable = 0;
            int startupUnreadable = 0;
            try
            {
                const uint bufferSize = 256 * 1024;
                buffer = Marshal.AllocHGlobal((int)bufferSize);
                uint resume = 0;
                while (true)
                {
                    uint needed, returned;
                    bool success = EnumServicesStatusEx(scm, 0, 0x30, 3, buffer, bufferSize,
                        out needed, out returned, ref resume, null);
                    int error = success ? 0 : Marshal.GetLastWin32Error();
                    if (!success && error != MoreData)
                        throw new Win32Exception(error, "Не удалось получить список служб Windows.");
                    int itemSize = Marshal.SizeOf(typeof(EnumServiceStatusProcess));
                    for (uint index = 0; index < returned; ++index)
                    {
                        var native = (EnumServiceStatusProcess)Marshal.PtrToStructure(
                            IntPtr.Add(buffer, checked((int)index * itemSize)), typeof(EnumServiceStatusProcess));
                        var entry = new ServiceEntry
                        {
                            Name = Marshal.PtrToStringUni(native.Name) ?? String.Empty,
                            DisplayName = Marshal.PtrToStringUni(native.DisplayName) ?? String.Empty,
                            State = StateName(native.Status.CurrentState),
                            BinaryPath = String.Empty,
                            StartMode = "Unknown",
                            CanStop = (native.Status.ControlsAccepted & 0x0001) != 0,
                            StartupKnown = false
                        };
                        if (!ReadConfiguration(scm, entry)) ++unreadable;
                        else if (!entry.StartupKnown) ++startupUnreadable;
                        snapshot.Entries.Add(entry);
                    }
                    if (success) break;
                    if (returned == 0 || resume == 0)
                        throw new Win32Exception(error, "Не удалось полностью получить список служб Windows.");
                }
                snapshot.Complete = unreadable == 0;
                if (unreadable > 0)
                    snapshot.Warning = "Проверка неполная: не удалось прочитать настройки служб: " + unreadable +
                        ". Доступные совпадения показаны; отсутствие совпадений не подтверждает отсутствие службы.";
                if (startupUnreadable > 0)
                    snapshot.Warning += (snapshot.Warning.Length > 0 ? " " : "") +
                        "Режим запуска части служб недоступен: " + startupUnreadable + ".";
                return snapshot;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                CloseServiceHandle(scm);
            }
        }

        private static bool ReadConfiguration(IntPtr scm, ServiceEntry entry)
        {
            IntPtr service = OpenService(scm, entry.Name, 0x0001);
            if (service == IntPtr.Zero) return false;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                uint needed;
                if (!QueryServiceConfig(service, IntPtr.Zero, 0, out needed) &&
                    Marshal.GetLastWin32Error() != 122) return false;
                if (needed == 0 || needed > 8192) return false;
                buffer = Marshal.AllocHGlobal((int)needed);
                if (!QueryServiceConfig(service, buffer, needed, out needed)) return false;
                var config = (QueryServiceConfigData)Marshal.PtrToStructure(buffer, typeof(QueryServiceConfigData));
                entry.BinaryPath = Marshal.PtrToStringUni(config.BinaryPath) ?? String.Empty;
                entry.StartMode = StartModeName(config.StartType);
                int delayed;
                if (QueryServiceConfig2(service, 3, out delayed, 4, out needed))
                {
                    entry.DelayedAutoStart = delayed != 0;
                    entry.StartupKnown = true;
                }
                return true;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                CloseServiceHandle(service);
            }
        }

        internal static string StateName(uint state)
        {
            switch (state)
            {
                case 1: return "Stopped";
                case 2: return "Start Pending";
                case 3: return "Stop Pending";
                case 4: return "Running";
                case 5: return "Continue Pending";
                case 6: return "Pause Pending";
                case 7: return "Paused";
                default: return "Unknown";
            }
        }

        internal static string StartModeName(uint start)
        {
            switch (start)
            {
                case 0: return "Boot";
                case 1: return "System";
                case 2: return "Auto";
                case 3: return "Manual";
                case 4: return "Disabled";
                default: return "Unknown";
            }
        }

        public static List<ServiceEntry> FindMatches(IEnumerable<ServiceEntry> entries, string executable)
        {
            if (entries == null) throw new ArgumentNullException("entries");
            // Validate once even when the catalog is empty.
            string normalized = ExecutablePaths.Normalize(executable);
            var result = new List<ServiceEntry>();
            foreach (var entry in entries)
                if (entry != null && ExecutablePaths.Matches(entry.BinaryPath, normalized))
                    result.Add(entry);
            return result;
        }
    }

    public static class ExecutablePaths
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint length);

        public static string Normalize(string path)
        {
            if (path == null) throw new ArgumentNullException("path");
            path = path.Trim();
            if (path.Length == 0) throw new ArgumentException("The executable path is empty.", "path");
            if (path.Length > 1 && path[0] == '"' && path[path.Length - 1] == '"')
                path = path.Substring(1, path.Length - 2);
            if (path.IndexOf('"') >= 0)
                throw new ArgumentException("The executable path contains a quote.", "path");
            foreach (char ch in path)
                if (ch < 32)
                    throw new ArgumentException("The executable path contains a control character.", "path");

            path = Environment.ExpandEnvironmentVariables(path).Replace('/', '\\');
            if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path.Substring(12));
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                path = @"\\" + path.Substring(8);
            else if (path.StartsWith(@"\??\UNC\", StringComparison.OrdinalIgnoreCase))
                path = @"\\" + path.Substring(8);
            else if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) ||
                     path.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(4);

            // Some Framework/path APIs can pass embedded NULs through to native calls.
            // Validate explicitly before normalization or querying a native path function.
            for (int i = 0; i < path.Length; ++i)
            {
                char ch = path[i];
                if (ch < 32 || ch == '"' || ch == '<' || ch == '>' || ch == '|' ||
                    ch == '*' || ch == '?' || (ch == ':' && i != 1))
                    throw new ArgumentException("The executable path contains an invalid character.", "path");
            }

            path = Path.GetFullPath(path);
            var buffer = new StringBuilder(512);
            uint length = GetLongPathName(path, buffer, (uint)buffer.Capacity);
            if (length >= buffer.Capacity && length < 32768)
            {
                buffer = new StringBuilder((int)length + 1);
                length = GetLongPathName(path, buffer, (uint)buffer.Capacity);
            }
            if (length > 0 && length < buffer.Capacity) path = buffer.ToString();
            return path;
        }

        public static bool Matches(string serviceBinaryPath, string executable)
        {
            // An invalid selected file must not accidentally match a malformed SCM path.
            string selected = Normalize(executable);
            if (String.IsNullOrWhiteSpace(serviceBinaryPath)) return false;
            if (serviceBinaryPath.IndexOf('\0') >= 0) return false;
            string binary;
            return TryGetExecutable(serviceBinaryPath, out binary) &&
                String.Equals(binary, selected, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryGetExecutable(string serviceBinaryPath, out string executable)
        {
            executable = null;
            if (String.IsNullOrWhiteSpace(serviceBinaryPath) || serviceBinaryPath.IndexOf('\0') >= 0) return false;
            string command = Environment.ExpandEnvironmentVariables(serviceBinaryPath.Trim());
            string binary;
            if (command[0] == '"')
            {
                int closingQuote = command.IndexOf('"', 1);
                if (closingQuote < 0 || (closingQuote + 1 < command.Length &&
                    !Char.IsWhiteSpace(command[closingQuote + 1]))) return false;
                binary = command.Substring(1, closingQuote - 1);
            }
            else
            {
                binary = ReadUnquotedExecutable(command);
            }
            if (String.IsNullOrEmpty(binary)) return false;
            return TryNormalize(binary, out executable) &&
                String.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadUnquotedExecutable(string command)
        {
            // CreateProcess probes progressively longer prefixes for unquoted paths with spaces.
            // Prefer the first file that exists. Do not treat an .exe appearing in arguments as the target.
            string fallback = null;
            for (int i = 0; i <= command.Length; ++i)
            {
                if (i < command.Length && !Char.IsWhiteSpace(command[i])) continue;
                if (i == 0 || (i > 0 && Char.IsWhiteSpace(command[i - 1]))) continue;
                string candidate = command.Substring(0, i);
                if (candidate.IndexOf('"') >= 0) break;
                string normalized;
                if (!TryNormalize(candidate, out normalized)) continue;
                string extension = Path.GetExtension(normalized);
                if (String.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (fallback == null) fallback = candidate;
                    if (File.Exists(normalized)) return candidate;
                }
                else if (extension.Length == 0 && File.Exists(normalized + ".exe"))
                {
                    return normalized + ".exe";
                }
            }
            return fallback;
        }

        private static bool TryNormalize(string path, out string normalized)
        {
            try
            {
                // SCM binary paths are fully qualified. Never guess a service by its filename
                // or resolve a service command relative to this utility's working directory.
                string candidate = path.Replace('/', '\\');
                bool absolute = candidate.StartsWith(@"\\", StringComparison.Ordinal) ||
                    candidate.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase) ||
                    candidate.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase) ||
                    (candidate.Length >= 3 && Char.IsLetter(candidate[0]) && candidate[1] == ':' && candidate[2] == '\\');
                if (absolute) { normalized = Normalize(path); return true; }
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (PathTooLongException) { }
            normalized = null;
            return false;
        }
    }

    public static class ServiceActions
    {
        public static ProcessStartInfo BuildStartInfo(string executable, bool install)
        {
            string path = ExecutablePaths.Normalize(executable);
            if (!String.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Выбери файл .exe.", "executable");
            if (!File.Exists(path)) throw new FileNotFoundException("Выбранный файл .exe больше не существует.", path);
            return new ProcessStartInfo
            {
                FileName = path,
                Arguments = install ? "-i" : "-u",
                WorkingDirectory = Path.GetDirectoryName(path),
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }
    }
}
