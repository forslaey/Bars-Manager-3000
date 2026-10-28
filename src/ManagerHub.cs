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
        private bool initialScaleApplied;
        private readonly ToolTip tips = new ToolTip();
        protected ManagerModuleForm(string title, Size size)
        {
            // Do not let Font/ClientSize trigger DPI scaling before the derived
            // module has added its controls, otherwise only the base header scales.
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 10F); Text = title; ClientSize = size;
            MinimumSize = new Size(860, 620); StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true; Icon = BrandAssets.LoadIcon();
            Colors = ThemePalette.Create(ThemePreference.LoadDark());
            NativeWindowTheme.Bind(this,()=>Colors.Dark);
            lightLogo = BrandAssets.LoadLogo(false); darkLogo = BrandAssets.LoadLogo(true);
            Root = new BufferedLayoutPanel { Size = size, Dock = DockStyle.Fill, Padding = new Padding(26, 12, 26, 12), ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            Root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96)); Root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(Root);
            var header = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(0, 8, 0, 0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            logo.Dock = DockStyle.Fill; logo.SizeMode = PictureBoxSizeMode.Zoom; logo.Margin = new Padding(0, 6, 0, 6); logo.Cursor = Cursors.Hand;
            BrandAssets.BindWebsite(logo, tips, this);
            header.Controls.Add(logo, 1, 0);
            theme.Size = new Size(86, 40); theme.Anchor = AnchorStyles.None; theme.AccessibleName = "Сменить тему";
            theme.Click += delegate { SetTheme(!Colors.Dark, true); }; header.Controls.Add(theme, 2, 0); Root.Controls.Add(header, 0, 0);
        }
        protected override void OnLoad(EventArgs e)
        {
            if(!initialScaleApplied)
            {
                initialScaleApplied=true;
                AutoScaleMode=AutoScaleMode.Dpi;
                AutoScaleDimensions=new SizeF(96,96);
                PerformAutoScale();
                ResumeLayout(true);
                if(Root.AutoScroll&&Root.Parent==this)
                {
                    // A Dock.Fill body with a minimum height does not reliably
                    // create scrollbars in TableLayoutPanel.AutoScroll.
                    Control body=Root.GetControlFromPosition(0,1);
                    if(body!=null&&body.MinimumSize.Height>0)
                    {
                        Root.AutoScroll=false;
                        Root.MinimumSize=new Size(Root.MinimumSize.Width,body.MinimumSize.Height+Root.Padding.Vertical+(int)Math.Ceiling(Root.RowStyles[0].Height));
                        Controls.Remove(Root);Controls.Add(new FillContentPanel(Root));
                    }
                }
                Rectangle area=Screen.FromControl(this).WorkingArea;
                MinimumSize=new Size(Math.Min(MinimumSize.Width,area.Width),Math.Min(MinimumSize.Height,area.Height));
                if(WindowState==FormWindowState.Normal)Size=new Size(Math.Min(Width,area.Width),Math.Min(Height,area.Height));
            }
            WindowPlacement.Constrain(this);
            base.OnLoad(e);
        }
        internal virtual void ResumeSession() { SetTheme(ThemePreference.LoadDark(), false); }
        protected override void OnShown(EventArgs e) { WindowPlacement.Constrain(this); base.OnShown(e); }
        protected void LimitContent(Size maximum)
        {
            Controls.Remove(Root);
            Controls.Add(new BoundedContentPanel(Root, maximum));
        }
        protected BrandButton Button(string text, bool primary)
        { return new BrandButton { Text = text, Primary = primary, Palette = Colors, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), Font = Font }; }
        protected Label Label(string text, bool muted)
        { return new Label { Text = text, Tag = muted ? "muted" : "", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, AutoEllipsis = true }; }
        protected Label ModuleHeading(string text)
        { return ModuleHeadingStyle.Create(text,true); }
        protected Label Caption(string text, bool muted)
        { return new InputCaptionLabel { Text=text.TrimEnd(':')+":",Tag=muted?"muted":"",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty,AutoEllipsis=true }; }
        public void SetTheme(bool dark, bool persist)
        {
            Colors = ThemePalette.Create(dark); BackColor = Colors.Canvas; ForeColor = Colors.Text;
            logo.Image = dark ? darkLogo : lightLogo; theme.Palette = Colors; theme.Dark = dark;
            ApplyTree(this, Colors.Canvas); NativeWindowTheme.Apply(this, dark); OnThemeChanged(); Invalidate(true);
            if (persist) ThemePreference.TrySave(dark);
        }
        private void ApplyTree(Control control, Color background)
        {
            var card = control as Card;
            if (card != null) { background = Colors.Surface; card.BorderColor = Colors.Border; }
            control.BackColor = background; control.ForeColor = Convert.ToString(control.Tag) == "muted" ? Colors.Muted : Colors.Text;
            var button = control as BrandButton; if (button != null) button.Palette = Colors;
            var journalScroll = control as JournalScrollBar; if (journalScroll != null) journalScroll.Palette = Colors;
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
        public ManagerHub() : base("BARS Manager 3000", new Size(900, 770))
        {
            FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false; MinimumSize = new Size(860, 740); ClientSize = new Size(900, 770);
            Root.Padding = new Padding(20, 8, 20, 10); Root.RowStyles[0].Height = 84;
            var launcherHeader=(TableLayoutPanel)Root.GetControlFromPosition(0,0);launcherHeader.Padding=new Padding(0,18,0,0);
            Root.AutoScroll=true;
            // Four modules in an even 2x2 grid, plus a full-width settings card.
            var body = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, MinimumSize=new Size(0,650) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 168)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            var title = Label("Инструменты BARS", false); title.Font = new Font("Segoe UI", 17, FontStyle.Bold); title.TextAlign = ContentAlignment.MiddleCenter; title.Margin = new Padding(0, 0, 0, 20); body.Controls.Add(title, 0, 0);
            // Windows components are reached from the component manager.
            var modules = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            modules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); modules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int i = 0; i < 2; i++) modules.RowStyles.Add(new RowStyle(SizeType.Absolute, ModuleRowHeight));
            // Percentages of an odd pixel count round to different heights, so the three rows are
            // sized explicitly to identical values whenever the panel is resized.
            modules.SizeChanged += delegate { EqualRows(modules, 2); };
            modules.Controls.Add(ModuleCard("Менеджер служб", "Установка и управление службами,\r\nрежим запуска и сбор логов", "Менеджер служб", delegate { OpenModule(() => new MainForm(null, false)); }), 0, 0);
            modules.Controls.Add(ModuleCard("Менеджер библиотек", "Проверка и установка библиотек.\r\nВерсии и рекомендации по обновлению", "Менеджер библиотек", delegate { OpenModule(() => new LibraryManagerForm(false)); }), 1, 0);
            modules.Controls.Add(ModuleCard("Установка программ", "Chrome, Notepad++, 7-Zip, AnyDesk, RuDesktop.\r\nУстановка, удаление и удалённый доступ", "Установка программ", delegate { OpenModule(() => new SoftwareManagerForm(false)); }), 0, 1);
            modules.Controls.Add(ModuleCard("Распаковка утилит", "Копирование fiLinn, Piglet и WinSCP.\r\nЯрлыки на рабочем столе с иконками", "Распаковка утилит", delegate { OpenModule(() => new ToolsDeploymentForm(false)); }), 1, 1);
            body.Controls.Add(modules, 0, 1);
            var footer=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=new Padding(5,0,5,0),Padding=new Padding(0,8,0,0)};
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var bundle=Button("Скачать комплект",false);bundle.Click+=delegate{OpenModule(()=>new BundleDownloadForm());};footer.Controls.Add(bundle,0,0);
            var guide=Button("Инструкция",false);guide.AccessibleName="Открыть инструкцию";guide.Click+=delegate{ModuleNavigation.ShowDialog(this,()=>new UserGuideForm());SetTheme(ThemePreference.LoadDark(),false);};footer.Controls.Add(guide,2,0);
            var about=Button("О программе",false);about.Margin=Padding.Empty;about.Click+=delegate{using(var dialog=new AboutDialog(Colors,Icon))dialog.ShowDialog(this);};footer.Controls.Add(about,3,0);
            body.Controls.Add(ModuleCard("Первичные системные настройки", "Политики паролей, режим администратора, брандмауэр и Defender", "Открыть настройки", delegate { OpenModule(() => new InitialSystemSettingsForm()); }),0,2);
            body.Controls.Add(footer, 0, 3);
            Root.Controls.Add(body, 0, 1);
            LimitContent(new Size(1160, 860));
            SetTheme(Colors.Dark, false);
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
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 14), Margin = new Padding(5, 0, 5, 10) };
            var layout = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // Compact heading/action rows leave two full description lines visible.
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            var heading = new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Margin = Padding.Empty }; heading.Font = new Font("Segoe UI", 13, FontStyle.Bold); layout.Controls.Add(heading, 0, 0);
            var detail = new Label { Text = description, Tag = "muted", Dock = DockStyle.Fill, Margin = Padding.Empty }; detail.TextAlign = ContentAlignment.MiddleCenter; detail.AutoEllipsis = false; layout.Controls.Add(detail, 0, 1);
            BrandButton open = Button(action, true); open.Margin = new Padding(0, 0, 0, 0); open.Dock = DockStyle.None; open.Anchor = AnchorStyles.Bottom; open.Size = new Size(280, 50); open.Click += click;
            layout.Controls.Add(open, 0, 2);layout.Layout+=delegate{int width=Math.Max(1,Math.Min((int)Math.Round(280F*open.Height/50F),layout.ClientSize.Width));if(open.Width!=width)open.Width=width;}; card.Controls.Add(layout); return card;
        }
        internal Func<Form> RequestedModule { get; private set; }
        internal Type RequestedModuleType { get; private set; }

        internal void OpenModule<T>(Func<T> factory) where T : Form
        {
            OpenModule(typeof(T), () => factory());
        }
        internal void OpenModule(Type type, Func<Form> factory)
        {
            if (RequestedModule != null || IsDisposed) return;
            RequestedModuleType = type;
            RequestedModule = factory;
            Close();
            // A cancelled close must not retain a stale navigation request.
            if (!IsDisposed && Visible) RequestedModule = null;
        }
    }
}