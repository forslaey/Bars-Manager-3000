using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace ServiceTool
{
    // One private directory per manager process. Other running managers retain their leases.
    internal static class EmbeddedHelpers
    {
        private static readonly object Gate = new object();
        internal static readonly string Root = Path.Combine(Path.GetTempPath(), "Bars Service Manager 3000");
        private static string session;
        private static FileStream owner;
        // Installing a user-selected assembly already executes its installer with full trust.
        // Scope web-origin assembly loading to this private helper, without removing file zone marks.
        private const string Configuration = "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><startup useLegacyV2RuntimeActivationPolicy=\"true\"><supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\"/></startup><runtime><loadFromRemoteSources enabled=\"true\"/></runtime></configuration>";

        internal static string Prepare(string architecture)
        {
            if (architecture != "x86" && architecture != "x64") throw new ArgumentException("Неизвестная разрядность установщика.");
            if (architecture == "x64" && !Environment.Is64BitOperatingSystem)
                throw new InvalidOperationException("Для этой службы нужна 64-разрядная Windows.");
            lock (Gate)
            {
                if (session == null)
                {
                    CleanupStale();
                    Directory.CreateDirectory(Root);
                    RejectLink(Root);
                    string directory = Path.Combine(Root, "session-" + Guid.NewGuid().ToString("N"));
                    var security = new DirectorySecurity();
                    security.SetAccessRuleProtection(true, false);
                    SecurityIdentifier user = WindowsIdentity.GetCurrent().User;
                    security.SetOwner(user);
                    foreach (SecurityIdentifier sid in new SecurityIdentifier[] { user,
                        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    Directory.CreateDirectory(directory, security);
                    owner = new FileStream(Path.Combine(directory, "owner.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                    session = directory;
                }
                RejectLink(session);
                string file = Path.Combine(session, "Installer." + architecture + ".exe");
                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("BARS.Helper." + architecture))
                {
                    if (resource == null) throw new InvalidOperationException("Встроенный установщик " + architecture + " отсутствует. Пересобери Bars Service Manager 3000.");
                    byte[] bytes;
                    using (var memory = new MemoryStream()) { resource.CopyTo(memory); bytes = memory.ToArray(); }
                    EnsureBytes(file, bytes);
                }
                EnsureBytes(file + ".config", new System.Text.UTF8Encoding(false).GetBytes(Configuration));
                return file;
            }
        }

        // Hold both files against modification/deletion until the helper process finishes.
        internal sealed class Lease : IDisposable
        {
            private FileStream executable, configuration;
            internal Lease(string file)
            {
                try
                {
                    executable = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    configuration = new FileStream(file + ".config", FileMode.Open, FileAccess.Read, FileShare.Read);
                    VerifyResource(executable, file.EndsWith("x86.exe", StringComparison.Ordinal) ? "x86" : "x64");
                    byte[] expected = new System.Text.UTF8Encoding(false).GetBytes(Configuration);
                    using (var hash = SHA256.Create())
                        if (!Equal(hash.ComputeHash(configuration), hash.ComputeHash(expected)))
                            throw new SecurityException("Не удалось проверить конфигурацию установщика.");
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                if (configuration != null) { configuration.Dispose(); configuration = null; }
                if (executable != null) { executable.Dispose(); executable = null; }
            }
        }

        private static void VerifyResource(Stream file, string architecture)
        {
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("BARS.Helper." + architecture))
            using (var hash = SHA256.Create())
                if (resource == null || !Equal(hash.ComputeHash(file), hash.ComputeHash(resource)))
                    throw new SecurityException("Не удалось проверить целостность встроенного установщика.");
        }

        private static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int difference = 0;
            for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }

        private static void EnsureBytes(string file, byte[] bytes)
        {
            if (File.Exists(file))
            {
                RejectLink(file);
                using (var hash = SHA256.Create())
                using (var existing = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    if (Equal(hash.ComputeHash(existing), hash.ComputeHash(bytes))) return;
            }
            using (var output = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
            { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
        }

        internal static void Cleanup()
        {
            lock (Gate)
            {
                if (owner != null) { owner.Dispose(); owner = null; }
                if (session != null) { TryRemoveSession(session); session = null; }
                TryRemoveRoot();
            }
        }

        internal static IDisposable HoldHelperSession(string helper)
        {
            RejectLink(Root);
            string directory = Path.GetDirectoryName(Path.GetFullPath(helper));
            string name = Path.GetFileName(directory);
            Guid id;
            if (!String.Equals(Path.GetDirectoryName(directory), Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase) ||
                !name.StartsWith("session-", StringComparison.Ordinal) || !Guid.TryParseExact(name.Substring(8), "N", out id) ||
                !(Path.GetFileName(helper) == "Installer.x86.exe" || Path.GetFileName(helper) == "Installer.x64.exe"))
                throw new SecurityException("Некорректная папка временного установщика.");
            RejectLink(directory);
            string marker = Path.Combine(directory, "owner.lock");
            RejectLink(marker);
            return new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        // If the user closes the manager before an installer exits, a headless instance of
        // the same distributed EXE removes its files after the helper releases its lease.
        internal static void CleanupAfterExit(string managerExe)
        {
            lock (Gate)
            {
                string leftover = session;
                Cleanup();
                if (leftover == null || !Directory.Exists(leftover)) return;
                try
                {
                    using (Process process = Process.Start(new ProcessStartInfo(managerExe,
                        "--cleanup-helpers " + Path.GetFileName(leftover)) { UseShellExecute = false,
                        CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })) { }
                }
                catch (System.ComponentModel.Win32Exception) { } // Startup cleanup remains available.
                catch (IOException) { }
                catch (SecurityException) { }
            }
        }

        internal static int RunCleanupWatcher(string sessionName)
        {
            Guid id;
            if (sessionName == null || !sessionName.StartsWith("session-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(sessionName.Substring(8), "N", out id)) return 87;
            string directory = Path.Combine(Root, sessionName);
            var clock = Stopwatch.StartNew();
            while (Directory.Exists(directory) && clock.Elapsed < TimeSpan.FromMinutes(30))
            {
                TryRemoveSession(directory);
                if (Directory.Exists(directory)) System.Threading.Thread.Sleep(500);
            }
            TryRemoveRoot();
            return Directory.Exists(directory) ? 1460 : 0;
        }

        internal static void CleanupStale()
        {
            try
            {
                if (!Directory.Exists(Root)) return;
                RejectLink(Root);
                foreach (string directory in Directory.GetDirectories(Root, "session-*", SearchOption.TopDirectoryOnly))
                    TryRemoveSession(directory);
                TryRemoveRoot();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }

        private static void TryRemoveSession(string directory)
        {
            try
            {
                RejectLink(Root);
                string full = Path.GetFullPath(directory);
                Guid id;
                string name = Path.GetFileName(full);
                if (!String.Equals(Path.GetDirectoryName(full), Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase) ||
                    !name.StartsWith("session-", StringComparison.Ordinal) || !Guid.TryParseExact(name.Substring(8), "N", out id)) return;
                RejectLink(full);
                string marker = Path.Combine(full, "owner.lock");
                if (Directory.GetDirectories(full).Length != 0) return;
                var allowed = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "owner.lock", "Installer.x86.exe", "Installer.x86.exe.config", "Installer.x64.exe", "Installer.x64.exe.config" };
                foreach (string file in Directory.GetFiles(full)) if (!allowed.Contains(Path.GetFileName(file))) return;
                if (File.Exists(marker)) RejectLink(marker);
                else if (DateTime.UtcNow - Directory.GetCreationTimeUtc(full) < TimeSpan.FromMinutes(1)) return;
                // Opening exclusively fails for a live owner, including another manager instance.
                using (var claim = new FileStream(marker, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    foreach (string file in new string[] { "Installer.x86.exe", "Installer.x86.exe.config", "Installer.x64.exe", "Installer.x64.exe.config" })
                    {
                        string path = Path.Combine(full, file);
                        if (File.Exists(path)) { RejectLink(path); File.Delete(path); }
                    }
                }
                File.Delete(marker);
                Directory.Delete(full, false); // Never recurse into unknown files or directories.
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }

        private static void TryRemoveRoot()
        {
            try { if (Directory.Exists(Root)) { RejectLink(Root); Directory.Delete(Root, false); } }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }

        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new SecurityException("Временная папка установщика содержит ссылку. Выбери другую временную папку Windows.");
        }
    }
}
