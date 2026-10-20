using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ServiceTool
{
    internal static class UtilityModuleTests
    {
        private static int count;
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        private static void Reject(Action action, string message) { try { action(); } catch (IOException) { count++; return; } catch (InvalidDataException) { count++; return; } throw new Exception(message); }
        private static IEnumerable<Control> Children(Control parent)
        { foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Children(child)) yield return nested; } }
        private static void Show(Form form)
        { form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.Show(); Application.DoEvents(); }
        [STAThread] public static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--token")
            {
                using (var id = WindowsIdentity.GetCurrent())
                { foreach (string p in new[] { "SeShutdownPrivilege", "SeRemoteShutdownPrivilege" }) if (HasPrivilege(id.Token, p)) return 3; }
                return 0;
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string temp = Path.Combine(Path.GetTempPath(), "bars-utilities-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
            try
            {
                Check(WindowsComponents.Profiles.Length == 3 && WindowsComponents.Profiles[0].Features.Length == 27, "Three logical rows and exact 27-feature IIS profile");
                Check(WindowsComponents.Resolve("iis,tftp").Contains("IIS-LegacyScripts") && !WindowsComponents.Resolve("iis").Contains("IIS-ASPNET45"), "IIS management compatibility preserved without unrelated ASP.NET");
                Reject(() => WindowsComponents.Resolve("iis;Start-Process evil"), "Reject command injection");
                Reject(() => WindowsComponents.Resolve("iis,iis"), "Reject duplicate selection");
                Reject(() => WindowsComponents.Resolve(""), "Reject empty feature installation");
                Check(WindowsComponents.EnableScript("iis").Contains("-All -NoRestart") && !WindowsComponents.QueryScript().Contains("Enable-"), "Query read-only and servicing prohibits reboot");
                var profile = WindowsComponents.Profiles[0]; var states = profile.Features.ToDictionary(f => f, f => "Enabled");
                Check(WindowsComponents.Status(profile, states) == "Установлено", "All selected features enabled");
                states[profile.Features[0]] = "EnablePending"; Check(WindowsComponents.Status(profile, states) == "Ожидает перезагрузки", "Pending isn't installed");
                states[profile.Features[0]] = "Disabled"; Check(WindowsComponents.Status(profile, states).StartsWith("Частично: 26"), "Partial IIS described");
                states.Remove(profile.Features[0]); Check(WindowsComponents.Status(profile, states).StartsWith("Недоступно"), "Missing OS feature isn't falsely installed");
                states = WindowsComponents.Parse("TelnetClient=Enabled\r\nTFTP=Disabled\nother text\n");
                Check(states.Count == 2 && WindowsComponents.Status(WindowsComponents.Profiles[2], states) == "Не установлено", "State parser independent of localized output");
                Check(!ManagerSystemActions.Known("cmd") && ManagerSystemActions.Known("features-query"), "Worker fixed action whitelist");
                string source = Path.Combine(temp, "source", "_tools"), target = Path.Combine(temp, "destination", "_tools"); Directory.CreateDirectory(source);
                string[] folders = { "fiLinn_V0.3", "Piglet_V0.12", "WinSCP v6.6.2" }, exes = { "cspPanel.exe", "DK-QStart.exe", "WinSCP.exe" };
                for (int i = 0; i < folders.Length; i++)
                {
                    string directory = Path.Combine(source, folders[i]); Directory.CreateDirectory(directory);
                    // Inert files; never executed. The actual Windows .lnk COM object can still reference them.
                    File.WriteAllText(Path.Combine(directory, exes[i]), "fixture " + i); File.WriteAllText(Path.Combine(directory, "settings.ini"), "default");
                    Directory.CreateDirectory(Path.Combine(directory, "nested")); File.WriteAllText(Path.Combine(directory, "nested", "keep.txt"), "nested");
                }
                File.WriteAllText(Path.Combine(source, folders[0], "tftpd32.exe"), "inert tftp fixture");
                Check(ToolsDeployment.Discover(source).Count == 3, "Discover three correct launchers");
                Reject(() => ToolsDeployment.Target(@"C:\DataKrat"), "Target must be a drive root");
                Reject(() => ToolsDeployment.Target(@"\\server\share\"), "UNC deployment rejected");
                ToolsDeployment.CopyToTarget(source, target, ignored => { }, () => false);
                for (int i = 0; i < folders.Length; i++)
                {
                    Check(File.ReadAllText(Path.Combine(target, folders[i], exes[i])) == "fixture " + i, "Executable copied intact");
                    string ini = Path.Combine(target, folders[i], "desktop.ini");
                    Check(File.ReadAllText(ini).Contains(Path.Combine(target, folders[i], exes[i]) + ",0"), "Folder icon retargeted");
                    Check((File.GetAttributes(ini) & (FileAttributes.Hidden | FileAttributes.System)) == (FileAttributes.Hidden | FileAttributes.System), "desktop.ini shell attributes");
                    Check(File.ReadAllText(Path.Combine(target, folders[i], "nested", "keep.txt")) == "nested", "Nested content preserved");
                }
                File.WriteAllText(Path.Combine(target, folders[0], "settings.ini"), "user config");
                File.WriteAllText(Path.Combine(target, "unrelated.txt"), "keep");
                ToolsDeployment.CopyToTarget(source, target, ignored => { }, () => false);
                Check(File.ReadAllText(Path.Combine(target, folders[0], "settings.ini")) == "user config", "Existing settings not overwritten");
                Check(File.Exists(Path.Combine(target, "unrelated.txt")) && Directory.GetDirectories(Path.GetDirectoryName(target), "_tools-backup-*").Length == 1, "Old tree backup and unrelated content retained");
                int pulses = 0; bool cancelled = false;
                try { ToolsDeployment.CopyToTarget(source, target, ignored => pulses++, () => pulses >= 2); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && File.ReadAllText(Path.Combine(target, folders[0], "settings.ini")) == "user config", "Cancellation leaves published target intact");
                Check(Directory.GetDirectories(Path.GetDirectoryName(target), ".bars-tools-*").Length == 0, "Cancelled staging removed");
                string desktop = Path.Combine(temp, "desktop"); ToolsDeployment.ShortcutsAt(source, target, desktop);
                Check(Directory.GetFiles(desktop, "*.lnk").Length == 4, "Exactly four desktop shortcuts");
                ToolsDeployment.ShortcutsAt(source, target, desktop); Check(Directory.GetFiles(desktop, "*.lnk").Length == 4, "Repeated deployment updates own shortcuts without duplicates");
                object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                try
                {
                    foreach (var tool in ToolsDeployment.Launchers(source))
                    {
                        object link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { Path.Combine(desktop, tool.Name + ".lnk") });
                        try
                        {
                            string exe = Path.Combine(target, tool.Directory, tool.Executable);
                            Check(Convert.ToString(link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null)).Equals(exe, StringComparison.OrdinalIgnoreCase), "Shortcut targets actual launcher");
                            Check(Convert.ToString(link.GetType().InvokeMember("IconLocation", BindingFlags.GetProperty, null, link, null)).Replace(", ", ",").Equals(exe + ",0", StringComparison.OrdinalIgnoreCase), "Shortcut embeds correct EXE icon reference");
                        }
                        finally { Marshal.FinalReleaseComObject(link); }
                    }
                }
                finally { Marshal.FinalReleaseComObject(shell); }
                Check(ToolsDeployment.Launchers(source).Select(t => t.Name).OrderBy(n => n).SequenceEqual(new[] { "fiLinn", "Piglet", "tftpd32", "WinSCP" }.OrderBy(n => n)), "Exact four shortcut names requested by user");
                Check(ToolsDeployment.AdministratorFlags("~ WIN7RTM HIGHDPIAWARE").Contains("WIN7RTM HIGHDPIAWARE RUNASADMIN"), "Admin property preserves other compatibility options");
                Check(!ToolsDeployment.AdministratorFlags("~ RUNASINVOKER").Contains("RUNASINVOKER"), "Conflicting run level replaced");
                string registryFixture = @"Software\BARS\Tests\" + Guid.NewGuid().ToString("N");
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(registryFixture))
                    {
                        var executables = ToolsDeployment.Launchers(source).Select(t => Path.Combine(target, t.Directory, t.Executable)).ToArray();
                        ToolsDeployment.AdministratorSettings(key, executables);
                        foreach (string executable in executables) Check(Convert.ToString(key.GetValue(executable)) == "~ RUNASADMIN", "Administrator property for each copied EXE");
                        ToolsDeployment.AdministratorSettings(key, executables);
                        Check(key.GetValueNames().Length == 4, "Repeated admin settings are idempotent");
                    }
                }
                finally { Registry.CurrentUser.DeleteSubKeyTree(registryFixture); }
                string queue = Path.Combine(temp, "queue.xml");
                PausedLibraryQueue.SaveAt(queue, new[] { "one", "two", "one" }, LibraryOperation.Reinstall);
                Check(PausedLibraryQueue.LoadAt(queue).SetEquals(new[] { "one", "two" }), "Paused selection persisted without duplicate IDs");
                PausedLibraryQueue.SaveAt(queue, new string[0], LibraryOperation.Install);
                Check(PausedLibraryQueue.LoadAt(queue).Count == 0, "New confirmed queue clears stale saved selection atomically");
                bool beforeShutdown, beforeRemote;
                using (var id = WindowsIdentity.GetCurrent()) { beforeShutdown = HasPrivilege(id.Token, "SeShutdownPrivilege"); beforeRemote = HasPrivilege(id.Token, "SeRemoteShutdownPrivilege"); }
                int exit = LibraryProcess.Run(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--token"), ignored => { });
                Check(exit == 0, "Inert child has no shutdown privileges");
                using (var id = WindowsIdentity.GetCurrent()) Check(beforeShutdown == HasPrivilege(id.Token, "SeShutdownPrivilege") && beforeRemote == HasPrivilege(id.Token, "SeRemoteShutdownPrivilege"), "Parent privileges untouched");
                foreach (bool dark in new[] { false, true })
                foreach (float scale in new[] { 1F, 1.25F, 1.5F, 2F })
                foreach (ManagerModuleForm form in new ManagerModuleForm[] { new ManagerHub(), new WindowsFeaturesForm(true), new ToolsDeploymentForm(true) })
                using (form)
                {
                    form.SetTheme(dark, false); Show(form); if (scale != 1) { form.Scale(new SizeF(scale, scale)); Application.DoEvents(); }
                    var buttons = Children(form).OfType<BrandButton>().ToArray();
                    foreach (var button in buttons)
                    {
                        Check(button.Height >= 35 * scale, "Useful button height at each scale");
                        Check(button.Parent.ClientRectangle.Contains(button.Bounds), "Button stays inside parent at each scale");
                        foreach (string line in button.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) Check(TextRenderer.MeasureText(line, button.Font).Width <= button.Width - 8, "Each action line fits horizontally");
                    }
                    if (form is ManagerHub)
                    {
                        var cards = Children(form).OfType<Card>().ToArray(); Check(cards.Length == 4 && cards.Max(c => c.Width) - cards.Min(c => c.Width) <= 1 && cards.Max(c => c.Height) - cards.Min(c => c.Height) <= 1, "Four equally sized launcher cards");
                        var actions = cards.Select(c => Children(c).OfType<BrandButton>().Single()).ToArray();
                        Check(actions.Select(b => b.Size).Distinct().Count() == 1, "All launch actions equal size");
                    }
                    else
                    {
                        var back = buttons.Single(b => b.Text == "В меню"); var about = buttons.Single(b => b.Text == "О программе");
                        Check(back.PointToScreen(Point.Empty).Y == about.PointToScreen(Point.Empty).Y, "New module footer baseline consistent");
                        Check(form.Icon != null, "Brand icon on new module");
                    }
                    if (form is ToolsDeploymentForm)
                    {
                        var combo = Children(form).OfType<BrandComboBox>().Single();
                        Check(combo.DrawMode == DrawMode.OwnerDrawFixed, "Drive picker uses themed text/background instead of native white field");
                        form.ActiveControl = buttons.First();
                        using (var comboImage = new Bitmap(combo.Width, combo.Height))
                        {
                            combo.DrawToBitmap(comboImage, combo.ClientRectangle);
                            Check(comboImage.GetPixel(combo.Width / 2, combo.Height / 2).ToArgb() == ThemePalette.Create(dark).SurfaceAlt.ToArgb(), "Drive picker background matches light/dark palette");
                        }
                    }
                    using (var image = new Bitmap(form.Width, form.Height)) form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    form.Close();
                }
                Console.WriteLine("PASS: " + count + " new module, staging, shortcut, token and scale checks. No feature installation, vendor installer or restart."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally
            {
                // The test directory is uniquely created here; no deployed/user directories are removed.
                foreach (string file in Directory.GetFiles(temp, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                foreach (string directory in Directory.GetDirectories(temp, "*", SearchOption.AllDirectories)) File.SetAttributes(directory, FileAttributes.Directory);
                Directory.Delete(temp, true);
            }
        }
        [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string system, string name, out Luid id);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int type, IntPtr buffer, int length, out int required);
        private static bool HasPrivilege(IntPtr token, string name)
        {
            Luid id; if (!LookupPrivilegeValue(null, name, out id)) throw new Exception("LookupPrivilegeValue failed");
            int required; GetTokenInformation(token, 3, IntPtr.Zero, 0, out required); IntPtr buffer = Marshal.AllocHGlobal(required);
            try { if (!GetTokenInformation(token, 3, buffer, required, out required)) throw new Exception("GetTokenInformation failed"); for (int i = 0; i < Marshal.ReadInt32(buffer); i++) if (unchecked((uint)Marshal.ReadInt32(buffer, 4 + i * 12)) == id.Low && Marshal.ReadInt32(buffer, 8 + i * 12) == id.High) return true; return false; }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
}
