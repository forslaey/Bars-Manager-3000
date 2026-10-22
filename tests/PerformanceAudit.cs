using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Preview forms only: no inventory, library payloads, network requests or installers.
internal static class PerformanceAudit
{
    private static Assembly product;
    private static string output;
    private static readonly Dictionary<string, List<double>> times = new Dictionary<string, List<double>>();
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object serviceSnapshot;
    private static T ReadSnapshot<T>() { return (T)serviceSnapshot; }
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flag);

    private static Form Create(string name)
    {
        object[] args = name == "MainForm" ? new object[] { null, true } :
            name == "WindowsFeaturesForm" ? new object[] { true, true } :
            name == "ManagerHub" || name == "UserGuideForm" ? new object[0] : new object[] { true };
        return (Form)Activator.CreateInstance(product.GetType("ServiceTool." + name, true),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, CultureInfo.InvariantCulture);
    }
    private static void Time(string name, Action action)
    {
        var watch = Stopwatch.StartNew(); action(); watch.Stop();
        if (!times.ContainsKey(name)) times[name] = new List<double>();
        times[name].Add(watch.Elapsed.TotalMilliseconds);
    }
    private static void Show(Form form)
    {
        form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-22000, -22000); form.Show(); Application.DoEvents();
    }
    private static void Theme(Form form, bool dark)
    { form.GetType().GetMethod("SetTheme", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(form, new object[] { dark, false }); Application.DoEvents(); }
    private static string Resources(string label)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Application.DoEvents();
        using (var process = Process.GetCurrentProcess())
        {
            process.Refresh();
            return String.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5}", label,
                GetGuiResources(process.Handle, 0), GetGuiResources(process.Handle, 1), process.HandleCount,
                GC.GetTotalMemory(false), process.PrivateMemorySize64);
        }
    }
    [STAThread] private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            product = Assembly.LoadFrom(Path.GetFullPath(args[0])); output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            if (args.Length > 2 && args[2] == "services") { ServiceStress(); return 0; }
            string[] names = { "ManagerHub", "MainForm", "SoftwareManagerForm", "LibraryManagerForm", "WindowsFeaturesForm", "ToolsDeploymentForm", "UserGuideForm" };
            foreach (string name in names) using (Form form = Create(name)) { Show(form); Theme(form, true); Theme(form, false); form.Close(); }
            var resources = new List<string> { "checkpoint,gdi,user,handles,managed_bytes,private_bytes", Resources("warm") };
            for (int round = 0; round < 30; round++)
            {
                foreach (string name in names)
                {
                    Form form = null;
                    try
                    {
                        Time(name + ".open", delegate { form = Create(name); Show(form); });
                        Time(name + ".theme", delegate { Theme(form, round % 2 == 0); });
                        if (round < 10) Time(name + ".resize", delegate {
                            Size size = form.ClientSize; for (int i = 0; i < 10; i++) { form.ClientSize = new Size(size.Width + i * 3, size.Height + i * 2); form.PerformLayout(); Application.DoEvents(); }
                        });
                        if (name == "UserGuideForm")
                        {
                            TextBox search = (TextBox)form.GetType().GetField("search", Fields).GetValue(form);
                            foreach (string query in new[] { "а", "служб", "перезагрузка", "NOT_FOUND_123", "" })
                                Time("Guide.search." + (query.Length == 0 ? "clear" : query), delegate { search.Text = query; Application.DoEvents(); });
                        }
                        if (name == "MainForm" && round < 2)
                        {
                            Control frame = (Control)form.GetType().GetField("pathField", Fields).GetValue(form);
                            TextBox edit = (TextBox)form.GetType().GetField("pathBox", Fields).GetValue(form);
                            edit.Focus(); edit.SelectAll(); Application.DoEvents();
                            using (var bitmap = new Bitmap(frame.Width, frame.Height)) { frame.DrawToBitmap(bitmap, frame.ClientRectangle); bitmap.Save(Path.Combine(output, "exe-focused-" + round + ".png")); }
                        }
                        form.Close();
                    }
                    finally { if (form != null) form.Dispose(); }
                }
                if (round == 4 || round == 14 || round == 29) resources.Add(Resources("round-" + (round + 1)));
            }
            File.WriteAllLines(Path.Combine(output, "resources.csv"), resources);
            var rows = new List<string> { "operation,n,min_ms,median_ms,p95_ms,max_ms" };
            foreach (var pair in times)
            {
                double[] values = pair.Value.OrderBy(v => v).ToArray();
                string row = String.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F2},{3:F2},{4:F2},{5:F2}", pair.Key, values.Length,
                    values[0], values[values.Length / 2], values[(int)Math.Ceiling(values.Length * .95) - 1], values[values.Length - 1]);
                rows.Add(row); Console.WriteLine(row);
            }
            File.WriteAllLines(Path.Combine(output, "timings.csv"), rows);
            foreach (string row in resources) Console.WriteLine(row);
            Console.WriteLine("PASS: 210 preview window cycles, 150 guide searches, 700 resize steps. No actual component/library operations.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void ServiceStress()
    {
        var snapshotType = product.GetType("ServiceTool.ServiceSnapshot", true);
        var entryType = product.GetType("ServiceTool.ServiceEntry", true);
        var formType = product.GetType("ServiceTool.MainForm", true);
        var read = typeof(PerformanceAudit).GetMethod("ReadSnapshot", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(snapshotType);
        Delegate reader = Delegate.CreateDelegate(typeof(Func<>).MakeGenericType(snapshotType), read);
        var rows = new List<string> { "services,operation,median_ms,p95_ms,max_ms" };
        foreach (int count in new[] { 100, 500, 1000 })
        {
            serviceSnapshot = Activator.CreateInstance(snapshotType);
            IList entries = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType));
            for (int i = 0; i < count; i++)
            {
                object entry = Activator.CreateInstance(entryType);
                entryType.GetField("Name").SetValue(entry, "Bars.Perf" + i);
                entryType.GetField("DisplayName").SetValue(entry, "Synthetic BARS service " + i);
                entryType.GetField("BinaryPath").SetValue(entry, "\"C:\\fixture\\service" + i % 20 + ".exe\"");
                entryType.GetField("State").SetValue(entry, "Stopped");
                entryType.GetField("StartMode").SetValue(entry, "Manual");
                entries.Add(entry);
            }
            snapshotType.GetField("Entries").SetValue(serviceSnapshot, entries);
            snapshotType.GetField("Complete").SetValue(serviceSnapshot, true);
            snapshotType.GetField("Warning").SetValue(serviceSnapshot, "");
            using (Form form = (Form)Activator.CreateInstance(formType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { null, false, reader }, CultureInfo.InvariantCulture))
            {
                Show(form);
                var refresh = formType.GetMethod("RefreshStatusAsync", Fields);
                Action update = delegate {
                    Task task = (Task)refresh.Invoke(form, new object[] { true });
                    var deadline = Stopwatch.StartNew();
                    while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(1); if (deadline.ElapsedMilliseconds > 15000) throw new TimeoutException("Synthetic refresh timed out"); }
                    task.GetAwaiter().GetResult(); Application.DoEvents();
                };
                var initial = Stopwatch.StartNew();
                while ((bool)formType.GetField("refreshing", Fields).GetValue(form)) { Application.DoEvents(); Thread.Sleep(1); if (initial.ElapsedMilliseconds > 15000) throw new TimeoutException(); }
                ((System.Windows.Forms.Timer)formType.GetField("timer", Fields).GetValue(form)).Stop();
                var grid = (DataGridView)formType.GetField("servicesGrid", Fields).GetValue(form);
                if (grid.RowCount != count) throw new Exception("Synthetic services are not displayed");
                foreach (string mode in new[] { "unchanged", "changed" })
                {
                    var samples = new List<double>();
                    for (int repeat = 0; repeat < 20; repeat++)
                    {
                        if (mode == "changed") entryType.GetField("State").SetValue(entries[0], repeat % 2 == 0 ? "Running" : "Stopped");
                        var clock = Stopwatch.StartNew(); update(); samples.Add(clock.Elapsed.TotalMilliseconds);
                    }
                    samples.Sort();
                    string row = String.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F2},{3:F2},{4:F2}", count, mode, samples[10], samples[18], samples[19]);
                    rows.Add(row); Console.WriteLine(row);
                }
                form.Close();
            }
        }
        serviceSnapshot = null;
        File.WriteAllLines(Path.Combine(output, "service-refresh.csv"), rows);
        Console.WriteLine("PASS: 120 synthetic catalog refreshes, 100/500/1000 rows. No SCM reads or mutations.");
    }
}
