using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class ServiceRemovalCancellationTests
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new MainForm(null, true))
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    var method = typeof(MainForm).GetMethod("PrepareServiceRemovalAsync",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var result = (Task<bool>)method.Invoke(form, new object[] { "fixture.exe", cancellation.Token });
                    if (!result.IsCanceled) throw new InvalidOperationException("Cancellation must precede the SCM read.");
                }
                Console.WriteLine("PASS: service removal honors cancellation before pre-stop without reading SCM.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}