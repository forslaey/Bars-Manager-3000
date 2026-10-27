using System;
using System.Collections.Generic;
using System.IO;

namespace ServiceTool
{
    internal static class ReviewRegressionTests
    {
        private static int count;
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            count++;
        }

        private static void CheckOutcomes(string directory)
        {
            var package = new SoftwarePackage { Id = "anydesk", Version = "9.8" };
            string log = Path.Combine(directory, "fixture.log");
            File.WriteAllText(log, "inert fixture");
            foreach (int code in new[] { 3010, 1641, 1603, 1618, -1 })
            {
                SoftwareCheck after;
                int probes = 0;
                var result = SoftwareInstaller.CheckInstallOutcome(code, package, log, () => {
                    probes++;
                    throw new Exception("Detection must not run after reboot/failure");
                }, out after);
                Check(result != null && result.ExitCode == code, "Preserve installer code " + code);
                Check(result.State == LibraryState.Unknown && after == null, "No premature readiness " + code);
                Check(probes == 0, "Stop before detection and post-install actions " + code);
                Check(result.Reboot == (code == 3010 || code == 1641), "Preserve reboot signal " + code);
                Check(result.LogPath == log, "Preserve existing installer log " + code);
            }
            foreach (LibraryState state in new[] { LibraryState.Missing, LibraryState.Unknown, LibraryState.NotApplicable })
            {
                SoftwareCheck after;
                var result = SoftwareInstaller.CheckInstallOutcome(0, package, "missing.log",
                    () => new SoftwareCheck { State = state, Version = "", Detail = "fixture detail" }, out after);
                Check(result != null && result.State == LibraryState.Unknown, "Unconfirmed state blocks continuation " + state);
                Check(result.LogPath == "" && result.Message.Contains("fixture detail"), "Missing log and diagnosis " + state);
            }
            foreach (string version in new[] { "9.7", "9.8", "9.9" })
            {
                SoftwareCheck after;
                int probes = 0;
                var found = new SoftwareCheck { State = LibraryState.Installed, Version = version };
                var result = SoftwareInstaller.CheckInstallOutcome(0, package, log, () => { probes++; return found; }, out after);
                Check((result == null) == (version != "9.7"), "Only equal/newer confirmed installation continues " + version);
                Check(probes == 1 && Object.ReferenceEquals(after, found), "Single detection snapshot reused " + version);
            }
        }

        private static void CheckCleanup(string directory)
        {
            var messages = new List<string>();
            string folder = Path.Combine(directory, "validation");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "fixture.part");
            File.WriteAllText(file, "fixture");
            SoftwareSources.CleanupDownload(file, folder, messages.Add);
            Check(!File.Exists(file) && !Directory.Exists(folder), "Normal cleanup removes exact file and empty directory");
            Check(messages.Count == 0, "Normal cleanup produces no warning");
            SoftwareSources.CleanupDownload(file, folder, null);
            Check(!Directory.Exists(folder), "Repeated cleanup is harmless");

            Directory.CreateDirectory(folder);
            File.WriteAllText(file, "locked fixture");
            var original = new InvalidDataException("fixture SHA-256 mismatch");
            using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Exception observed = null;
                try { try { throw original; } finally { SoftwareSources.CleanupDownload(file, folder, messages.Add); } }
                catch (Exception ex) { observed = ex; }
                Check(Object.ReferenceEquals(observed, original), "Locked file cannot replace original validation failure");
                Check(File.Exists(file) && Directory.Exists(folder), "Locked file retained for later cleanup");
                Check(messages.Count == 2, "Both file and directory cleanup failures reported separately");
                observed = null;
                try { try { throw original; } finally { SoftwareSources.CleanupDownload(file, folder, text => { throw new InvalidOperationException("disposed logger"); }); } }
                catch (Exception ex) { observed = ex; }
                Check(Object.ReferenceEquals(observed, original), "Broken logger cannot hide original failure");
                SoftwareSources.CleanupDownload(file, folder, null);
                count++;
            }
            SoftwareSources.CleanupDownload(file, folder, messages.Add);
            Check(!Directory.Exists(folder), "Retry succeeds after file unlock");

            Directory.CreateDirectory(folder);
            string unrelated = Path.Combine(folder, "keep.txt");
            File.WriteAllText(unrelated, "must survive");
            SoftwareSources.CleanupDownload(file, folder, messages.Add);
            Check(File.ReadAllText(unrelated) == "must survive", "Unknown files are never recursively removed");
            File.Delete(unrelated);
            Directory.Delete(folder);
        }

        public static int Main()
        {
            string directory = Path.Combine(Path.GetTempPath(), "bars-review-regression-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                CheckOutcomes(directory);
                CheckCleanup(directory);
                Console.WriteLine("PASS: " + count + " review regression checks. No installers, system settings or remote clients executed.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally
            {
                // Only this test's private fixture directory is removed.
                Directory.Delete(directory, true);
            }
        }
    }
}
