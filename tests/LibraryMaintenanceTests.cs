using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class LibraryMaintenanceTests
    {
        private static int count;
        private static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        private static void Reject(Action action, string message) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, message); }
        private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
        private static IEnumerable<Control> Children(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (Control item in Children(child)) yield return item; } }
        private static void Show(Form form) { form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-22000, -22000); form.ShowInTaskbar = false; form.Show(); Application.DoEvents(); }
        [STAThread] public static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--inert-child") return 0;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maintenance-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            string fixture = Path.Combine(directory, "Инертный файл.exe"); File.WriteAllText(fixture, "NOT AN EXECUTABLE. NEVER RUN.");
            try
            {
                if (args.Length == 2 && args[0] == "--inventory")
                {
                    var report = new List<string>();
                    foreach (var package in LibraryCatalog.All())
                    {
                        if (package.Kind == LibraryKind.Composite) continue;
                        try
                        {
                            var products = LibraryMaintenance.Resolve(package);
                            foreach (var product in products)
                            {
                                var planned = LibraryMaintenance.Command(product, Path.Combine(directory, "read-only.log"));
                                report.Add(package.Name + " | " + product.Name + " | " + product.Version + " | " + Path.GetFileName(planned.FileName));
                            }
                        }
                        catch (Exception error) { report.Add(package.Name + " | unavailable | " + error.Message); }
                    }
                    File.WriteAllLines(args[1], report, System.Text.Encoding.UTF8);
                    Console.WriteLine("PASS: read-only uninstall inventory saved. No uninstall/repair/install was launched."); return 0;
                }
                Check(LibraryProcess.Run(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--inert-child"), text => { }) == 0, "Native process starts with default empty WorkingDirectory (MSI uninstall case)");
                string shared = Path.Combine(directory, "shared"), candidate = Path.Combine(shared, "6.0.36"); Directory.CreateDirectory(candidate);
                string dll = Path.Combine(candidate, "coreclr.dll"); File.WriteAllText(dll, "inert file, not a DLL");
                var requested = new LibraryPackage { Kind = LibraryKind.DotNet, RuntimeFamily = "Microsoft.NETCore.App", Version = "6.0.36", Architecture = "x86" };
                var installed = new LibraryRegisteredProduct { Key = "{11111111-1111-1111-1111-111111111112}", Msi = true, Hidden = true, Name = "Microsoft .NET Runtime - 6.0.36 (x86)", Version = "48.144.23141", Architecture = "x86" };
                Check(LibraryDetection.ProbeRuntime(requested, new[] { shared }, new LibraryRegisteredProduct[0], p => false).State == LibraryState.Unknown, "Leftover DLL alone is not an installed runtime");
                Check(LibraryDetection.ProbeRuntime(requested, new[] { shared }, new[] { installed }, p => false).State == LibraryState.Unknown, "Stale registry entry and leftover files not marked installed after uninstall");
                Check(LibraryDetection.ProbeRuntime(requested, new[] { shared }, new[] { installed }, p => true).State == LibraryState.Installed, "Runtime requires matching files and live registration");
                File.Delete(dll);
                Check(LibraryDetection.ProbeRuntime(requested, new[] { shared }, new[] { installed }, p => true).State == LibraryState.Unknown, "Broken runtime with registered MSI and missing files is not marked installed");
                Check(LibraryDetection.ProbeRuntime(requested, new[] { shared }, new LibraryRegisteredProduct[0], p => false).State == LibraryState.Missing, "Removed runtime with no files or registration is missing");
                string newer = Path.Combine(shared, "6.0.37"); Directory.CreateDirectory(newer); string newerDll = Path.Combine(newer, "coreclr.dll"); File.WriteAllText(newerDll, "inert file");
                installed.Name = "Microsoft .NET Runtime - 6.0.37 (x86)";
                LibraryCheck evidence = LibraryDetection.ProbeRuntime(requested, new[] { shared }, new[] { installed }, p => true);
                Check(evidence.State == LibraryState.Installed && evidence.Detail.Contains("6.0.37") && evidence.Detail.Contains(newer), "Remaining newer registered patch shown explicitly after removal of older patch");
                installed.Architecture = "x64";
                Check(LibraryDetection.ProbeRuntime(requested, new[] { shared }, new[] { installed }, p => true).State == LibraryState.Unknown, "Other architecture cannot prove installation");
                File.Delete(newerDll); Directory.Delete(newer); Directory.Delete(candidate); Directory.Delete(shared);
                var preview = new LibraryPackage { Kind = LibraryKind.DotNet, RuntimeFamily = "Microsoft.NETCore.App", Version = "11.0.0-rc.1.26425.128", Architecture = "x86", Preview = true };
                var previewRecord = new LibraryRegisteredProduct { Msi = true, Hidden = true, Name = "Microsoft .NET Runtime - 11.0.0 RC 1 (x86)", Version = "88.0.58512", Architecture = "x86" };
                Check(LibraryMaintenance.Matches(preview, previewRecord), "Human-readable RC 1 maps to canonical rc.1");
                previewRecord.Name = "Microsoft .NET Runtime - 11.0.0 RC 10 (x86)"; Check(!LibraryMaintenance.Matches(preview, previewRecord), "RC 10 is not RC 1");
                previewRecord.Name = "Microsoft .NET Runtime - 11.0.0 Preview 1 (x86)"; Check(!LibraryMaintenance.Matches(preview, previewRecord), "Preview 1 is not RC 1");
                previewRecord.Name = "Microsoft .NET Runtime - 11.0.0-rc.1.26425.129 (x86)"; Check(!LibraryMaintenance.Matches(preview, previewRecord), "Explicit other build excluded");
                previewRecord.Name = "Microsoft .NET Runtime - 11.0.0 RC 1 (x86)";
                var stable11 = new LibraryPackage { Kind = LibraryKind.DotNet, RuntimeFamily = preview.RuntimeFamily, Version = "11.0.0", Architecture = "x86" };
                Check(!LibraryMaintenance.Matches(stable11, previewRecord), "Spaced RC label cannot satisfy stable runtime");
                string previewRoot = Path.Combine(directory, "preview"), previewFolder = Path.Combine(previewRoot, preview.Version); Directory.CreateDirectory(previewFolder);
                string core = Path.Combine(previewFolder, "coreclr.dll"), managed = Path.Combine(previewFolder, "System.Private.CoreLib.dll"); File.WriteAllText(core, "inert"); File.WriteAllText(managed, "inert");
                Check(LibraryDetection.ProbeRuntime(preview, new[] { previewRoot }, new[] { previewRecord }, p => true, p => preview.Version + "+testcommit").State == LibraryState.Installed, "Exact DLL build plus live RC registration confirms preview");
                Check(LibraryDetection.ProbeRuntime(preview, new[] { previewRoot }, new[] { previewRecord }, p => true, p => "11.0.0-rc.1.26425.129+other").State == LibraryState.Unknown, "Different DLL build not accepted from folder name alone");
                Check(LibraryDetection.ProbeRuntime(preview, new[] { previewRoot }, new[] { previewRecord }, p => false, p => preview.Version).State == LibraryState.Unknown, "Preview leftovers without live registration remain unknown");
                File.Delete(managed);
                Check(LibraryDetection.ProbeRuntime(preview, new[] { previewRoot }, new[] { previewRecord }, p => true, p => preview.Version).State == LibraryState.Unknown, "Missing managed runtime DLL cannot confirm preview");
                File.Delete(core); Directory.Delete(previewFolder); Directory.Delete(previewRoot);
                preview.RuntimeFamily = "Microsoft.AspNetCore.App"; previewRecord.Name = "Microsoft ASP.NET Core Runtime - 11.0.0 RC 1 (x86)";
                Check(LibraryMaintenance.Matches(preview, previewRecord), "ASP.NET Core RC record matched separately");
                previewRecord.Architecture = "x64"; Check(!LibraryMaintenance.Matches(preview, previewRecord), "RC architecture remains exact");
                var msi = new LibraryRegisteredProduct { Key = "{11111111-1111-1111-1111-111111111111}", Msi = true };
                ProcessStartInfo command = LibraryMaintenance.Command(msi, "log with spaces.log");
                Check(command.FileName.EndsWith("msiexec.exe") && command.Arguments.StartsWith("/x ") && command.Arguments.Contains("/qn /norestart"), "MSI uses removal, not install/repair");
                var burn = new LibraryRegisteredProduct { Key = msi.Key, Name = "Microsoft .NET Runtime", QuietUninstall = LibraryPlan.Quote(fixture) + " /uninstall /quiet /norestart" };
                command = LibraryMaintenance.Command(burn, "x.log");
                Check(command.FileName == fixture && command.Arguments.Contains("/uninstall /quiet"), "Burn GUID is not mistaken for an MSI ProductCode");
                burn.QuietUninstall = ""; burn.Uninstall = LibraryPlan.Quote(fixture) + " /uninstall";
                Check(LibraryMaintenance.Command(burn, "x.log").Arguments == "/uninstall", "Vendor registered arguments preserved");
                burn.Key = "DevExpress_is1";
                Check(LibraryMaintenance.Command(burn, "x.log").Arguments.Contains("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"), "Inno uninstaller gets supported quiet options");
                burn.Uninstall = "\"missing.exe"; Reject(() => LibraryMaintenance.Command(burn, "x.log"), "Unclosed quote rejected");
                burn.Uninstall = "relative.exe /uninstall"; Reject(() => LibraryMaintenance.Command(burn, "x.log"), "Relative deinstaller rejected");
                burn.Uninstall = LibraryPlan.Quote(Path.Combine(Environment.SystemDirectory, "cmd.exe")) + " /c echo BAD"; Reject(() => LibraryMaintenance.Command(burn, "x.log"), "Shell deinstaller requires a specific rule");
                burn.Uninstall = LibraryPlan.Quote(Path.Combine(directory, "missing.exe")); Reject(() => LibraryMaintenance.Command(burn, "x.log"), "Missing deinstaller rejected before execution");
                var runtime = LibraryCatalog.All().First(p => p.RuntimeFamily == "Microsoft.NETCore.App" && p.Version.StartsWith("6.") && p.Architecture == "x64");
                var record = new LibraryRegisteredProduct { Name = "Microsoft .NET Runtime - 6.0.36 (x64)", Version = "6.0.36.1234", Architecture = "x64" };
                Check(LibraryMaintenance.Matches(runtime, record), "Correct .NET runtime family/version/architecture matched");
                LibraryMaintenance.ValidateReinstall(runtime, new[] { record }); Check(true, "Same .NET patch may be reinstalled despite ARP build suffix");
                record.Name = "Microsoft .NET Runtime - 6.0.37 (x64)"; Reject(() => LibraryMaintenance.ValidateReinstall(runtime, new[] { record }), "Reinstall refuses implicit downgrade");
                record.Name = "Microsoft .NET Runtime - 6.0.36 (x64)"; record.Hidden = record.Msi = true; Check(LibraryMaintenance.Matches(runtime, record), "Standalone runtime MSI matched even when hidden in Programs and Features"); record.Hidden = record.Msi = false;
                record.Name = "Microsoft Windows Desktop Runtime - 6.0.36 (x64)"; Check(!LibraryMaintenance.Matches(runtime, record), "Desktop runtime excluded from base runtime removal");
                record.Name = "Microsoft ASP.NET Core 6.0.36 Shared Framework (x64)"; Check(!LibraryMaintenance.Matches(runtime, record), "ASP runtime excluded from base runtime removal");
                var asp = LibraryCatalog.All().First(p => p.RuntimeFamily == "Microsoft.AspNetCore.App" && p.Version.StartsWith("6.") && p.Architecture == "x64");
                Check(LibraryMaintenance.Matches(asp, record), "ASP runtime matched separately");
                record.Architecture = "x86"; Check(!LibraryMaintenance.Matches(asp, record), "Other architecture excluded"); record.Architecture = "x64";
                record.Name = "Microsoft ASP.NET Core 8.0.31 Shared Framework (x64)"; Check(!LibraryMaintenance.Matches(asp, record), "Other runtime generation excluded");
                var framework = LibraryCatalog.All().First(p => p.Kind == LibraryKind.Framework);
                record.Name = "Microsoft .NET Framework 4.8.1 SDK"; Check(LibraryMaintenance.Matches(framework, record), "Framework SDK selectable for removal");
                record.Name = "Microsoft .NET Framework 4.8.1"; Check(!LibraryMaintenance.Matches(framework, record), "Integrated Framework runtime excluded from Developer Pack removal");
                record.Name = "Microsoft .NET Framework 4.8 SDK"; Check(!LibraryMaintenance.Matches(framework, record), "Other SDK generation excluded");
                var vc = LibraryCatalog.All().First(p => p.Kind == LibraryKind.VcLegacy);
                record.Name = "Microsoft Visual C++ 2013 Redistributable (x64)"; record.Version = "12.0.40664.0";
                Check(LibraryMaintenance.Matches(vc, record), "VC registered redistributable matched");
                record.Hidden = true; Check(!LibraryMaintenance.Matches(vc, record), "Hidden VC subpackages excluded to avoid duplicate removal"); record.Hidden = false;
                record.Name = "Unrelated Tool"; Check(!LibraryMaintenance.Matches(vc, record), "Unrelated product excluded");
                var composite = new LibraryPackage { Kind = LibraryKind.Composite, Name = "RuntimePack Full" };
                Check(!LibraryMaintenance.Matches(composite, record) && !composite.Interactive && !composite.Recommended, "Excluded Full has no removal rule");
                Reject(() => LibraryPlan.Command(composite, fixture, "x.log"), "Excluded RuntimePack Full cannot build a launch command");
                foreach (bool dark in new[] { false, true })
                {
                    using (var form = new LibraryManagerForm(true))
                    {
                        form.SetTheme(dark, false); Show(form);
                        var grid = Field<ServiceGrid>(form, "grid"); var install = Field<BrandButton>(form, "install"); var refresh = Field<BrandButton>(form, "refresh"); var actions = (TableLayoutPanel)install.Parent;
                        Check(actions.GetColumn(install) == 0 && actions.GetColumn(refresh) == 3, "Install and check buttons swapped");
                        Check(install.Height >= 52, "Action button can fit maintenance caption on two lines");
                        var checks = Field<Dictionary<string, LibraryCheck>>(form, "checks");
                        int installedRow = grid.Rows.Cast<DataGridViewRow>().First(row => checks[((LibraryPackage)row.Tag).Id].State == LibraryState.Installed).Index;
                        grid.Focus(); grid.CurrentCell = grid.Rows[installedRow].Cells[1]; Application.DoEvents();
                        Check(install.Text == "Переустановить/удалить выбранные", "Installed focused row changes primary action");
                        var targets = (List<LibraryCheck>)typeof(LibraryManagerForm).GetMethod("ActionTargets", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                        Check(targets.Count == 1 && targets[0].State == LibraryState.Installed, "Focused row overrides previously checked missing components");
                        int packRow = grid.Rows.Cast<DataGridViewRow>().First(row => checks[((LibraryPackage)row.Tag).Id].State == LibraryState.Missing).Index;
                        grid.CurrentCell = grid.Rows[packRow].Cells[1]; Application.DoEvents();
                        Check(install.Text == "Установить выбранные", "Missing focused row offers install");
                        var selected = new[] { checks.Values.First(c => c.State == LibraryState.Installed) };
                        using (var dialog = new LibraryActionDialog(ThemePalette.Create(dark), form.Icon, selected))
                        {
                            Show(dialog);
                            if (args.Length == 1) { Directory.CreateDirectory(args[0]); using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(args[0], "maintenance-" + (dark ? "dark" : "light") + ".png")); } }
                            var remove = Children(dialog).OfType<BrandButton>().First(b => b.Text == "Удалить"); remove.PerformClick();
                            Check(dialog.Operation == LibraryOperation.Remove && dialog.DialogResult == DialogResult.OK, "Explicit remove choice selects removal; test never starts worker"); dialog.Close();
                        }
                        using (var dialog = new LibraryActionDialog(ThemePalette.Create(dark), form.Icon, selected))
                        { Show(dialog); Children(dialog).OfType<BrandButton>().First(b => b.Text == "Переустановить").PerformClick(); Check(dialog.Operation == LibraryOperation.Reinstall, "Reinstall choice separate from removal"); dialog.Close(); }
                        form.Close();
                    }
                    using (var hub = new ManagerHub()) { hub.SetTheme(dark, false); Show(hub); Check(hub.ClientSize.Width <= 840 && hub.ClientSize.Height <= 560, "Launcher compact at baseline DPI"); var headings = Children(hub).OfType<FlushLabel>().Where(c => c.Text == "Менеджер служб" || c.Text == "Менеджер компонентов").ToArray(); Check(headings.Length == 2 && headings[0].PointToScreen(Point.Empty).X == headings[1].PointToScreen(Point.Empty).X, "Launcher headings share exact left coordinate without native label padding"); hub.Close(); }
                }
                Console.WriteLine("PASS: " + count + " maintenance command/matching/UI checks. No deinstaller, UAC, or installer started."); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { File.Delete(fixture); Directory.Delete(directory, false); }
        }
    }
}
