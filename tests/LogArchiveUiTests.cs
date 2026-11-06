using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    // Only injected snapshots, in-memory catalogs and inert .exe fixtures are used.
    // No archive command, service command, file picker or Save As dialog is invoked.
    internal static class LogArchiveUiTests
    {
        private static int checks;
        private static int result = 1;
        private static string executableA;
        private static string executableB;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "log-archive-ui-fixtures", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixture);
            executableA = Path.Combine(fixture, "Служба А.exe");
            executableB = Path.Combine(fixture, "Служба Б.exe");
            File.WriteAllText(executableA, "Inert UI fixture, not a PE executable.");
            File.WriteAllText(executableB, "Inert UI fixture, not a PE executable.");
            using (var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                Location = new Point(-30000, -30000), Size = new Size(1, 1), Opacity = 0 })
            {
                host.Shown += async delegate
                {
                    try
                    {
                        await CheckSelectionMappingAsync();
                        await CheckStatesAsync();
                        await CheckStaleAndFailedReadAsync();
                        CheckDateDialogSelection();
                        CheckScaledGeometry();
                        Console.WriteLine("PASS LogArchiveUiTests: " + checks
                            + " checks. Injected catalogs and inert EXEs only; no archives or file dialogs opened.");
                        result = 0;
                    }
                    catch (Exception error) { Console.Error.WriteLine(error); }
                    finally { host.Close(); }
                };
                Application.Run(host);
            }
            return result;
        }

        private static MainForm NewForm(Func<ServiceSnapshot> reader)
        {
            ManagedServiceStore store = ManagedServiceStore.CreateMemory();
            store.Remember(executableA);
            store.Remember(executableB);
            MainForm form = new MainForm(null, false, reader, store);
            IntPtr handle = form.Handle;
            return form;
        }

        private static ServiceEntry Entry(string name, string executable, string state, string startMode)
        {
            return new ServiceEntry { Name = name, DisplayName = "Название " + name,
                BinaryPath = "\"" + executable + "\" --service", State = state, StartMode = startMode,
                CanStop = true, StartupKnown = true };
        }

        private static ServiceSnapshot Snapshot(bool complete, params ServiceEntry[] entries)
        {
            return new ServiceSnapshot { Complete = complete, Warning = complete ? "" : "Injected partial read",
                Entries = new List<ServiceEntry>(entries) };
        }

        private static ServiceSnapshot Both()
        {
            return Snapshot(true, Entry("bars-A", executableA, "Running", "Manual"),
                Entry("bars-B", executableB, "Stopped", "Manual"));
        }

        private static async Task CheckSelectionMappingAsync()
        {
            int reads = 0;
            using (MainForm form = NewForm(delegate { ++reads; return Both(); }))
            {
                await Refresh(form);
                Assert(Grid(form).Rows.Count == 2, "restored catalog contains both services");
                Assert(Grid(form).CurrentRow == null && !CollectButton(form).Enabled,
                    "collection waits for an explicitly selected service row");
                string name, executable;
                Assert(!Source(form, out name, out executable), "no row cannot identify a log source");

                Select(form, "bars-A");
                Assert(CollectButton(form).Enabled, "selecting A enables collection");
                Assert(Source(form, out name, out executable) && name == "bars-A"
                    && String.Equals(executable, executableA, StringComparison.OrdinalIgnoreCase),
                    "A's row identifies A's system name and exact .exe");

                // Registration actions have a separate .exe target. A collector must use the row mapping.
                SetField(form, "selectedPath", executableB);
                Field<TextBox>(form, "pathBox").Text = executableB;
                Assert(Source(form, out name, out executable)
                    && String.Equals(executable, executableA, StringComparison.OrdinalIgnoreCase),
                    "a different .exe in the upper field cannot redirect collection from A's row");

                Select(form, "bars-B");
                Assert(CollectButton(form).Enabled && Source(form, out name, out executable)
                    && name == "bars-B" && String.Equals(executable, executableB, StringComparison.OrdinalIgnoreCase),
                    "switching to B resolves B's own logs instead of the previous service");
                Assert(reads == 1, "row selection resolves stored catalog mappings without another SCM read");
                Grid(form).CurrentCell = null;
                Grid(form).ClearSelection();
                Assert(!CollectButton(form).Enabled && !Source(form, out name, out executable),
                    "an upper .exe target alone does not enable collection after the row is cleared");
            }
        }

        private static async Task CheckStatesAsync()
        {
            string state = "Running";
            string startMode = "Manual";
            using (MainForm form = NewForm(delegate {
                return Snapshot(true, Entry("bars-A", executableA, state, startMode));
            }))
            {
                foreach (string[] pair in new[] {
                    new[] { "Running", "Manual" }, new[] { "Stopped", "Manual" },
                    new[] { "Stopped", "Disabled" }, new[] { "StartPending", "Auto" },
                    new[] { "StopPending", "Auto" } })
                {
                    state = pair[0];
                    startMode = pair[1];
                    await Refresh(form);
                    Select(form, "bars-A");
                    string name, executable;
                    Assert(CollectButton(form).Enabled && Source(form, out name, out executable)
                        && String.Equals(executable, executableA, StringComparison.OrdinalIgnoreCase),
                        "verified " + state + "/" + startMode + " service can collect logs independently of start/stop availability");
                }
            }
        }

        private static async Task CheckStaleAndFailedReadAsync()
        {
            int reads = 0;
            using (MainForm form = NewForm(delegate {
                ++reads;
                if (reads == 1) return Both();
                if (reads == 2) return Snapshot(false, Entry("bars-B", executableB, "Running", "Manual"));
                throw new IOException("Injected catalog read failure");
            }))
            {
                await Refresh(form);
                Select(form, "bars-A");
                await Refresh(form);
                string name, executable;
                Assert(Grid(form).Rows.Count == 2 && !CollectButton(form).Enabled
                    && !Source(form, out name, out executable),
                    "a retained stale A row cannot authorize log collection after a partial read");
                Select(form, "bars-B");
                Assert(CollectButton(form).Enabled && Source(form, out name, out executable)
                    && String.Equals(executable, executableB, StringComparison.OrdinalIgnoreCase),
                    "a directly observed B row remains collectible during an otherwise partial read");
                await Refresh(form);
                Assert(Grid(form).Rows.Count == 2 && !CollectButton(form).Enabled
                    && !Source(form, out name, out executable),
                    "a total catalog read error retains rows but disables collection from unverified mappings");
            }
        }

        private static void CheckScaledGeometry()
        {
            foreach (float targetScale in new[] { 1F, 1.25F, 1.5F })
            using (MainForm form = new MainForm(null, true, Both, ManagedServiceStore.CreateMemory()))
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.Show(); // Preview mode suppresses catalog reads and timer startup.
                Application.DoEvents();
                float systemScale;
                using (Graphics graphics = form.CreateGraphics()) systemScale = graphics.DpiX / 96F;
                form.AutoScaleMode = AutoScaleMode.None;
                form.MinimumSize = Size.Empty;
                form.Scale(new SizeF(targetScale / systemScale, targetScale / systemScale));
                form.PerformLayout();
                foreach (bool dark in new[] { false, true })
                {
                    form.SetTheme(dark, false);
                    form.PerformLayout();
                    Button collect = CollectButton(form);
                    ComboBox startup = Field<ComboBox>(form, "startupCombo");
                    VerifyFits(collect, "collection button", targetScale, dark);
                    VerifyFits(startup, "startup selector", targetScale, dark);
                    Rectangle collectBounds = form.RectangleToClient(collect.RectangleToScreen(collect.ClientRectangle));
                    Rectangle startupBounds = form.RectangleToClient(startup.RectangleToScreen(startup.ClientRectangle));
                    Assert(!collectBounds.IntersectsWith(startupBounds),
                        "collection and startup controls do not overlap at simulated " + targetScale + "/dark=" + dark);
                    Size text = TextRenderer.MeasureText(collect.Text, collect.Font);
                    Assert(text.Width + 12 <= collect.ClientSize.Width && text.Height + 4 <= collect.ClientSize.Height,
                        "collection label fits without clipping at simulated " + targetScale + "/dark=" + dark);
                }
            }
        }

        private static void CheckDateDialogSelection()
        {
            DateTime today = new DateTime(2026, 10, 15);
            var available = new List<LogDateFolder>();
            for (int i = 0; i < 26; ++i)
            {
                DateTime date = new DateTime(2026, 10, 1).AddDays(i);
                available.Add(new LogDateFolder { Name = date.ToString("yyyy.MM.dd"), Date = date });
            }
            foreach (bool dark in new[] { false, true })
            using (var dialog = new LogArchiveDialog(ThemePalette.Create(dark), "Служба А", available, today))
            {
                ShowOffscreen(dialog);
                ComboBox scope = DialogField<ComboBox>(dialog, "scope");
                TableLayoutPanel dates = DialogField<TableLayoutPanel>(dialog, "dates");
                Assert(dialog.Text == "Сбор логов", "date dialog uses the requested title (dark=" + dark + ")");
                Assert(((Button)dialog.AcceptButton).Text == "Выбрать путь...",
                    "date dialog describes choosing a destination path (dark=" + dark + ")");
                VerifyDialogButtons(dialog, dark);
                Assert(scope.SelectedIndex == 0, "Today is preselected when present");
                scope.SelectedIndex = 2;
                Assert(dates.Controls.Count == 12
                    && MarkedDates(dates).Count == 12 && AllDateChoicesDisabled(dates),
                    "All displays every visible dated folder checked and inactive (dark=" + dark + ")");
                CheckBox allDate = (CheckBox)dates.Controls[0];
                VerifyDateTextPaint(allDate, ThemePalette.Create(dark), "All", dark);
                AssertUnchangedByClick(allDate, "All", dark);
                scope.SelectedIndex = 0;
                Assert(MarkedDates(dates).Count == 0 && AllDateChoicesDisabled(dates),
                    "Today leaves nonmatching dates unchecked and inactive (dark=" + dark + ")");
                VerifyDateTextPaint((CheckBox)dates.Controls[0], ThemePalette.Create(dark), "Today unchecked", dark);
                AssertUnchangedByClick((CheckBox)dates.Controls[0], "Today unchecked", dark);
                DialogField<Button>(dialog, "next").PerformClick();
                HashSet<string> todayMarks = MarkedDates(dates);
                Assert(todayMarks.Count == 1 && todayMarks.Contains("2026.10.15") && AllDateChoicesDisabled(dates),
                    "Today marks only the matching folder on its page (dark=" + dark + ")");
                CheckBox todayDate = null;
                foreach (CheckBox item in dates.Controls)
                    if (item.Checked) todayDate = item;
                VerifyDateTextPaint(todayDate, ThemePalette.Create(dark), "Today checked", dark);
                AssertUnchangedByClick(todayDate, "Today checked", dark);
                DialogField<Button>(dialog, "previous").PerformClick();
                scope.SelectedIndex = 1;
                Assert(MarkedDates(dates).Count == 0,
                    "All and Today display marks do not populate the manual selection (dark=" + dark + ")");
                ((Button)dialog.AcceptButton).PerformClick();
                Assert(dialog.Selection == null && dialog.DialogResult != DialogResult.OK,
                    "empty multiple-date selection remains open instead of saving (dark=" + dark + ")");
                CheckBox firstDate = (CheckBox)dates.Controls[0];
                int manualChanges = 0;
                firstDate.CheckedChanged += delegate { ++manualChanges; };
                ClickDate(firstDate);
                Assert(firstDate.Enabled && firstDate.Checked && manualChanges == 1,
                    "a user click toggles the selected-date checkbox exactly once (dark=" + dark + ")");
                VerifyDateTextPaint(firstDate, ThemePalette.Create(dark), "Selected", dark);
                DialogField<Button>(dialog, "next").PerformClick();
                ((CheckBox)dates.Controls[0]).Checked = true;
                DialogField<Button>(dialog, "next").PerformClick();
                Assert(dates.Controls.Count == 2 && !DialogField<Button>(dialog, "next").Enabled,
                    "last date page contains only remaining folders (dark=" + dark + ")");
                ((CheckBox)dates.Controls[1]).Checked = true;
                DialogField<Button>(dialog, "previous").PerformClick();
                DialogField<Button>(dialog, "previous").PerformClick();
                Assert(((CheckBox)dates.Controls[0]).Checked && !DialogField<Button>(dialog, "previous").Enabled,
                    "returning to the first page restores its checkbox selection (dark=" + dark + ")");
                scope.SelectedIndex = 0;
                scope.SelectedIndex = 2;
                scope.SelectedIndex = 1;
                ((Button)dialog.AcceptButton).PerformClick();
                var selected = new HashSet<string>(dialog.Selection.FolderNames, StringComparer.OrdinalIgnoreCase);
                Assert(dialog.DialogResult == DialogResult.OK && dialog.Selection.Scope == LogArchiveScope.SelectedFolders
                    && selected.Count == 3 && selected.Contains("2026.10.01")
                    && selected.Contains("2026.10.13") && selected.Contains("2026.10.26"),
                    "confirmation captures selections from all pages across scope changes (dark=" + dark + ")");
            }

            using (var dialog = new LogArchiveDialog(ThemePalette.Create(true), "Без сегодняшних логов",
                new List<LogDateFolder> { new LogDateFolder { Name = "2026.10.14", Date = today.AddDays(-1) } }, today))
            {
                ShowOffscreen(dialog);
                DialogField<ComboBox>(dialog, "scope").SelectedIndex = 0;
                ((Button)dialog.AcceptButton).PerformClick();
                Assert(dialog.Selection == null && dialog.DialogResult != DialogResult.OK,
                    "Today cannot confirm when no matching dated folder exists");
            }
            using (var dialog = new LogArchiveDialog(ThemePalette.Create(false), "Сегодня", available, today))
            {
                ShowOffscreen(dialog);
                DialogField<ComboBox>(dialog, "scope").SelectedIndex = 0;
                ((Button)dialog.AcceptButton).PerformClick();
                Assert(dialog.Selection != null && dialog.Selection.Scope == LogArchiveScope.Today
                    && dialog.Selection.Today.Date == today,
                    "Today confirmation captures the date used by the folder filter");
            }
            using (var dialog = new LogArchiveDialog(ThemePalette.Create(false), "Логи в корне",
                new List<LogDateFolder>(), today))
            {
                ShowOffscreen(dialog);
                ((Button)dialog.AcceptButton).PerformClick();
                Assert(dialog.Selection != null && dialog.Selection.Scope == LogArchiveScope.All,
                    "All remains valid without dated folders so root TXT logs can still be collected");
            }
        }

        private static void AssertUnchangedByClick(CheckBox item, string scopeName, bool dark)
        {
            bool original = item.Checked;
            int changes = 0;
            item.CheckedChanged += delegate { ++changes; };
            item.Select();
            ClickDate(item);
            Assert(!item.Enabled && !item.CanSelect && !item.Focused && item.Checked == original && changes == 0,
                scopeName + " read-only date cannot receive user input or change selection (dark=" + dark + ")");
        }

        private static void ClickDate(CheckBox item)
        {
            // Native activation exercises the enabled checkbox and the explicit read-only
            // guard, including synthetic activation that bypasses normal input routing.
            SendMessage(item.Handle, 0x00F5, IntPtr.Zero, IntPtr.Zero);
            Application.DoEvents();
        }

        private static void VerifyDateTextPaint(CheckBox item, ThemePalette palette, string scopeName, bool dark)
        {
            Assert(item != null && item.Width > 0 && item.Height > 0,
                scopeName + " has a visible date control to render (dark=" + dark + ")");
            item.Update();
            using (var bitmap = new Bitmap(item.Width, item.Height))
            {
                item.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                // Exclude the check glyph and all outer edges. Checking the actual pixels
                // catches the native disabled-text rendering that ignores ForeColor.
                float scale;
                using (Graphics graphics = item.CreateGraphics()) scale = graphics.DpiX / 96F;
                int left = Math.Min(bitmap.Width - 1, (int)Math.Ceiling(34 * scale));
                int top = Math.Min(bitmap.Height - 1, (int)Math.Ceiling(3 * scale));
                int right = Math.Max(left, bitmap.Width - (int)Math.Ceiling(5 * scale));
                int bottom = Math.Max(top, bitmap.Height - (int)Math.Ceiling(3 * scale));
                int glyphPixels = 0;
                int glyphLeft = bitmap.Width, glyphRight = -1;
                for (int y = top; y < bottom; ++y)
                    for (int x = left; x < right; ++x)
                    {
                        Color pixel = bitmap.GetPixel(x, y);
                        if (Contrast(pixel, item.BackColor) < 4.5) continue;
                        // The checked mark lies outside this region; a legible date must
                        // supply several characters' worth of contrasting text pixels.
                        ++glyphPixels;
                        glyphLeft = Math.Min(glyphLeft, x);
                        glyphRight = Math.Max(glyphRight, x);
                    }
                Assert(glyphPixels >= 30 && glyphRight - glyphLeft >= item.Font.Height * 2,
                    scopeName + " date has legible rendered text with contrast >= 4.5 (dark=" + dark
                    + "; pixels=" + glyphPixels + ", span=" + (glyphRight - glyphLeft) + ")");
                if (dark && scopeName == "All")
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log-archive-dark-disabled-date.png"));
            }
        }

        private static double Contrast(Color a, Color b)
        {
            double first = Luminance(a), second = Luminance(b);
            return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
        }

        private static double Luminance(Color color)
        {
            return .2126 * Linear(color.R / 255.0) + .7152 * Linear(color.G / 255.0)
                + .0722 * Linear(color.B / 255.0);
        }

        private static double Linear(double channel)
        {
            return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }

        private static void ShowOffscreen(Form form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-30000, -30000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
        }

        private static HashSet<string> MarkedDates(TableLayoutPanel dates)
        {
            var marked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CheckBox item in dates.Controls)
                if (item.Checked) marked.Add(item.Text);
            return marked;
        }

        private static bool AllDateChoicesDisabled(TableLayoutPanel dates)
        {
            foreach (CheckBox item in dates.Controls)
                if (item.Enabled) return false;
            return true;
        }

        private static void VerifyDialogButtons(LogArchiveDialog dialog, bool dark)
        {
            Button[] buttons = { (Button)dialog.AcceptButton, (Button)dialog.CancelButton,
                DialogField<Button>(dialog, "previous"), DialogField<Button>(dialog, "next") };
            foreach (Button button in buttons)
            {
                Assert(button.Visible && button.Width > 0 && button.Height >= button.Font.Height + 6,
                    "date dialog button '" + button.Text + "' remains visible with usable text height at real DPI (dark=" + dark + ")");
                Size text = TextRenderer.MeasureText(button.Text, button.Font);
                Assert(text.Width + 12 <= button.ClientSize.Width && text.Height + 4 <= button.ClientSize.Height,
                    "date dialog button '" + button.Text + "' has room for its label (dark=" + dark + ")");
                Rectangle visible = button.Bounds;
                for (Control parent = button.Parent; parent != null; parent = parent.Parent)
                {
                    Assert(parent.ClientRectangle.Contains(visible), "date dialog button '" + button.Text
                        + "' stays inside " + parent.GetType().Name + " at real DPI (dark=" + dark
                        + "; button=" + visible + ", parent=" + parent.ClientRectangle + ")");
                    visible.Offset(parent.Left, parent.Top);
                }
            }
        }

        private static T DialogField<T>(LogArchiveDialog dialog, string name)
        {
            return (T)typeof(LogArchiveDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
        }

        private static void VerifyFits(Control control, string label, float scale, bool dark)
        {
            Rectangle visible = control.Bounds;
            for (Control parent = control.Parent; parent != null; parent = parent.Parent)
            {
                Assert(parent.ClientRectangle.Contains(visible), label + " fits " + parent.GetType().Name
                    + " at simulated " + scale + "/dark=" + dark + " (" + visible + " in " + parent.ClientRectangle + ")");
                visible.Offset(parent.Left, parent.Top);
            }
        }

        private static Button CollectButton(MainForm form) { return Field<Button>(form, "collectLogsButton"); }
        private static DataGridView Grid(MainForm form) { return Field<DataGridView>(form, "servicesGrid"); }

        private static void Select(MainForm form, string name)
        {
            foreach (DataGridViewRow row in Grid(form).Rows)
                if (((ServiceEntry)row.Tag).Name == name)
                {
                    Grid(form).CurrentCell = row.Cells[0];
                    row.Selected = true;
                    return;
                }
            throw new InvalidOperationException("Expected fixture row is missing: " + name);
        }

        private static bool Source(MainForm form, out string name, out string executable)
        {
            object[] arguments = { null, null };
            bool valid = (bool)typeof(MainForm).GetMethod("TryGetSelectedLogSource",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, arguments);
            name = (string)arguments[0];
            executable = (string)arguments[1];
            return valid;
        }

        private static Task Refresh(MainForm form)
        {
            return (Task)typeof(MainForm).GetMethod("RefreshStatusAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { false });
        }

        private static T Field<T>(MainForm form, string name)
        {
            return (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        }

        private static void SetField(MainForm form, string name, object value)
        {
            typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, value);
        }

        private static void Assert(bool value, string message)
        {
            ++checks;
            if (!value) throw new InvalidOperationException("FAIL: " + message);
        }
    }
}
