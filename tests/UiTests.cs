using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    // Uses only injected catalogs and inert text files. Never invokes a service command.
    internal static class UiTests
    {
        private static int checks;
        private static int exitCode = 1;
        private static string fixtureRoot;
        private static string executableA;
        private static string executableB;

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            fixtureRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-data-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtureRoot);
            executableA = Path.Combine(fixtureRoot, "Service A.exe");
            executableB = Path.Combine(fixtureRoot, "Service B.exe");
            File.WriteAllText(executableA, "Inert UI-test fixture. This file is not a PE executable.");
            File.WriteAllText(executableB, "Inert UI-test fixture. This file is not a PE executable.");
            try
            {
                using (Form host = new Form
                {
                    Text = "ServiceTool UI test host",
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-30000, -30000),
                    Size = new Size(1, 1),
                    Opacity = 0
                })
                {
                    host.Shown += async delegate
                    {
                        try
                        {
                            await RunAllAsync();
                            Console.WriteLine("PASS: " + checks + " UI assertions. Injected catalogs only; no executable, UAC or service action launched.");
                            exitCode = 0;
                        }
                        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); }
                        finally { host.Close(); }
                    };
                    Application.Run(host);
                }
            }
            finally
            {
                // Delete only the unique fixture directory created by this process.
                string basePath = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string actualPath = Path.GetFullPath(fixtureRoot);
                if (actualPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(actualPath).StartsWith("ui-data-", StringComparison.Ordinal) && Directory.Exists(actualPath))
                    Directory.Delete(actualPath, true);
            }
            return exitCode;
        }

        private static async Task RunAllAsync()
        {
            await CompleteWithoutMatchAsync();
            await StoppedServiceAsync();
            await ReadErrorAndRecoveryAsync();
            await PartialWithoutMatchAsync();
            await PartialWithMatchAsync();
            await StaleSelectionAsync();
        }

        private static MainForm NewForm(Func<ServiceSnapshot> reader)
        {
            MainForm form = new MainForm(null, false, reader);
            IntPtr handle = form.Handle; // Allows posted continuations without showing a UI or starting its timer.
            return form;
        }

        private static ServiceSnapshot Snapshot(bool complete, string warning, params ServiceEntry[] entries)
        {
            return new ServiceSnapshot { Complete = complete, Warning = warning, Entries = new List<ServiceEntry>(entries) };
        }

        private static ServiceEntry Entry(string name, string path, string state)
        {
            return new ServiceEntry { Name = name, DisplayName = "Display " + name, BinaryPath = "\"" + path + "\" --service", State = state, StartMode = "Manual" };
        }

        private static async Task CompleteWithoutMatchAsync()
        {
            using (MainForm form = NewForm(delegate { return Snapshot(true, "", Entry("unrelated", executableB, "Running")); }))
            {
                Assert(!Field<Button>(form, "installButton").Enabled && !Field<Button>(form, "removeButton").Enabled, "No selection disables both actions");
                await InvokeAsync(form, "SetExecutableAsync", executableA);
                Assert(Field<Button>(form, "installButton").Enabled, "A complete catalog without an exact match permits installation");
                Assert(!Field<Button>(form, "removeButton").Enabled, "An unrelated service cannot enable removal");
                Assert(Field<DataGridView>(form, "servicesGrid").Rows.Count == 0, "Unrelated entries are excluded from the grid");
                Assert(Field<Label>(form, "statusTitle").Text == "Регистрация не найдена", "Confirmed absence is displayed");
                Assert(Field<bool>(form, "verified"), "Complete absence is verified");
            }
            Console.WriteLine("PASS: complete catalog without matching registration");
        }

        private static async Task StoppedServiceAsync()
        {
            using (MainForm form = NewForm(delegate { return Snapshot(true, "", Entry("stopped-A", executableA, "Stopped")); }))
            {
                await InvokeAsync(form, "SetExecutableAsync", executableA);
                Assert(!Field<Button>(form, "installButton").Enabled && Field<Button>(form, "removeButton").Enabled, "A stopped service remains registered and removable");
                Assert(Field<Label>(form, "statusTitle").Text == "Служба зарегистрирована", "Stopped registration is not presented as running");
                DataGridView grid = Field<DataGridView>(form, "servicesGrid");
                Assert(grid.Rows.Count == 1, "Stopped registration appears in the grid");
                Assert(Convert.ToString(grid.Rows[0].Cells[1].Value) == "stopped-A" && Convert.ToString(grid.Rows[0].Cells[2].Value) == "Остановлена", "The grid preserves identity and translates stopped state");
            }
            Console.WriteLine("PASS: stopped service registration");
        }

        private static async Task ReadErrorAndRecoveryAsync()
        {
            int calls = 0;
            using (MainForm form = NewForm(delegate
            {
                int call = Interlocked.Increment(ref calls);
                if (call == 1) return Snapshot(true, "", Entry("initial-A", executableA, "Running"));
                if (call == 2) throw new InvalidOperationException("Injected catalog access failure");
                return Snapshot(true, "");
            }))
            {
                await InvokeAsync(form, "SetExecutableAsync", executableA);
                Assert(Field<Button>(form, "removeButton").Enabled, "Error scenario starts with a known registration");
                await InvokeAsync(form, "RefreshStatusAsync", false);
                Assert(!Field<Button>(form, "installButton").Enabled && !Field<Button>(form, "removeButton").Enabled, "Catalog failure cannot permit either action");
                Assert(!Field<bool>(form, "verified") && Field<DataGridView>(form, "servicesGrid").Rows.Count == 1, "Catalog failure retains the last known registration without trusting it");
                Assert(Field<Label>(form, "statusTitle").Text == "Статус не удалось проверить", "Catalog failure is distinct from absent registration");
                Assert(Field<Label>(form, "statusDetail").Text.Contains("Injected catalog access failure"), "Failure details reach the UI");
                Assert(Field<Button>(form, "refreshButton").Enabled && Field<Button>(form, "chooseButton").Enabled, "Read failure leaves a recovery path");
                await InvokeAsync(form, "RefreshStatusAsync", false);
                Assert(Field<Button>(form, "installButton").Enabled && !Field<Button>(form, "removeButton").Enabled, "A successful refresh recovers after an error");
            }
            Console.WriteLine("PASS: catalog error clears stale state and refresh recovers");
        }

        private static async Task PartialWithoutMatchAsync()
        {
            const string warning = "Injected partial catalog: some service paths are unreadable";
            using (MainForm form = NewForm(delegate { return Snapshot(false, warning, Entry("other-B", executableB, "Stopped")); }))
            {
                await InvokeAsync(form, "SetExecutableAsync", executableA);
                Assert(!Field<Button>(form, "installButton").Enabled && !Field<Button>(form, "removeButton").Enabled, "Incomplete absence cannot authorize installation or removal");
                Assert(!Field<bool>(form, "verified"), "An unmatched partial catalog remains unverified");
                Assert(Field<Label>(form, "statusTitle").Text == "Статус регистрации неизвестен", "Partial absence is shown as unknown");
                Assert(Field<Label>(form, "statusDetail").Text.Contains(warning), "Partial absence explains its uncertainty");
            }
            Console.WriteLine("PASS: unmatched partial catalog is unknown");
        }

        private static async Task PartialWithMatchAsync()
        {
            const string warning = "Injected partial catalog warning";
            using (MainForm form = NewForm(delegate { return Snapshot(false, warning, Entry("known-A", executableA, "Running")); }))
            {
                await InvokeAsync(form, "SetExecutableAsync", executableA);
                Assert(!Field<Button>(form, "installButton").Enabled && Field<Button>(form, "removeButton").Enabled, "A directly observed match permits removal despite a partial catalog");
                Assert(Field<bool>(form, "verified") && Field<DataGridView>(form, "servicesGrid").Rows.Count == 1, "Known partial match remains visible and verified");
                Assert(Field<Label>(form, "statusTitle").Text.Contains("зарегистрирована") && Field<Label>(form, "statusTitle").Text.Contains("работает"), "Known running registration is shown");
                Assert(Field<Label>(form, "hint").Text.Contains(warning), "The partial-read warning remains visible beside a known match");
            }
            Console.WriteLine("PASS: matched partial catalog retains warning and known registration");
        }

        private static async Task StaleSelectionAsync()
        {
            using (ManualResetEventSlim aStarted = new ManualResetEventSlim())
            using (ManualResetEventSlim aRelease = new ManualResetEventSlim())
            using (ManualResetEventSlim bStarted = new ManualResetEventSlim())
            using (ManualResetEventSlim bRelease = new ManualResetEventSlim())
            {
                int calls = 0;
                using (MainForm form = NewForm(delegate
                {
                    int call = Interlocked.Increment(ref calls);
                    if (call == 1)
                    {
                        aStarted.Set();
                        if (!aRelease.Wait(10000)) throw new TimeoutException("Test A gate was not released");
                        return Snapshot(true, "", Entry("stale-A", executableA, "Running"));
                    }
                    if (call == 2)
                    {
                        bStarted.Set();
                        if (!bRelease.Wait(10000)) throw new TimeoutException("Test B gate was not released");
                        return Snapshot(true, "", Entry("current-B", executableB, "Stopped"));
                    }
                    throw new InvalidOperationException("Unexpected extra catalog query: " + call);
                }))
                {
                    try
                    {
                        Task selectingA = InvokeAsync(form, "SetExecutableAsync", executableA);
                        await UntilAsync(delegate { return aStarted.IsSet; }, "A query starts");
                        await InvokeAsync(form, "SetExecutableAsync", executableB);
                        Assert(Field<string>(form, "selectedPath") == executableB, "B becomes selected while A is pending");
                        Assert(!Field<Button>(form, "installButton").Enabled && !Field<Button>(form, "removeButton").Enabled, "Selection change invalidates actions immediately");
                        aRelease.Set();
                        await selectingA;
                        await UntilAsync(delegate { return bStarted.IsSet; }, "B is automatically re-queried after stale A returns");
                        Assert(Field<DataGridView>(form, "servicesGrid").Rows.Count == 0 && !Field<bool>(form, "verified"), "A's late response never populates B's registration");
                        Assert(!Field<Button>(form, "installButton").Enabled && !Field<Button>(form, "removeButton").Enabled, "Actions stay disabled while B remains unresolved");
                        bRelease.Set();
                        await UntilAsync(delegate { return !Field<bool>(form, "refreshing"); }, "B query completes");
                        DataGridView grid = Field<DataGridView>(form, "servicesGrid");
                        Assert(grid.Rows.Count == 1 && Convert.ToString(grid.Rows[0].Cells[1].Value) == "current-B", "Only B's result reaches the grid");
                        Assert(Field<Button>(form, "removeButton").Enabled && !Field<Button>(form, "installButton").Enabled, "Only B's verified state enables its appropriate action");
                        Assert(calls == 2, "Selection race schedules exactly one replacement query");
                    }
                    finally { aRelease.Set(); bRelease.Set(); }
                }
            }
            Console.WriteLine("PASS: stale asynchronous A result cannot overwrite selection B");
        }

        private static T Field<T>(MainForm form, string name)
        {
            FieldInfo info = typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (info == null) throw new MissingFieldException(typeof(MainForm).FullName, name);
            return (T)info.GetValue(form);
        }

        private static Task InvokeAsync(MainForm form, string name, params object[] arguments)
        {
            MethodInfo info = typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (info == null) throw new MissingMethodException(typeof(MainForm).FullName, name);
            try { return (Task)info.Invoke(form, arguments); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        private static async Task UntilAsync(Func<bool> predicate, string message)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!predicate())
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException(message);
                await Task.Delay(10);
            }
        }

        private static void Assert(bool condition, string message)
        {
            ++checks;
            if (!condition) throw new InvalidOperationException("FAIL: " + message);
        }
    }
}
