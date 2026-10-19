using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using ServiceTool;

internal static class ManagementTests
{
    private static int checks;

    private static void Assert(bool value, string message)
    {
        ++checks;
        if (!value) throw new Exception("FAIL: " + message);
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        ++checks;
        try { action(); }
        catch (T) { return; }
        throw new Exception("FAIL: " + message);
    }

    private static void Win32Error(Action action, int expected, string message)
    {
        ++checks;
        try { action(); }
        catch (Win32Exception ex)
        {
            if (ex.NativeErrorCode == expected) return;
            throw new Exception("FAIL: " + message + "; expected " + expected + ", got " + ex.NativeErrorCode);
        }
        throw new Exception("FAIL: " + message + "; no exception");
    }

    private static string Encode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static bool Parses(string[] args)
    {
        ServiceManagement.ManagementRequest ignored;
        return ServiceManagement.TryParseRequest(args, out ignored);
    }

    private static ServiceManagement.StartupConfiguration Config(uint type, bool delayed)
    {
        return new ServiceManagement.StartupConfiguration { StartType = type, Delayed = delayed };
    }

    private static int Main()
    {
        string parent = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "management-test-data");
        string scratch = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            string helper = Path.Combine(scratch, "Утилита & helper.exe");
            File.WriteAllText(helper, "Inert test data. Never executed.");
            string name = "Служба \"A&B\" ; $() с пробелами";
            var start = ServiceManagement.BuildHelperStartInfo(helper, name, "start", "none");
            string[] args = start.Arguments.Split(' ');
            ServiceManagement.ManagementRequest request;
            Assert(args.Length == 4 && args[0] == "--manage-service", "helper has exactly four arguments");
            Assert(ServiceManagement.TryParseRequest(args, out request), "helper launch round-trips through parser");
            Assert(request.ServiceName == name && request.Action == "start" && request.Mode == "none", "UTF8 service identity is preserved");
            Assert(!start.Arguments.Contains(name), "raw service name cannot inject command-line arguments");
            Assert(start.FileName == helper && start.WorkingDirectory == scratch, "helper path and working directory are separate and absolute");
            Assert(start.UseShellExecute && start.Verb == "runas", "only helper requests elevation");
            Assert(start.WindowStyle == ProcessWindowStyle.Hidden, "helper starts hidden");
            Assert(!start.RedirectStandardOutput && !start.RedirectStandardError && !start.RedirectStandardInput, "helper uses no redirected result channels");
            foreach (string mode in new[] { "auto", "auto-delayed", "manual", "disabled" })
            {
                var info = ServiceManagement.BuildHelperStartInfo(helper, name, "startup", mode);
                Assert(ServiceManagement.TryParseRequest(info.Arguments.Split(' '), out request) && request.Mode == mode,
                    "startup mode " + mode + " round-trip");
            }
            var stop = ServiceManagement.BuildHelperStartInfo(helper, name, "stop", "none");
            Assert(ServiceManagement.TryParseRequest(stop.Arguments.Split(' '), out request) && request.Action == "stop", "stop round-trip");
            Assert(Parses(new[] { "--manage-service", "start", Encode(new string('Я', 256)), "none" }), "maximum Unicode service name is accepted");

