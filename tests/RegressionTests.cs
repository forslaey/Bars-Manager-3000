using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class RegressionTests
    {
        private static int checks, result = 1;
        private static readonly ServiceSnapshot Empty = new ServiceSnapshot { Complete = true, Warning = "", Entries = new List<ServiceEntry>() };
        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                VerifyProcedureWait(); VerifyHelperLease(); VerifyButtonRaster();
                using (var host = new Form { Opacity = 0, ShowInTaskbar = false, Size = new Size(1, 1) })
                {
                    host.Shown += async delegate
                    {
                        try { VerifyClose(); VerifyResize(); VerifyAccountDialog(); await VerifyStableGrid(); Console.WriteLine("PASS: " + checks + " regression checks: resize, busy close, pending installers and fast refresh. No service procedures executed."); result = 0; }
                        catch (Exception ex) { Console.Error.WriteLine(ex); }
                        finally { host.Close(); }
                    };
                    Application.Run(host);
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            return result;
        }

        private static void VerifyButtonRaster()
        {
            foreach (bool dark in new bool[] { false, true })
                foreach (int dpi in new int[] { 96, 120, 144 })
                {
                    var palette = ThemePalette.Create(dark);
                    Color fill = dark ? palette.Surface : palette.Accent;
                    Color edge = dark ? palette.Border : palette.Accent;
                    using (var button = new BrandButton { Palette = palette, Size = new Size(190 * dpi / 96, 40 * dpi / 96) })
                    {
                        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        var paint = typeof(BrandButton).GetMethod("EnsureSurface", flags);
                        var field = typeof(BrandButton).GetField("surface", flags);
                        paint.Invoke(button, new object[] { fill, edge, palette.Canvas, (float)dpi });
                        var image = (Bitmap)field.GetValue(button);
                        bool symmetric = true;
                        var shades = new HashSet<int>();
                        for (int y = 0; y < image.Height; y++)
                            for (int x = 0; x < image.Width; x++)
                            {
                                int pixel = image.GetPixel(x, y).ToArgb();
                                symmetric &= pixel == image.GetPixel(image.Width - 1 - x, y).ToArgb() &&
                                    pixel == image.GetPixel(x, image.Height - 1 - y).ToArgb();
                                if (x < 16 && y < 16) shades.Add(pixel);
                            }
                        Check(symmetric, "Rounded button corners are symmetric at 100/125/150 percent in both themes");
                        Check(shades.Count > 8, "Corner edges contain gradual subpixel coverage rather than hard steps");
                        int middle = image.Width / 2;
                        Check(image.GetPixel(middle, 0).ToArgb() == palette.Canvas.ToArgb() &&
                            image.GetPixel(middle, 1).ToArgb() == edge.ToArgb() &&
                            image.GetPixel(middle, 2).ToArgb() == fill.ToArgb(), "Straight border stays sharp without blur or halo");
                        paint.Invoke(button, new object[] { fill, edge, palette.Canvas, (float)dpi });
                        Check(Object.ReferenceEquals(image, field.GetValue(button)), "Unchanged redraw reuses the surface cache");
                    }
                }
        }

        private static void VerifyAccountDialog()
        {
            foreach (bool dark in new bool[] { false, true })
            {
                var palette = ThemePalette.Create(dark);
                var saved = ServiceInstallProfile.Default(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HttpService.exe"));
                saved.Mode = ServiceInstallMode.ManagedDotNet; saved.Account = InstallAccountKind.NetworkService;
                using (var dialog = new ServiceAccountDialog(palette, saved, false))
                {
                    dialog.Show(); Application.DoEvents();
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var account = (ComboBox)typeof(ServiceAccountDialog).GetField("account", flags).GetValue(dialog);
                    var remember = (BrandCheckBox)typeof(ServiceAccountDialog).GetField("remember", flags).GetValue(dialog);
                    var password = (TextBox)typeof(ServiceAccountDialog).GetField("password", flags).GetValue(dialog);
                    var detail = (Label)typeof(ServiceAccountDialog).GetField("passwordDetail", flags).GetValue(dialog);
                    Check(account.SelectedIndex == (int)InstallAccountKind.LocalSystem, "Saved NetworkService still opens with LocalSystem selected");
                    Check(saved.Account == InstallAccountKind.NetworkService, "Opening dialog does not mutate persisted profile before acceptance");
                    Check(!remember.Enabled, "Password checkbox is disabled for LocalSystem");
                    Check(password.Bottom <= remember.Top && remember.Bottom <= detail.Top, "Password rows do not overlap");
                    foreach (bool enabled in new bool[] { false, true })
                    {
                        remember.Enabled = enabled;
                        using (var bitmap = new Bitmap(remember.Width, remember.Height))
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            graphics.Clear(Color.Magenta);
                            typeof(BrandCheckBox).GetMethod("OnPaint", flags).Invoke(remember,
                                new object[] { new PaintEventArgs(graphics, remember.ClientRectangle) });
                            Check(bitmap.GetPixel(bitmap.Width - 1, bitmap.Height / 2).ToArgb() == palette.Canvas.ToArgb(),
                                "Enabled and disabled checkbox repaint their complete opaque background in both themes");
                        }
                    }
                    dialog.Close();
                }
            }
        }

        private static void VerifyProcedureWait()
        {
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "inert-payment.exe");
            var present = new ServiceSnapshot { Complete = true, Entries = new List<ServiceEntry> {
                new ServiceEntry { Name = "Bars.Payment.Test", BinaryPath = "\"" + exe + "\"", State = "Stopped" } } };
            Func<int, bool> open = delegate(int ms) { Thread.Sleep(ms); return false; };
            int codes = 0, probes = 0;
            Func<int> code = delegate { codes++; return 31; };
            ProcedureWaitResult installed = ProcedureWait.Run(open, code, delegate { probes++; return present; }, exe, true, CancellationToken.None, null, 200, 2);
            Check(!installed.Exited && installed.Reason == ProcedureWaitReason.RegistrationObserved && probes == 2 && codes == 0 && installed.ExitCode == -1,
                "Payment-style process can remain alive after install; stable registration releases manager without inventing exit code 0");
            probes = 0;
            var removed = ProcedureWait.Run(open, code, delegate { probes++; return Empty; }, exe, false, CancellationToken.None, null, 200, 2);
            Check(removed.Reason == ProcedureWaitReason.RegistrationObserved && probes == 2 && codes == 0, "Removal observed twice releases pending uninstaller");
            var partial = new ServiceSnapshot { Complete = false, Entries = new List<ServiceEntry>() };
            Check(ProcedureWait.Run(open, code, delegate { return partial; }, exe, false, CancellationToken.None, null, 12, 2).Reason == ProcedureWaitReason.TimedOut,
                "Incomplete catalog cannot confirm removal");
            Check(ProcedureWait.Run(open, code, delegate { throw new IOException("injected"); }, exe, false, CancellationToken.None, null, 12, 2).Reason == ProcedureWaitReason.TimedOut,
                "Read error cannot confirm removal or block forever");
            Check(ProcedureWait.Run(open, code, delegate { return Empty; }, exe, true, CancellationToken.None, null, 12, 2).Reason == ProcedureWaitReason.TimedOut,
                "Missing registration has bounded wait");
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                Check(ProcedureWait.Run(open, code, delegate { throw new Exception(); }, exe, true, cancel.Token, null).Reason == ProcedureWaitReason.UserStopped,
                    "User can stop waiting without killing installer");
            }
            Check(ProcedureWait.Run(delegate(int ms) { return true; }, code, delegate { throw new Exception(); }, exe, true, CancellationToken.None, null).ExitCode == 31,
                "Actual process exit code is preserved");
            probes = 0;
            ProcedureWaitResult transient = ProcedureWait.Run(open, code, delegate { probes++; return probes == 2 ? Empty : present; }, exe, true, CancellationToken.None, null, 200, 2);
            Check(transient.Reason == ProcedureWaitReason.RegistrationObserved && probes == 4, "Transient registration followed by rollback is not accepted as stable");
            Check(ProcedureWait.DefaultTimeoutMilliseconds == 120000, "Default pending-process wait is two minutes");
        }

        private static void VerifyHelperLease()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "helper-regression-" + Guid.NewGuid().ToString("N"));
            typeof(EmbeddedHelpers).GetField("Root", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, root);
            string file = EmbeddedHelpers.Prepare("x86"), directory = Path.GetDirectoryName(file);
            Task<int> watcher;
            using (EmbeddedHelpers.HoldHelperSession(file))
            {
                EmbeddedHelpers.Cleanup();
                Check(Directory.Exists(directory) && File.Exists(file + ".config"), "Early manager close cannot delete an active helper's files");
                watcher = Task.Factory.StartNew(delegate { return EmbeddedHelpers.RunCleanupWatcher(Path.GetFileName(directory)); });
                Thread.Sleep(50);
                Check(!watcher.IsCompleted && File.Exists(file), "Deferred cleanup waits for the actual helper lease");
            }
            Check(watcher.Wait(3000) && watcher.Result == 0 && !Directory.Exists(directory), "Deferred cleanup removes helper and config after operation completes");
            Check(EmbeddedHelpers.RunCleanupWatcher("..\\outside") == 87 && EmbeddedHelpers.RunCleanupWatcher("session-invalid") == 87, "Cleanup rejects malformed paths");
        }

        private static void VerifyClose()
        {
            using (var form = new MainForm(null, false, delegate { return Empty; }))
            {
                Set(form, "operationRunning", true);
                int questions = 0;
                Set(form, "confirmBusyClose", new Func<DialogResult>(delegate { questions++; return DialogResult.No; }));
                var args = new FormClosingEventArgs(CloseReason.UserClosing, false);
                Call(form, "OnFormClosing", form, args);
                Check(questions == 1 && args.Cancel && !Field<bool>(form, "closing"), "Busy close prompts and No keeps manager open");
                Set(form, "confirmBusyClose", new Func<DialogResult>(delegate { questions++; return DialogResult.Yes; }));
                args = new FormClosingEventArgs(CloseReason.UserClosing, false);
                Call(form, "OnFormClosing", form, args);
                Check(questions == 2 && !args.Cancel && Field<bool>(form, "closing"), "Yes permits immediate exit without stopping installer");
                Check(MainForm.BusyCloseMessage.Contains("оснастке") && MainForm.BusyCloseMessage.Contains("установки, удаления"), "Prompt explicitly tells user to check service procedure in Windows");
                Set(form, "operationRunning", false);
            }
        }

        private static void VerifyResize()
        {
            using (var form = new MainForm(null, false, delegate { return Empty; }))
            {
                form.Opacity = 0; form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                foreach (bool dark in new bool[] { false, true })
                {
                    form.SetTheme(dark, false);
                    Card card = Field<Label>(form, "fileTitle").Parent.Parent as Card;
                    Check(card != null && Style(card, ControlStyles.ResizeRedraw) && Style(card, ControlStyles.OptimizedDoubleBuffer), "Cards redraw their whole resized rounded outline in both themes");
                    var dirty = new List<Rectangle>();
                    using (var standalone = new Card { Size = new Size(240, 100), BackColor = Field<ThemePalette>(form, "palette").Surface })
                    {
                        form.Controls.Add(standalone); standalone.BringToFront(); standalone.CreateControl();
                        standalone.Invalidated += delegate(object sender, InvalidateEventArgs e) { dirty.Add(e.InvalidRect); };
                        dirty.Clear(); standalone.Width = 320;
                        bool repaintsOldEdge = false;
                        foreach (Rectangle area in dirty) if (area.Contains(237, 10)) repaintsOldEdge = true;
                        Check(repaintsOldEdge, "Growing card invalidates old right outline, not only newly exposed strip");
                        form.Controls.Remove(standalone);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        form.WindowState = FormWindowState.Maximized; Application.DoEvents();
                        form.WindowState = FormWindowState.Normal; Application.DoEvents();
                        foreach (Control child in Descendants(form)) if (child is Card) { Check(child.Width > 0 && Style(child, ControlStyles.ResizeRedraw), "Maximize/restore retains full redraw on all cards"); }
                    }
                    TextBox path = Field<TextBox>(form, "pathBox");
                    Label title = Field<Label>(form, "fileTitle");
                    Check(title is FlushLabel && title.PointToScreen(Point.Empty).X == path.PointToScreen(Point.Empty).X, "File drop title aligns exactly with path field");
                    Button button = Field<Button>(form, "refreshButton");
                    DataGridView grid = Field<DataGridView>(form, "servicesGrid");
                    Label detail = Field<Label>(form, "statusDetail");
                    int gapAbove = button.PointToScreen(Point.Empty).Y - (detail.PointToScreen(Point.Empty).Y + detail.Font.Height);
                    int gapBelow = grid.PointToScreen(Point.Empty).Y - (button.PointToScreen(Point.Empty).Y + button.Height);
                    Check(Math.Abs(gapAbove - gapBelow) <= 5, "Registration actions are centered between description text and list");
                }
                Set(form, "operationRunning", false); form.Close();
            }
        }

        private static async Task VerifyStableGrid()
        {
            var data = new ServiceSnapshot { Complete = true, Warning = "", Entries = new List<ServiceEntry>() };
            for (int i = 0; i < 40; i++) data.Entries.Add(new ServiceEntry { Name = "Bars.Test" + i, DisplayName = "Test " + i,
                BinaryPath = "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "inert-" + (i % 5) + ".exe") + "\"", State = "Stopped", StartMode = "Manual", StartupKnown = true });
            using (var form = new MainForm(null, false, delegate { return data; }))
            {
                IntPtr handle = form.Handle;
                await Refresh(form);
                var grid = Field<DataGridView>(form, "servicesGrid");
                DataGridViewRow original = grid.Rows[0];
                int removals = 0;
                grid.RowsRemoved += delegate { removals++; };
                grid.CurrentCell = grid.Rows[15].Cells[0];
                grid.FirstDisplayedScrollingRowIndex = 12;
                var key = new string('A', 150);
                object wrapped = Call(form, "WrapCellText", key, grid.Font, 100);
                object again = Call(form, "WrapCellText", key, grid.Font, 100);
                Check(Object.ReferenceEquals(wrapped, again), "Repeated cell formatting uses bounded cached result");
                await Refresh(form);
                Check(removals == 0 && Object.ReferenceEquals(original, grid.Rows[0]), "Unchanged refresh does not recreate any table rows");
                Check(((ServiceEntry)grid.CurrentRow.Tag).Name == "Bars.Test15" && grid.FirstDisplayedScrollingRowIndex == 12, "Stable refresh retains selection and scroll position");
                Check(Field<Dictionary<string, List<ServiceEntry>>>(form, "indexedServices").Count == 5, "Snapshot paths are indexed once per distinct EXE");
                data.Entries[0].State = "Running"; data.Entries[0].CanStop = true;
                await Refresh(form);
                Check(removals > 0 && Convert.ToString(grid.Rows[0].Cells[2].Value) == "Работает", "Changed status still updates the table");
                Check(grid.FirstDisplayedScrollingRowIndex == 12, "Changed refresh also retains scroll position");
            }
        }
        private static async Task Refresh(MainForm form) { await (Task)Call(form, "RefreshStatusAsync", true); }
        private static bool Style(Control control, ControlStyles style) { return (bool)typeof(Control).GetMethod("GetStyle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { style }); }
        private static IEnumerable<Control> Descendants(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (Control nested in Descendants(child)) yield return nested; } }
        private static object Call(MainForm form, string name, params object[] args) { return typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Invoke(form, args); }
        private static T Field<T>(MainForm form, string name) { return (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
        private static void Set(MainForm form, string name, object value) { typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, value); }
        private static void Check(bool value, string text) { checks++; if (!value) throw new Exception("FAIL: " + text); }
    }
}
