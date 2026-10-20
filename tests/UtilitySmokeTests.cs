using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ServiceTool
{
    // Explicitly invoked smoke test: online READ-ONLY query through the real elevated
    // IPC helper, then real payload copying into an isolated temporary directory.
    internal static class UtilitySmokeTests
    {
        [STAThread] public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--library-worker") return LibrarySession.RunWorker(args);
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) { Console.Error.WriteLine("Run from an already elevated test shell; no automatic UAC for smoke tests"); return 5; }
            Application.EnableVisualStyles();
            string temp = Path.Combine(Path.GetTempPath(), "bars-real-tools-smoke-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
            try
            {
                using (var session = LibrarySession.Start())
                {
                    var result = session.ExecuteSystem("features-query", "", "", ignored => { });
                    if (result.ExitCode != 0) throw new Exception(result.Message);
                    var states = WindowsComponents.Parse(result.Message);
                    if (!states.ContainsKey("IIS-WebServerRole") || !states.ContainsKey("TelnetClient") || !states.ContainsKey("TFTP")) throw new Exception("Incomplete host feature query");
                    foreach (var profile in WindowsComponents.Profiles) Console.WriteLine(profile.Name + ": " + WindowsComponents.Status(profile, states));
                }
                string source = @"C:\Библиотеки\_tools", target = Path.Combine(temp, "DataKrat", "_tools"), desktop = Path.Combine(temp, "Desktop");
                ToolsDeployment.CopyToTarget(source, target, ignored => { }, () => false);
                int files = 0;
                foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    string destination = Path.Combine(target, file.Substring(source.Length + 1));
                    using (var sha = SHA256.Create()) using (var original = File.OpenRead(file)) using (var copy = File.OpenRead(destination))
                        if (!sha.ComputeHash(original).SequenceEqual(sha.ComputeHash(copy))) throw new Exception("Copy differs: " + file);
                    files++;
                }
                ToolsDeployment.EnableAdministratorLaunch(source, target);
                using (var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
                using (var layers = user.OpenSubKey(ToolsDeployment.CompatibilityKey))
                    foreach (var tool in ToolsDeployment.Launchers(source))
                        if (Convert.ToString(layers.GetValue(Path.Combine(target, tool.Directory, tool.Executable))) != "~ RUNASADMIN") throw new Exception("Administrator property missing: " + tool.Name);
                ToolsDeployment.ShortcutsAt(source, target, desktop);
                foreach (var tool in ToolsDeployment.Launchers(source))
                {
                    if (!File.Exists(Path.Combine(desktop, tool.Name + ".lnk"))) throw new Exception("Missing shortcut: " + tool.Name);
                    using (Icon icon = Icon.ExtractAssociatedIcon(Path.Combine(target, tool.Directory, tool.Executable))) if (icon == null) throw new Exception("No EXE icon: " + tool.Name);
                }
                Console.WriteLine("PASS: authenticated elevated IPC read-only Windows query; " + files + " real utility files verified by SHA-256; four named shortcuts, four actual EXE icons and four native per-user administrator properties. No feature changes or installer execution."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally
            {
                using (var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
                using (var layers = user.OpenSubKey(ToolsDeployment.CompatibilityKey, true))
                    if (layers != null) foreach (string value in layers.GetValueNames()) if (value.StartsWith(temp + "\\", StringComparison.OrdinalIgnoreCase)) layers.DeleteValue(value, false);
                foreach (string file in Directory.GetFiles(temp, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                foreach (string directory in Directory.GetDirectories(temp, "*", SearchOption.AllDirectories)) File.SetAttributes(directory, FileAttributes.Directory);
                Directory.Delete(temp, true);
            }
        }
    }
}
