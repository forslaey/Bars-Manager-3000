using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ServiceTool
{
    internal abstract class ManagerModuleForm : Form
    {
        protected ThemePalette Colors;
        protected readonly TableLayoutPanel Root;
        private readonly PictureBox logo = new PictureBox();
        private readonly ThemeToggleButton theme = new ThemeToggleButton();
        private readonly Image lightLogo, darkLogo;
        private readonly ToolTip tips = new ToolTip();
        protected ManagerModuleForm(string title, Size size)
        {
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10F); Text = title; ClientSize = size;
            MinimumSize = new Size(860, 620); StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true; Icon = BrandAssets.LoadIcon();
            Colors = ThemePalette.Create(ThemePreference.LoadDark());
            lightLogo = BrandAssets.LoadLogo(false); darkLogo = BrandAssets.LoadLogo(true);
            Root = new BufferedLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 12, 26, 12), ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            Root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96)); Root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(Root);
            var header = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 12) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            logo.Dock = DockStyle.Fill; logo.SizeMode = PictureBoxSizeMode.Zoom; logo.Margin = new Padding(0, 6, 0, 6); logo.Cursor = Cursors.Hand;
            tips.SetToolTip(logo, "Официальный сайт");
            logo.Click += delegate { try { Process.Start(new ProcessStartInfo("https://www.datakrat.ru/") { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); } };
            header.Controls.Add(logo, 1, 0);
            theme.Size = new Size(86, 40); theme.Anchor = AnchorStyles.None; theme.AccessibleName = "Сменить тему";
            theme.Click += delegate { SetTheme(!Colors.Dark, true); }; header.Controls.Add(theme, 2, 0); Root.Controls.Add(header, 0, 0);
        }
        protected BrandButton Button(string text, bool primary)
        { return new BrandButton { Text = text, Primary = primary, Palette = Colors, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), Font = Font }; }
        protected Label Label(string text, bool muted)
        { return new Label { Text = text, Tag = muted ? "muted" : "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, AutoEllipsis = true }; }
        public void SetTheme(bool dark, bool persist)
        {
            Colors = ThemePalette.Create(dark); BackColor = Colors.Canvas; ForeColor = Colors.Text;
            logo.Image = dark ? darkLogo : lightLogo; theme.Palette = Colors; theme.Dark = dark;
            ApplyTree(this, Colors.Canvas); OnThemeChanged(); Invalidate(true);
            if (persist) ThemePreference.TrySave(dark);
        }
        private void ApplyTree(Control control, Color background)
        {
            var card = control as Card;
            if (card != null) { background = Colors.Surface; card.BorderColor = Colors.Border; }
            control.BackColor = background; control.ForeColor = Convert.ToString(control.Tag) == "muted" ? Colors.Muted : Colors.Text;
            var button = control as BrandButton; if (button != null) button.Palette = Colors;
            var toggle = control as RefreshToggle; if (toggle != null) toggle.Palette = Colors;
            var input = control as BrandTextBox; if (input != null) { input.Palette = Colors; input.BackColor = Colors.SurfaceAlt; }
            var combo = control as BrandComboBox; if (combo != null) { combo.Palette = Colors; combo.BackColor = Colors.SurfaceAlt; }
            foreach (Control child in control.Controls) ApplyTree(child, background);
        }
        protected virtual void OnThemeChanged() { }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { logo.Image = null; if (lightLogo != null) lightLogo.Dispose(); if (darkLogo != null) darkLogo.Dispose(); tips.Dispose(); }
            base.Dispose(disposing);
        }
    }
    internal sealed class ManagerHub : ManagerModuleForm
    {
        public ManagerHub() : base("BARS Manager 3000", new Size(840, 680))
        {
            MinimumSize = new Size(800, 640); ClientSize = new Size(840, 680);
            Root.Padding = new Padding(20, 8, 20, 10); Root.RowStyles[0].Height = 70;
            // Three module rows of identical height; the body scrolls if the window is shorter.
            var body = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            var title = Label("Инструменты BARS", false); title.Font = new Font("Segoe UI", 17, FontStyle.Bold); title.Margin = new Padding(18, 0, 0, 0); body.Controls.Add(title, 0, 0);
            // Five modules in a 2x3 grid (the last cell stays empty).
            var modules = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
            modules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); modules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int i = 0; i < 3; i++) modules.RowStyles.Add(new RowStyle(SizeType.Absolute, ModuleRowHeight));
            // Percentages of an odd pixel count round to different heights, so the three rows are
            // sized explicitly to identical values whenever the panel is resized.
            modules.SizeChanged += delegate { EqualRows(modules, 3); };
            modules.Controls.Add(ModuleCard("Менеджер служб", "Установка и управление службами,\r\nрежим запуска и сбор логов", "Менеджер Служб", delegate { using (var form = new MainForm(null, false)) OpenModule(form); }), 0, 0);
            modules.Controls.Add(ModuleCard("Менеджер компонентов", "Проверка и установка библиотек.\r\nВерсии и рекомендации по обновлению", "Менеджер компонентов", delegate { using (var form = new LibraryManagerForm(false)) OpenModule(form); }), 1, 0);
            modules.Controls.Add(ModuleCard("Установка программ", "Notepad++, 7-Zip, AnyDesk, RuDesktop.\r\nАссоциации файлов и удалённый доступ", "Установка программ", delegate { using (var form = new SoftwareManagerForm(false)) OpenModule(form); }), 0, 1);
            modules.Controls.Add(ModuleCard("Утилиты BARS", "Копирование fiLinn, Piglet и WinSCP.\r\nЯрлыки на рабочем столе с иконками", "Распаковка утилит BARS", delegate { using (var form = new ToolsDeploymentForm(false)) OpenModule(form); }), 1, 1);
            modules.Controls.Add(ModuleCard("Компоненты Windows", "Telnet и TFTP для fiLinn и piglet.\r\nIIS для отчётов арендаторов", "Компоненты Windows", delegate { using (var form = new WindowsFeaturesForm(false)) OpenModule(form); }), 0, 2);
            body.Controls.Add(modules, 0, 1);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footer.Controls.Add(Label("Версия " + typeof(ManagerHub).Assembly.GetName().Version.ToString(3), true), 0, 0);
            BrandButton about = Button("О программе", false); about.Margin = Padding.Empty; about.Click += delegate { using (var dialog = new AboutDialog(Colors, Icon)) dialog.ShowDialog(this); }; footer.Controls.Add(about, 1, 0);
            body.Controls.Add(footer, 0, 2); Root.Controls.Add(body, 0, 1); SetTheme(Colors.Dark, false);
        }
        private const int ModuleRowHeight = 160;
        private static void EqualRows(TableLayoutPanel panel, int rows)
        {
            if (panel.Height <= 0) return;
            int each = panel.Height / rows;
            if (each <= 0) return;
            bool changed = false;
            for (int i = 0; i < rows && i < panel.RowStyles.Count; i++)
                if ((int)panel.RowStyles[i].Height != each) { panel.RowStyles[i] = new RowStyle(SizeType.Absolute, each); changed = true; }
            if (changed) panel.PerformLayout();
        }
        private Card ModuleCard(string title, string description, string action, EventHandler click)
        {
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 14), Margin = new Padding(0, 0, 8, 10) };
            var layout = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // Compact heading/action rows leave two full description lines visible.
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            var heading = new FlushLabel { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }; heading.Font = new Font("Segoe UI", 13, FontStyle.Bold); layout.Controls.Add(heading, 0, 0);
            var detail = new FlushLabel { Text = description, Tag = "muted", Dock = DockStyle.Fill, Margin = Padding.Empty }; detail.TextAlign = ContentAlignment.TopLeft; detail.AutoEllipsis = false; layout.Controls.Add(detail, 0, 1);
            BrandButton open = Button(action, true); open.Margin = new Padding(0, 4, 8, 8); open.Dock = DockStyle.None; open.Anchor = AnchorStyles.Right | AnchorStyles.Bottom; open.Size = new Size(280, 50); open.Click += click;
            layout.Controls.Add(open, 0, 2); card.Controls.Add(layout); return card;
        }
        private void OpenModule(Form form)
        {
            // Keep a visible owner while Windows activates the modal module. Hiding the
            // launcher first leaves no foreground window and can activate another app.
            if (form.StartPosition != FormStartPosition.Manual) form.StartPosition = FormStartPosition.CenterParent;
            EventHandler activate = delegate { form.BringToFront(); form.Activate(); };
            form.Shown += activate;
            try { form.ShowDialog(this); }
            finally
            {
                form.Shown -= activate;
                if (!IsDisposed)
                {
                    var library = form as LibraryManagerForm; var services = form as MainForm;
                    var utility = form as UtilityModuleForm; var software = form as SoftwareManagerForm;
                    bool menu = library != null ? library.MenuRequested : software != null ? software.MenuRequested : utility != null ? utility.MenuRequested : services != null && services.MenuRequested;
                    if (menu) { SetTheme(ThemePreference.LoadDark(), false); Show(); Activate(); }
                    else Close();
                }
            }
        }
    }
}
