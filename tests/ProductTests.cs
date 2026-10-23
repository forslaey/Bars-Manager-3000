using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    // Injected SCM data. Helpers are launched only with invalid input (exit 87, no UAC/SCM).
    internal static class ProductTests
    {
        private static int checks, result = 1;
        private static string root;
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--probe-load")
            {
                try { Assembly.LoadFrom(args[1]); return 0; }
                catch (FileLoadException ex) { return ex.HResult == unchecked((int)0x80131515) ? 42 : 43; }
                catch { return 44; }
            }
            root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "product-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                VerifyMigration();
                VerifyHelpers();
                using (var host = new Form { ShowInTaskbar = false, Location = new Point(-30000, -30000), StartPosition = FormStartPosition.Manual, Size = new Size(1, 1) })
                {
                    host.Shown += async delegate
                    {
                        try { await VerifyDiscoveryAndTimer(); Console.WriteLine("PASS: " + checks + " product checks: embedded helpers, cleanup, migration, discovery, refresh and layout."); result = 0; }
                        catch (Exception ex) { Console.Error.WriteLine(ex); }
                        finally { host.Close(); }
                    };
                    Application.Run(host);
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally
            {
                EmbeddedHelpers.Cleanup();
                string allowed = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (Path.GetFullPath(root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("product-")) Directory.Delete(root, true);
            }
            return result;
        }

        private static void VerifyMigration()
        {
            string old = Path.Combine(root, "old"), current = Path.Combine(root, "current");
            Directory.CreateDirectory(old); Directory.CreateDirectory(current);
            File.WriteAllText(Path.Combine(old, "services.xml"), "catalog");
            File.WriteAllText(Path.Combine(old, "install-profiles.xml"), "ciphertext-preserved");
            File.WriteAllText(Path.Combine(old, "theme.txt"), "dark");
            File.WriteAllText(Path.Combine(old, "unrelated.txt"), "keep");
            File.WriteAllText(Path.Combine(current, "theme.txt"), "light");
            ApplicationStorage.MigrateLegacy(old, current);
            Check(File.ReadAllText(Path.Combine(current, "services.xml")) == "catalog", "Old catalog migrates");
            Check(File.ReadAllText(Path.Combine(current, "install-profiles.xml")) == "ciphertext-preserved", "Encrypted profiles copied without rewriting credentials");
            Check(File.ReadAllText(Path.Combine(current, "theme.txt")) == "light", "Existing new settings take precedence");
            Check(File.ReadAllText(Path.Combine(old, "theme.txt")) == "dark", "Old settings preserved");
            Check(!File.Exists(Path.Combine(current, "unrelated.txt")), "Only known settings migrate");
            ApplicationStorage.MigrateLegacy(old, current);
            Check(Directory.GetFiles(current).Length == 3, "Migration is repeatable without temporary remnants");
        }

        private static void VerifyHelpers()
        {
            // Isolate the static helper store of this test assembly from the actual manager.
            typeof(EmbeddedHelpers).GetField("Root", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Path.Combine(root, "helpers"));
            string session = null;
            foreach (string architecture in Environment.Is64BitOperatingSystem ? new string[] { "x86", "x64" } : new string[] { "x86" })
            {
                string file = EmbeddedHelpers.Prepare(architecture);
                session = Path.GetDirectoryName(file);
                Check(ServiceProfileInstaller.ReadManagedArchitecture(file) == architecture, "Embedded helper architecture " + architecture);
                Check(File.Exists(file + ".config"), "Helper carries extracted runtime configuration");
                Check(File.ReadAllText(file + ".config").Contains("useLegacyV2RuntimeActivationPolicy"), "Legacy runtime support stays in helper");
                VerifyWebOriginAssembly(file + ".config", architecture);
                using (var lease = new EmbeddedHelpers.Lease(file))
                {
                    bool protectedFile = false;
                    try { using (var forbidden = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { } }
                    catch (IOException) { protectedFile = true; }
                    Check(protectedFile, "Leased helper cannot be modified during elevation");
                    using (Process process = Process.Start(new ProcessStartInfo(file, "--invalid-test-input") { UseShellExecute = false, CreateNoWindow = true }))
                    {
                        Check(process.WaitForExit(10000), "Helper starts without any adjacent manager files");
                        Check(process.ExitCode == 87, "Invalid helper exits before pipe, installer or SCM operations");
                    }
                }
                byte[] original = File.ReadAllBytes(file);
                File.WriteAllText(file, "damaged fixture");
                Check(EmbeddedHelpers.Prepare(architecture) == file && File.ReadAllBytes(file).Length == original.Length, "Damaged extracted helper is restored");
                File.WriteAllText(file + ".config", "changed");
                EmbeddedHelpers.Prepare(architecture);
                using (var lease = new EmbeddedHelpers.Lease(file)) { Check(true, "Restored helper and config pass hash verification"); }
            }
            EmbeddedHelpers.CleanupStale();
            Check(Directory.Exists(session), "Cleanup preserves a live owner's session");
            string abandoned = Path.Combine(EmbeddedHelpers.Root, "session-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(abandoned);
            File.WriteAllText(Path.Combine(abandoned, "owner.lock"), "");
            File.WriteAllText(Path.Combine(abandoned, "Installer.x86.exe"), "inert abandoned fixture");
            EmbeddedHelpers.CleanupStale();
            Check(!Directory.Exists(abandoned), "Abandoned helper files are removed on next startup");
            string unknown = Path.Combine(EmbeddedHelpers.Root, "session-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(unknown);
            File.WriteAllText(Path.Combine(unknown, "owner.lock"), "");
            File.WriteAllText(Path.Combine(unknown, "personal.txt"), "keep");
            EmbeddedHelpers.CleanupStale();
            Check(File.ReadAllText(Path.Combine(unknown, "personal.txt")) == "keep", "Cleanup leaves unknown content untouched");
            EmbeddedHelpers.Cleanup();
            Check(!Directory.Exists(session), "Normal shutdown deletes helpers, configuration and session directory");
        }

        private static void VerifyWebOriginAssembly(string configuration, string architecture)
        {
            string directory = Path.Combine(root, "load-" + architecture);
            Directory.CreateDirectory(directory);
            string name = "InertFixture" + architecture;
            var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(name),
                System.Reflection.Emit.AssemblyBuilderAccess.Save, directory);
            var module = assembly.DefineDynamicModule(name, name + ".dll");
            module.DefineType("Inert", TypeAttributes.Public).CreateType();
            assembly.Save(name + ".dll");
            string fixture = Path.Combine(directory, name + ".dll");
            using (var stream = ZoneStream(fixture))
            using (var writer = new StreamWriter(stream)) writer.Write("[ZoneTransfer]\r\nZoneId=3\r\n");
            string probe = Path.Combine(directory, "LoadProbe.exe");
            File.Copy(Assembly.GetExecutingAssembly().Location, probe);
            var start = new ProcessStartInfo(probe, "--probe-load \"" + fixture + "\"")
                { UseShellExecute = false, CreateNoWindow = true };
            using (Process process = Process.Start(start))
            {
                Check(process.WaitForExit(10000), "Baseline assembly load finishes");
                Check(process.ExitCode == 42, "Internet-marked local assembly reproduces HRESULT 0x80131515");
            }
            File.Copy(configuration, probe + ".config");
            using (Process process = Process.Start(start))
            {
                Check(process.WaitForExit(10000), "Configured assembly load finishes");
                Check(process.ExitCode == 0, "Helper configuration loads the inert internet-marked assembly");
            }
            using (var stream = ZoneStream(fixture))
            using (var reader = new StreamReader(stream))
                Check(reader.ReadToEnd().Contains("ZoneId=3"), "Loading preserves original internet zone marker");
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string path, uint access, uint share,
            IntPtr security, uint creation, uint attributes, IntPtr template);

        private static FileStream ZoneStream(string fixture)
        {
            var handle = CreateFile(fixture + ":Zone.Identifier", 0xC0000000, 0, IntPtr.Zero, 4, 0, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()); }
            return new FileStream(handle, FileAccess.ReadWrite);
        }

        private static ServiceEntry Entry(string name, string executable)
        {
            return new ServiceEntry { Name = name, DisplayName = "Display " + name, BinaryPath = executable == null ? "" : "\"" + executable + "\" -s", State = "Stopped", StartMode = "Manual", StartupKnown = true };
        }
        private static async Task VerifyDiscoveryAndTimer()
        {
            string exe = Path.Combine(root, "service.exe"); File.WriteAllText(exe, "inert fixture");
            var snapshot = new ServiceSnapshot { Complete = true, Warning = "", Entries = new List<ServiceEntry> {
                Entry("DK.First", exe), Entry("Bars.Second", exe), Entry("bArS.Mixed", exe), Entry("DK.UnreadablePath", null),
                Entry("HttpService", exe), Entry("BarsWithoutDot", exe), Entry("foreign", exe) } };
            snapshot.Entries[6].DisplayName = "Bars.DisplayNameOnly";
            int reads = 0;
            using (var form = new MainForm(null, false, delegate { reads++; return snapshot; }))
            {
                IntPtr handle = form.Handle;
                await Invoke(form, "RefreshStatusAsync", false);
                DataGridView grid = Field<DataGridView>(form, "servicesGrid");
                var toggle = Field<RefreshToggle>(form, "autoRefreshToggle");
                var timer = Field<System.Windows.Forms.Timer>(form, "timer");
                Check(grid.Rows.Count == 4, "First launch discovers system names DK./Bars. case-insensitively");
                Check(Field<List<ServiceEntry>>(form, "matches").Count == 0, "Automatic discovery does not select a service or install anything");
                Check(toggle.Checked && timer.Interval == 10000, "Autorefresh defaults to on, interval 10 seconds");
                typeof(MainForm).GetMethod("UpdateRefreshTimer", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                Check(timer.Enabled, "Default-on refresh starts the timer when the window opens");
                toggle.Checked = false;
                MethodInfo tick = typeof(System.Windows.Forms.Timer).GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic);
                int before = reads;
                tick.Invoke(timer, new object[] { EventArgs.Empty }); await Task.Delay(30);
                Check(reads == before, "Queued ticks cannot refresh while switched off");
                toggle.Checked = true;
                Check(timer.Enabled, "Toggle enables timer");
                tick.Invoke(timer, new object[] { EventArgs.Empty });
                await Wait(form);
                Check(reads == before + 1, "Enabled tick reads the injected SCM catalog");
                toggle.Checked = false;
                Check(!timer.Enabled, "Toggle stops timer immediately");
                typeof(MainForm).GetMethod("UpdateRefreshTimer", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                Check(!timer.Enabled, "Procedure completion respects manually disabled refresh");
                await Invoke(form, "SetExecutableAsync", exe);
                Check(grid.Rows.Count == 7, "Manual EXE discovery remains available without duplicate prefixed rows");
                Select(grid, "DK.UnreadablePath");
                Check(Field<Button>(form, "startButton").Enabled, "Prefixed service with unreadable EXE can still be managed by exact system name");
                Check(!Field<Button>(form, "collectLogsButton").Enabled && !Field<Button>(form, "installButton").Enabled && !Field<Button>(form, "removeButton").Enabled,
                    "Missing path prevents logs/registration actions against the previously selected EXE");
                snapshot = new ServiceSnapshot { Complete = false, Warning = "injected partial read", Entries = new List<ServiceEntry>() };
                await Invoke(form, "RefreshStatusAsync", false);
                Check(grid.Rows.Count == 7 && !Field<Button>(form, "startButton").Enabled, "Partial catalog retains old rows including unmappable names and disables unverified actions");
                foreach (bool dark in new bool[] { false, true })
                {
                    form.SetTheme(dark, false);
                    Check(toggle.Palette.Dark == dark, "Refresh switch follows theme");
                }
                Check(Field<Label>(form, "statusTitle").Parent.Parent.Parent == grid.Parent.Parent, "Status summary and service list share one card");
                Check(Field<Button>(form, "installButton").Margin.Top == Field<Button>(form, "installButton").Margin.Bottom, "Action buttons have symmetric vertical spacing");
                Application.DoEvents();
                Check(Field<PictureBox>(form, "brandImage").Top == (Field<PictureBox>(form, "brandImage").Parent.ClientSize.Height - Field<PictureBox>(form, "brandImage").Height) / 2,
                    "Logo vertically centered in its header slot: "+Field<PictureBox>(form,"brandImage").Bounds+" parent="+Field<PictureBox>(form,"brandImage").Parent.ClientSize);
            }
        }
        private static void Select(DataGridView grid, string name)
        {
            foreach (DataGridViewRow row in grid.Rows) if (((ServiceEntry)row.Tag).Name == name) { grid.CurrentCell = row.Cells[0]; row.Selected = true; return; }
            throw new Exception("Missing fixture row " + name);
        }
        private static async Task Wait(MainForm form)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (Field<bool>(form, "refreshing")) { if (DateTime.UtcNow > deadline) throw new TimeoutException(); await Task.Delay(5); }
        }
        private static T Field<T>(MainForm form, string name) { return (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
        private static Task Invoke(MainForm form, string name, params object[] args) { return (Task)typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args); }
        private static void Check(bool value, string message) { checks++; if (!value) throw new Exception("FAIL: " + message); }
    }
}
