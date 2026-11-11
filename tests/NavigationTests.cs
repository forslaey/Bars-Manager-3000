using System;
using System.Windows.Forms;
using System.Linq;

namespace ServiceTool
{
    internal static class NavigationTests
    {
        private static System.Collections.Generic.IEnumerable<Control> Children(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control descendant in Children(child)) yield return descendant;
            }
        }

        private static Button ButtonWithText(Form owner, string text)
        { return Children(owner).OfType<Button>().Single(button => button.Text == text); }

        private sealed class ActiveModule : Form, IModuleOperation
        {
            internal bool Running = true;
            internal int CloseRequests;
            public string ActiveOperation { get { return Running ? "Тестовая операция" : null; } }
            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                base.OnFormClosing(e);
                CloseRequests++;
                if (Running) e.Cancel = true;
            }
        }

        private static void VerifyBusyExit()
        {
            var hub = new ManagerHub();
            var operation = new ActiveModule();
            int prompts = 0;
            using (var context = new ManagerApplicationContext(() => hub, () => hub, (owner, message) =>
            {
                if (!message.Contains("Тестовая операция")) throw new InvalidOperationException("Active operation omitted.");
                return ++prompts == 1 ? DialogResult.No : DialogResult.Yes;
            }))
            {
                context.OpenModule(typeof(ActiveModule), () => operation);
                ButtonWithText(hub, "Закрыть программу").PerformClick();
                Application.DoEvents();
                if (prompts != 1 || operation.CloseRequests != 0 || hub.IsDisposed)
                    throw new InvalidOperationException("Declining exit must leave the operation intact.");
                ButtonWithText(hub, "Закрыть программу").PerformClick();
                Application.DoEvents();
                if (prompts != 2 || operation.CloseRequests != 1 || hub.IsDisposed)
                    throw new InvalidOperationException("Confirmed exit must wait for the operation.");
                operation.Running = false;
                operation.Close();
                Application.DoEvents();
                if (!hub.IsDisposed || context.CurrentWindow != null)
                    throw new InvalidOperationException("Exit must complete after the last operation closes.");
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr handle);

        private static void VerifyNativeDialog(ManagerHub hub, Form owner, Form other, CommonDialog picker)
        {
            bool checkedWindows = false;
            Exception failure = null;
            using (picker)
            using (var clock = new Timer { Interval = 300 })
            {
                clock.Tick += delegate
                {
                    clock.Stop();
                    try
                    {
                        checkedWindows = true;
                        if (!IsWindowEnabled(hub.Handle) || !IsWindowEnabled(other.Handle) || IsWindowEnabled(owner.Handle))
                            throw new InvalidOperationException("Native file picker must lock only its owner.");
                        hub.Activate();
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { ScopedDialogs.DismissForExit(() => {}); }
                };
                clock.Start();
                if (ScopedDialogs.Show(owner, picker) != DialogResult.Cancel || !checkedWindows)
                    throw new InvalidOperationException("Native file picker did not cancel cleanly.");
            }
            if (failure != null) throw failure;
            if (!IsWindowEnabled(owner.Handle)) throw new InvalidOperationException("Native dialog did not unlock its owner.");
        }

        private static void VerifyOwnedWindows()
        {
            var hub = new ManagerHub();
            using (var context = new ManagerApplicationContext(() => hub, () => hub,
                (owner, message) => { throw new InvalidOperationException("Idle dialog exit asked for confirmation."); }))
            {
                var libraries = new LibraryManagerForm(true);
                var services = new MainForm(null, true);
                context.OpenModule(typeof(LibraryManagerForm), () => libraries);
                var footerNames = new[] { "Открыть журнал", "Компоненты", "Перезагрузка", "В меню", "Закрыть" };
                var footer = footerNames.Select(name => ButtonWithText(libraries, name)).ToArray();
                foreach (var state in new[] { FormWindowState.Normal, FormWindowState.Maximized })
                {
                    libraries.WindowState = state;
                    Application.DoEvents();
                    int gap = footer[1].Left - footer[0].Right;
                    if (gap <= 0 || footer.Any(button => button.Size != footer[0].Size) ||
                        footer.Skip(1).Where((button, index) => button.Left - footer[index].Right != gap).Any())
                        throw new InvalidOperationException("Library footer buttons have unequal dimensions or gaps.");
                }
                ButtonWithText(libraries, "Проверить обновления .NET").PerformClick();
                Application.DoEvents();
                var updates = libraries.OwnedForms.OfType<DotNetUpdatesForm>().Single();
                if (!hub.Enabled || libraries.Enabled || !updates.Enabled || updates.Modal)
                    throw new InvalidOperationException(".NET updates must only lock their owner.");
                context.OpenModule(typeof(MainForm), () => services);
                if (!services.Enabled || !services.Visible) throw new InvalidOperationException("Cannot open another module during .NET updates.");
                ButtonWithText(updates, "Назад").PerformClick();
                Application.DoEvents();
                if (!libraries.Enabled) throw new InvalidOperationException("Returning from .NET did not unlock libraries.");

                using (var logs = new LogArchiveDialog(ThemePalette.Create(false), "Служба BARS",
                    new System.Collections.Generic.List<LogDateFolder>(), DateTime.Today))
                {
                    var result = ModuleNavigation.ShowOwnedAsync(services, logs);
                    if (!hub.Enabled || services.Enabled || !logs.Enabled) throw new InvalidOperationException("Log dialog must only lock its owner.");
                    ButtonWithText(logs, "Отмена").PerformClick();
                    Application.DoEvents();
                    if (!result.IsCompleted || result.Result != DialogResult.Cancel || !services.Enabled)
                        throw new InvalidOperationException("Log cancellation was not returned to its owner.");
                }

                ButtonWithText(libraries, "Компоненты").PerformClick();
                Application.DoEvents();
                var features = libraries.OwnedForms.OfType<WindowsFeaturesForm>().Single();
                if (!hub.Enabled || libraries.Enabled || !features.Enabled || features.Modal)
                    throw new InvalidOperationException("Windows components must only lock their owner.");
                ButtonWithText(features, "Назад").PerformClick();
                Application.DoEvents();
                if (!libraries.Enabled) throw new InvalidOperationException("Returning from components did not unlock libraries.");
                VerifyNativeDialog(hub, services, libraries, new OpenFileDialog { Title = "BARS audit: open" });
                VerifyNativeDialog(hub, services, libraries, new SaveFileDialog { Title = "BARS audit: save" });
                VerifyNativeDialog(hub, services, libraries, new FolderBrowserDialog { Description = "BARS audit: folder" });
                using (var remote = new SoftwareRemoteDialog(ThemePalette.Create(false), hub.Icon, "rudesktop"))
                {
                    var completion = ModuleNavigation.ShowOwnedAsync(libraries, remote);
                    var area = Screen.FromControl(libraries).WorkingArea;
                    var origin = libraries.Bounds;
                    var centered = new System.Drawing.Rectangle(origin.Left + (origin.Width - remote.Width) / 2,
                        origin.Top + (origin.Height - remote.Height) / 2, remote.Width, remote.Height);
                    if (remote.Bounds != WindowPlacement.Fit(centered, area))
                        throw new InvalidOperationException("Remote access dialog is not centered over its owner.");
                    remote.Close();
                    Application.DoEvents();
                    if (!completion.IsCompleted) throw new InvalidOperationException("Remote dialog did not close.");
                }
                Children(hub).OfType<Button>().Single(button => button.AccessibleName == "О программе").PerformClick();
                Application.DoEvents();
                var about = hub.OwnedForms.OfType<AboutDialog>().Single();
                if (hub.Enabled || !services.Enabled || !libraries.Enabled || !about.Enabled)
                    throw new InvalidOperationException("About must lock only the launcher.");
                ButtonWithText(services, "Закрыть").PerformClick();
                if (!services.IsDisposed || about.IsDisposed) throw new InvalidOperationException("About prevented closing another module.");
                Children(about).OfType<Button>().Single().PerformClick();
                Application.DoEvents();
                if (!hub.Enabled) throw new InvalidOperationException("About did not unlock the launcher.");
                ButtonWithText(libraries, "Компоненты").PerformClick();
                ButtonWithText(hub, "Закрыть программу").PerformClick();
                Application.DoEvents();
                if (!hub.IsDisposed || !services.IsDisposed || !libraries.IsDisposed)
                    throw new InvalidOperationException("Global exit did not close an open child window and its owner.");
            }
        }

        [STAThread]
        public static int Main()
        {
            try
            {
                var hub = new ManagerHub();
                using (var context = new ManagerApplicationContext(() => hub, () => hub, (owner, message) => { throw new InvalidOperationException("Idle exit asked for confirmation."); }))
                {
                    var services = new MainForm(null, true);
                    var libraries = new LibraryManagerForm(true);
                    context.OpenModule(typeof(MainForm), () => services);
                    context.OpenModule(typeof(LibraryManagerForm), () => libraries);
                    Application.DoEvents();
                    if (!hub.Visible || !hub.Enabled || !services.Visible || !libraries.Visible)
                        throw new InvalidOperationException("Launcher and both modeless modules remain available.");
                    if (!ModuleNavigation.ReturnToHub() || context.CurrentWindow != hub)
                        throw new InvalidOperationException("Return focuses the existing launcher.");
                    context.OpenModule(typeof(MainForm), () => { throw new InvalidOperationException("Duplicate module created."); });
                    if (context.CurrentWindow != services)
                        throw new InvalidOperationException("Existing module was not activated.");
                    ButtonWithText(services, "Закрыть").PerformClick();
                    if (!services.IsDisposed || libraries.IsDisposed || hub.IsDisposed)
                        throw new InvalidOperationException("Closing a module must leave other windows open.");
                    ButtonWithText(hub, "Закрыть программу").PerformClick();
                    Application.DoEvents();
                    if (!hub.IsDisposed || !libraries.IsDisposed)
                        throw new InvalidOperationException("Idle exit must close all windows.");
                    if (context.CurrentWindow != null)
                        throw new InvalidOperationException("Closing the last window did not end the session.");
                }
                VerifyBusyExit();
                VerifyOwnedWindows();
                Console.WriteLine("PASS: modeless navigation and both idle and active exit paths.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}