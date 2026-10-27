using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace ServiceTool
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--library-worker") return LibrarySession.RunWorker(args);
            if (args.Length > 0 && args[0] == "--cleanup-helpers")
                return args.Length == 2 ? EmbeddedHelpers.RunCleanupWatcher(args[1]) : 87;
            EmbeddedHelpers.CleanupStale();
            try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length > 0 && args[0] == "--manage-service") return ServiceManagement.RunHelper(args);
                if (args.Length > 0 && args[0] == "--install-profile-helper") return ServiceProfileInstaller.RunHelper(args);
                if ((args.Length == 2 || args.Length == 3) && args[0] == "--preview")
                {
                    using (MainForm form = new MainForm(null, true))
                    {
                        form.SetTheme(args.Length == 3 && args[2] == "dark", false);
                        form.ShowInTaskbar = false;
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-20000, -20000);
                        form.Show();
                        Application.DoEvents();
                        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.GetFullPath(args[1]), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        form.Close();
                    }
                    return 0;
                }
                if ((args.Length == 2 || args.Length == 3) && (args[0] == "--preview-hub" || args[0] == "--preview-libraries" || args[0] == "--preview-windows" || args[0] == "--preview-tools" || args[0] == "--preview-software"))
                {
                    using (ManagerModuleForm form = args[0] == "--preview-hub" ? (ManagerModuleForm)new ManagerHub() : args[0] == "--preview-windows" ? (ManagerModuleForm)new WindowsFeaturesForm(true) : args[0] == "--preview-tools" ? (ManagerModuleForm)new ToolsDeploymentForm(true) : args[0] == "--preview-software" ? (ManagerModuleForm)new SoftwareManagerForm(true) : new LibraryManagerForm(true))
                    {
                        form.SetTheme(args.Length == 3 && args[2] == "dark", false); form.ShowInTaskbar = false;
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.Show(); Application.DoEvents();
                        using (Bitmap bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.GetFullPath(args[1]), System.Drawing.Imaging.ImageFormat.Png); }
                        form.Close();
                    }
                    return 0;
                }
                ApplicationStorage.MigrateLegacy();
                Func<Form> initialWindow = args.Length == 1 && (args[0] == "--services" || File.Exists(args[0]))
                    ? (Func<Form>)(() => new MainForm(args[0] == "--services" ? null : args[0], false))
                    : (() => new ManagerHub());
                using (var context = new ManagerApplicationContext(initialWindow)) Application.Run(context);
                return 0;
            }
            catch (Exception ex)
            {
                BrandMessageBox.Show("Не удалось открыть утилиту.\r\n\r\n" + ex.Message,
                    "Bars Service Manager 3000", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            finally { EmbeddedHelpers.CleanupAfterExit(Application.ExecutablePath); }
        }
    }

    internal sealed class Card : Panel
    {
        public Color BorderColor = Color.FromArgb(178, 196, 184);
        public Card()
        {
            BackColor = Color.White;
            Padding = new Padding(18);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var background = new SolidBrush(Parent == null ? BackColor : Parent.BackColor))
                e.Graphics.FillRectangle(background, e.ClipRectangle);
            using (var path = BrandButton.RoundRect(new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), 10F * e.Graphics.DpiX / 96F))
            using (Pen pen = new Pen(BorderColor))
            using (Brush brush = new SolidBrush(BackColor))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class PathFieldFrame : Panel
    {
        internal Color EdgeColor;
        internal Color FocusColor;
        internal PathFieldFrame()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(ContainsFocus ? FocusColor : EdgeColor))
                e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        }
    }

    internal sealed class MainForm : Form
    {
        internal bool MenuRequested { get; private set; }
        internal void SuspendSession() { closing=false; timer.Stop(); }
        internal void ResumeSession() { MenuRequested=false; closing=false; SetTheme(ThemePreference.LoadDark(),false); UpdateRefreshTimer(); }
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        private readonly Color ink = Color.FromArgb(26, 39, 60);
        private readonly Color muted = Color.FromArgb(91, 107, 128);
        private readonly Color blue = Color.FromArgb(37, 99, 235);
        private readonly Font serviceHeaderFont = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        private readonly TextBox pathBox = new BrandTextBox();
        private readonly PathFieldFrame pathField = new PathFieldFrame();
        private readonly FlushLabel fileTitle = new FlushLabel();
        private readonly Label statusTitle = new FlushLabel();
        private readonly Label statusDetail = new FlushLabel();
        private readonly Label hint = new FlushLabel();
        private readonly Label activity = new FlushLabel();
        private readonly RefreshToggle autoRefreshToggle = new RefreshToggle();
        private DateTime? lastChecked;
        private readonly Button chooseButton;
        private readonly Button installButton;
        private readonly Button removeButton;
        private readonly Button refreshButton;
        private readonly Button servicesButton;
        private readonly Button themeToggleButton;
        private readonly Button installProfileButton;
        private readonly ToolTip toolTips = new ToolTip();
        private readonly Button startButton;
        private readonly Button stopButton;
        private readonly Button applyStartupButton;
        private readonly Button collectLogsButton;
        private readonly ComboBox startupCombo = new BrandComboBox();
        private readonly PictureBox brandImage = new PictureBox();
        private readonly Image lightBrandLogo;
        private readonly Image darkBrandLogo;
        private ThemePalette palette = ThemePalette.Create(false);
        private string statusTone = "neutral";
        private bool bindingServices;
        private bool startupDirty;
        private readonly DataGridView servicesGrid = new ServiceGrid();
        private readonly Dictionary<Tuple<string, Font, int>, string> wrappedText = new Dictionary<Tuple<string, Font, int>, string>();
        private Dictionary<string, List<ServiceEntry>> indexedServices = new Dictionary<string, List<ServiceEntry>>(StringComparer.OrdinalIgnoreCase);
        private readonly TextBox log = new JournalTextBox();
        private readonly JournalScrollBar logScrollBar = new JournalScrollBar();
        private readonly Timer timer = new Timer();
        private readonly bool preview;
        private readonly Func<ServiceSnapshot> readCatalog;
        private readonly ManagedServiceStore managedStore;
        private readonly ServiceInstallProfileStore installProfiles;
        private readonly HashSet<string> knownExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> servicePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> currentServiceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<ServiceEntry> managedRows = new List<ServiceEntry>();
        private ServiceSnapshot lastSnapshot;
        private bool catalogCurrent;
        private string loggedStoreWarning = String.Empty;
        private bool focusSelectedExecutable;
        private string selectedPath;
        private int revision;
        private bool refreshing;
        private bool operationRunning;
        private CancellationTokenSource procedureWaitCancellation;
        private bool logArchiveRunning, closeAfterLogArchive;
        private CancellationTokenSource logArchiveCancellation;
        private bool verified;
        private List<ServiceEntry> matches = new List<ServiceEntry>();
        private bool closing;
        private Func<DialogResult> confirmBusyClose;
        internal const string BusyCloseMessage = "Я ещё работаю со службой.\r\n\r\nПосле закрытия проверь результат установки, удаления или другого действия в оснастке «Службы» Windows.\r\n\r\nЗакрыть программу?";

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closing = true;
                brandImage.Image = null;
                if (lightBrandLogo != null) lightBrandLogo.Dispose();
                if (darkBrandLogo != null) darkBrandLogo.Dispose();
                timer.Dispose();
                toolTips.Dispose();
            }
            base.Dispose(disposing);
            if (disposing) serviceHeaderFont.Dispose();
        }

        public MainForm(string initialPath, bool previewMode)
            : this(initialPath, previewMode, ServiceCatalog.ReadSnapshot,
                previewMode ? ManagedServiceStore.CreateMemory() : ManagedServiceStore.CreateDefault(),
                previewMode ? ServiceInstallProfileStore.CreateMemory() : ServiceInstallProfileStore.CreateDefault()) { }

        internal MainForm(string initialPath, bool previewMode, Func<ServiceSnapshot> catalogReader)
            : this(initialPath, previewMode, catalogReader, ManagedServiceStore.CreateMemory()) { }

        internal MainForm(string initialPath, bool previewMode, Func<ServiceSnapshot> catalogReader, ManagedServiceStore store)
            : this(initialPath, previewMode, catalogReader, store, ServiceInstallProfileStore.CreateMemory()) { }

        internal MainForm(string initialPath, bool previewMode, Func<ServiceSnapshot> catalogReader, ManagedServiceStore store, ServiceInstallProfileStore profiles)
        {
            if (catalogReader == null) throw new ArgumentNullException("catalogReader");
            if (store == null) throw new ArgumentNullException("store");
            if (profiles == null) throw new ArgumentNullException("profiles");
            SuspendLayout();
            readCatalog = catalogReader;
            managedStore = store;
            installProfiles = profiles;
            foreach (string executable in store.GetExecutablePaths()) knownExecutables.Add(executable);
            preview = previewMode;
            confirmBusyClose = delegate { return BrandMessageBox.Show(this, BusyCloseMessage, "Процедура ещё выполняется",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2); };
            DoubleBuffered = true;
            Text = "Bars Service Manager 3000";
            Font = new Font("Segoe UI", 10F);
            ForeColor = ink;
            BackColor = Color.FromArgb(244, 247, 251);
            ClientSize = new Size(1080, 790);
            MinimumSize = new Size(980, 700);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Icon = BrandAssets.LoadIcon() ?? SystemIcons.Application;

            TableLayoutPanel layout = new BufferedLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.MinimumSize = new Size(940, 700);
            layout.Padding = new Padding(26, 12, 26, 12);
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 390));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Panel viewport = new FillContentPanel(layout);
            Controls.Add(viewport);

            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(0, 8, 0, 0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Panel brandSlot = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 6) };
            lightBrandLogo = BrandAssets.LoadLogo(false);
            darkBrandLogo = BrandAssets.LoadLogo(true);
            brandImage.Image = lightBrandLogo;
            brandImage.SizeMode = PictureBoxSizeMode.Zoom;
            brandImage.SetBounds(0, 0, 310, 76);
            brandImage.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            brandImage.Margin = new Padding(0);
            brandImage.BorderStyle = BorderStyle.None;
            BrandAssets.BindWebsite(brandImage, toolTips, this);
            brandImage.Tag = "brand";
            brandSlot.Controls.Add(brandImage);
            bool brandLayoutPending=false;
            brandSlot.Layout += delegate
            {
                if(brandLayoutPending||!IsHandleCreated||IsDisposed)return;
                brandLayoutPending=true;
                BeginInvoke((MethodInvoker)delegate{brandLayoutPending=false;if(!IsDisposed&&!brandSlot.IsDisposed)LayoutBrandImage(brandSlot);});
            };
            header.Controls.Add(brandSlot, 1, 0);
            TableLayoutPanel themeSelector = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
            themeSelector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            themeSelector.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            themeSelector.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            themeSelector.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            themeToggleButton = new ThemeToggleButton { AccessibleName = "Переключить светлую и тёмную тему", Dock = DockStyle.Fill, Margin = new Padding(0) };
            themeToggleButton.Click += delegate { SetTheme(!palette.Dark, true); };
            themeSelector.Controls.Add(themeToggleButton, 0, 1);
            header.Controls.Add(themeSelector, 2, 0);
            layout.Controls.Add(header, 0, 0);

            Card fileCard = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            fileCard.Padding = new Padding(ModuleHeadingStyle.Left, ModuleHeadingStyle.Top, 12, 10);
            TableLayoutPanel fileLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = new Padding(0), Padding = new Padding(0) };
            fileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            fileLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            fileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            fileLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            fileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            fileCard.Controls.Add(fileLayout);
            fileTitle.Text = "Перетащи сюда .exe-файл службы BARS или выбери файл кнопкой.";
            fileTitle.Font = new Font(Font, FontStyle.Bold);
            fileTitle.Dock = DockStyle.Fill;
            fileTitle.AutoEllipsis = true;
            fileTitle.SingleLine = true;
            fileTitle.TextAlign = ContentAlignment.MiddleLeft;
            fileTitle.Margin = new Padding(0);
            var moduleTitle=ModuleHeadingStyle.Create("Менеджер служб");
            fileLayout.Controls.Add(moduleTitle,0,0);
            fileLayout.Controls.Add(fileTitle, 0, 2);
            TableLayoutPanel fileInput = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = new Padding(0) };
            fileInput.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fileInput.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 192));
            fileInput.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fileLayout.Controls.Add(fileInput, 0, 4);
            pathBox.ReadOnly = true;
            pathBox.AutoSize = true;
            pathBox.Multiline = false;
            pathBox.WordWrap = false;
            pathBox.BackColor = Color.FromArgb(248, 250, 253);
            pathBox.BorderStyle = BorderStyle.None;
            pathBox.Dock = DockStyle.None;
            pathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            pathBox.Margin = Padding.Empty;
            pathBox.TabStop = false;
            pathBox.Text = ".exe пока не выбран";
            pathField.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            pathField.Margin = new Padding(0, 0, 12, 0);
            pathField.Controls.Add(pathBox);
            pathField.Layout += delegate { ApplyPathTextInset(); };
            pathField.Click += delegate { pathBox.Focus(); };
            pathBox.GotFocus += delegate { pathField.Invalidate(); };
            pathBox.LostFocus += delegate { pathField.Invalidate(); };
            pathBox.HandleCreated += delegate { ApplyPathTextInset(); };
            pathBox.SizeChanged += delegate { ApplyPathTextInset(); };
            pathBox.FontChanged += delegate
            {
                if (pathBox.IsHandleCreated && !pathBox.IsDisposed)
                    pathBox.BeginInvoke((MethodInvoker)ApplyPathTextInset);
            };
            fileInput.Controls.Add(pathField, 0, 0);
            chooseButton = MakeButton("Выбрать службу...", false);
            chooseButton.Dock = DockStyle.Fill;
            chooseButton.Margin = new Padding(0);
            chooseButton.Click += delegate { SelectFile(); };
            fileInput.Controls.Add(chooseButton, 1, 0);
            layout.Controls.Add(fileCard, 0, 1);

            Card registered = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(12) };
            TableLayoutPanel serviceLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 1, RowCount = 5 };
            serviceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            serviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            serviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            serviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            serviceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            serviceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            registered.Controls.Add(serviceLayout);
            TableLayoutPanel statusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            statusTitle.Text = "Службы BARS";
            statusTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            statusTitle.Dock = DockStyle.Fill;
            statusTitle.Margin = Padding.Empty;
            statusTitle.TextAlign = ContentAlignment.MiddleLeft;
            statusTitle.AutoEllipsis = true;
            statusDetail.Text = "Читаем службы Windows…";
            statusDetail.Dock = DockStyle.Fill;
            statusDetail.Font = new Font("Segoe UI", 9F);
            statusDetail.Margin = Padding.Empty;
            statusDetail.TextAlign = ContentAlignment.TopLeft;
            statusDetail.AutoEllipsis = true;
            statusDetail.Tag = "muted";
            statusDetail.Visible = false;
            statusLayout.Controls.Add(statusTitle, 0, 0);
            statusLayout.Controls.Add(statusDetail, 0, 1);
            statusLayout.SetColumnSpan(statusDetail, 2);
            autoRefreshToggle.Text = "Проверять каждый 10с.";
            autoRefreshToggle.AccessibleName = "Автообновление каждые 10 секунд";
            autoRefreshToggle.AccessibleDescription = "По умолчанию включено; обновление каждые 10 секунд";
            autoRefreshToggle.Checked = true;
            autoRefreshToggle.Dock = DockStyle.None;
            autoRefreshToggle.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            autoRefreshToggle.Margin = Padding.Empty;
            autoRefreshToggle.Font = new Font("Segoe UI", 8.5F);
            autoRefreshToggle.Size = new Size(54 + TextRenderer.MeasureText(autoRefreshToggle.Text, autoRefreshToggle.Font,
                new Size(Int32.MaxValue, Int32.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width, 28);
            autoRefreshToggle.CheckedChanged += delegate { UpdateRefreshTimer(); UpdateRefreshActivity(); };
            var refreshSlot=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};
            refreshSlot.Controls.Add(autoRefreshToggle);statusLayout.Controls.Add(refreshSlot,1,0);
            refreshSlot.Layout+=delegate
            {
                using(var graphics=autoRefreshToggle.CreateGraphics())
                {
                    int prefix=1+(int)Math.Round(42*graphics.DpiX/96F)+(int)(8*graphics.DpiX/96F);
                    int text=TextRenderer.MeasureText(graphics,autoRefreshToggle.Text,autoRefreshToggle.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width;
                    int width=Math.Min(refreshSlot.ClientSize.Width,prefix+text+4);
                    int height=Math.Min(refreshSlot.ClientSize.Height,(int)Math.Round(28*graphics.DpiY/96F));
                    autoRefreshToggle.Bounds=new Rectangle(Math.Max(0,refreshSlot.ClientSize.Width-width),Math.Max(0,refreshSlot.ClientSize.Height-height),width,height);
                }
            };
            serviceLayout.Controls.Add(statusLayout, 0, 0);

            Panel actions = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(0, 7, 0, 7) };
            TableLayoutPanel actionButtons = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), ColumnCount = 5, RowCount = 1 };
            actionButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            installButton = MakeButton("Установить службу", true);
            removeButton = MakeButton("Удалить службу", false);
            ((BrandButton)removeButton).Destructive = true;
            refreshButton = MakeButton("Обновить статус", false);
            servicesButton = MakeButton("Открыть службы", false);
            installProfileButton = MakeButton("Настройка установки", false);
            installButton.Dock = removeButton.Dock = refreshButton.Dock = servicesButton.Dock = DockStyle.Fill;
            installButton.Margin = removeButton.Margin = refreshButton.Margin = new Padding(0, 0, 10, 0);
            servicesButton.Margin = Padding.Empty;
            installProfileButton.Dock = DockStyle.Fill;
            installProfileButton.Margin = new Padding(0, 0, 10, 0);
            installProfileButton.Click += delegate { ConfigureInstallProfile(); };
            installButton.Click += async delegate { await RunOperationAsync(true); };
            removeButton.Click += async delegate { await RunOperationAsync(false); };
            refreshButton.Click += async delegate
            {
                if (procedureWaitCancellation != null) { procedureWaitCancellation.Cancel(); activity.Text = "Прекращаем ожидание и проверяем регистрацию…"; }
                else await RefreshStatusAsync(false);
            };
            servicesButton.Click += delegate { OpenServices(); };
            actionButtons.Controls.Add(installButton, 0, 0);
            actionButtons.Controls.Add(removeButton, 1, 0);
            actionButtons.Controls.Add(installProfileButton, 2, 0);
            actionButtons.Controls.Add(refreshButton, 3, 0);
            actionButtons.Controls.Add(servicesButton, 4, 0);
            // Match the right header slot to the actual last action column, including DPI rounding.
            EventHandler alignRefresh = delegate
            {
                if (servicesButton.Width > 0 && statusLayout.ColumnStyles[1].Width != servicesButton.Width)
                    statusLayout.ColumnStyles[1].Width = servicesButton.Width;
            };
            servicesButton.SizeChanged += alignRefresh;
            actionButtons.Layout += delegate { alignRefresh(null, EventArgs.Empty); };
            actions.Controls.Add(actionButtons);
            serviceLayout.Controls.Add(actions, 0, 2);
            hint.Dock = DockStyle.Fill;
            hint.Tag = "muted";
            hint.ForeColor = muted;
            hint.Font = new Font("Segoe UI", 9F);
            hint.Text = String.Empty;
            hint.Visible = false;
            hint.AutoSize = true;
            hint.Margin = Padding.Empty;
            serviceLayout.SizeChanged += delegate { hint.MaximumSize = new Size(Math.Max(1, serviceLayout.ClientSize.Width), 0); };
            serviceLayout.Controls.Add(hint, 0, 1);

            servicesGrid.Dock = DockStyle.Fill;
            servicesGrid.BackgroundColor = Color.White;
            servicesGrid.BorderStyle = BorderStyle.None;
            servicesGrid.ReadOnly = true;
            servicesGrid.AllowUserToAddRows = false;
            servicesGrid.AllowUserToDeleteRows = false;
            servicesGrid.AllowUserToResizeRows = false;
            servicesGrid.AllowUserToResizeColumns = false;
            servicesGrid.AllowUserToOrderColumns = false;
            servicesGrid.RowHeadersVisible = false;
            servicesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            servicesGrid.MultiSelect = false;
            servicesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            servicesGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            servicesGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            servicesGrid.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
            servicesGrid.ColumnHeadersHeight = 42;
            servicesGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            servicesGrid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            servicesGrid.ColumnHeadersDefaultCellStyle.Font = serviceHeaderFont;
            servicesGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            servicesGrid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            servicesGrid.EnableHeadersVisualStyles = false;
            servicesGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(237, 242, 249);
            servicesGrid.ColumnHeadersDefaultCellStyle.ForeColor = ink;
            servicesGrid.DefaultCellStyle.Padding = new Padding(5);
            servicesGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            servicesGrid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            servicesGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(225, 236, 255);
            servicesGrid.DefaultCellStyle.SelectionForeColor = ink;
            servicesGrid.GridColor = Color.FromArgb(235, 239, 245);
            servicesGrid.Columns.Add("DisplayName", "Название");
            servicesGrid.Columns.Add("Name", "Системное имя");
            servicesGrid.Columns.Add("State", "Состояние");
            servicesGrid.Columns.Add("StartMode", "Запуск");
            servicesGrid.Columns[0].FillWeight = 37;
            servicesGrid.Columns[1].FillWeight = 28;
            servicesGrid.Columns[2].FillWeight = 20;
            servicesGrid.Columns[3].FillWeight = 15;
            int[] minWidths = { 185, 160, 150, 160 };
            for (int i = 0; i < servicesGrid.Columns.Count; i++)
            {
                servicesGrid.Columns[i].Resizable = DataGridViewTriState.False;
                servicesGrid.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
                servicesGrid.Columns[i].MinimumWidth = minWidths[i];
            }
            float[] columnWeights = { 34, 28, 18, 20 };
            for (int i = 0; i < servicesGrid.Columns.Count; i++) servicesGrid.Columns[i].FillWeight = columnWeights[i];
            servicesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            servicesGrid.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Value == null) return;
                DataGridViewCell cell = servicesGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                int width = cell.Size.Width - cell.InheritedStyle.Padding.Horizontal - 8;
                e.Value = WrapCellText(Convert.ToString(e.Value), cell.InheritedStyle.Font ?? servicesGrid.Font, Math.Max(30, width));
                e.FormattingApplied = true;
            };
            servicesGrid.CurrentCellChanged += delegate
            {
                if (bindingServices) return;
                startupDirty = false;
                SelectServiceExecutable();
                UpdateServiceControls(true);
            };
            servicesGrid.CellClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (!bindingServices && e.RowIndex >= 0) SelectServiceExecutable();
            };
            TableLayoutPanel management = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 70, Padding = new Padding(0, 6, 0, 0), RowCount = 2, ColumnCount = 5 };
            management.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            management.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            management.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            management.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            management.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
            management.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            management.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label managementLabel = new FlushLabel { Text = "Действия для выбранной службы", Tag = "muted", Font = new Font("Segoe UI", 9F, FontStyle.Bold), Dock = DockStyle.Fill, Margin = new Padding(12, 0, 0, 0) };
            management.Controls.Add(managementLabel, 0, 0);
            management.SetColumnSpan(managementLabel, 3);
            Label startupLabel = new FlushLabel { Text = "Режим запуска:", Tag = "muted", Font = new Font("Segoe UI", 9F, FontStyle.Bold), Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
            management.Controls.Add(startupLabel, 3, 0);
            startButton = MakeButton("Запустить", false);
            stopButton = MakeButton("Остановить", false);
            applyStartupButton = MakeButton("Применить", false);
            collectLogsButton = MakeButton("Собрать логи", false);
            collectLogsButton.Dock = DockStyle.Fill;
            collectLogsButton.Margin = new Padding(0, 0, 8, 0);
            collectLogsButton.Click += async delegate { await CollectLogsAsync(); };
            startButton.Dock = stopButton.Dock = applyStartupButton.Dock = DockStyle.Fill;
            startButton.Margin = stopButton.Margin = new Padding(0, 0, 8, 0);
            applyStartupButton.Margin = new Padding(8, 0, 0, 0);
            startupCombo.Dock = DockStyle.None;
            startupCombo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            startupCombo.Margin = Padding.Empty;
            startupCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            startupCombo.FlatStyle = FlatStyle.Flat;
            startupCombo.DrawMode = DrawMode.OwnerDrawFixed;
            ((BrandComboBox)startupCombo).HoverHighlight=false;
            startupCombo.ItemHeight=startupCombo.Font.Height+8;
            startupCombo.FontChanged+=delegate{startupCombo.ItemHeight=startupCombo.Font.Height+8;};
            Action matchPathField = delegate
            {
                if (!pathBox.Font.Equals(startupCombo.Font)) pathBox.Font = new Font(startupCombo.Font, startupCombo.Font.Style);
                pathField.Height = startupCombo.Height;
                ApplyPathTextInset();
            };
            startupCombo.SizeChanged += delegate { matchPathField(); };
            startupCombo.FontChanged += delegate { matchPathField(); };
            matchPathField();
            startupCombo.Items.AddRange(new object[] { "Автоматически", "Автоматически (отложенный запуск)", "Вручную", "Отключена" });
            startupCombo.DrawItem += DrawStartupItem;
            startupCombo.SelectedIndexChanged += delegate { if (!bindingServices) { startupDirty = true; UpdateServiceControls(false); } };
            startButton.Click += async delegate { await RunServiceCommandAsync("start", "none"); };
            stopButton.Click += async delegate { await RunServiceCommandAsync("stop", "none"); };
            applyStartupButton.Click += async delegate { await RunServiceCommandAsync("startup", StartupValue(startupCombo.SelectedIndex)); };
            management.Controls.Add(startButton, 0, 1);
            management.Controls.Add(stopButton, 1, 1);
            management.Controls.Add(collectLogsButton, 2, 1);
            Panel startupSlot = new Panel { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
            startupSlot.Controls.Add(startupCombo);
            startupSlot.Layout += delegate
            {
                int width=(int)Math.Round(startupSlot.ClientSize.Width*0.8);
                startupCombo.SetBounds(0, (startupSlot.ClientSize.Height - startupCombo.Height) / 2, width, startupCombo.Height);
                int dropWidth=width;foreach(object item in startupCombo.Items)dropWidth=Math.Max(dropWidth,TextRenderer.MeasureText(Convert.ToString(item),startupCombo.Font).Width+40);
                startupCombo.DropDownWidth=dropWidth;
            };
            management.Controls.Add(startupSlot, 3, 1);
            management.Controls.Add(applyStartupButton, 4, 1);
            servicesGrid.Margin = Padding.Empty;
            management.Margin = Padding.Empty;
            management.Dock = DockStyle.Fill;
            serviceLayout.Controls.Add(servicesGrid, 0, 3);
            serviceLayout.Controls.Add(management, 0, 4);
            layout.Controls.Add(registered, 0, 2);

            Card journal = new Card { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(12, 8, 12, 8) };
            Label logTitle = new FlushLabel { Text = "Журнал текущего сеанса", Tag = "muted", Dock = DockStyle.Top, Height = 24, ForeColor = muted, Font = new Font("Segoe UI", 9F), Margin = Padding.Empty };
            log.Dock = DockStyle.Fill;
            log.ReadOnly = true;
            log.Multiline = true;
            log.ScrollBars = ScrollBars.None;
            log.BorderStyle = BorderStyle.None;
            log.BackColor = Color.White;
            log.Font = new Font("Consolas", 9F);
            Panel logViewport = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            logScrollBar.Dock = DockStyle.Right;
            logScrollBar.Width = 18;
            logViewport.Controls.Add(log);
            logViewport.Controls.Add(logScrollBar);
            logScrollBar.Bind((JournalTextBox)log);
            journal.Controls.Add(logViewport);
            journal.Controls.Add(logTitle);
            layout.Controls.Add(journal, 0, 3);
            activity.Dock = DockStyle.Fill;
            activity.Margin = new Padding(12, 0, 0, 0);
            activity.TextAlign = ContentAlignment.MiddleLeft;
            activity.Font = new Font("Segoe UI", 8.5F);
            activity.Tag = "muted";
            activity.ForeColor = muted;
            activity.Text = "Локальный компьютер; автообновление каждые 10 с";
            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footer.Controls.Add(activity, 0, 0);
            Button menuButton = MakeButton("В меню", false);
            ModuleNavigation.Configure(menuButton);
            menuButton.Click += delegate { MenuRequested = true; Close(); };
            footer.Controls.Add(menuButton, 1, 0);
            layout.Controls.Add(footer, 0, 4);

            timer.Interval = 10000;
            timer.Tick += async delegate { if (autoRefreshToggle.Checked && !startupCombo.DroppedDown) await RefreshStatusAsync(true); };
            Shown += async delegate
            {
                if (preview) return;
                if (!String.IsNullOrWhiteSpace(initialPath)) await SetExecutableAsync(initialPath);
                else await RefreshStatusAsync(false);
                UpdateRefreshTimer();
            };
            VisibleChanged += delegate { UpdateRefreshTimer(); };
            FormClosing += OnFormClosing;
            FormClosed += delegate { closing = true; timer.Dispose(); toolTips.Dispose(); };
            AppendLog("Готово. Общий список служб BARS загрузится автоматически. Для новой службы выбери .exe службы BARS.");
            if (!String.IsNullOrEmpty(ApplicationStorage.MigrationWarning)) AppendLog(ApplicationStorage.MigrationWarning);
            if (!String.IsNullOrEmpty(managedStore.Warning))
            {
                AppendLog(managedStore.Warning);
                loggedStoreWarning = managedStore.Warning;
            }
            EnableDrop(this);
            SetTheme(!preview && ThemePreference.LoadDark(), false);
            UpdateActions();
            ResumeLayout(true);
        }

        protected override void OnLoad(EventArgs e)
        {
            Rectangle area=Screen.FromControl(this).WorkingArea;
            MinimumSize=new Size(Math.Min(MinimumSize.Width,area.Width),Math.Min(MinimumSize.Height,area.Height));
            if(WindowState==FormWindowState.Normal)Size=new Size(Math.Min(Width,area.Width),Math.Min(Height,area.Height));
            WindowPlacement.Constrain(this);
            base.OnLoad(e);
        }
        protected override void OnShown(EventArgs e)
        {
            WindowPlacement.Constrain(this);
            LayoutBrandImage(brandImage.Parent);
            base.OnShown(e);
        }

        private void UpdateRefreshTimer()
        {
            if (Visible && !closing && !operationRunning && !preview && autoRefreshToggle.Checked) timer.Start();
            else timer.Stop();
        }

        private void UpdateRefreshActivity()
        {
            activity.Text = "Локальный компьютер" + (lastChecked.HasValue ? "; проверено в " + lastChecked.Value.ToString("HH:mm:ss") : "") +
                (autoRefreshToggle.Checked ? "; автообновление каждые 10 с" : "; автообновление выключено");
        }

        private Label MakeLabel(string text, float size, Color color, Rectangle bounds)
        {
            Label label = new FlushLabel { Text = text, Font = new Font("Segoe UI", size), ForeColor = color };
            label.Bounds = bounds;
            return label;
        }

        private Button MakeButton(string text, bool primary)
        {
            BrandButton button = new BrandButton { Text = text, Primary = primary, Palette = palette };
            button.Font = new Font("Segoe UI", 9.5F, primary ? FontStyle.Bold : FontStyle.Regular);
            return button;
        }

        private void ApplyPathTextInset()
        {
            if (pathBox.IsDisposed || pathField.IsDisposed) return;
            int inset;
            using (Graphics graphics = pathField.CreateGraphics())
                inset = Math.Max(1, (int)Math.Round(8F * graphics.DpiX / 96F));
            // Center the actual native edit window, including its caret and selection.
            // Its normal single-line height prevents native text from sticking to the top
            // of the taller outer frame. Never paint over the edit's text.
            int height = pathBox.PreferredHeight;
            Rectangle bounds = new Rectangle(inset,
                Math.Max(1, (pathField.ClientSize.Height - height) / 2),
                Math.Max(1, pathField.ClientSize.Width - inset * 2), height);
            if (pathBox.Bounds != bounds) pathBox.Bounds = bounds;
        }
        private void LayoutBrandImage(Control slot)
        {
            if (slot.ClientSize.Width < 1 || slot.ClientSize.Height < 1 || brandImage.Image == null) return;
            double aspect = (double)brandImage.Image.Width / brandImage.Image.Height;
            int height = Math.Max(1, slot.ClientSize.Height - slot.Padding.Vertical);
            int width = Math.Max(1, (int)Math.Round(height * aspect));
            if (width > slot.ClientSize.Width - slot.Padding.Horizontal)
            {
                width = slot.ClientSize.Width - slot.Padding.Horizontal;
                height = Math.Max(1, (int)Math.Round(width / aspect));
            }
            Rectangle bounds = new Rectangle((slot.ClientSize.Width - width) / 2,
                (slot.ClientSize.Height - height) / 2, width, height);
            if (brandImage.Bounds != bounds) brandImage.Bounds = bounds;
        }

        internal void SetTheme(bool dark, bool persist)
        {
            palette = ThemePalette.Create(dark);
            SuspendLayout();
            ApplyThemeTree(this, palette.Canvas);
            brandImage.Image = dark ? darkBrandLogo : lightBrandLogo;
            ((ThemeToggleButton)themeToggleButton).Dark = dark;
            ((ThemeToggleButton)themeToggleButton).Palette = palette;
            themeToggleButton.AccessibleDescription = dark ? "Сейчас тёмная тема. Нажми, чтобы включить светлую." : "Сейчас светлая тема. Нажми, чтобы включить тёмную.";
            toolTips.SetToolTip(themeToggleButton, dark ? "Включить светлую тему" : "Включить тёмную тему");
            servicesGrid.BackgroundColor = palette.Surface;
            servicesGrid.GridColor = palette.Border;
            servicesGrid.DefaultCellStyle.BackColor = palette.Surface;
            servicesGrid.DefaultCellStyle.ForeColor = palette.Text;
            servicesGrid.DefaultCellStyle.SelectionBackColor = palette.Selection;
            servicesGrid.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
            servicesGrid.AlternatingRowsDefaultCellStyle.BackColor = palette.SurfaceAlt;
            servicesGrid.AlternatingRowsDefaultCellStyle.ForeColor = palette.Text;
            servicesGrid.AlternatingRowsDefaultCellStyle.SelectionBackColor = palette.Selection;
            servicesGrid.AlternatingRowsDefaultCellStyle.SelectionForeColor = palette.SelectionText;
            servicesGrid.ColumnHeadersDefaultCellStyle.BackColor = palette.SurfaceAlt;
            servicesGrid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
            servicesGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.SurfaceAlt;
            servicesGrid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
            startupCombo.BackColor = palette.SurfaceAlt;
            startupCombo.ForeColor = palette.Text;
            ((BrandComboBox)startupCombo).Palette = palette;
            logScrollBar.Palette = palette;
            autoRefreshToggle.Palette = palette;
            ApplyStatusColor();
            ApplyNativeTheme();
            ResumeLayout(true);
            if (brandImage.Parent != null) LayoutBrandImage(brandImage.Parent);
            Invalidate(true);
            if (persist && !preview && !ThemePreference.TrySave(dark))
                AppendLog("Тема применена, но сохранить выбор для следующего запуска не удалось.");
        }

        private void ApplyThemeTree(Control control, Color background)
        {
            Card card = control as Card;
            if (card != null)
            {
                background = Convert.ToString(card.Tag) == "brand" ? Color.FromArgb(247, 250, 245) : palette.Surface;
                card.BorderColor = Convert.ToString(card.Tag) == "brand" ? palette.Accent : palette.Border;
            }
            control.BackColor = background;
            control.ForeColor = Convert.ToString(control.Tag) == "muted" ? palette.Muted : palette.Text;
            BrandButton button = control as BrandButton;
            if (button != null)
            {
                button.Palette = palette;
                button.BackColor = button.Primary || button.Selected ? palette.Accent : background;
                button.ForeColor = button.Primary || button.Selected ? palette.AccentText : palette.Text;
            }
            BrandTextBox input = control as BrandTextBox;
            if (input != null) input.Palette = palette;
            if (control == pathField)
            {
                background = palette.SurfaceAlt;
                control.BackColor = background;
                pathField.EdgeColor = palette.InputBorder;
                pathField.FocusColor = palette.Accent;
                pathField.Invalidate();
            }
            if (control == pathBox) control.BackColor = palette.SurfaceAlt;
            foreach (Control child in control.Controls) ApplyThemeTree(child, background);
        }

        private string WrapCellText(string text, Font font, int width)
        {
            var key = Tuple.Create(text, font, width);
            string formatted;
            if (wrappedText.TryGetValue(key, out formatted)) return formatted;
            if (wrappedText.Count >= 2048) wrappedText.Clear();
            formatted = WrapLongTokens(text, font, width);
            wrappedText.Add(key, formatted);
            return formatted;
        }

        private static string WrapLongTokens(string text, Font font, int width)
        {
            string[] words = text.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (TextRenderer.MeasureText(word, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width <= width) continue;
                var formatted = new System.Text.StringBuilder();
                string line = String.Empty;
                var elements = System.Globalization.StringInfo.GetTextElementEnumerator(word);
                while (elements.MoveNext())
                {
                    string element = elements.GetTextElement();
                    if (line.Length > 0 && TextRenderer.MeasureText(line + element, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width > width)
                    {
                        formatted.Append(line).Append('\n');
                        line = String.Empty;
                    }
                    line += element;
                }
                formatted.Append(line);
                words[i] = formatted.ToString();
            }
            return String.Join(" ", words);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyNativeTheme();
        }

        private void ApplyNativeTheme()
        {
            NativeWindowTheme.Apply(this, palette.Dark);
        }

        private void ApplyStatusColor()
        {
            statusTitle.ForeColor = statusTone == "error" ? palette.Danger :
                statusTone == "warning" ? palette.Warning :
                statusTone == "success" || statusTone == "info" ? palette.Good : palette.Text;
        }

        private void DrawStartupItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color background = selected ? palette.Selection : palette.SurfaceAlt;
            Color foreground = selected ? palette.SelectionText : palette.Text;
            using (Brush brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, Convert.ToString(startupCombo.Items[e.Index]), startupCombo.Font,
                new Rectangle(e.Bounds.X + 7, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 12), e.Bounds.Height), foreground,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }

        private ServiceEntry SelectedService()
        {
            return servicesGrid.CurrentRow == null ? null : servicesGrid.CurrentRow.Tag as ServiceEntry;
        }

        private void SelectServiceExecutable()
        {
            ServiceEntry entry = SelectedService();
            string executable;
            if (entry == null) return;
            if (!servicePaths.TryGetValue(entry.Name, out executable)) executable = null;
            if (String.Equals(selectedPath, executable, StringComparison.OrdinalIgnoreCase)) return;
            selectedPath = executable;
            revision++;
            pathBox.Text = executable ?? "Путь .exe этой службы недоступен";
            fileTitle.Text = executable == null ? "Выбери .exe для установки" : Path.GetFileName(executable);
            SetSelectedMatches();
            ShowRegistrationStatus();
            ShowCatalogWarning();
            UpdateActions();
        }

        private void SetSelectedMatches()
        {
            List<ServiceEntry> found;
            matches = !String.IsNullOrEmpty(selectedPath) && indexedServices.TryGetValue(selectedPath, out found)
                ? new List<ServiceEntry>(found) : new List<ServiceEntry>();
            verified = catalogCurrent && !String.IsNullOrEmpty(selectedPath) &&
                (lastSnapshot.Complete || matches.Count > 0);
        }

        private void AcceptSnapshot(ServiceSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Entries == null)
                throw new InvalidOperationException("Не удалось прочитать список служб Windows.");
            var index = new Dictionary<string, List<ServiceEntry>>(StringComparer.OrdinalIgnoreCase);
            var executableByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ServiceEntry entry in snapshot.Entries)
            {
                string executable;
                if (entry == null || String.IsNullOrEmpty(entry.Name) || !ExecutablePaths.TryGetExecutable(entry.BinaryPath, out executable)) continue;
                executableByName[entry.Name] = executable;
                List<ServiceEntry> group;
                if (!index.TryGetValue(executable, out group)) index.Add(executable, group = new List<ServiceEntry>());
                group.Add(entry);
            }
            indexedServices = index;
            lastSnapshot = snapshot;
            catalogCurrent = true;
            SetSelectedMatches();
            if (!String.IsNullOrEmpty(selectedPath) && matches.Count > 0)
            {
                knownExecutables.Add(selectedPath);
                managedStore.Remember(selectedPath);
            }

            var paths = new HashSet<string>(knownExecutables, StringComparer.OrdinalIgnoreCase);
            if (!String.IsNullOrEmpty(selectedPath)) paths.Add(selectedPath);
            var rows = new List<ServiceEntry>();
            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            currentServiceNames.Clear();
            foreach (ServiceEntry entry in snapshot.Entries)
            {
                if (entry == null || String.IsNullOrEmpty(entry.Name)) continue;
                string executable;
                bool mapped = executableByName.TryGetValue(entry.Name, out executable);
                bool prefix = entry.Name.StartsWith("DK.", StringComparison.OrdinalIgnoreCase) || entry.Name.StartsWith("Bars.", StringComparison.OrdinalIgnoreCase);
                if (!(prefix || mapped && paths.Contains(executable)) || !currentServiceNames.Add(entry.Name)) continue;
                rows.Add(entry);
                if (mapped) mappings[entry.Name] = executable;
            }
            // An incomplete read cannot establish that a previously known registration is gone.
            if (!snapshot.Complete)
                foreach (ServiceEntry entry in managedRows)
                {
                    string executable;
                    if (!currentServiceNames.Contains(entry.Name))
                    {
                        rows.Add(entry);
                        if (servicePaths.TryGetValue(entry.Name, out executable)) mappings[entry.Name] = executable;
                    }
                }
            managedRows = rows;
            servicePaths.Clear();
            foreach (KeyValuePair<string, string> mapping in mappings) servicePaths.Add(mapping.Key, mapping.Value);
            ShowMatches();
        }

        private void ShowCatalogWarning()
        {
            string warning = lastSnapshot == null ? String.Empty : lastSnapshot.Warning;
            string storeWarning = managedStore.Warning;
            if (!String.IsNullOrEmpty(storeWarning))
                warning = (String.IsNullOrEmpty(warning) ? "" : warning + " ") + storeWarning;
            if (storeWarning != loggedStoreWarning)
            {
                if (!String.IsNullOrEmpty(storeWarning)) AppendLog(storeWarning);
                loggedStoreWarning = storeWarning;
            }
            if (!String.IsNullOrEmpty(warning))
            {
                toolTips.SetToolTip(statusTitle, warning);
                if (!log.Text.Contains(warning)) AppendLog(warning);
            }
        }

        private static string StartupValue(int index)
        {
            string[] values = { "auto", "auto-delayed", "manual", "disabled" };
            return index >= 0 && index < values.Length ? values[index] : "none";
        }

        private static int StartupIndex(ServiceEntry entry)
        {
            if (!entry.StartupKnown) return -1;
            if (entry.StartMode == "Auto") return entry.DelayedAutoStart ? 1 : 0;
            if (entry.StartMode == "Manual") return 2;
            if (entry.StartMode == "Disabled") return 3;
            return -1;
        }

        private void UpdateServiceControls(bool resetMode)
        {
            ServiceEntry entry = SelectedService();
            bool available = entry != null && catalogCurrent && currentServiceNames.Contains(entry.Name) && !operationRunning && !refreshing;
            bool pending = entry != null && (entry.State ?? String.Empty).EndsWith("Pending", StringComparison.Ordinal);
            startButton.Enabled = available && !pending && entry.State == "Stopped" && entry.StartMode != "Disabled";
            stopButton.Enabled = available && !pending && (entry.State == "Running" || entry.State == "Paused") && entry.CanStop;
            startupCombo.Enabled = available && entry.StartupKnown;
            if (resetMode)
            {
                bool previousBinding = bindingServices;
                bindingServices = true;
                startupCombo.SelectedIndex = entry == null ? -1 : StartupIndex(entry);
                bindingServices = previousBinding;
                startupDirty = false;
            }
            applyStartupButton.Enabled = available && entry.StartupKnown && startupCombo.SelectedIndex >= 0 && startupCombo.SelectedIndex != StartupIndex(entry);
            collectLogsButton.Text = logArchiveRunning ? "Отменить сбор" : "Собрать логи";
            collectLogsButton.Enabled = logArchiveRunning
                ? logArchiveCancellation != null && !logArchiveCancellation.IsCancellationRequested
                : available && servicePaths.ContainsKey(entry.Name);
        }

        private void EnableDrop(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += delegate(object sender, DragEventArgs e)
            {
                e.Effect = !operationRunning && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            control.DragDrop += async delegate(object sender, DragEventArgs e)
            {
                if (operationRunning) return;
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null || paths.Length == 0) return;
                if (paths.Length != 1)
                {
                    BrandMessageBox.Show(this, "Перетащи один .exe службы BARS. Утилита выполняет процедуры для одного .exe за раз.", "Выбор службы", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                await SetExecutableAsync(paths[0]);
            };
            foreach (Control child in control.Controls) EnableDrop(child);
        }

        private async void SelectFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Выбери .exe службы BARS";
                dialog.Filter = ".exe службы BARS (*.exe)|*.exe";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (!String.IsNullOrEmpty(selectedPath)) dialog.InitialDirectory = Path.GetDirectoryName(selectedPath);
                if (dialog.ShowDialog(this) == DialogResult.OK) await SetExecutableAsync(dialog.FileName);
            }
        }

        private async Task SetExecutableAsync(string path)
        {
            if (operationRunning) return;
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (!String.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
                    throw new ArgumentException("Выбери существующий файл с расширением .exe.");
                if (String.Equals(ExecutablePaths.Normalize(fullPath), ExecutablePaths.Normalize(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Выбери .exe-файл службы, а не сам Bars Service Manager 3000.");
                selectedPath = fullPath;
                revision++;
                verified = false;
                focusSelectedExecutable = true;
                SetHint(String.Empty);
                matches.Clear();
                bindingServices = true;
                try { servicesGrid.ClearSelection(); servicesGrid.CurrentCell = null; }
                finally { bindingServices = false; }
                startupDirty = false;
                UpdateServiceControls(true);
                pathBox.Text = fullPath;
                fileTitle.Text = Path.GetFileName(fullPath);
                SetStatus("Проверяем регистрацию…", "Читаем список служб локального компьютера.", muted);
                AppendLog("Выбран .exe службы BARS: " + fullPath);
                UpdateActions();
                await RefreshStatusAsync(false);
            }
            catch (Exception ex)
            {
                BrandMessageBox.Show(this, ex.Message, "Не удалось выбрать .exe", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task RefreshStatusAsync(bool quiet)
        {
            if (preview || closing || operationRunning || refreshing) return;
            refreshing = true;
            int currentRevision = revision;
            UpdateActions();
            try
            {
                ServiceSnapshot snapshot = await Task.Factory.StartNew<ServiceSnapshot>(readCatalog);
                if (closing) return;
                if (currentRevision != revision) return;
                AcceptSnapshot(snapshot);
                if (!String.IsNullOrEmpty(selectedPath) && !snapshot.Complete && matches.Count == 0)
                    SetStatus("Статус регистрации неизвестен", snapshot.Warning, Color.DarkOrange);
                else
                {
                    ShowRegistrationStatus();
                }
                ShowCatalogWarning();
                lastChecked = DateTime.Now;
                UpdateRefreshActivity();
                if (!quiet)
                {
                    AppendLog("Проверка завершена. Служб: " + managedRows.Count + ". Для выбранного .exe: " + matches.Count + ".");
                    if (!String.IsNullOrEmpty(snapshot.Warning)) AppendLog(snapshot.Warning);
                }
            }
            catch (Exception ex)
            {
                if (!closing && currentRevision == revision)
                {
                    verified = false;
                    catalogCurrent = false;
                    currentServiceNames.Clear();
                    matches.Clear();
                    SetStatus("Статус не удалось проверить", ex.Message, Color.Firebrick);
                    if (!quiet) AppendLog("Ошибка проверки: " + ex.Message);
                    SetHint(String.Empty);
                    activity.Text = "Ошибка чтения служб. Отображается последнее известное состояние. Повтори проверку.";
                }
            }
            finally
            {
                refreshing = false;
                if (!closing)
                {
                    UpdateActions();
                    if (currentRevision != revision)
                        BeginInvoke((Action)(async delegate { await RefreshStatusAsync(false); }));
                }
            }
        }

        private void ShowMatches()
        {
            ServiceEntry previous = SelectedService();
            string previousName = previous == null ? null : previous.Name;
            string desiredName = focusSelectedExecutable ? null : previousName;
            if (focusSelectedExecutable)
                foreach (ServiceEntry entry in managedRows)
                {
                    string executable;
                    if (servicePaths.TryGetValue(entry.Name, out executable) &&
                        String.Equals(executable, selectedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        desiredName = entry.Name;
                        break;
                    }
                }
            bool rebuild = !GridRowIdentitiesUnchanged();
            int scrollRow = servicesGrid.FirstDisplayedScrollingRowIndex;
            bindingServices = true;
            servicesGrid.SuspendLayout();
            try
            {
                if (rebuild)
                {
                    servicesGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
                    servicesGrid.Rows.Clear();
                    foreach (ServiceEntry entry in managedRows)
                    {
                        int index = servicesGrid.Rows.Add(entry.DisplayName, entry.Name,
                            currentServiceNames.Contains(entry.Name) ? StateText(entry.State) : "Не проверено", RenderedStartupMode(entry));
                        DataGridViewRow row = servicesGrid.Rows[index];
                        row.Tag = entry;
                        row.Cells[0].ToolTipText = entry.DisplayName + Environment.NewLine + entry.BinaryPath;
                        foreach (DataGridViewCell cell in row.Cells)
                            if (cell.ColumnIndex != 0) cell.ToolTipText = Convert.ToString(cell.Value);
                    }
                }
                else for (int i = 0; i < managedRows.Count; i++)
                {
                    ServiceEntry entry = managedRows[i];
                    DataGridViewRow row = servicesGrid.Rows[i];
                    row.Tag = entry;
                    string[] values = { entry.DisplayName, entry.Name,
                        currentServiceNames.Contains(entry.Name) ? StateText(entry.State) : "Не проверено", RenderedStartupMode(entry) };
                    for (int column = 0; column < values.Length; column++)
                    {
                        DataGridViewCell cell = row.Cells[column];
                        if (Convert.ToString(cell.Value) != values[column]) cell.Value = values[column];
                        string tip = column == 0 ? entry.DisplayName + Environment.NewLine + entry.BinaryPath : values[column];
                        if (cell.ToolTipText != tip) cell.ToolTipText = tip;
                    }
                }
                if (rebuild || focusSelectedExecutable)
                {
                    servicesGrid.ClearSelection();
                    servicesGrid.CurrentCell = null;
                    if (desiredName != null)
                        foreach (DataGridViewRow row in servicesGrid.Rows)
                            if (String.Equals(((ServiceEntry)row.Tag).Name, desiredName, StringComparison.OrdinalIgnoreCase))
                            { servicesGrid.CurrentCell = row.Cells[0]; row.Selected = true; break; }
                }
            }
            finally
            {
                if (rebuild) servicesGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
                servicesGrid.ResumeLayout(true);
                if (rebuild && scrollRow >= 0 && scrollRow < servicesGrid.Rows.Count) servicesGrid.FirstDisplayedScrollingRowIndex = scrollRow;
                bindingServices = false;
                focusSelectedExecutable = false;
            }
            ServiceEntry current = SelectedService();
            UpdateServiceControls(!startupDirty || current == null || previousName != current.Name);
        }

        private static string RenderedStartupMode(ServiceEntry entry)
        {
            return !entry.StartupKnown ? "Не удалось прочитать" :
                entry.StartMode == "Auto" && entry.DelayedAutoStart ? "Автоматически (отложенный запуск)" : ModeText(entry.StartMode);
        }

        private bool GridRowIdentitiesUnchanged()
        {
            if (servicesGrid.Rows.Count != managedRows.Count) return false;
            for (int i = 0; i < managedRows.Count; i++)
            {
                ServiceEntry entry = managedRows[i];
                DataGridViewRow row = servicesGrid.Rows[i];
                if (Convert.ToString(row.Cells[1].Value) != entry.Name) return false;
            }
            return true;
        }

        private void ShowRegistrationStatus()
        {
            if (String.IsNullOrEmpty(selectedPath))
                SetStatus("Службы BARS", managedRows.Count > 0
                    ? "Зарегистрированных служб: " + managedRows.Count + ". Выбери строку для управления или .exe-файл для установки."
                    : "Выбери .exe службы BARS. Найденные регистрации сохранятся в общем списке.", muted);
            else if (!verified)
                SetStatus("Статус регистрации неизвестен", lastSnapshot == null ? "Обнови список служб." : lastSnapshot.Warning, Color.DarkOrange);
            else if (matches.Count == 0)
                SetStatus("Регистрация не найдена", "Среди служб Windows нет прямой ссылки на выбранный .exe.", muted);
            else
            {
                string title = matches.Count == 1 ? RegistrationText(matches[0].State)
                    : "Зарегистрировано служб: " + matches.Count;
                SetStatus(title, String.Empty, Color.FromArgb(21, 128, 85));
            }
            SetHint(matches.Count > 1
                ? "Процедура -u относится к .exe службы и может удалить все связанные с ним службы."
                : String.Empty);
        }

        private void SetHint(string text)
        {
            hint.Text = String.Empty;
            hint.Visible = false;
            toolTips.SetToolTip(removeButton, text);
        }

        private void ConfigureInstallProfile()
        {
            if (operationRunning || String.IsNullOrEmpty(selectedPath)) return;
            using (ServiceAccountDialog dialog = new ServiceAccountDialog(palette, installProfiles.Get(selectedPath), false))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!installProfiles.Save(dialog.Profile)) AppendLog(installProfiles.Warning);
                else AppendLog("Настройки установки .exe службы BARS сохранены.");
            }
            ShowRegistrationStatus();
            ShowCatalogWarning();
            UpdateActions();
        }

        private bool TryGetSelectedLogSource(out string serviceName, out string executable)
        {
            serviceName = executable = null;
            ServiceEntry entry = SelectedService();
            if (entry == null || !catalogCurrent || !currentServiceNames.Contains(entry.Name) || operationRunning || refreshing) return false;
            if (!servicePaths.TryGetValue(entry.Name, out executable) || String.IsNullOrEmpty(executable)) return false;
            serviceName = entry.Name;
            return true;
        }

        private async Task CollectLogsAsync()
        {
            if (logArchiveRunning)
            {
                if (logArchiveCancellation != null) logArchiveCancellation.Cancel();
                UpdateActions();
                return;
            }
            string serviceName, executable;
            if (!TryGetSelectedLogSource(out serviceName, out executable)) return;
            logArchiveCancellation = new CancellationTokenSource();
            CancellationToken token = logArchiveCancellation.Token;
            logArchiveRunning = operationRunning = true;
            timer.Stop();
            UpdateActions();
            try
            {
                activity.Text = "Читаем папки логов выбранной службы…";
                List<LogDateFolder> folders = await Task.Factory.StartNew(delegate
                {
                    ServiceLogArchive.ValidateSource(executable, new LogArchiveSelection(), token);
                    return ServiceLogArchive.ListDateFolders(executable, token);
                });
                token.ThrowIfCancellationRequested();
                LogArchiveSelection selection;
                using (LogArchiveDialog dialog = new LogArchiveDialog(palette, serviceName, folders, DateTime.Today))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    selection = dialog.Selection;
                }
                string destination;
                using (SaveFileDialog dialog = new SaveFileDialog
                {
                    Title = "Сохрани логи службы " + serviceName,
                    Filter = "ZIP-архив (*.zip)|*.zip", DefaultExt = "zip", AddExtension = true,
                    OverwritePrompt = true, CheckPathExists = true,
                    FileName = ServiceLogArchive.BuildSuggestedFileName(serviceName, selection, DateTime.Now)
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    destination = dialog.FileName;
                }
                token.ThrowIfCancellationRequested();
                SetStatus("Собираем логи…", serviceName + ": ZIP создаётся без остановки службы.", blue);
                AppendLog("Сбор TXT/LOG-логов службы " + serviceName + ".");
                activity.Text = "Создаём ZIP. Для отмены нажми «Отменить сбор».";
                LogArchiveResult result = await Task.Factory.StartNew(delegate
                    { return ServiceLogArchive.Create(executable, serviceName, destination, selection, token); });
                if (closing) return;
                SetStatus("Логи собраны", "Файлов: " + result.FileCount + ". Архив: " + destination, Color.FromArgb(21, 128, 85));
                AppendLog("Логи службы " + serviceName + " сохранены: " + destination + ". Файлов: " + result.FileCount + ".");
                activity.Text = "Архив сохранён в " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (OperationCanceledException)
            {
                if (!closing) { SetStatus("Сбор логов отменён", "Незавершённый ZIP-архив не сохранён.", muted); AppendLog("Сбор логов службы " + serviceName + " отменён."); }
            }
            catch (Exception ex)
            {
                if (!closing) { SetStatus("Не удалось собрать логи", ex.Message, Color.Firebrick); AppendLog("Ошибка сбора логов службы " + serviceName + ": " + ex.Message); }
            }
            finally
            {
                logArchiveRunning = operationRunning = false;
                logArchiveCancellation.Dispose();
                logArchiveCancellation = null;
                if (!closing)
                {
                    UpdateRefreshTimer();
                    UpdateActions();
                    if (closeAfterLogArchive) BeginInvoke((Action)Close);
                }
            }
        }

        private async Task RunOperationAsync(bool install)
        {
            if (operationRunning || refreshing || !verified || String.IsNullOrEmpty(selectedPath)) return;
            string executable = selectedPath;
            ServiceInstallProfile installProfile = installProfiles.Get(executable);
            string accountPassword = null;
            operationRunning = true;
            timer.Stop();
            UpdateActions();
            SetStatus(install ? "Устанавливаем службу…" : "Удаляем регистрацию…", "Подтверди запрос Windows на права администратора, если он появится.", blue);
            activity.Text = "Выполняется процедура. Дождись завершения работы.";
            try
            {
                if (install && installProfile.Mode == ServiceInstallMode.ManagedDotNet && installProfile.Account == InstallAccountKind.User)
                {
                    string warning;
                    bool savedPassword = installProfile.TryGetSavedPassword(out accountPassword, out warning);
                    if (!String.IsNullOrEmpty(warning)) AppendLog(warning);
                    if (!installProfiles.HasStoredProfile(executable) || !savedPassword)
                    {
                        using (ServiceAccountDialog dialog = new ServiceAccountDialog(palette, installProfile, true))
                        {
                            if (dialog.ShowDialog(this) != DialogResult.OK)
                            {
                                SetStatus("Установка отменена", "Учётная запись не подтверждена. Служба не изменена.", muted);
                                return;
                            }
                            installProfile = dialog.Profile;
                            accountPassword = dialog.Password;
                            if (!installProfiles.Save(installProfile)) AppendLog(installProfiles.Warning);
                        }
                    }
                }
                int exitCode;
                bool processStillRunning = false;
                int procedureProcessId = 0;
                ProcedureWaitReason waitReason = ProcedureWaitReason.Exited;
                string executionDetail = String.Empty;
                if (installProfile.Mode == ServiceInstallMode.ManagedDotNet)
                {
                    AppendLog((install ? "Установка" : "Удаление") + " службы BARS в особом режиме .NET: " + executable + ".");
                    ProfileInstallResult result = await Task.Factory.StartNew(delegate
                        { return ServiceProfileInstaller.Execute(Application.ExecutablePath, installProfile, accountPassword, !install); });
                    exitCode = result.ExitCode;
                    executionDetail = result.Message;
                    if (!String.IsNullOrEmpty(executionDetail)) AppendLog(executionDetail);
                }
                else
                {
                    AppendLog("Запуск .exe службы BARS: \"" + executable + "\" " + (install ? "-i" : "-u"));
                    ProcessStartInfo startInfo = ServiceActions.BuildStartInfo(executable, install);
                    procedureWaitCancellation = new CancellationTokenSource();
                    CancellationToken waitCancellation = procedureWaitCancellation.Token;
                    UpdateActions();
                    ProcedureWaitResult completion = await Task.Factory.StartNew(delegate
                    {
                        using (Process process = Process.Start(startInfo))
                        {
                            if (process == null) throw new InvalidOperationException("Windows не вернула процесс службы BARS.");
                            procedureProcessId = process.Id;
                            return ProcedureWait.Run(process.WaitForExit, delegate { return process.ExitCode; },
                                readCatalog, executable, install, waitCancellation,
                                delegate(int seconds) { PostLog(".exe процедуры ещё работает (" + seconds + " с). Можно прекратить ожидание, не останавливая процесс."); });
                        }
                    });
                    exitCode = completion.ExitCode;
                    processStillRunning = !completion.Exited;
                    waitReason = completion.Reason;
                }
                if (closing) return;
                AppendLog(processStillRunning
                    ? "Ожидание .exe прекращено; процесс " + procedureProcessId + " ещё работает. Код выхода неизвестен. Проверяем регистрацию."
                    : "Выполнение работы со службой BARS завершено. Код выхода: " + exitCode + ".");
                SetStatus("Проверяем результат…", "Повторно читаем регистрацию службы после выполнения процедуры.", blue);
                Exception lastError = null;
                verified = false;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    if (closing) return;
                    try
                    {
                        ServiceSnapshot snapshot = await Task.Factory.StartNew<ServiceSnapshot>(readCatalog);
                        if (closing) return;
                        AcceptSnapshot(snapshot);
                        lastError = null;
                        if (!snapshot.Complete) lastError = new InvalidOperationException(snapshot.Warning);
                        if (verified && ((install && matches.Count > 0) || (!install && matches.Count == 0))) break;
                    }
                    catch (Exception ex) { lastError = ex; verified = false; catalogCurrent = false; currentServiceNames.Clear(); }
                    if (attempt < 5) await Task.Delay(1000);
                }
                if (closing) return;
                bool expected = verified && (install ? matches.Count > 0 : matches.Count == 0);
                if (processStillRunning)
                {
                    string reason = waitReason == ProcedureWaitReason.RegistrationObserved ? "Регистрация изменилась" :
                        waitReason == ProcedureWaitReason.TimedOut ? "Истекли 2 минуты ожидания" : "Ожидание остановлено";
                    SetStatus(reason + ", но .exe ещё работает",
                        (expected ? (install ? "Служба уже зарегистрирована. " : "Регистрация службы уже удалена. ") : "Результат регистрации пока не подтверждён. ") +
                        "Процесс " + procedureProcessId + " не остановлен; код выхода неизвестен. Менеджер доступен. Проверь результат в оснастке «Службы» Windows.", Color.DarkOrange);
                }
                else if (exitCode != 0)
                {
                    SetStatus("Процедура вернула ошибку с кодом " + exitCode,
                        !String.IsNullOrEmpty(executionDetail) ? executionDetail : verified ? "Наблюдаемое состояние регистрации показано ниже. Подробности смотри в журнале службы BARS." : "Дополнительно не удалось проверить регистрацию службы.", Color.Firebrick);
                }
                else if (!verified)
                    SetStatus("Процедура завершена, но статус не проверен", lastError == null ? "Повтори проверку статуса." : lastError.Message, Color.DarkOrange);
                else if (expected)
                    SetStatus(install ? "Установка подтверждена" : "Удаление подтверждено",
                        install ? "Служба найдена в регистрации Windows. Установка не означает автоматический запуск." : "Прямые ссылки на выбранный .exe в списке служб больше не найдены.", Color.FromArgb(21, 128, 85));
                else
                    SetStatus("Результат не подтверждён",
                        install ? "Процедура вернула 0, но регистрация этой службы пока не найдена." : "Процедура вернула 0, но связанные службы ещё присутствуют. Возможно, удаление ожидает остановки службы или закрытия оснастки.", Color.DarkOrange);
                AppendLog(expected ? "Ожидаемое состояние регистрации подтверждено." : "Ожидаемое состояние не подтверждено. Обнови статус и проверь журнал службы BARS.");
                if (lastError != null) AppendLog("Ошибка проверки: " + lastError.Message);
                ShowCatalogWarning();
                activity.Text = "Проверка после процедуры завершена в " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Win32Exception ex)
            {
                if (closing) return;
                if (ex.NativeErrorCode == 1223)
                {
                    SetStatus("Операция отменена", "Запрос прав администратора был отменён. Процедура не выполнена.", muted);
                    AppendLog("Запрос UAC отменён пользователем.");
                }
                else HandleOperationError(ex);
            }
            catch (Exception ex) { if (!closing) HandleOperationError(ex); }
            finally
            {
                accountPassword = null;
                if (procedureWaitCancellation != null) { procedureWaitCancellation.Dispose(); procedureWaitCancellation = null; }
                operationRunning = false;
                if (!closing) { UpdateRefreshTimer(); UpdateActions(); }
            }
        }

        private void HandleOperationError(Exception ex)
        {
            verified = false;
            SetStatus("Не удалось выполнить процедуру", ex.Message, Color.Firebrick);
            AppendLog("Ошибка выполнения: " + ex.Message);
        }

        private void UpdateActions()
        {
            bool ready = !operationRunning && !refreshing && verified && !String.IsNullOrEmpty(selectedPath);
            chooseButton.Enabled = !operationRunning;
            installProfileButton.Enabled = !operationRunning && !String.IsNullOrEmpty(selectedPath);
            installButton.Enabled = ready && matches.Count == 0;
            removeButton.Enabled = ready && matches.Count > 0;
            refreshButton.Text = procedureWaitCancellation == null ? "Обновить статус" : "Прекратить ожидание";
            refreshButton.Enabled = !refreshing && (!operationRunning || procedureWaitCancellation != null);
            servicesGrid.Enabled = !operationRunning;
            UpdateServiceControls(false);
        }

        private void SetStatus(string title, string detail, Color color)
        {
            bool changed = statusTitle.Text != title || statusDetail.Text != detail;
            statusTitle.Text = title;
            statusTone = color == Color.Firebrick ? "error" : color == Color.DarkOrange ? "warning" :
                color == Color.FromArgb(21, 128, 85) ? "success" : color == blue ? "info" : "neutral";
            ApplyStatusColor();
            statusDetail.Text = detail;
            statusDetail.Visible = false;
            toolTips.SetToolTip(statusTitle, detail);
            if (changed && (statusTone == "error" || statusTone == "warning") && !String.IsNullOrEmpty(detail))
                AppendLog(title + ": " + detail);
        }

        private async Task RunServiceCommandAsync(string action, string mode)
        {
            ServiceEntry entry = SelectedService();
            if (entry == null || operationRunning || refreshing || !catalogCurrent || !currentServiceNames.Contains(entry.Name)) return;
            string serviceName = entry.Name;
            operationRunning = true;
            timer.Stop();
            UpdateActions();
            string description = action == "start" ? "Запускаем службу…" : action == "stop" ? "Останавливаем службу…" : "Изменяем режим запуска…";
            SetStatus(description, entry.DisplayName + ", имя службы: " + serviceName, blue);
            AppendLog(ServiceActionMessage(entry,action,mode));
            activity.Text = "Выполняется действие для выбранной службы.";
            int result = -1;
            Exception failure = null;
            bool cancelled = false;
            try
            {
                ProcessStartInfo startInfo = ServiceManagement.BuildHelperStartInfo(Application.ExecutablePath, serviceName, action, mode);
                result = await Task.Factory.StartNew(delegate
                {
                    using (Process process = Process.Start(startInfo))
                    {
                        if (process == null) throw new InvalidOperationException("Windows не вернула процесс выполнения процедуры.");
                        int seconds = 0;
                        while (!process.WaitForExit(1000))
                        {
                            if (++seconds % 15 == 0) PostLog("Ожидаем изменения состояния службы (" + seconds + " с)…");
                        }
                        return process.ExitCode;
                    }
                });
            }
            catch (Win32Exception ex) { cancelled = ex.NativeErrorCode == 1223; if (!cancelled) failure = ex; }
            catch (Exception ex) { failure = ex; }
            finally
            {
                operationRunning = false;
                if (!closing) { UpdateRefreshTimer(); UpdateActions(); }
            }
            if (closing) return;
            startupDirty = false;
            await RefreshStatusAsync(false);
            if (closing) return;
            if (cancelled)
            {
                SetStatus("Операция отменена", "Запрос прав администратора отменён. Процедура не выполнена.", muted);
                AppendLog("Запрос UAC отменён пользователем.");
                return;
            }
            if (failure != null) { HandleOperationError(failure); UpdateActions(); return; }
            if (result != 0)
            {
                string message = action == "startup" && result == 1003
                    ? "Изменение не завершено; восстановить прежние настройки тоже не удалось. Проверь режим запуска в оснастке Windows."
                    : new Win32Exception(result).Message;
                SetStatus("Действие завершилось с ошибкой, код " + result, message, Color.Firebrick);
                AppendLog("Ошибка " + result + ": " + message);
                return;
            }
            ServiceEntry observed = managedRows.Find(delegate(ServiceEntry item) { return item.Name == serviceName; });
            bool confirmed = catalogCurrent && currentServiceNames.Contains(serviceName) && observed != null &&
                (action == "start" ? observed.State == "Running" : action == "stop" ? observed.State == "Stopped" :
                observed.StartupKnown && StartupValue(StartupIndex(observed)) == mode);
            if (confirmed)
            {
                string title = action == "start" ? "Служба запущена" : action == "stop" ? "Служба остановлена" : "Режим запуска сохранён";
                SetStatus(title, observed.DisplayName + ", имя службы: " + serviceName, Color.FromArgb(21, 128, 85));
                AppendLog(title + ": " + serviceName + ". Состояние проверено.");
            }
            else
            {
                SetStatus("Процедура выполнена, но результат не подтверждён", "Обнови статус: ожидаемое состояние выбранной службы пока не удалось подтвердить.", Color.DarkOrange);
                AppendLog("Код выхода 0, но ожидаемое состояние службы не подтверждено.");
            }
        }

        internal static string ServiceActionMessage(ServiceEntry entry,string action,string mode)
        {
            string verb=action=="start"?"Запрос на запуск":action=="stop"?"Запрос на остановку":"Изменение режима запуска";
            string startup=mode=="auto"?"автоматически":mode=="auto-delayed"?"автоматически, с задержкой":mode=="manual"?"вручную":mode=="disabled"?"отключена":mode;
            return verb+" службы «"+entry.DisplayName+"» ("+entry.Name+")"+(action=="startup"?": "+startup:"")+".";
        }

        private void AppendLog(string message)
        {
            if (log.TextLength > 60000) log.Text = log.Text.Substring(log.TextLength - 40000);
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
            log.SelectionStart=log.TextLength;log.SelectionLength=0;
            ((JournalTextBox)log).ScrollToLine(Int32.MaxValue);
        }

        private void PostLog(string message)
        {
            if (closing || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)delegate { if (!closing) { AppendLog(message); activity.Text = message; } }); }
            catch (InvalidOperationException) { }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (logArchiveRunning)
            {
                closeAfterLogArchive = true;
                if (logArchiveCancellation != null) logArchiveCancellation.Cancel();
                activity.Text = "Отменяем сбор логов перед закрытием…";
                e.Cancel = true;
                return;
            }
            if (operationRunning)
            {
                if (confirmBusyClose() != DialogResult.Yes) { MenuRequested = false; e.Cancel = true; return; }
            }
            closing = true;
        }

        private void OpenServices()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "mmc.exe"), "\"" + Path.Combine(Environment.SystemDirectory, "services.msc") + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { BrandMessageBox.Show(this, ex.Message, "Не удалось открыть службы", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        internal static ProcessStartInfo BuildBrandLinkStartInfo()
        {
            return BrandAssets.WebsiteStartInfo();
        }

        internal static string RegistrationText(string state)
        {
            switch (state)
            {
                case "Running": return "Служба зарегистрирована и работает";
                case "Stopped": return "Служба зарегистрирована и остановлена";
                case "Start Pending": return "Служба зарегистрирована и запускается";
                case "Stop Pending": return "Служба зарегистрирована и останавливается";
                case "Paused": return "Служба зарегистрирована и приостановлена";
                case "Pause Pending": return "Служба зарегистрирована и приостанавливается";
                case "Continue Pending": return "Служба зарегистрирована и возобновляет работу";
                default: return "Служба зарегистрирована, состояние неизвестно";
            }
        }

        private static string StateText(string state)
        {
            switch (state)
            {
                case "Running": return "Работает";
                case "Stopped": return "Остановлена";
                case "Start Pending": return "Запускается";
                case "Stop Pending": return "Останавливается";
                case "Paused": return "Приостановлена";
                case "Pause Pending": return "Приостанавливается";
                case "Continue Pending": return "Возобновляется";
                default: return String.IsNullOrEmpty(state) ? "Неизвестно" : state;
            }
        }

        private static string ModeText(string mode)
        {
            switch (mode)
            {
                case "Auto": return "Автоматически";
                case "Manual": return "Вручную";
                case "Disabled": return "Отключена";
                default: return mode;
            }
        }
    }
}
