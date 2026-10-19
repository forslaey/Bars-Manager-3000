using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ServiceTool;

internal static class CoreTests
{
    private static int checks;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint length);

    private static void Assert(bool condition, string message)
    {
        ++checks;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        ++checks;
        try { action(); }
        catch (T) { return; }
        throw new Exception("FAIL: " + message);
    }

    private static int Main()
    {
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tests", "core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string oldEnvironment = Environment.GetEnvironmentVariable("SERVICE_TOOL_CORE_TEST");
        try
        {
            string directory = Path.Combine(root, "folder.exe with spaces", "Кириллица & symbols");
            Directory.CreateDirectory(directory);
            string exe = Path.Combine(directory, "Service App.exe");
            File.WriteAllText(exe, "This is inert test data, not a PE executable.");
            string other = Path.Combine(root, "Other.exe");
            File.WriteAllText(other, "Also inert test data.");
            string quoted = "\"" + exe + "\"";

            Assert(ExecutablePaths.Matches(quoted, exe), "quoted executable");
            Assert(ExecutablePaths.Matches(quoted + " -service --mode production", exe), "quoted executable with arguments");
            Assert(ExecutablePaths.Matches(exe + " -service", exe), "unquoted existing path, spaces and .exe folder");
            Assert(ExecutablePaths.Matches(exe.ToUpperInvariant(), exe), "case-insensitive Windows paths");
            Assert(ExecutablePaths.Matches(quoted.Replace('\\', '/'), exe), "forward-slash normalization");
            Assert(ExecutablePaths.Matches("\"" + Path.Combine(directory, ".", "Service App.exe") + "\" -x", exe), "dot path normalization");
            Assert(ExecutablePaths.Matches("\"\\\\?\\" + exe + "\" -x", exe), "extended Windows path prefix");
            Assert(ExecutablePaths.Matches("\"\\??\\" + exe + "\" -x", exe), "NT DOS path prefix");

            Environment.SetEnvironmentVariable("SERVICE_TOOL_CORE_TEST", directory);
            Assert(ExecutablePaths.Matches("\"%SERVICE_TOOL_CORE_TEST%\\Service App.exe\" -x", exe), "environment expansion");
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            Assert(ExecutablePaths.Matches(@"\SystemRoot\System32\svchost.exe -k example", Path.Combine(windows, @"System32\svchost.exe")), "SystemRoot alias");

            Assert(!ExecutablePaths.Matches("\"" + other + "\" \"" + exe + "\"", exe), ".exe argument is not the service executable");
            Assert(!ExecutablePaths.Matches(other + " --config \"" + exe + "\"", exe), "unquoted wrapper with .exe argument");
            Assert(!ExecutablePaths.Matches("\"" + Path.Combine(root, "Another", "Service App.exe") + "\"", exe), "same filename in a different directory");
            Assert(!ExecutablePaths.Matches(quoted + "suffix", exe), "quoted executable requires command boundary");
            Assert(!ExecutablePaths.Matches(exe + "suffix -i", exe), "unquoted executable requires command boundary");
            Assert(!ExecutablePaths.Matches("\"" + exe, exe), "unterminated quoted path");
            Assert(!ExecutablePaths.Matches("\"\" -x", exe), "empty quoted path");
            Assert(!ExecutablePaths.Matches(null, exe), "null service path");
            Assert(!ExecutablePaths.Matches("   ", exe), "empty service path");
            Assert(!ExecutablePaths.Matches("\"bad\0path.exe\"", exe), "invalid SCM path");
            Assert(!ExecutablePaths.Matches(quoted + "\0 --bad", exe), "embedded NUL cannot truncate a service command");
            string relative = Path.GetFileName(exe);
            Assert(!ExecutablePaths.Matches(relative, Path.Combine(Environment.CurrentDirectory, relative)), "bare SCM filename is not guessed against current directory");
            Assert(!ExecutablePaths.Matches(@"C:Service.exe", @"C:\Service.exe"), "drive-relative SCM path is not guessed");

            string missing = Path.Combine(root, "Missing.exe");
            Assert(ExecutablePaths.Matches("\"" + missing + "\" -service", missing), "registration survives a missing binary");
            Assert(ExecutablePaths.Matches(missing + " -service", missing), "unquoted missing binary fallback");
            Assert(!ExecutablePaths.Matches(missing + " \"" + exe + "\"", exe), "missing main binary does not match .exe in arguments");

            string ambiguous = Path.Combine(root, "Ambiguous Path.exe");
            string shorter = Path.Combine(root, "Ambiguous.exe");
            File.WriteAllText(ambiguous, "Inert");
            File.WriteAllText(shorter, "Inert");
            Assert(!ExecutablePaths.Matches(ambiguous + " -service", ambiguous), "unquoted path ambiguity resolves the earlier executable");
            Assert(ExecutablePaths.Matches(ambiguous + " -service", shorter), "detect executable Windows would resolve first");
            Assert(ExecutablePaths.Matches("\"" + ambiguous + "\" -service", ambiguous), "quoting removes earlier-prefix ambiguity");

            var entries = new List<ServiceEntry>
            {
                new ServiceEntry { Name = "first", BinaryPath = quoted, State = "Stopped" },
                new ServiceEntry { Name = "second", BinaryPath = quoted + " -instance B", State = "Running" },
                new ServiceEntry { Name = "other", BinaryPath = "\"" + other + "\" \"" + exe + "\"" },
                null,
                new ServiceEntry { Name = "empty" }
            };
            var matches = ServiceCatalog.FindMatches(entries, exe);
            Assert(matches.Count == 2, "all services for the executable are returned");
            Assert(matches[0].Name == "first" && matches[0].State == "Stopped", "stopped services remain installed");
            Assert(matches[1].Name == "second", "multiple registrations retain their identities");

            string[] states = { "Unknown", "Stopped", "Start Pending", "Stop Pending", "Running", "Continue Pending", "Pause Pending", "Paused" };
            for (uint state = 0; state < states.Length; ++state)
                Assert(ServiceCatalog.StateName(state) == states[state], "SCM state " + state);
            Assert(ServiceCatalog.StateName(999) == "Unknown", "unknown SCM state remains unknown");
            string[] startModes = { "Boot", "System", "Auto", "Manual", "Disabled" };
            for (uint mode = 0; mode < startModes.Length; ++mode)
                Assert(ServiceCatalog.StartModeName(mode) == startModes[mode], "SCM start mode " + mode);
            Assert(ServiceCatalog.StartModeName(999) == "Unknown", "unknown SCM start mode remains unknown");

            ProcessStartInfo install = ServiceActions.BuildStartInfo(exe, true);
            ProcessStartInfo uninstall = ServiceActions.BuildStartInfo(exe, false);
            Assert(install.FileName == exe, "launch file is a separate full path");
            Assert(install.Arguments == "-i" && uninstall.Arguments == "-u", "exact install and uninstall switches");
            Assert(install.WorkingDirectory == directory, "working directory is executable folder");
            Assert(install.UseShellExecute && install.Verb == "runas", "request elevation only for target operation");
            Assert(install.WindowStyle == ProcessWindowStyle.Hidden, "console window hidden");
            Assert(!install.RedirectStandardOutput && !install.RedirectStandardError, "no invalid redirection with shell elevation");
            Throws<ArgumentNullException>(delegate { ExecutablePaths.Normalize(null); }, "null selected path error");
            Throws<ArgumentException>(delegate { ExecutablePaths.Normalize("  "); }, "empty selected path error");
            Throws<ArgumentException>(delegate { ExecutablePaths.Normalize("bad\0file.exe"); }, "invalid selected path error");
            Throws<ArgumentException>(delegate { ServiceActions.BuildStartInfo(Path.Combine(root, "file.txt"), true); }, "non-.exe launch rejected");
            Throws<FileNotFoundException>(delegate { ServiceActions.BuildStartInfo(missing, true); }, "missing executable launch rejected");
            Throws<ArgumentNullException>(delegate { ServiceCatalog.FindMatches(null, exe); }, "null catalog error");
            Throws<ArgumentException>(delegate { ServiceCatalog.FindMatches(new List<ServiceEntry>(), ""); }, "empty catalog does not hide invalid path");

            var shortBuffer = new StringBuilder(32768);
            uint shortLength = GetShortPathName(exe, shortBuffer, (uint)shortBuffer.Capacity);
            if (shortLength > 0 && shortLength < shortBuffer.Capacity &&
                !String.Equals(shortBuffer.ToString(), exe, StringComparison.OrdinalIgnoreCase))
            {
                Assert(ExecutablePaths.Matches("\"" + shortBuffer.ToString() + "\" -x", exe), "8.3 short service path normalization");
                Assert(ExecutablePaths.Matches(quoted, shortBuffer.ToString()), "8.3 short selected path normalization");
            }
            else
                Console.WriteLine("SKIP: this volume does not provide an 8.3 alias for the test file.");

            Console.WriteLine("PASS: " + checks + " core checks. No executable or service action was launched.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICE_TOOL_CORE_TEST", oldEnvironment);
            // Restrict cleanup to the unique directory created by this test invocation.
            string expectedRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tests")) + Path.DirectorySeparatorChar;
            string actualRoot = Path.GetFullPath(root);
            if (actualRoot.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(actualRoot))
                Directory.Delete(actualRoot, true);
        }
    }
}