            Assert(!Parses(null), "null argument array rejected");
            Assert(!Parses(new string[0]), "empty argument array rejected");
            Assert(!Parses(new[] { "--manage-service", "start", Encode(name) }), "missing mode rejected");
            Assert(!Parses(new[] { "--manage-service", "start", Encode(name), "none", "extra" }), "extra arguments rejected");
            Assert(!Parses(new[] { "--MANAGE-SERVICE", "start", Encode(name), "none" }), "helper marker is exact");
            Assert(!Parses(new[] { "--manage-service", "Start", Encode(name), "none" }), "action allowlist is exact");
            Assert(!Parses(new[] { "--manage-service", "start", Encode(name), "auto" }), "start cannot carry a startup mode");
            Assert(!Parses(new[] { "--manage-service", "stop", Encode(name), "disabled" }), "stop cannot change startup type");
            Assert(!Parses(new[] { "--manage-service", "startup", Encode(name), "none" }), "startup requires a valid mode");
            Assert(!Parses(new[] { "--manage-service", "startup", Encode(name), "Auto" }), "startup allowlist is exact");
            Assert(!Parses(new[] { "--manage-service", "delete", Encode(name), "none" }), "delete is not an available action");
            Assert(!Parses(new[] { "--manage-service", "start", "/w==", "none" }), "invalid UTF8 rejected");
            Assert(!Parses(new[] { "--manage-service", "start", "Y Q==", "none" }), "base64 whitespace rejected");
            Assert(!Parses(new[] { "--manage-service", "start", "YQ===", "none" }), "invalid base64 rejected");
            Assert(!Parses(new[] { "--manage-service", "start", new string('A', 2049), "none" }), "oversized payload rejected");
            Assert(!Parses(new[] { "--manage-service", "start", "", "none" }), "empty name payload rejected");
            foreach (string invalidName in new[] { " ", "bad/name", "bad\\name", "bad\0name", "bad\nname", new string('a', 257) })
                Assert(!Parses(new[] { "--manage-service", "start", Encode(invalidName), "none" }), "invalid service name rejected");
            Throws<ArgumentException>(delegate { ServiceManagement.BuildHelperStartInfo(helper, "bad\ud800", "start", "none"); }, "unpaired UTF16 surrogate rejected");
            Throws<ArgumentException>(delegate { ServiceManagement.BuildHelperStartInfo(helper, name, "restart", "none"); }, "unlisted builder action rejected");
            Throws<ArgumentException>(delegate { ServiceManagement.BuildHelperStartInfo(Path.Combine(scratch, "helper.txt"), name, "start", "none"); }, "non-.exe helper rejected");
            Throws<FileNotFoundException>(delegate { ServiceManagement.BuildHelperStartInfo(Path.Combine(scratch, "missing.exe"), name, "start", "none"); }, "missing helper rejected");
            // Only invalid helper requests are executed: validation must return before any native call.
            Assert(ServiceManagement.RunHelper(null) == 87, "invalid helper invocation returns Win32 invalid-parameter");
            Assert(ServiceManagement.RunHelper(new[] { "--manage-service", "delete", Encode(name), "none" }) == 87, "invalid action cannot reach SCM");

            Assert(ServiceManagement.NextRunningStep(4, true, false) == ServiceManagement.RunningStep.Done, "already running is idempotent");
            Assert(ServiceManagement.NextRunningStep(1, false, false) == ServiceManagement.RunningStep.Done, "already stopped is idempotent");
            Assert(ServiceManagement.NextRunningStep(1, true, false) == ServiceManagement.RunningStep.Start, "start applies only to stopped state");
            Assert(ServiceManagement.NextRunningStep(4, false, false) == ServiceManagement.RunningStep.Stop, "running service can receive stop");
            Assert(ServiceManagement.NextRunningStep(7, false, false) == ServiceManagement.RunningStep.Stop, "paused service can receive stop");
            Assert(ServiceManagement.NextRunningStep(7, true, false) == ServiceManagement.RunningStep.Invalid, "start does not silently resume paused service");
            foreach (uint pending in new uint[] { 2, 3, 5, 6 })
            {
                Assert(ServiceManagement.NextRunningStep(pending, true, false) == ServiceManagement.RunningStep.Wait, "start waits through pending state " + pending);
                Assert(ServiceManagement.NextRunningStep(pending, false, false) == ServiceManagement.RunningStep.Wait, "stop waits through pending state " + pending);
            }
            Assert(ServiceManagement.NextRunningStep(4, false, true) == ServiceManagement.RunningStep.Wait, "stop control is not repeated");
            Assert(ServiceManagement.NextRunningStep(0, true, false) == ServiceManagement.RunningStep.Invalid, "unknown state never triggers a control");
            Assert(new ServiceEntry().StartupKnown, "synthetic ServiceEntry defaults preserve existing test callers");

