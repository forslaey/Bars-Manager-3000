using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    // All service data is injected. The test .exe fixture is inert text, never run.
    // No install/remove/start/stop/startup command or elevated process is invoked.
    internal static class ThemeTests
    {
        private const string LongServiceName = "BARS_TariffProcessor_Primary_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private static int checks;
        private static int exitCode = 1;
        private static string fixtureRoot;
        private static string fixtureExe;
        private static string renderDirectory;

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0) renderDirectory = Path.GetFullPath(args[0]);
            fixtureRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme-data-" + Guid.NewGuid().ToString("N"));
            fixtureExe = Path.Combine(fixtureRoot, "BARS Service Example.exe");
            Directory.CreateDirectory(fixtureRoot);
            File.WriteAllText(fixtureExe, "Inert theme-test fixture. Never execute this text file.");
            try
            {
                using (Form host = new Form
                {
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-30000, -30000),
                    Size = new Size(1, 1),
                    Opacity = 0
                })
                {
                    host.Shown += async delegate
                    {
                        try
                        {
                            await RunAllAsync();
                            Console.WriteLine("PASS: " + checks + " theme/layout assertions. Injected service data only; no service operation or elevated process launched.");
                            exitCode = 0;
                        }
                        catch (Exception ex) { Console.Error.WriteLine(ex); }
                        finally { host.Close(); }
                    };
                    Application.Run(host);
                }
            }
            finally
            {
                string allowedRoot = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string target = Path.GetFullPath(fixtureRoot);
                if (target.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(target).StartsWith("theme-data-", StringComparison.Ordinal) && Directory.Exists(target))
                    Directory.Delete(target, true);
            }
            return exitCode;
        }

        private static async Task RunAllAsync()
        {
            using (MainForm form = NewForm(delegate { return SampleSnapshot(); }))
            {
                Assert(form.Text == "Bars Service Manager 3000", "Exact requested window title");
                VerifyBranding(form);
                VerifyLogoVariantsAndPosition(form);
                VerifyFileSelectionCopy(form);
                VerifyFileSelectionLayout(form, "current system scale");
                VerifyFixedGrid(form);
                VerifyManagementControls(form);
                await InvokeAsync(form, "SetExecutableAsync", fixtureExe);
                Label multipleHint = Field<Label>(form, "hint");
                Assert(!multipleHint.Visible && Field<ToolTip>(form, "toolTips").GetToolTip(Field<Button>(form, "removeButton")).Contains("может удалить"),
                    "Multiple-registration warning is available on the removal button without restoring the removed line");
                VerifySelectedServiceControls(form);
                VerifyWrappedServiceText(form);
                foreach (bool dark in new bool[] { false, true })
                {
                    SetTheme(form, dark);
                    VerifyTheme(form, dark);
                    if (!String.IsNullOrEmpty(renderDirectory)) Render(form, dark);
                }
                Console.WriteLine("PASS: BARS resources, fixed grid, wrapped service names and both themes");
            }
            await VerifySemanticStatusAsync("error");
            await VerifySemanticStatusAsync("warning");
            await VerifySemanticStatusAsync("good");
            VerifyScaledFileSelectionLayout();
        }

        private static MainForm NewForm(Func<ServiceSnapshot> reader)
        {
            MainForm form = new MainForm(null, false, reader);
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            IntPtr handle = form.Handle;
            form.Show();
            form.PerformLayout(); Application.DoEvents();
            Application.DoEvents(); // Dispatch the posted Shown event before waiting for its injected refresh.
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (Field<bool>(form, "refreshing"))
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("Initial injected catalog refresh did not complete within five seconds.");
                Application.DoEvents();
                System.Threading.Thread.Sleep(1);
            }
            Application.DoEvents();
            Field<System.Windows.Forms.Timer>(form, "timer").Stop();
            return form;
        }

        private static ServiceSnapshot SampleSnapshot()
        {
            return new ServiceSnapshot
            {
                Complete = true,
                Warning = "",
                Entries = new List<ServiceEntry>
                {
                    new ServiceEntry
                    {
                        Name = LongServiceName,
                        DisplayName = "BARS: обработка тарифов и перерасчёт начислений для объектов с расширенными индивидуальными условиями обслуживания",
                        BinaryPath = "\"" + fixtureExe + "\" --service",
                        State = "Running",
                        StartMode = "Auto",
                        DelayedAutoStart = true,
                        CanStop = true,
                        StartupKnown = true
                    },
                    new ServiceEntry
                    {
                        Name = "BARS_BackgroundWorker",
                        DisplayName = "BARS: фоновая обработка",
                        BinaryPath = "\"" + fixtureExe + "\" --worker",
                        State = "Stopped",
                        StartMode = "Manual",
                        StartupKnown = true
                    }
                }
            };
        }

        private static void VerifyBranding(MainForm form)
        {
            Assembly assembly = typeof(MainForm).Assembly;
            using (Stream logoStream = assembly.GetManifestResourceStream("BARS.Logo.png"))
                Assert(logoStream != null && logoStream.Length > 0, "BARS logo is embedded");
            using (Stream darkLogoStream = assembly.GetManifestResourceStream("BARS.Logo.Dark.png"))
                Assert(darkLogoStream != null && darkLogoStream.Length > 0, "BARS dark-theme logo is embedded");
            using (Stream iconStream = assembly.GetManifestResourceStream("BARS.Icon.ico"))
                Assert(iconStream != null && iconStream.Length > 0, "BARS application icon is embedded");
            PictureBox image = Field<PictureBox>(form, "brandImage");
            Assert(image.Image != null && image.Image.Width > 16 && image.Image.Height > 16, "Header displays a decoded BARS logo");
            Assert(image.SizeMode == PictureBoxSizeMode.Zoom, "BARS logo retains its proportions");
            using (Graphics graphics = form.CreateGraphics())
                Assert(image.Width >= Math.Ceiling(60F * graphics.DpiY / 96F * image.Image.Width / image.Image.Height)-1 && image.Height >= Math.Ceiling(60F * graphics.DpiY / 96F),
                    "Logo remains readable at current DPI instead of collapsing to its preferred minimum (actual " + image.Width + " x " + image.Height + ")");
            VerifyFitsAncestors(image, "Header logo");
            Assert(image.Height <= image.Parent.ClientSize.Height * 0.95F, "Logo fits within header height with visible breathing room");
            Assert(image.BorderStyle == BorderStyle.None, "Logo has no picture frame");
            Assert(image.Cursor == Cursors.Hand, "Clickable logo uses a link cursor");
            for (Control parent = image.Parent; parent != null; parent = parent.Parent)
                Assert(!(parent is Card), "Logo is displayed directly without a card frame");
            Assert(form.Icon != null, "Form has an application icon");
            Button toggle = Field<Button>(form, "themeToggleButton");
            Assert(toggle is ThemeToggleButton, "Single theme selector is the custom toggle");
            VerifyFitsAncestors(toggle, "Single theme toggle");
            Assert(typeof(MainForm).GetField("themeLightButton", BindingFlags.Instance | BindingFlags.NonPublic) == null, "Old light-theme button was removed");
            Assert(typeof(MainForm).GetField("themeDarkButton", BindingFlags.Instance | BindingFlags.NonPublic) == null, "Old dark-theme button was removed");
            foreach (Control control in AllControls(form))
            {
                Assert(!(control is Label) || control.Text != "Службы под контролем", "Old header text has been replaced by branding");
                Assert(!(control is Label) || control.Text != "ОФОРМЛЕНИЕ", "Theme toggle needs no appearance caption");
            }
            MethodInfo buildLink = typeof(MainForm).GetMethod("BuildBrandLinkStartInfo", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(buildLink != null, "Logo URL is constructed separately from launching the browser");
            ProcessStartInfo link = (ProcessStartInfo)buildLink.Invoke(null, null);
            Assert(link.FileName == "https://www.datakrat.ru/", "Logo link targets the requested Datakrat HTTPS URL");
            Assert(link.UseShellExecute && String.IsNullOrEmpty(link.Arguments), "Logo link uses the default browser without extra arguments");
        }

        private static void VerifyLogoVariantsAndPosition(MainForm form)
        {
            PictureBox image = Field<PictureBox>(form, "brandImage");
            SetTheme(form, false);
            VerifyTransparentCorners(image.Image, "Light logo");
            string lightPixels = HashPixels(image.Image);
            SetTheme(form, true);
            VerifyTransparentCorners(image.Image, "Dark logo");
            Assert(HashPixels(image.Image) != lightPixels, "Dark theme uses a distinct logo pixel variant");
            SetTheme(form, false);
            Assert(HashPixels(image.Image) == lightPixels, "Returning to light theme restores the original logo pixels");

            VerifyLogoCentered(form, "normal width");
            Size original = form.ClientSize;
            try
            {
                form.ClientSize = new Size(original.Width + 173, original.Height);
                form.PerformLayout(); Application.DoEvents();
                VerifyLogoCentered(form, "wider window");
                form.ClientSize = new Size(Math.Max(1, original.Width - 61), original.Height);
                form.PerformLayout(); Application.DoEvents();
                VerifyLogoCentered(form, "narrower window");
            }
            finally
            {
                form.ClientSize = original;
                form.PerformLayout(); Application.DoEvents();
            }
        }

        private static void VerifyLogoCentered(MainForm form, string description)
        {
            PictureBox image = Field<PictureBox>(form, "brandImage");
            float center = image.Left + image.Width / 2F;
            for (Control parent = image.Parent; parent != null && parent != form; parent = parent.Parent)
                center += parent.Left;
            float expected = form.ClientSize.Width / 2F;
            Assert(Math.Abs(center - expected) <= 1F, "Logo is centered within one pixel of the whole window at " + description + " (" + center + " vs " + expected + ")");
        }

        private static void VerifyTransparentCorners(Image image, string description)
        {
            using (Bitmap bitmap = new Bitmap(image))
                Assert(bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(bitmap.Width - 1, 0).A == 0 &&
                    bitmap.GetPixel(0, bitmap.Height - 1).A == 0 && bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A == 0,
                    description + " has transparent corners instead of a rectangular background plate");
        }

        private static string HashPixels(Image image)
        {
            using (Bitmap bitmap = new Bitmap(image))
            using (SHA256 sha = SHA256.Create())
            {
                BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] pixels = new byte[Math.Abs(data.Stride) * bitmap.Height];
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                    return Convert.ToBase64String(sha.ComputeHash(pixels));
                }
                finally { bitmap.UnlockBits(data); }
            }
        }

        private static void VerifyFileSelectionCopy(MainForm form)
        {
            Assert(Field<Button>(form, "chooseButton").Text == "Выбрать службу...", "File picker uses the requested service caption");
            FlushLabel fileTitle = Field<FlushLabel>(form, "fileTitle");
            Assert(fileTitle.Text == "Перетащи сюда .exe-файл службы BARS или выбери файл кнопкой.", "File drop zone uses the requested combined wording");
            Assert(fileTitle.SingleLine && ((TableLayoutPanel)fileTitle.Parent).GetRow(fileTitle) == 3, "Combined instruction is single-line at the former lower caption position");
            int helpCount = 0;
            foreach (Control control in AllControls(form))
            {
                if (control is Label && control.Text.StartsWith("Или выбери файл кнопкой", StringComparison.Ordinal)) helpCount++;
                Assert(control.Text != "Права администратора Windows запросит при выполнении процедуры.", "Removed UAC hint is absent from the UI");
            }
            Assert(helpCount == 0, "File selection has no separate second help label");
            Label hint = Field<Label>(form, "hint");
            Assert(String.IsNullOrEmpty(hint.Text) || !hint.Visible, "Default service hint is empty or hidden");
        }

        private static void VerifyFileSelectionLayout(MainForm form, string scale)
        {
            TextBox path = Field<TextBox>(form, "pathBox");
            Control frame = Field<Control>(form, "pathField");
            Button choose = Field<Button>(form, "chooseButton");
            Assert(path.Parent == frame && frame.Parent == choose.Parent, "Path frame and picker share the same input row at " + scale);
            VerifyFitsAncestors(path, "Path input at " + scale);
            VerifyFitsAncestors(choose, "File-picker button at " + scale);
            VerifyFitsMargins(frame, "Path-frame margins at " + scale);
            VerifyFitsMargins(choose, "File-picker margins at " + scale);
            Assert(frame.Right + frame.Margin.Right <= choose.Left - choose.Margin.Left, "Path and picker do not overlap at " + scale);
            Assert(path.ReadOnly && !path.Multiline && path.BorderStyle == BorderStyle.None && path.AutoSize, "Path retains a native single-line edit at its normal height at " + scale);
            Assert(Math.Abs(path.Top - (frame.ClientSize.Height - path.Bottom)) <= 1, "Native text, caret and selection are vertically centered within the frame at " + scale);
            Assert(frame.Height == Field<ComboBox>(form, "startupCombo").Height, "Outer frame matches the startup-mode height at " + scale);
            path.SelectAll();
            Assert(path.SelectionLength == path.TextLength, "Native path selection remains functional at " + scale);
            int textHeight = TextRenderer.MeasureText(choose.Text, choose.Font,
                new Size(Math.Max(1, choose.ClientSize.Width - 16), Int32.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            Assert(textHeight <= choose.ClientSize.Height - 6, "File-picker text fits its button at " + scale);
            VerifyUnifiedLayout(form, scale);
        }

        private static void VerifyUnifiedLayout(MainForm form, string scale)
        {
            DataGridView grid = Field<DataGridView>(form, "servicesGrid");
            Label title = Field<Label>(form, "statusTitle"), detail = Field<Label>(form, "statusDetail");
            Assert(title.PointToScreen(Point.Empty).X == grid.PointToScreen(Point.Empty).X,
                "Summary aligns with the list without a left inset at " + scale);
            Assert(!detail.Visible, "Removed status detail remains hidden at " + scale);
            CheckBox refresh = Field<RefreshToggle>(form, "autoRefreshToggle");
            VerifyFitsAncestors(refresh, "Autorefresh toggle at " + scale);

            Control[] registration = { Field<Button>(form, "installButton"), Field<Button>(form, "removeButton"), Field<Button>(form, "installProfileButton"),
                Field<Button>(form, "refreshButton"), Field<Button>(form, "servicesButton") };
            float center = registration[0].Top + registration[0].Height / 2F;
            foreach (Control control in registration)
            {
                Assert(Math.Abs(control.Top + control.Height / 2F - center) <= 1F, "Registration actions share a vertical center at " + scale);
                Assert(Math.Abs(control.Margin.Top - control.Margin.Bottom) <= 1, "Registration actions have symmetric vertical margins at " + scale);
            }
            Control[] management = { Field<Button>(form, "startButton"), Field<Button>(form, "stopButton"), Field<Button>(form, "collectLogsButton"),
                Field<ComboBox>(form, "startupCombo"), Field<Button>(form, "applyStartupButton") };
            center = management[0].PointToScreen(Point.Empty).Y + management[0].Height / 2F;
            foreach (Control control in management)
                Assert(Math.Abs(control.PointToScreen(Point.Empty).Y + control.Height / 2F - center) <= 1F, "Management controls share a vertical center at " + scale);
            PictureBox logo = Field<PictureBox>(form, "brandImage");
            Assert(Math.Abs(logo.Top + logo.Height / 2F - logo.Parent.ClientSize.Height / 2F) <= 1F, "Logo is centered vertically at " + scale);
        }

        private static void VerifyFitsMargins(Control child, string description)
        {
            Rectangle area = child.Bounds;
            area.X -= child.Margin.Left;
            area.Y -= child.Margin.Top;
            area.Width += child.Margin.Horizontal;
            area.Height += child.Margin.Vertical;
            Assert(child.Parent.ClientRectangle.Contains(area), description + " fit the input row");
        }

        private static void VerifyScaledFileSelectionLayout()
        {
            foreach (float targetScale in new float[] { 1F, 1.25F, 1.5F })
            {
                using (MainForm form = NewForm(delegate { return SampleSnapshot(); }))
                {
                    float systemScale;
                    using (Graphics graphics = form.CreateGraphics()) systemScale = graphics.DpiX / 96F;
                    // Simulates layout geometry at each scale; it does not change
                    // the monitor DPI or launch a process with a different DPI.
                    form.AutoScaleMode = AutoScaleMode.None;
                    form.MinimumSize = Size.Empty;
                    form.Scale(new SizeF(targetScale / systemScale, targetScale / systemScale));
                    form.PerformLayout(); Application.DoEvents();
                    VerifyFileSelectionLayout(form, "simulated " + (targetScale * 100F).ToString("0") + "%");
                    VerifyFitsAncestors(Field<Button>(form, "themeToggleButton"), "Theme toggle at simulated " + targetScale);
                    VerifyLogoCentered(form, "simulated " + (targetScale * 100F).ToString("0") + "%");
                }
            }
            Console.WriteLine("PASS: input controls fit and logo stays centered at simulated 100%, 125% and 150% layout geometry");
        }

        private static void VerifyFitsAncestors(Control child, string description)
        {
            Rectangle visible = child.Bounds;
            for (Control parent = child.Parent; parent != null; parent = parent.Parent)
            {
                Rectangle parentBounds = parent.ClientRectangle;
                Assert(parentBounds.Contains(visible), description + " stays inside " + parent.GetType().Name + " viewport (button " + visible + "; parent " + parentBounds + ")");
                visible.Offset(parent.Left, parent.Top);
            }
        }

        private static void VerifyFixedGrid(MainForm form)
        {
            DataGridView grid = Field<DataGridView>(form, "servicesGrid");
            Assert(grid.ReadOnly && !grid.AllowUserToAddRows && !grid.AllowUserToDeleteRows, "Service catalog is read-only");
            Assert(!grid.AllowUserToResizeColumns && !grid.AllowUserToResizeRows && !grid.AllowUserToOrderColumns, "Mouse cannot resize or rearrange table columns and rows");
            Assert(grid.ColumnHeadersHeightSizeMode == DataGridViewColumnHeadersHeightSizeMode.DisableResizing, "Column-header height cannot be dragged");
            Assert(grid.RowHeadersWidthSizeMode == DataGridViewRowHeadersWidthSizeMode.DisableResizing, "Row-header width cannot be dragged");
            Assert(grid.AutoSizeColumnsMode == DataGridViewAutoSizeColumnsMode.Fill, "Columns fill their fixed table area");
            Assert(grid.AutoSizeRowsMode == DataGridViewAutoSizeRowsMode.AllCells, "Rows grow automatically to show all text");
            Assert(grid.DefaultCellStyle.WrapMode == DataGridViewTriState.True, "Service cell text wraps");
            Assert(grid.ColumnHeadersDefaultCellStyle.WrapMode == DataGridViewTriState.True, "Column-header text wraps");
            Assert(!grid.EnableHeadersVisualStyles, "Both themes control header colors");
            Assert(grid.Columns.Count == 4, "Expected four service columns");
            string[] expectedHeaders = { "Название", "Системное имя", "Состояние", "Запуск" };
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                DataGridViewColumn column = grid.Columns[i];
                Assert(column.HeaderText == expectedHeaders[i], "Requested service column " + i);
                Assert(column.Resizable == DataGridViewTriState.False, "Column " + i + " explicitly disables resizing");
                Assert(column.SortMode == DataGridViewColumnSortMode.NotSortable, "Column " + i + " has stable ordering");
                Assert(column.MinimumWidth >= 100, "Column " + i + " cannot collapse below a readable width");
            }
            foreach (Control control in AllControls(form))
                Assert(!(control is Splitter) && !(control is SplitContainer), "Service pane has no draggable splitter");
        }

        private static void VerifyManagementControls(MainForm form)
        {
            Button start = Field<Button>(form, "startButton");
            Button stop = Field<Button>(form, "stopButton");
            Button apply = Field<Button>(form, "applyStartupButton");
            ComboBox startup = Field<ComboBox>(form, "startupCombo");
            Assert(!start.Enabled && !stop.Enabled && !apply.Enabled, "Management actions require a selected verified service");
            Assert(startup.DropDownStyle == ComboBoxStyle.DropDownList, "Startup mode is selected from supported values");
            Assert(startup.Items.Count == 4, "Four startup choices include delayed automatic start");
        }

        private static void VerifyWrappedServiceText(MainForm form)
        {
            DataGridView grid = Field<DataGridView>(form, "servicesGrid");
            form.PerformLayout(); Application.DoEvents();
            grid.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells);
            Assert(grid.Rows.Count == 2, "Injected matching services are rendered");
            Assert(Convert.ToString(grid.Rows[0].Cells[0].Value) == SampleSnapshot().Entries[0].DisplayName, "Long display name is preserved without truncating data");
            int shortHeight = grid.Rows[1].Height;
            Assert(grid.Rows[0].Height > shortHeight, "Long display name grows beyond a short row");
            Assert(grid.Rows[0].Height >= grid.Font.Height * 2 + grid.DefaultCellStyle.Padding.Vertical, "Long name occupies at least two readable lines");
            DataGridViewCell identity = grid.Rows[0].Cells[1];
            Assert(Convert.ToString(identity.Value) == LongServiceName, "Display wrapping preserves exact system service identity");
            string formatted = Convert.ToString(identity.FormattedValue);
            Assert(formatted.Replace("\r", "").Replace("\n", "") == LongServiceName, "Formatted system identity only adds line breaks");
            Assert(formatted.Contains("\n"), "Long unbroken system identity wraps explicitly");
            int availableWidth = identity.Size.Width - identity.InheritedStyle.Padding.Horizontal - 4;
            foreach (string line in formatted.Replace("\r", "").Split('\n'))
            {
                int measured = TextRenderer.MeasureText(line, identity.InheritedStyle.Font, new Size(Int32.MaxValue, Int32.MaxValue), TextFormatFlags.NoPrefix).Width;
                Assert(measured <= availableWidth, "Formatted system-name line fits its column without clipping (" + measured + " <= " + availableWidth + ")");
            }
            int lineCount = formatted.Split('\n').Length;
            Assert(grid.Rows[0].Height >= lineCount * grid.Font.Height + grid.DefaultCellStyle.Padding.Vertical, "Wrapped system-name row is tall enough for all lines");
        }

        private static void VerifySelectedServiceControls(MainForm form)
        {
            DataGridView grid = Field<DataGridView>(form, "servicesGrid");
            Button start = Field<Button>(form, "startButton");
            Button stop = Field<Button>(form, "stopButton");
            Button apply = Field<Button>(form, "applyStartupButton");
            ComboBox startup = Field<ComboBox>(form, "startupCombo");
            grid.CurrentCell = grid.Rows[0].Cells[0];
            Invoke(form, "UpdateServiceControls", true);
            Assert(!start.Enabled && stop.Enabled, "Running selected service permits stop and prevents redundant start");
            Assert(startup.Enabled && startup.SelectedIndex == 1, "Selected running service exposes delayed automatic startup");
            Assert(!apply.Enabled, "Unchanged selected startup mode cannot be applied redundantly");
            grid.CurrentCell = grid.Rows[1].Cells[0];
            Invoke(form, "UpdateServiceControls", true);
            Assert(start.Enabled && !stop.Enabled, "Stopped selected service permits start and prevents redundant stop");
            Assert(startup.SelectedIndex == 2, "Selection change reflects that service's manual startup");
            startup.SelectedIndex = 3;
            Assert(apply.Enabled, "Changing the selected service's startup mode enables apply without executing it");
            grid.CurrentCell = grid.Rows[0].Cells[0];
            Invoke(form, "UpdateServiceControls", true);
            Assert(startup.SelectedIndex == 1 && !apply.Enabled, "Changing service resets the unapplied startup selection");
        }

        private static void VerifyTheme(MainForm form, bool dark)
        {
            ThemePalette p = Field<ThemePalette>(form, "palette");
            Assert(p.Dark == dark, "Requested theme is active");
            Assert(form.BackColor == p.Canvas && form.ForeColor == p.Text, "Window uses theme colors");
            ThemeToggleButton toggle = Field<ThemeToggleButton>(form, "themeToggleButton");
            Assert(toggle.Dark == dark, "Single theme toggle reflects the active theme");
            Assert(Object.ReferenceEquals(toggle.Palette, p), "Theme toggle receives the current palette");
            Assert(Field<PictureBox>(form, "brandImage").BackColor == p.Canvas, "Logo background matches the window canvas in the active theme");
            Contrast(p.Text, p.Canvas, "Main text on canvas");
            Contrast(p.Text, p.Surface, "Main text on card");
            Contrast(p.Muted, p.Canvas, "Secondary text on canvas");
            Contrast(p.Muted, p.Surface, "Secondary text on card");
            Contrast(p.Text, p.SurfaceAlt, "Header and input text");
            Contrast(p.SelectionText, p.Selection, "Selected service text");
            Contrast(p.AccentText, p.Accent, "Primary and selected-theme button text");
            Contrast(p.DisabledText, p.DisabledSurface, "Disabled action text");
            Contrast(p.Danger, p.Surface, "Destructive action and error text");
            Contrast(p.Warning, p.Surface, "Warning text");
            Contrast(p.Good, p.Surface, "Success text");
            DataGridView grid = Field<DataGridView>(form, "servicesGrid");
            Assert(grid.BackgroundColor == p.Surface, "Grid background follows theme");
            Contrast(grid.DefaultCellStyle.ForeColor, grid.DefaultCellStyle.BackColor, "Actual grid cell text");
            Contrast(grid.DefaultCellStyle.SelectionForeColor, grid.DefaultCellStyle.SelectionBackColor, "Actual grid selection text");
            Contrast(grid.ColumnHeadersDefaultCellStyle.ForeColor, grid.ColumnHeadersDefaultCellStyle.BackColor, "Actual grid header text");
            foreach (Control control in AllControls(form))
            {
                Label label = control as Label;
                if (label != null && !String.IsNullOrEmpty(label.Text))
                    Contrast(label.ForeColor, EffectiveBackground(label), "Label: " + label.Text.Substring(0, Math.Min(label.Text.Length, 36)));
                BrandButton button = control as BrandButton;
                if (button != null)
                {
                    Assert(Object.ReferenceEquals(button.Palette, p), "Owner-drawn button receives current palette");
                    Color fill = !button.Enabled ? p.DisabledSurface : ((button.Primary || button.Selected) ? p.Accent : p.Surface);
                    Color text = !button.Enabled ? p.DisabledText : ((button.Primary || button.Selected) ? p.AccentText : (button.Destructive ? p.Danger : p.Text));
                    Contrast(text, fill, "Button: " + button.Text);
                }
            }
            TextBox path = Field<TextBox>(form, "pathBox");
            TextBox journal = Field<TextBox>(form, "log");
            Assert(path.ReadOnly && journal.ReadOnly, "Path and journal remain read-only");
            Contrast(path.ForeColor, path.BackColor, "Path input text");
            Contrast(journal.ForeColor, journal.BackColor, "Session journal text");
        }

        private static async Task VerifySemanticStatusAsync(string kind)
        {
            using (MainForm form = NewForm(delegate
            {
                if (kind == "error") throw new InvalidOperationException("Injected catalog error for theme verification");
                if (kind == "warning") return new ServiceSnapshot { Complete = false, Warning = "Injected partial catalog warning", Entries = new List<ServiceEntry>() };
                return SampleSnapshot();
            }))
            {
                await InvokeAsync(form, "SetExecutableAsync", fixtureExe);
                string status = Field<Label>(form, "statusTitle").Text;
                if (kind == "warning")
                {
                    Label warningHint = Field<Label>(form, "hint");
                    Assert(!warningHint.Visible && Field<TextBox>(form, "log").Text.Contains("Injected partial catalog warning"),
                        "Partial-catalog warning remains available in the journal without a lower caption");
                }
                foreach (bool dark in new bool[] { false, true, false })
                {
                    SetTheme(form, dark);
                    ThemePalette p = Field<ThemePalette>(form, "palette");
                    Label title = Field<Label>(form, "statusTitle");
                    Color expected = kind == "error" ? p.Danger : (kind == "warning" ? p.Warning : p.Good);
                    Assert(title.Text == status, "Switching theme preserves " + kind + " status message");
                    Assert(title.ForeColor == expected, "Switching theme preserves " + kind + " semantic color");
                    Contrast(title.ForeColor, EffectiveBackground(title), "Semantic " + kind + " status");
                }
            }
            Console.WriteLine("PASS: theme switching preserves " + kind + " status");
        }

        private static void Render(MainForm form, bool dark)
        {
            Directory.CreateDirectory(renderDirectory);
            bool wasVisible = form.Visible;
            if (!wasVisible) form.Show();
            Field<Timer>(form, "timer").Stop();
            Application.DoEvents();
            form.PerformLayout(); Application.DoEvents();
            using (Bitmap bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            using (Bitmap full = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(full, new Rectangle(Point.Empty, full.Size));
                Point client = form.PointToScreen(Point.Empty);
                Rectangle crop = new Rectangle(client.X - form.Left, client.Y - form.Top, form.ClientSize.Width, form.ClientSize.Height);
                using (Graphics g = Graphics.FromImage(bitmap))
                    g.DrawImage(full, new Rectangle(Point.Empty, bitmap.Size), crop, GraphicsUnit.Pixel);
                bitmap.Save(Path.Combine(renderDirectory, dark ? "theme-dark.png" : "theme-light.png"), ImageFormat.Png);
            }
            if (!wasVisible) form.Hide();
        }

        private static Color EffectiveBackground(Control control)
        {
            for (Control current = control; current != null; current = current.Parent)
                if (current.BackColor.A == 255) return current.BackColor;
            return Color.White;
        }

        private static IEnumerable<Control> AllControls(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control descendant in AllControls(child)) yield return descendant;
            }
        }

        private static void SetTheme(MainForm form, bool dark)
        {
            Invoke(form, "SetTheme", dark, false);
            form.PerformLayout(); Application.DoEvents();
        }

        private static object Invoke(MainForm form, string name, params object[] args)
        {
            MethodInfo method = typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException(typeof(MainForm).FullName, name);
            return method.Invoke(form, args);
        }

        private static Task InvokeAsync(MainForm form, string name, params object[] args)
        {
            return (Task)Invoke(form, name, args);
        }

        private static T Field<T>(MainForm form, string name)
        {
            FieldInfo field = typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(typeof(MainForm).FullName, name);
            return (T)field.GetValue(form);
        }

        private static void Contrast(Color text, Color background, string description)
        {
            double a = Luminance(text), b = Luminance(background);
            double ratio = (Math.Max(a, b) + 0.05D) / (Math.Min(a, b) + 0.05D);
            Assert(ratio >= 4.5D, description + " contrast " + ratio.ToString("0.00") + ":1 is at least 4.5:1");
        }

        private static double Luminance(Color color)
        {
            return 0.2126D * Linear(color.R) + 0.7152D * Linear(color.G) + 0.0722D * Linear(color.B);
        }

        private static double Linear(byte channel)
        {
            double value = channel / 255D;
            return value <= 0.04045D ? value / 12.92D : Math.Pow((value + 0.055D) / 1.055D, 2.4D);
        }

        private static void Assert(bool condition, string description)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("FAIL: " + description);
        }
    }
}
