using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class AuditRegressionTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static T Get<T>(object target, string name)
        { return (T)target.GetType().GetField(name, Fields).GetValue(target); }

        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static void PressSpace(ServiceGrid grid)
        {
            grid.CurrentCell = grid.Rows[0].Cells[1];
            typeof(ServiceGrid).GetMethod("OnKeyDown", Fields).Invoke(grid, new object[] { new KeyEventArgs(Keys.Space) });
        }

        [STAThread]
        public static int Main()
        {
            try
            {
                var first = new BundleDifferenceItem { Path = "a.exe", Allowed = true };
                var last = new BundleDifferenceItem { Path = "z.exe", Allowed = true };
                using (var form = new BundleDifferencesForm(new[] { first, last }))
                {
                    var grid = Get<ServiceGrid>(form, "grid");
                    grid.Sort(grid.Columns[1], ListSortDirection.Descending);
                    PressSpace(grid);
                    Check(last.Selected && !first.Selected, "Cloud selection must follow sorted row.");
                }
                using (var form = new DotNetUpdatesForm(() => null))
                {
                    var grid = Get<ServiceGrid>(form, "grid");
                    grid.Sort(grid.Columns[1], ListSortDirection.Descending);
                    var selected = (DotNetUpdate)grid.Rows[0].Tag;
                    Check((string)grid.Rows[0].Cells[1].Value == selected.Local.Name, "Runtime row identity after sorting.");
                    selected.Version = "999.0.0";
                    PressSpace(grid);
                    Check((bool)grid.Rows[0].Cells[0].Value, "Runtime selection must follow sorted row.");
                }
                using (var form = new BundleDownloadForm(@"C:\audit"))
                {
                    Get<TextBox>(form, "folder").Text = " ";
                    var scan = (Task)typeof(BundleDownloadForm).GetMethod("Scan", Fields).Invoke(form, new object[] { (BundleCloud?)BundleCloud.Yandex });
                    scan.GetAwaiter().GetResult();
                    Check(!Get<bool>(form, "busy"), "Invalid path must not lock the dialog.");
                }
                using (var dialog = new SoftwareRemoteDialog(ThemePalette.Create(false), null, "anydesk", false))
                {
                    Check(!dialog.Controls.OfType<TableLayoutPanel>().SelectMany(p => p.Controls.OfType<CheckBox>()).Any(c => c.Text.Contains("стандартный пароль")),
                        "Remote dialog must not offer an embedded password.");
                }
                IDisposable lease;
                Check(WindowsOperationQueue.TryEnter(out lease), "Queue initially available.");
                var completion = new TaskCompletionSource<bool>();
                WindowsOperationQueue.ReleaseAfter(lease, completion.Task, "fixture.exe");
                Check(WindowsOperationQueue.WaitingMessage.Contains("fixture.exe"), "Detached holder is named.");
                IDisposable next = null;
                Check(!WindowsOperationQueue.TryEnter(out next), "External operation must retain the queue.");
                completion.SetResult(true);
                Check(SpinWait.SpinUntil(() => WindowsOperationQueue.TryEnter(out next), 2000), "Queue released after process completion.");
                next.Dispose();
                Console.WriteLine("PASS: sorted selections, malformed path, password UI and queue ownership.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}