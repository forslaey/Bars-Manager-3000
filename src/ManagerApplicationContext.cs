using System;
using System.Collections.Generic;
using System.Windows.Forms;
namespace ServiceTool
{
    internal sealed class ManagerApplicationContext : ApplicationContext
    {
        private readonly Func<ManagerHub> createHub;
        private readonly Dictionary<Type, Form> modules = new Dictionary<Type, Form>();
        private WindowPlacement hubPlacement;
        internal static ManagerApplicationContext ActiveContext;
        private readonly Dictionary<Type,Form> dialogs=new Dictionary<Type,Form>();
        private bool navigating, shuttingDown;
        internal Form CurrentWindow { get; private set; }
        internal ManagerApplicationContext(Func<Form> initialWindow) : this(initialWindow, () => new ManagerHub()) { }
        internal ManagerApplicationContext(Func<Form> initialWindow, Func<ManagerHub> hubFactory)
        { createHub = hubFactory; ActiveContext=this; try { ShowWindow(initialWindow()); } catch { ActiveContext=null; throw; } }
        internal T Dialog<T>(Func<T> factory) where T : Form
        {
            Form form;
            if(!dialogs.TryGetValue(typeof(T),out form)||form.IsDisposed) { form=factory();dialogs[typeof(T)]=form; }
            form.DialogResult=DialogResult.None;
            return (T)form;
        }
        private void DisposeDialogs()
        { foreach(Form dialog in dialogs.Values)if(!dialog.IsDisposed)dialog.Dispose(); dialogs.Clear(); if(ActiveContext==this)ActiveContext=null; }
        private void ShowWindow(Form window)
        {
            if (!(window is ManagerHub)) modules[window.GetType()] = window;
            CurrentWindow = window;
            window.FormClosing -= WindowClosing; window.FormClosing += WindowClosing;
            window.FormClosed -= WindowClosed; window.FormClosed += WindowClosed;
            var services = window as MainForm;
            if (services != null) services.ResumeSession();
            var module = window as ManagerModuleForm;
            if (module != null) module.ResumeSession();
            if (!window.IsHandleCreated)
                window.Load += delegate {
                    if (window is ManagerHub && hubPlacement != null) hubPlacement.Restore(window);
                    else if(hubPlacement!=null)WindowPlacement.Center(window,hubPlacement.WorkingArea);
                    else WindowPlacement.Constrain(window);
                };
            else WindowPlacement.Constrain(window);
            window.Show();
        }
        private void WindowClosing(object sender, FormClosingEventArgs e)
        {
            Form window = (Form)sender;
            if (shuttingDown || e.Cancel || !MenuRequested(window) ||
                e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing) return;
            // Module handlers have already validated/cancelled their active operation.
            // Keep its handle and controls alive; only the launcher is reconstructed.
            e.Cancel = true;
            if (navigating) return;
            navigating = true;
            window.BeginInvoke((MethodInvoker)delegate {
                try
                {
                    var services=window as MainForm; if(services!=null)services.SuspendSession();
                    window.Hide(); CurrentWindow = null;
                    ShowWindow(createHub());
                }
                catch (Exception error) { Fail(error); }
                finally { navigating = false; }
            });
        }
        private void WindowClosed(object sender, FormClosedEventArgs e)
        {
            Form window = (Form)sender;
            window.FormClosing -= WindowClosing; window.FormClosed -= WindowClosed;
            var hub = window as ManagerHub;
            Func<Form> factory = hub == null ? null : hub.RequestedModule;
            Type type = hub == null ? null : hub.RequestedModuleType;
            if (hub != null) { hubPlacement = new WindowPlacement(); hubPlacement.Capture(hub); }
            CurrentWindow = null; window.Dispose();
            if (factory == null || e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
            { ExitThread(); return; }
            try
            {
                Form next;
                if (type == null || !modules.TryGetValue(type, out next) || next.IsDisposed) next = factory();
                ShowWindow(next);
            }
            catch (Exception error) { Fail(error); }
        }
        private void Fail(Exception error)
        {
            BrandMessageBox.Show("Не удалось открыть утилиту.\r\n\r\n"+error.Message,"BARS Manager 3000",MessageBoxButtons.OK,MessageBoxIcon.Error);
            ExitThread();
        }
        private static bool MenuRequested(Form window)
        {
            var services=window as MainForm; if(services!=null)return services.MenuRequested;
            var library=window as LibraryManagerForm; if(library!=null)return library.MenuRequested;
            var software=window as SoftwareManagerForm; if(software!=null)return software.MenuRequested;
            var utility=window as UtilityModuleForm; return utility!=null&&utility.MenuRequested;
        }
        protected override void ExitThreadCore()
        {
            shuttingDown=true; DisposeDialogs();
            foreach(Form form in modules.Values) if(!form.IsDisposed) form.Dispose();
            modules.Clear();
            if(CurrentWindow!=null&&!CurrentWindow.IsDisposed)CurrentWindow.Dispose();
            CurrentWindow=null;
            base.ExitThreadCore();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing) { shuttingDown=true; DisposeDialogs(); foreach(Form form in modules.Values)if(!form.IsDisposed)form.Dispose(); modules.Clear(); if(CurrentWindow!=null)CurrentWindow.Dispose();CurrentWindow=null; }
            base.Dispose(disposing);
        }
    }
}