            uint currentType = 3;
            bool currentDelayed = false;
            int typeWrites = 0, delayedWrites = 0;
            Action<uint> writeType = delegate(uint value) { ++typeWrites; currentType = value; };
            Action<bool> writeDelayed = delegate(bool value) { ++delayedWrites; currentDelayed = value; };
            Func<ServiceManagement.StartupConfiguration> read = delegate { return Config(currentType, currentDelayed); };
            ServiceManagement.ApplyStartupChange(Config(3, false), Config(2, true), writeType, writeDelayed, read);
            Assert(currentType == 2 && currentDelayed, "auto-delayed sets both startup settings");
            Assert(typeWrites == 1 && delayedWrites == 1, "startup writes only settings that differ");
            typeWrites = delayedWrites = 0;
            ServiceManagement.ApplyStartupChange(Config(2, true), Config(2, true), writeType, writeDelayed, read);
            Assert(typeWrites == 0 && delayedWrites == 0, "already-correct startup performs no writes");
            ServiceManagement.ApplyStartupChange(Config(2, true), Config(2, false), writeType, writeDelayed, read);
            Assert(typeWrites == 0 && delayedWrites == 1 && !currentDelayed, "auto clears delayed flag without changing other config");

            currentType = 3; currentDelayed = false; typeWrites = delayedWrites = 0;
            Win32Error(delegate
            {
                ServiceManagement.ApplyStartupChange(Config(3, false), Config(2, true),
                    delegate(uint value) { throw new Win32Exception(5); }, writeDelayed, read);
            }, 5, "first-step error is preserved");
            Assert(typeWrites == 0 && delayedWrites == 0 && currentType == 3, "first-step failure cannot run second step");

            Win32Error(delegate
            {
                ServiceManagement.ApplyStartupChange(Config(3, false), Config(2, true), writeType,
                    delegate(bool value) { if (value) throw new Win32Exception(87); currentDelayed = value; }, read);
            }, 87, "second-step error is preserved after successful rollback");
            Assert(currentType == 3 && !currentDelayed, "second-step failure restores original startup settings");

            currentType = 3;
            Win32Error(delegate
            {
                ServiceManagement.ApplyStartupChange(Config(3, false), Config(2, true), writeType,
                    delegate(bool value) { throw new Win32Exception(5); }, read);
            }, 5, "readback can confirm successful rollback when reset API returns error");
            Assert(currentType == 3 && !currentDelayed, "readback verified the original configuration");

            currentType = 3;
            Win32Error(delegate
            {
                ServiceManagement.ApplyStartupChange(Config(3, false), Config(2, true),
                    delegate(uint value) { if (value == 3) throw new Win32Exception(5); currentType = value; },
                    delegate(bool value) { if (value) throw new Win32Exception(87); currentDelayed = value; }, read);
            }, ServiceManagement.RollbackFailed, "unrestored startup returns explicit rollback-failed code");
            Assert(currentType == 2, "test models failed rollback without touching Windows");

            Win32Error(delegate
            {
                ServiceManagement.ApplyStartupChange(Config(3, false), Config(2, true),
                    delegate(uint value) { if (value == 3) throw new Win32Exception(5); },
                    delegate(bool value) { throw new Win32Exception(87); },
                    delegate { throw new Win32Exception(5); });
            }, ServiceManagement.RollbackFailed, "unverifiable rollback is never reported as success");

            Console.WriteLine("PASS: " + checks + " management checks. No real SCM mutation, Execute, or valid RunHelper was invoked.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
        finally
        {
            string allowed = Path.GetFullPath(parent) + Path.DirectorySeparatorChar;
            string actual = Path.GetFullPath(scratch);
            if (actual.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(actual))
                Directory.Delete(actual, true);
        }
    }
}
