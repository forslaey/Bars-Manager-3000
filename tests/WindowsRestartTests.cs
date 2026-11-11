using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class WindowsRestartTests
    {
        private static int count;
        private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); count++; }
        private static T Field<T>(object form, string name) { return (T)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
        private static void Call(object form, string name, params object[] args) { form.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args); }
        private static void Show(Form form) { form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.Show(); Application.DoEvents(); }
        [STAThread] public static int Main()
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                foreach (bool dark in new[] { false, true })
                {
                    using (var features = new WindowsFeaturesForm(true, true))
                    {
                        features.SetTheme(dark, false); Show(features);
                        features.RecordRestartResults(new[] { new LibraryInstallResult { ExitCode = 0 } });
                        Check(!features.RestartRequired, "Successful install alone does not request reboot");
                        features.RecordRestartResults(new[] { new LibraryInstallResult { ExitCode = 5 } });
                        Check(!features.RestartRequired, "Failed install alone does not request reboot");
                        Call(features, "RecordFeatureStates", "TelnetClient=Enabled\nTFTP=Enabled");
                        Check(!features.RestartRequired, "Installed Telnet/TFTP without pending state do not request reboot");
                        features.RecordRestartResults(new[] { new LibraryInstallResult { ExitCode = 3010 } });
                        Check(features.RestartRequired, "DISM RestartNeeded mapped to 3010 persists");
                        features.RecordRestartResults(new[] { new LibraryInstallResult { ExitCode = 0 } });
                        Call(features, "RecordFeatureStates", "TelnetClient=Enabled\nTFTP=Enabled");
                        Check(features.RestartRequired, "Later success or enabled states cannot clear a confirmed reboot request");
                        Call(features, "RecordFeatureStates", "");
                        Check(features.RestartRequired, "Missing follow-up result cannot erase confirmed request");
                        features.Close();
                    }
                    foreach (string pending in new[] { "EnablePending", "DisablePending" })
                    using (var features = new WindowsFeaturesForm(true, true))
                    {
                        Call(features, "RecordFeatureStates", "TFTP=" + pending);
                        Check(features.RestartRequired, pending + " independently confirms pending servicing");
                    }
                    using (var library = new LibraryManagerForm(true))
                    {
                        library.SetTheme(dark, false); Show(library);
                        Check(!Field<bool>(library, "pendingRestart"), "No synthetic reboot request on entry");
                        using (var timer = new Timer { Interval = 100 })
                        {
                            bool returned = false;
                            timer.Tick += delegate
                            {
                                var features = Application.OpenForms.OfType<WindowsFeaturesForm>().FirstOrDefault();
                                if (features == null) return;
                                timer.Stop(); features.RecordRestartResults(new[] { new LibraryInstallResult { ExitCode = 3010 } });
                                returned = true; features.Close();
                            };
                            timer.Start(); var task=(System.Threading.Tasks.Task)library.GetType().GetMethod("OpenWindowsComponents",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(library,null);var deadline=DateTime.UtcNow.AddSeconds(10);while(!task.IsCompleted&&DateTime.UtcNow<deadline){Application.DoEvents();System.Threading.Thread.Sleep(10);}Check(task.IsCompleted,"Component dialog task completed");task.GetAwaiter().GetResult();
                            Check(returned, "Completed owned component window returned to library manager");
                        }
                        Check(Field<bool>(library, "pendingRestart"), "Request transferred from actual child dialog");
                        var button = Field<BrandButton>(library, "restart");
                        Check(button.Enabled && button.AccessibleDescription.Contains("необходимости"), "Parent restart button immediately enabled with explanation");
                        library.RememberWindowsRestart(false);
                        Check(button.Enabled, "Returning without another request does not forget previous requirement");
                        typeof(LibraryManagerForm).GetField("checking", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(library, true);
                        Call(library, "UpdateRestartAvailability"); Check(!button.Enabled, "Restart blocked during system scan");
                        typeof(LibraryManagerForm).GetField("checking", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(library, false);
                        Call(library, "UpdateRestartAvailability"); Check(button.Enabled, "Restart re-enabled after scan completes");
                        library.Close();
                    }
                }
                Console.WriteLine("PASS: " + count + " Windows component restart checks; synthetic results and preview dialogs only. No DISM, installers, registry writes or restart."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
