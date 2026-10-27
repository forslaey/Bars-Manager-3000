using System;
using System.Windows.Forms;

namespace ServiceTool
{
    // Own the message loop, not a launcher window. Closing a window must not
    // terminate the loop while a requested navigation transition is in progress.
    internal sealed class ManagerApplicationContext : ApplicationContext
    {
        private readonly Func<ManagerHub> createHub;
        internal Form CurrentWindow { get; private set; }

        internal ManagerApplicationContext(Func<Form> initialWindow)
            : this(initialWindow, () => new ManagerHub()) { }

        internal ManagerApplicationContext(Func<Form> initialWindow, Func<ManagerHub> hubFactory)
        {
            createHub = hubFactory;
            ShowWindow(initialWindow);
        }

        private void ShowWindow(Func<Form> factory)
        {
            Form window = factory();
            CurrentWindow = window;
            window.FormClosed += WindowClosed;
            try { window.Show(); window.Activate(); }
            catch
            {
                window.FormClosed -= WindowClosed;
                CurrentWindow = null;
                window.Dispose();
                throw;
            }
        }

        private void WindowClosed(object sender, FormClosedEventArgs e)
        {
            var window = (Form)sender;
            window.FormClosed -= WindowClosed;
            var hub = window as ManagerHub;
            Func<Form> next = hub == null ? null : hub.RequestedModule;
            bool menu = MenuRequested(window);
            CurrentWindow = null;
            window.Dispose();
            if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
            {
                ExitThread();
                return;
            }
            try
            {
                if (next != null) ShowWindow(next);
                else if (menu) ShowWindow(() => createHub());
                else ExitThread();
            }
            catch (Exception error)
            {
                BrandMessageBox.Show("Не удалось открыть утилиту.\r\n\r\n" + error.Message,
                    "BARS Manager 3000", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitThread();
            }
        }

        private static bool MenuRequested(Form window)
        {
            var services = window as MainForm;
            if (services != null) return services.MenuRequested;
            var library = window as LibraryManagerForm;
            if (library != null) return library.MenuRequested;
            var software = window as SoftwareManagerForm;
            if (software != null) return software.MenuRequested;
            var utility = window as UtilityModuleForm;
            return utility != null && utility.MenuRequested;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && CurrentWindow != null)
            {
                CurrentWindow.FormClosed -= WindowClosed;
                CurrentWindow.Dispose();
                CurrentWindow = null;
            }
            base.Dispose(disposing);
        }
    }
}
