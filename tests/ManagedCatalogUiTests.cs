using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    // All catalogs are injected; EXEs are inert text fixtures. No service action is invoked.
    internal static class ManagedCatalogUiTests
    {
        private static int checks;
        private static int result = 1;
        private static string fixtureRoot, executableA, executableB, executableOther;

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            fixtureRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "managed-ui-data-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtureRoot);
            executableA = Fixture("Service A.exe");
            executableB = Fixture("Service B.exe");
            executableOther = Fixture("Other vendor.exe");
            try
            {
                using (Form host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                    Location = new Point(-30000, -30000), Size = new Size(1, 1), Opacity = 0 })
                {
                    host.Shown += async delegate
                    {
                        try
                        {
                            await AggregateAndSelectAsync();
                            await SingleRestoredRowAsync();
                            await ExplicitUnregisteredFileAsync();
                            await RestoredCatalogAndDirtyModeAsync();
                            await ErrorsAndConfirmedRemovalAsync();
                            await PartialReadAsync();
                            await PersistedCatalogAsync();
                            Console.WriteLine("PASS: " + checks + " managed catalog UI assertions. Injected catalogs and inert files only.");
                            result = 0;
                        }
                        catch (Exception ex) { Console.Error.WriteLine(ex); }
                        finally { host.Close(); }
                    };
                    Application.Run(host);
                }
            }
            finally
            {
                string basePath = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string actualPath = Path.GetFullPath(fixtureRoot);
                if (actualPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(actualPath).StartsWith("managed-ui-data-", StringComparison.Ordinal) && Directory.Exists(actualPath))
                    Directory.Delete(actualPath, true);
            }
            return result;
        }

        private static string Fixture(string name)
        {
            string path = Path.Combine(fixtureRoot, name);
            File.WriteAllText(path, "Inert managed catalog UI fixture; this is not a PE executable.");
            return path;
        }

        private static MainForm NewForm(Func<ServiceSnapshot> reader, ManagedServiceStore store)
        {
            MainForm form = new MainForm(null, false, reader, store);
            IntPtr handle = form.Handle;
            return form;
        }

        private static ServiceEntry Entry(string name, string path, string state)
        {
            return new ServiceEntry { Name = name, DisplayName = "Display " + name, BinaryPath = "\"" + path + "\" --service",
                State = state, StartMode = "Manual", CanStop = true, StartupKnown = true };
        }

        private static ServiceSnapshot Snapshot(bool complete, string warning, params ServiceEntry[] entries)
        {
            return new ServiceSnapshot { Complete = complete, Warning = warning, Entries = new List<ServiceEntry>(entries) };
        }

        private static ServiceSnapshot Both()
        {
            return Snapshot(true, "", Entry("bars-A", executableA, "Running"), Entry("bars-B", executableB, "Stopped"),
                Entry("foreign", executableOther, "Running"));
        }

        private static async Task AggregateAndSelectAsync()
        {
            int reads = 0;
            ManagedServiceStore store = ManagedServiceStore.CreateMemory();
            using (MainForm form = NewForm(delegate { reads++; return Both(); }, store))
            {
                await InvokeAsync(form, "SetExecutableAsync", executableA);
                Assert(Grid(form).Rows.Count == 1, "Selecting A adds its registration only");
                await InvokeAsync(form, "SetExecutableAsync", executableB);
                Assert(Grid(form).Rows.Count == 2, "Selecting B preserves A and adds B");
                Assert(Matches(form).Count == 1 && Matches(form)[0].Name == "bars-B", "Selected .exe matches remain separate from all rows");
                Assert(Field<string>(form, "selectedPath") == executableB, "Programmatic rebinding leaves the explicitly selected B path intact");
                Assert(store.GetExecutablePaths().Length == 2, "Observed registrations of A and B are remembered");
                Select(form, "bars-A");
                Assert(Field<string>(form, "selectedPath") == executableA && Field<TextBox>(form, "pathBox").Text == executableA,
                    "Selecting A row switches the executable shown above");
                Assert(Matches(form).Count == 1 && Matches(form)[0].Name == "bars-A", "Row selection retargets .exe registration actions");
                Assert(Field<Button>(form, "removeButton").Enabled && Field<Button>(form, "stopButton").Enabled,
                    "A row enables its own .exe removal and running-service stop");
                Assert(reads == 2, "Row selection reuses the catalog without another SCM query");
                Assert(!HasRow(form, "foreign"), "Unselected unrelated vendor service is excluded");
            }
        }

        private static async Task ExplicitUnregisteredFileAsync()
        {
            ManagedServiceStore store = ManagedServiceStore.CreateMemory();
            using (MainForm form = NewForm(delegate { return Snapshot(true, "", Entry("bars-A", executableA, "Running")); }, store))
            {
                await InvokeAsync(form, "SetExecutableAsync", executableA);
                await InvokeAsync(form, "SetExecutableAsync", executableB);
                Assert(Grid(form).Rows.Count == 1 && HasRow(form, "bars-A"), "An unregistered B selection preserves the known A row");
                Assert(Field<string>(form, "selectedPath") == executableB && Matches(form).Count == 0,
                    "An explicitly selected unregistered B remains the installation target");
                Assert(Grid(form).CurrentRow == null && Grid(form).SelectedRows.Count == 0, "No unrelated row is highlighted for the B installation target");
                Assert(Field<Button>(form, "installButton").Enabled && !Field<Button>(form, "removeButton").Enabled,
                    "Confirmed B absence enables installation without enabling removal");
                Assert(store.GetExecutablePaths().Length == 1, "Merely choosing an unregistered .exe does not persist it as a managed service");
            }
        }

        private static async Task SingleRestoredRowAsync()
        {
            ManagedServiceStore store = ManagedServiceStore.CreateMemory();
            store.Remember(executableA);
            int reads = 0;
            using (MainForm form = NewForm(delegate { reads++; return Both(); }, store))
            {
                await InvokeAsync(form, "RefreshStatusAsync", false);
                Assert(Grid(form).Rows.Count == 1 && Grid(form).CurrentRow == null && Grid(form).SelectedRows.Count == 0,
                    "A single restored row starts without an implicit current selection");
                Select(form, "bars-A");
                Assert(Field<string>(form, "selectedPath") == executableA, "First selection of the single restored row sets the executable path");
                Assert(Field<Button>(form, "removeButton").Enabled && Field<Button>(form, "stopButton").Enabled,
                    "The single restored service is immediately removable and manageable after selection");
                Assert(reads == 1, "Selecting the single restored row uses its existing snapshot");
            }
        }

        private static async Task RestoredCatalogAndDirtyModeAsync()
        {
            ManagedServiceStore store = ManagedServiceStore.CreateMemory();
            store.Remember(executableA); store.Remember(executableB);
            int reads = 0;
            using (MainForm form = NewForm(delegate { reads++; return Both(); }, store))
            {
                await InvokeAsync(form, "RefreshStatusAsync", false);
                Assert(Grid(form).Rows.Count == 2, "Remembered registrations appear without choosing another .exe");
                Assert(String.IsNullOrEmpty(Field<string>(form, "selectedPath")), "Startup rebinding does not silently select an .exe");
                Assert(!Field<Button>(form, "installButton").Enabled && !Field<Button>(form, "removeButton").Enabled,
                    ".exe actions wait for an explicit file or row selection");
                Select(form, "bars-A");
                ComboBox mode = Field<ComboBox>(form, "startupCombo");
                mode.SelectedIndex = 3;
                Assert(Field<bool>(form, "startupDirty") && Field<Button>(form, "applyStartupButton").Enabled, "A changed startup choice is pending");
                await InvokeAsync(form, "RefreshStatusAsync", true);
                Assert(SelectedName(form) == "bars-A", "Auto refresh preserves selected service identity");
                Assert(mode.SelectedIndex == 3 && Field<bool>(form, "startupDirty") && Field<Button>(form, "applyStartupButton").Enabled,
                    "Auto refresh preserves an unapplied startup choice");
                Assert(Field<string>(form, "selectedPath") == executableA && reads == 2, "Refresh keeps the chosen executable and uses one catalog query");
            }
        }

        private static async Task ErrorsAndConfirmedRemovalAsync()
        {
            ManagedServiceStore store = ManagedServiceStore.CreateMemory();
            store.Remember(executableA); store.Remember(executableB);
            int reads = 0;
            using (MainForm form = NewForm(delegate
            {
                if (++reads == 1) return Both();
                if (reads == 2) throw new InvalidOperationException("Injected catalog failure");
                return Snapshot(true, "", Entry("bars-B", executableB, "Stopped"));
            }, store))
            {
                await InvokeAsync(form, "SetExecutableAsync", executableA);
                await InvokeAsync(form, "RefreshStatusAsync", false);
                Assert(Grid(form).Rows.Count == 2 && !Field<bool>(form, "verified"), "Read failure retains rows but invalidates registration certainty");
                Assert(!Field<Button>(form, "installButton").Enabled && !Field<Button>(form, "removeButton").Enabled &&
                    !Field<Button>(form, "startButton").Enabled && !Field<Button>(form, "stopButton").Enabled,
                    "Read failure disables both .exe and selected-service mutations");
                Assert(Field<Label>(form, "activity").Text.Contains("Отображается последнее известное состояние"), "Read failure explicitly identifies the retained data as last known state");
                Assert(store.GetExecutablePaths().Length == 2, "Read failure never forgets managed executables");
                await InvokeAsync(form, "RefreshStatusAsync", false);
                Assert(Grid(form).Rows.Count == 1 && HasRow(form, "bars-B"), "A complete refresh removes an absent registration from the grid");
                Assert(Field<Button>(form, "installButton").Enabled && Matches(form).Count == 0, "Complete A absence permits A reinstallation");
                Assert(store.GetExecutablePaths().Length == 2, "An absent registration still retains its known executable for future installation");
                Select(form, "bars-B");
                Assert(Field<Button>(form, "removeButton").Enabled && Field<Button>(form, "startButton").Enabled,
                    "Recovery makes another managed service actionable");
            }
        }

        private static async Task PartialReadAsync()
        {
            ManagedServiceStore store = ManagedServiceStore.CreateMemory();
            store.Remember(executableA); store.Remember(executableB);
            int reads = 0;
            using (MainForm form = NewForm(delegate
            {
                if (++reads == 1) return Both();
                return Snapshot(false, "Injected incomplete catalog", Entry("bars-B", executableB, "Stopped"));
            }, store))
            {
                await InvokeAsync(form, "SetExecutableAsync", executableA);
                await InvokeAsync(form, "RefreshStatusAsync", false);
                Assert(Grid(form).Rows.Count == 2 && HasRow(form, "bars-A"), "Incomplete reads preserve previously known unconfirmed rows");
                Assert(!Field<bool>(form, "verified") && !Field<Button>(form, "installButton").Enabled && !Field<Button>(form, "removeButton").Enabled,
                    "Incomplete A absence never enables installation or removal");
                Assert(!Field<Button>(form, "stopButton").Enabled, "A retained unconfirmed row cannot authorize service management");
                Assert(Convert.ToString(Row(form, "bars-A").Cells[2].Value) == "Не проверено", "Retained partial-read row is explicitly unverified");
                Select(form, "bars-B");
                Assert(Field<bool>(form, "verified") && Field<Button>(form, "removeButton").Enabled && Field<Button>(form, "startButton").Enabled,
                    "A directly observed B registration stays manageable despite another incomplete entry");
                Assert(!Field<Label>(form, "hint").Visible && Field<TextBox>(form, "log").Text.Contains("Injected incomplete catalog"), "Partial-read warning remains in the journal after row selection without restoring the removed caption");
            }
        }

        private static async Task PersistedCatalogAsync()
        {
            string storePath = Path.Combine(fixtureRoot, "settings", "services.xml");
            ManagedServiceStore first = new ManagedServiceStore(storePath);
            using (MainForm form = NewForm(Both, first))
            {
                await InvokeAsync(form, "SetExecutableAsync", executableA);
                await InvokeAsync(form, "SetExecutableAsync", executableB);
            }
            ManagedServiceStore reopened = new ManagedServiceStore(storePath);
            using (MainForm form = NewForm(Both, reopened))
            {
                await InvokeAsync(form, "RefreshStatusAsync", false);
                Assert(Grid(form).Rows.Count == 2 && HasRow(form, "bars-A") && HasRow(form, "bars-B"),
                    "A fresh form restores both registered services from the fixture's persisted catalog");
                Assert(!HasRow(form, "foreign"), "Persisted catalog restore excludes unknown executables");
            }
        }

        private static DataGridView Grid(MainForm form) { return Field<DataGridView>(form, "servicesGrid"); }
        private static List<ServiceEntry> Matches(MainForm form) { return Field<List<ServiceEntry>>(form, "matches"); }
        private static string SelectedName(MainForm form)
        {
            return Grid(form).CurrentRow == null ? null : ((ServiceEntry)Grid(form).CurrentRow.Tag).Name;
        }
        private static bool HasRow(MainForm form, string name) { return Row(form, name) != null; }
        private static DataGridViewRow Row(MainForm form, string name)
        {
            foreach (DataGridViewRow row in Grid(form).Rows)
                if (((ServiceEntry)row.Tag).Name == name) return row;
            return null;
        }
        private static void Select(MainForm form, string name)
        {
            DataGridViewRow row = Row(form, name);
            if (row == null) throw new InvalidOperationException("Missing expected row " + name);
            Grid(form).CurrentCell = row.Cells[0];
            row.Selected = true;
        }
        private static T Field<T>(MainForm form, string name)
        {
            return (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        }
        private static Task InvokeAsync(MainForm form, string name, params object[] args)
        {
            try { return (Task)typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }
        private static void Assert(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("FAIL: " + message);
        }
    }
}
