using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class ManagerApplicationContext : ApplicationContext
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        private readonly Func<ManagerHub> createHub;
        private readonly Func<ManagerHub, string, DialogResult> confirmExit;
        private readonly Dictionary<Type, Form> modules = new Dictionary<Type, Form>();
        private readonly Dictionary<Type, Form> dialogs = new Dictionary<Type, Form>();
        private ManagerHub hub;
        private bool shuttingDown, exitRequested;
        private Timer dialogExitTimer;
        internal bool ExitConfirmed { get { return exitRequested; } }
        internal static ManagerApplicationContext ActiveContext;
        internal Form CurrentWindow { get; private set; }

        internal ManagerApplicationContext(Func<Form> initialWindow) : this(initialWindow, () => new ManagerHub()) { }

        internal ManagerApplicationContext(Func<Form> initialWindow, Func<ManagerHub> hubFactory, Func<ManagerHub, string, DialogResult> confirm = null)
        {
            createHub = hubFactory;
            confirmExit = confirm ?? ((owner, message) => BrandMessageBox.Show(owner, message,
                "Выход из менеджера", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2));
            ActiveContext = this;
            try
            {
                Form window = CreateWindow(initialWindow);
                ShowWindow(window);
                if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("BARS_MANAGER_LAUNCH_DIRECTORY")))
                {
                    window.BeginInvoke((MethodInvoker)delegate
                    {
                        if (window.IsDisposed || !window.Visible) return;
                        if (IsIconic(window.Handle) || window.WindowState == FormWindowState.Minimized)
                        {
                            window.WindowState = FormWindowState.Normal;
                            ShowWindow(window.Handle, 9);
                        }
                        window.Activate();
                        SetForegroundWindow(window.Handle);
                    });
                }
            }
            catch { Dispose(); throw; }
        }

        internal T Dialog<T>(Func<T> factory) where T : Form
        {
            Form form;
            if (!dialogs.TryGetValue(typeof(T), out form) || form.IsDisposed)
            {
                form = CreateWindow(factory);
                dialogs[typeof(T)] = form;
            }
            form.DialogResult = DialogResult.None;
            return (T)form;
        }

        internal bool ShowHub()
        {
            if (shuttingDown || exitRequested) return false;
            if (hub == null) ShowWindow(CreateWindow(createHub));
            else ActivateWindow(hub);
            return true;
        }

        internal void OpenModule(Type type, Func<Form> factory)
        {
            if (shuttingDown || exitRequested) return;
            Form existing;
            if (modules.TryGetValue(type, out existing) && !existing.IsDisposed)
            {
                ActivateWindow(existing);
                return;
            }

            try { ShowWindow(CreateWindow(factory)); }
            catch (Exception ex)
            {
                BrandMessageBox.Show(hub, "Не удалось открыть модуль.\r\n\r\n" + ex.Message,
                    "BARS Manager 3000", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static T CreateWindow<T>(Func<T> factory) where T : Form
        {
            using (PerformanceTrace.Begin("ui.construct." + typeof(T).Name)) return factory();
        }

        private void ShowWindow(Form window)
        {
            if (window is ManagerHub) hub = (ManagerHub)window;
            else modules[window.GetType()] = window;
            window.FormClosing += WindowClosing;
            window.FormClosed += WindowClosed;
            window.Activated += WindowActivated;
            window.Load += delegate
            {
                if (hub != null && window != hub) WindowPlacement.Center(window, Screen.FromControl(hub).WorkingArea);
                else WindowPlacement.Constrain(window);
            };
            using (PerformanceTrace.Begin("ui.show." + window.GetType().Name))
            {
                window.Show();
                ActivateWindow(window);
            }
            if (PerformanceTrace.Enabled)
            {
                string name = window.GetType().Name;
                window.BeginInvoke((MethodInvoker)delegate { PerformanceTrace.Mark("ui.message-loop." + name); });
            }
        }

        private void ActivateWindow(Form window)
        {
            while (!window.Enabled)
            {
                var child = window.OwnedForms.LastOrDefault(form => form.Visible && !form.IsDisposed);
                if (child == null) break;
                window = child;
            }
            CurrentWindow = window;
            if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
            if (!window.Visible) window.Show();
            window.Activate();
        }

        private void WindowActivated(object sender, EventArgs e)
        {
            var window = (Form)sender;
            CurrentWindow = window;
            bool dark = ThemePreference.LoadDark();
            var services = window as MainForm;
            if (services != null && (services.BackColor.GetBrightness() < .5F) != dark) services.SetTheme(dark, false);
            var module = window as ManagerModuleForm;
            if (module != null && (module.BackColor.GetBrightness() < .5F) != dark) module.SetTheme(dark, false);
        }

        private bool IsOwnedByModule(Form form)
        {
            for (Form owner = form.Owner; owner != null; owner = owner.Owner)
                if (modules.ContainsValue(owner)) return true;
            return false;
        }

        private void WindowClosing(object sender, FormClosingEventArgs e)
        {
            if (sender != hub || e.Cancel || shuttingDown || e.CloseReason != CloseReason.UserClosing) return;
            if (modules.Count == 0) return;
            e.Cancel = true;
            if (exitRequested) return;
            var operationDialogs = Application.OpenForms.Cast<Form>()
                .Where(form => form.Visible && IsOwnedByModule(form)).Reverse().ToArray();
            var active = modules.Values.Concat(operationDialogs).OfType<IModuleOperation>().Select(module => module.ActiveOperation);
            var running = active.Where(item => item != null).Select(item => "• " + item).ToArray();
            if (running.Length != 0 && confirmExit(hub,
                "Закрыть программу и все открытые модули?\r\n\r\nСейчас выполняется:\r\n" + String.Join("\r\n", running) +
                "\r\n\r\nМенеджер запросит отмену там, где это поддерживается. Для остальных действий нужно дождаться завершения.") != DialogResult.Yes) return;

            exitRequested = true;
            hub.BeginInvoke((MethodInvoker)(() => CloseDialogs(operationDialogs)));
        }

        private void CloseDialogs(Form[] operationDialogs)
        {
            if (hub == null || hub.IsDisposed) return;
            if (ScopedDialogs.DismissForExit(() => hub.BeginInvoke((MethodInvoker)(() => CloseDialogs(operationDialogs))))) return;
            foreach (var dialog in operationDialogs)
                if (!dialog.IsDisposed && dialog.Visible) dialog.Close();
            if (!operationDialogs.Any(dialog => !dialog.IsDisposed && dialog.Visible)) { CloseModules(); return; }
            dialogExitTimer = new Timer { Interval = 100 };
            dialogExitTimer.Tick += delegate
            {
                if (operationDialogs.Any(dialog => !dialog.IsDisposed && dialog.Visible)) return;
                dialogExitTimer.Dispose();
                dialogExitTimer = null;
                CloseModules();
            };
            dialogExitTimer.Start();
        }

        private void CloseModules()
        {
            foreach (Form module in new List<Form>(modules.Values))
            {
                if (module.IsDisposed) continue;
                module.Close();
                if (!module.IsDisposed && module.Visible) module.Enabled = false;
            }
            CloseHubWhenReady();
        }

        private void CloseHubWhenReady()
        {
            if (!exitRequested || modules.Count != 0 || hub == null || hub.IsDisposed) return;
            var window = hub;
            window.BeginInvoke((MethodInvoker)delegate { if (!window.IsDisposed) window.Close(); });
        }

        private void WindowClosed(object sender, FormClosedEventArgs e)
        {
            var window = (Form)sender;
            if (window == hub) hub = null;
            else modules.Remove(window.GetType());
            if (CurrentWindow == window) CurrentWindow = hub;
            if (window != hub) CloseHubWhenReady();
            if (!shuttingDown && hub == null && modules.Count == 0) ExitThread();
        }

        protected override void ExitThreadCore()
        {
            ReleaseWindows();
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ReleaseWindows();
            base.Dispose(disposing);
        }

        private void ReleaseWindows()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            if (dialogExitTimer != null) dialogExitTimer.Dispose();
            if (ActiveContext == this) ActiveContext = null;
            foreach (Form dialog in dialogs.Values) dialog.Dispose();
            dialogs.Clear();
            foreach (Form module in new List<Form>(modules.Values)) module.Dispose();
            modules.Clear();
            if (hub != null) hub.Dispose();
            hub = null;
            CurrentWindow = null;
        }
    }
}
