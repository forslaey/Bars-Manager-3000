using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class NavigationTests
    {
        private static int count;
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        private static System.Collections.Generic.IEnumerable<Control> Children(Control parent)
        {
            foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Children(child)) yield return nested; }
        }
        private static T Quiet<T>(T form) where T : Form
        {
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            return form;
        }
        private static ManagerHub Hub() { return Quiet(new ManagerHub()); }
        private static void Pump() { Application.DoEvents(); }
        [STAThread] public static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Func<Form>[] factories = {
                    () => Quiet(new MainForm(null, true)),
                    () => Quiet(new LibraryManagerForm(true)),
                    () => Quiet(new SoftwareManagerForm(true)),
                    () => Quiet(new ToolsDeploymentForm(true)),
                    () => Quiet(new InitialSystemSettingsForm(true))
                };
                foreach (var factory in factories)
                using (var context = new ManagerApplicationContext(() => Hub(), () => Hub()))
                {
                    bool exited = false;
                    context.ThreadExit += delegate { exited = true; };
                    for (int cycle = 0; cycle < 3; cycle++)
                    {
                        var oldHub = (ManagerHub)context.CurrentWindow;
                        Form module = null;
                        oldHub.OpenModule(() => {
                            Check(oldHub.IsDisposed && !oldHub.IsHandleCreated, "Launcher destroyed before module construction");
                            module = factory();
                            return module;
                        });
                        Pump();
                        Check(Object.ReferenceEquals(context.CurrentWindow, module), "Module is current window");
                        Check(module.Visible && module.Enabled && !module.Modal && module.Owner == null, "Independent usable module");
                        Check(!Application.OpenForms.OfType<ManagerHub>().Any(), "No launcher retained while module is open");
                        module.WindowState = FormWindowState.Minimized; Pump();
                        Check(module.WindowState == FormWindowState.Minimized, "Module can minimize");
                        module.WindowState = FormWindowState.Normal; Pump();
                        Children(module).OfType<Button>().Single(b => b.Text == "В меню").PerformClick();
                        Pump();
                        Check(module.IsDisposed, "Module disposed on return");
                        Check(context.CurrentWindow is ManagerHub && context.CurrentWindow.Visible, "Fresh launcher opens on menu request");
                        Check(!Object.ReferenceEquals(oldHub, context.CurrentWindow) && !exited, "Return does not terminate message loop");
                    }
                    context.CurrentWindow.Close(); Pump();
                    Check(exited && context.CurrentWindow == null, "Launcher close exits context");
                }
                foreach (var factory in factories)
                using (var context = new ManagerApplicationContext(factory, () => Hub()))
                {
                    bool exited = false;
                    context.ThreadExit += delegate { exited = true; };
                    var module = context.CurrentWindow;
                    FormClosingEventHandler cancel = (sender, e) => e.Cancel = true;
                    module.FormClosing += cancel;
                    module.Close(); Pump();
                    Check(!exited && context.CurrentWindow == module && !module.IsDisposed, "Cancelled close keeps module and context alive");
                    module.FormClosing -= cancel;
                    module.Close(); Pump();
                    Check(exited && context.CurrentWindow == null, "Module cross exits without reopening launcher");
                }
                // Actual Application.Run loop: direct services start -> menu -> launcher close.
                using (var context = new ManagerApplicationContext(() => Quiet(new MainForm(null, true)), () => Hub()))
                using (var timer = new Timer { Interval = 100 })
                {
                    int step = 0;
                    Exception failure = null;
                    timer.Tick += delegate {
                        try
                        {
                            if (step++ == 0) Children(context.CurrentWindow).OfType<Button>().Single(b => b.Text == "В меню").PerformClick();
                            else { Check(context.CurrentWindow is ManagerHub, "Direct service start returns to launcher in real loop"); timer.Stop(); context.CurrentWindow.Close(); }
                        }
                        catch (Exception ex) { failure = ex; timer.Stop(); context.ExitThread(); }
                    };
                    timer.Start();
                    Application.Run(context);
                    if (failure != null) throw failure;
                    Check(context.CurrentWindow == null, "Real message loop exits cleanly");
                }
                // Exercise actual launcher button wiring without showing live modules.
                foreach (string caption in new[] { "Менеджер служб", "Менеджер библиотек", "Установка программ", "Распаковка утилит", "Открыть настройки" })
                using (var hub = Hub())
                {
                    hub.Show(); Pump();
                    Children(hub).OfType<Button>().Single(b => b.Text == caption).PerformClick();
                    Check(hub.IsDisposed && hub.RequestedModule != null, "Launcher button requests navigation: " + caption);
                    using (var module = hub.RequestedModule())
                        Check(module is MainForm || module is LibraryManagerForm || module is SoftwareManagerForm || module is UtilityModuleForm,
                            "Launcher factory creates a supported module: " + caption);
                }
                // Nested dialogs retain their immediate module, not a hidden launcher.
                using (var context = new ManagerApplicationContext(() => Quiet(new LibraryManagerForm(true)), () => Hub()))
                {
                    Form parent = context.CurrentWindow;
                    foreach (Form dialog in new Form[] { Quiet(new WindowsFeaturesForm(true)), Quiet(new UserGuideForm()) })
                    using (dialog)
                    using (var closer = new Timer { Interval = 50 })
                    {
                        closer.Tick += delegate { closer.Stop(); dialog.Close(); };
                        closer.Start();
                        dialog.ShowDialog(parent);
                        Check(context.CurrentWindow == parent && parent.Visible && !parent.IsDisposed,
                            "Nested dialog returns to existing module");
                        Check(!Application.OpenForms.OfType<ManagerHub>().Any(), "Nested dialog does not resurrect launcher");
                    }
                    parent.Close();
                }
                Check(Application.OpenForms.Count == 0, "No orphan windows after cycles");
                Console.WriteLine("PASS: " + count + " navigation lifecycle checks; five preview modules, repeated menu/cross/cancel/minimize and real message loop. No system changes.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
