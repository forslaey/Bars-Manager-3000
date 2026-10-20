using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal abstract class UtilityModuleForm : ManagerModuleForm
    {
        protected readonly BufferedLayoutPanel Body;
        protected readonly Label Activity;
        protected readonly BrandButton Back;
        protected bool Busy;
        protected LibrarySession Session;
        protected volatile bool CancelRequested = false;
        internal bool MenuRequested { get; private set; }
        protected UtilityModuleForm(string title) : base(title, new Size(940, 700))
        {
            MinimumSize = new Size(900, 660);
            Body = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Body.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); Body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Body.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); Body.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            var heading = Label(title, false); heading.Font = new Font("Segoe UI", 17, FontStyle.Bold); Body.Controls.Add(heading, 0, 0);
            var footer = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 12, 0, 0), Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Activity = Label("Готово к работе", true); Activity.AutoEllipsis = false; Activity.Font = new Font("Segoe UI", 9); footer.Controls.Add(Activity, 0, 0);
            var about = Button("О программе", false); about.Click += delegate { using (var dialog = new AboutDialog(Colors, Icon, title, "Инструменты BARS для Windows")) dialog.ShowDialog(this); }; footer.Controls.Add(about, 1, 0);
            Back = Button("В меню", false); Back.Margin = Padding.Empty; Back.Click += delegate { if (!Busy) { MenuRequested = true; Close(); } }; footer.Controls.Add(Back, 2, 0);
            Body.Controls.Add(footer, 0, 3); Root.Controls.Add(Body, 0, 1);
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!Busy) return;
                e.Cancel = true;
                MessageBox.Show(this, "Дождись завершения операции. Обслуживание компонентов Windows нельзя безопасно оборвать закрытием окна.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
        }
        protected void Progress(string text)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) Activity.Text = text; }); } catch (InvalidOperationException) { }
        }
        protected async Task<LibraryInstallResult> Execute(string action, string data, string path)
        {
            using (LibrarySession session = await Task.Run(() => LibrarySession.Start(() => CancelRequested)))
            { Session = session; try { return await Task.Run(() => session.ExecuteSystem(action, data, path, Progress)); } finally { Session = null; } }
        }
        protected void Failure(Exception ex) { Activity.Text = "Не выполнено"; MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        protected async Task Restart()
        {
            await RestartOffer.Show(this);
        }
    }
    internal sealed class WindowsFeaturesForm : UtilityModuleForm
    {
        private readonly List<BrandCheckBox> choices = new List<BrandCheckBox>();
        private readonly List<Label> statuses = new List<Label>();
        private readonly BrandButton check, install;
        private Dictionary<string, string> states = new Dictionary<string, string>();
        private readonly bool preview;
        internal WindowsFeaturesForm(bool previewMode) : base("Компоненты Windows")
        {
            preview = previewMode;
            var list = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            list.RowStyles.Add(new RowStyle(SizeType.Percent, 44)); list.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
            for (int group = 0; group < 2; group++)
            {
                var card = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(18, 10, 18, 10) };
                var content = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = group == 0 ? 2 : 3, Margin = Padding.Empty };
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                var description = Label(WindowsComponents.Profiles[group == 0 ? 0 : 1].Group, true); description.AutoEllipsis = false;
                content.Controls.Add(description, 0, 0);
                int first = group == 0 ? 0 : 1, last = group == 0 ? 1 : 3;
                for (int i = first; i < last; i++)
                {
                    content.RowStyles.Add(new RowStyle(SizeType.Percent, group == 0 ? 100 : 50));
                    var row = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230)); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                    var choice = new BrandCheckBox { Text = WindowsComponents.Profiles[i].Name, Font = new Font("Segoe UI", 12, FontStyle.Bold), Dock = DockStyle.Fill, Margin = Padding.Empty, Enabled = false, Palette = Colors };
                    var status = Label("Не проверено", true); status.TextAlign = ContentAlignment.MiddleRight;
                    row.Controls.Add(choice, 0, 0); row.Controls.Add(status, 1, 0);
                    choices.Add(choice); statuses.Add(status); content.Controls.Add(row, 0, i - first + 1);
                }
                card.Controls.Add(content); list.Controls.Add(card, 0, group);
            }
            Body.Controls.Add(list, 0, 1);
            var actions = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            check = Button("Проверить компоненты", false); check.Click += async delegate { await Check(); }; actions.Controls.Add(check, 0, 0);
            install = Button("Установить выбранные", true); install.Margin = Padding.Empty; install.Enabled = false; install.Click += async delegate { await Install(); }; actions.Controls.Add(install, 1, 0);
            Body.Controls.Add(actions, 0, 2); SetTheme(Colors.Dark, false);
            Shown += async delegate
            {
                if (preview) { states = WindowsComponents.Profiles.SelectMany(p => p.Features).ToDictionary(f => f, f => "Enabled"); states["TFTP"] = "Disabled"; ApplyStates(); }
                else await Check();
            };
        }
        protected override void OnThemeChanged() { foreach (var choice in choices) choice.Palette = Colors; }
        private void Lock(bool busy)
        { Busy = busy; Back.Enabled = check.Enabled = !busy; install.Enabled = !busy && choices.Any(c => c.Enabled); if (busy) foreach (var c in choices) c.Enabled = false; else ApplyStates(); }
        private void ApplyStates()
        {
            for (int i = 0; i < choices.Count; i++)
            {
                string status = states.Count == 0 ? "Не проверено" : WindowsComponents.Status(WindowsComponents.Profiles[i], states);
                statuses[i].Text = status;
                choices[i].Enabled = !Busy && (status == "Не установлено" || status.StartsWith("Частично:", StringComparison.Ordinal));
                if (!choices[i].Enabled) choices[i].Checked = status == "Установлено";
            }
            install.Enabled = !Busy && choices.Any(c => c.Enabled);
        }
        private async Task Check()
        {
            if (Busy || preview) return; Lock(true); Activity.Text = "Проверяем Windows…";
            try { var result = await Execute("features-query", "", ""); states = WindowsComponents.Parse(result.Message); if (states.Count == 0) throw new IOException("Windows не вернула список компонентов"); Activity.Text = "Проверка завершена"; }
            catch (Exception ex) { states.Clear(); Failure(ex); }
            finally { Lock(false); }
        }
        private async Task Install()
        {
            if (Busy || preview) return;
            string ids = String.Join(",", choices.Select((c, i) => c.Enabled && c.Checked ? WindowsComponents.Profiles[i].Id : null).Where(id => id != null));
            if (ids.Length == 0) { Activity.Text = "Отметь компоненты для установки"; return; }
            if (MessageBox.Show(this, "Установить выбранные компоненты Windows? Может потребоваться доступ к Windows Update или установочному носителю. Автоматическая перезагрузка отключена.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Lock(true); bool reboot = false;
            try
            {
                var result = await Execute("features-enable", ids, ""); reboot = result.Reboot;
                var observed = await Execute("features-query", "", ""); states = WindowsComponents.Parse(observed.Message);
                Activity.Text = reboot ? "Изменения применены. Нужна перезагрузка" : "Проверка после установки завершена";
            }
            catch (Exception ex) { states.Clear(); Failure(ex); }
            finally { Lock(false); }
            if (reboot) await Restart();
        }
    }
    internal sealed class ToolsDeploymentForm : UtilityModuleForm
    {
        private readonly Label sourceLabel, destination;
        private readonly BrandComboBox drives;
        private readonly BrandButton choose, deploy;
        private readonly bool preview;
        private string source;
        internal ToolsDeploymentForm(bool previewMode) : base("Распаковка утилит BARS")
        {
            preview = previewMode;
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 0, 0, 10) };
            var content = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (float h in new[] { 68F, 66F, 40F, 46F }) content.RowStyles.Add(new RowStyle(SizeType.Absolute, h)); content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var description = Label("fiLinn · Piglet · WinSCP\r\nЯрлыки с иконками EXE и запуском от имени администратора", false); description.Font = new Font("Segoe UI", 12); content.Controls.Add(description, 0, 0);
            sourceLabel = Label("Папка _tools будет найдена в комплекте «Библиотеки»", true); sourceLabel.AutoEllipsis = true; content.Controls.Add(sourceLabel, 0, 1);
            content.Controls.Add(Label("Выбери диск для папки DataKrat\\_tools", false), 0, 2);
            drives = new BrandComboBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6), DropDownStyle = ComboBoxStyle.DropDownList, Palette = Colors };
            drives.DrawMode = DrawMode.OwnerDrawFixed; drives.ItemHeight = Font.Height + 6;
            drives.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                Color background = selected ? Colors.Selection : Colors.SurfaceAlt;
                using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
                string value = e.Index >= 0 && e.Index < drives.Items.Count ? Convert.ToString(drives.Items[e.Index]) : Convert.ToString(drives.SelectedItem);
                Rectangle text = Rectangle.Inflate(e.Bounds, -8, 0);
                TextRenderer.DrawText(e.Graphics, value, Font, text, selected ? Colors.SelectionText : Colors.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            };
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try { if ((drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable) && drive.IsReady) drives.Items.Add(drive.RootDirectory.FullName); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            if (drives.Items.Count > 0) drives.SelectedIndex = 0; content.Controls.Add(drives, 0, 3);
            destination = Label("", true); destination.AutoEllipsis = false; content.Controls.Add(destination, 0, 4);
            drives.SelectedIndexChanged += delegate { UpdateDestination(); }; UpdateDestination(); card.Controls.Add(content); Body.Controls.Add(card, 0, 1);
            var actions = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            choose = Button("Выбрать папку _tools…", false); choose.Click += delegate { Choose(); }; actions.Controls.Add(choose, 0, 0);
            deploy = Button("Скопировать и создать ярлыки", true); deploy.Margin = Padding.Empty; deploy.Click += async delegate { await Deploy(); }; actions.Controls.Add(deploy, 1, 0); Body.Controls.Add(actions, 0, 2);
            if (!preview) try { source = ToolsDeployment.FindSource(); if (source != null && Directory.Exists(source)) sourceLabel.Text = source; } catch (Exception ex) { Activity.Text = ex.Message; }
            SetTheme(Colors.Dark, false);
        }
        private void UpdateDestination() { if (destination != null) destination.Text = drives.SelectedItem == null ? "Нет доступных дисков" : "Путь: " + ToolsDeployment.Target(Convert.ToString(drives.SelectedItem)) + "\r\nСуществующие настройки сохранятся. Предыдущая папка останется в резервной копии.\r\nЧетыре основных EXE будут запускаться от имени администратора."; }
        private bool Choose()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Выбери папку _tools или папку Библиотеки", ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                string path = dialog.SelectedPath; if (Directory.Exists(Path.Combine(path, "_tools"))) path = Path.Combine(path, "_tools");
                try { ToolsDeployment.Discover(path); source = path; sourceLabel.Text = source; Activity.Text = "Комплект утилит найден"; return true; }
                catch (Exception ex) { Failure(ex); return false; }
            }
        }
        private async Task Deploy()
        {
            if (Busy || preview || drives.SelectedItem == null) return;
            if ((source == null || !Directory.Exists(source)) && !Choose()) return;
            string drive = Convert.ToString(drives.SelectedItem);
            try { ToolsDeployment.Discover(source); } catch (Exception ex) { Failure(ex); return; }
            if (MessageBox.Show(this, "Скопировать утилиты в " + ToolsDeployment.Target(drive) + " и создать четыре ярлыка на твоём рабочем столе? Существующая папка будет сохранена в резервной копии.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Busy = true; choose.Enabled = deploy.Enabled = drives.Enabled = Back.Enabled = false;
            try
            {
                await Execute("tools-deploy", source, drive);
                ToolsDeployment.Shortcuts(source, drive, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                Activity.Text = "Готово: утилиты скопированы, запуск от администратора настроен, ярлыки созданы";
            }
            catch (Exception ex) { Failure(ex); }
            finally { Busy = false; choose.Enabled = deploy.Enabled = drives.Enabled = Back.Enabled = true; }
        }
    }
    internal static class RestartOffer
    {
        internal static async Task Show(IWin32Window owner)
        {
            if (MessageBox.Show(owner, "Для завершения изменений нужна перезагрузка Windows. Сохрани открытые документы.\r\n\r\nПерезагрузить компьютер сейчас? Нажми «Нет», чтобы сделать это позже.", "Перезагрузка Windows", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { using (var session = await Task.Run(() => LibrarySession.Start())) await Task.Run(() => session.ExecuteSystem("system-restart", "restart-confirmed", "", ignored => { })); }
            catch (Exception ex) { MessageBox.Show(owner, ex.Message, "Перезагрузка Windows", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
