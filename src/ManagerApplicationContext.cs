using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class ManagerApplicationContext : ApplicationContext
    {
        private readonly Func<ManagerHub> createHub;
        private readonly Dictionary<Type, Form> modules = new Dictionary<Type, Form>();
        private readonly Dictionary<Type, Form> dialogs = new Dictionary<Type, Form>();
        private ManagerHub hub;
        private bool shuttingDown;
        internal static ManagerApplicationContext ActiveContext;
        internal Form CurrentWindow { get; private set; }

        internal ManagerApplicationContext(Func<Form> initialWindow) : this(initialWindow, () => new ManagerHub()) { }

        internal ManagerApplicationContext(Func<Form> initialWindow, Func<ManagerHub> hubFactory)
        {
            createHub = hubFactory;
            ActiveContext = this;
            try { ShowWindow(initialWindow()); }
            catch { Dispose(); throw; }
        }

        internal T Dialog<T>(Func<T> factory) where T : Form
        {
            Form form;
            if (!dialogs.TryGetValue(typeof(T), out form) || form.IsDisposed)
            {
                form = factory();
                dialogs[typeof(T)] = form;
            }
            form.DialogResult = DialogResult.None;
            return (T)form;
        }

        internal bool ShowHub()
        {
            if (shuttingDown) return false;
            if (hub == null) ShowWindow(createHub());
            else ActivateWindow(hub);
            return true;
        }

        internal void OpenModule(Type type, Func<Form> factory)
        {
            if (shuttingDown) return;
            Form existing;
            if (modules.TryGetValue(type, out existing) && !existing.IsDisposed)
            {
                ActivateWindow(existing);
                return;
            }

            try { ShowWindow(factory()); }
            catch (Exception ex)
            {
                BrandMessageBox.Show(hub, "Не удалось открыть модуль.\r\n\r\n" + ex.Message,
                    "BARS Manager 3000", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
            window.Show();
            ActivateWindow(window);
        }

        private void ActivateWindow(Form window)
        {
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

        private void WindowClosing(object sender, FormClosingEventArgs e)
        {
            if (sender != hub || e.Cancel || shuttingDown || modules.Count == 0 || e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            BrandMessageBox.Show(hub, "Закрой открытые модули перед выходом из менеджера. В каждом модуле можно дождаться завершения операции или отменить её штатной кнопкой.",
                "Выход из менеджера", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void WindowClosed(object sender, FormClosedEventArgs e)
        {
            var window = (Form)sender;
            if (window == hub) hub = null;
            else modules.Remove(window.GetType());
            if (CurrentWindow == window) CurrentWindow = hub;
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
