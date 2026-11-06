using System;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class NavigationTests
    {
        [STAThread]
        public static int Main()
        {
            try
            {
                var hub = new ManagerHub();
                using (var context = new ManagerApplicationContext(() => hub))
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
                    services.Close();
                    libraries.Close();
                    hub.Close();
                    if (context.CurrentWindow != null)
                        throw new InvalidOperationException("Closing the last window did not end the session.");
                }
                Console.WriteLine("PASS: modeless navigation, one instance per module and session close.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}