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
        // Small utility modules carry no product description; only the launcher keeps About.
        protected UtilityModuleForm(string title, Size size) : base(title, size)
        {
            // The base class already raised MinimumSize (which grows the window), so the requested
            // compact size is re-applied after lowering the minimum.
            MinimumSize = new Size(700, 470); ClientSize = size;
            Body = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); Body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Body.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); Body.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            var heading = Label(title, false); heading.Font = new Font("Segoe UI", 15, FontStyle.Bold); Body.Controls.Add(heading, 0, 0);
            var footer = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 8, 0, 0), Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Activity = Label("Готово к работе", true); Activity.AutoEllipsis = false; Activity.Font = new Font("Segoe UI", 9); footer.Controls.Add(Activity, 0, 0);
            Back = Button("В меню", false); Back.Margin = Padding.Empty; Back.Click += delegate { if (!Busy) { MenuRequested = true; Close(); } }; footer.Controls.Add(Back, 1, 0);
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
        // Runs several privileged actions inside one elevation prompt.
        protected async Task<List<LibraryInstallResult>> ExecuteAll(IList<string[]> steps)
        {
            var results = new List<LibraryInstallResult>();
            using (LibrarySession session = await Task.Run(() => LibrarySession.Start(() => CancelRequested)))
            {
                Session = session;
                try { foreach (var step in steps) results.Add(await Task.Run(() => session.ExecuteSystem(step[0], step[1], step[2], Progress))); }
                finally { Session = null; }
            }
            return results;
        }
        protected void Failure(Exception ex) { Activity.Text = "Не выполнено"; MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        protected async Task Restart()
        {
            await RestartOffer.Show(this);
        }
    }
    internal sealed class WindowsFeaturesForm : UtilityModuleForm
    {
        private sealed class Row
        {
            public WindowsComponent Profile;
            public BrandCheckBox Choice;
            public Label Status;
        }
        private readonly List<Row> rows = new List<Row>();
        private readonly BrandButton check, action;
        private Dictionary<string, string> states = new Dictionary<string, string>();
        private readonly bool preview;
        internal WindowsFeaturesForm(bool previewMode) : base("Компоненты Windows", new Size(760, 590))
        {
            preview = previewMode;
            // Telnet/TFTP first: they belong to the small fiLinn/piglet toolset and are checked
            // far more often than the large IIS profile.
            var list = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = WindowsComponents.GroupOrder.Length, Margin = Padding.Empty };
            list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var group in WindowsComponents.GroupOrder)
            {
                var members = WindowsComponents.InGroup(group);
                var card = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(16, 8, 16, 8) };
                var content = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = members.Length + 1, Margin = Padding.Empty };
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                var caption = Label(group, true); caption.Font = new Font("Segoe UI", 9); content.Controls.Add(caption, 0, 0);
                for (int i = 0; i < members.Length; i++)
                {
                    content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / members.Length));
                    var row = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210)); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                    var choice = new BrandCheckBox { Text = members[i].Name, Font = new Font("Segoe UI", 12, FontStyle.Bold), Dock = DockStyle.Fill, Margin = Padding.Empty, Enabled = false, Palette = Colors };
                    choice.CheckedChanged += delegate { UpdateAction(); };
                    var status = Label("Не проверено", true); status.TextAlign = ContentAlignment.MiddleRight;
                    row.Controls.Add(choice, 0, 0); row.Controls.Add(status, 1, 0);
                    rows.Add(new Row { Profile = members[i], Choice = choice, Status = status });
                    content.Controls.Add(row, 0, i + 1);
                }
                int index = list.Controls.Count;
                card.Controls.Add(content); list.Controls.Add(card, 0, index);
            }
            // Two short rows (Telnet, TFTP) share the upper band; the single IIS row takes the rest.
            list.RowStyles.Clear();
            foreach (var group in WindowsComponents.GroupOrder)
                list.RowStyles.Add(new RowStyle(SizeType.Percent, WindowsComponents.InGroup(group).Length > 1 ? 62F : 38F));
            Body.Controls.Add(list, 0, 1);
            var actions = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 6, 0, 0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            check = Button("Проверить компоненты", false); check.Click += async delegate { await Check(); }; actions.Controls.Add(check, 0, 0);
            action = Button("Установить выбранные", true); action.Margin = Padding.Empty; action.Enabled = false; action.Click += async delegate { await Apply(); }; actions.Controls.Add(action, 1, 0);
            Body.Controls.Add(actions, 0, 2); SetTheme(Colors.Dark, false);
            // No automatic query on open: the operator presses "Проверить компоненты".
            Shown += delegate
            {
                if (preview)
                {
                    states = WindowsComponents.Profiles.SelectMany(p => p.Features).ToDictionary(f => f, f => "Enabled");
                    states["TFTP"] = "Disabled"; ApplyStates();
                }
            };
        }
        protected override void OnThemeChanged() { foreach (var row in rows) row.Choice.Palette = Colors; }
        private void Lock(bool busy)
        { Busy = busy; Back.Enabled = check.Enabled = !busy; if (busy) foreach (var row in rows) row.Choice.Enabled = false; else ApplyStates(); }
        private void ApplyStates()
        {
            foreach (var row in rows)
            {
                var state = states.Count == 0 ? WindowsComponents.ComponentState.Unknown : WindowsComponents.Classify(row.Profile, states);
                row.Status.Text = states.Count == 0 ? "Не проверено" : WindowsComponents.Status(row.Profile, states);
                // Installed rows stay selectable so the same window can remove them.
                row.Choice.Enabled = !Busy && (state == WindowsComponents.ComponentState.Missing || state == WindowsComponents.ComponentState.Partial || state == WindowsComponents.ComponentState.Installed);
                if (!row.Choice.Enabled) row.Choice.Checked = false;
            }
            UpdateAction();
        }
        private void UpdateAction()
        {
            var selected = rows.Where(r => r.Choice.Enabled && r.Choice.Checked).ToArray();
            bool anyInstall = selected.Any(r => WindowsComponents.Classify(r.Profile, states) != WindowsComponents.ComponentState.Installed);
            bool anyRemove = selected.Any(r => WindowsComponents.Classify(r.Profile, states) == WindowsComponents.ComponentState.Installed);
            action.Enabled = !Busy && selected.Length > 0;
            action.Text = !anyRemove ? "Установить выбранные" : !anyInstall ? "Удалить выбранные" : "Установить и удалить";
        }
        private async Task Check()
        {
            if (Busy || preview) return; Lock(true); Activity.Text = "Проверяем Windows…";
            try { var result = await Execute("features-query", "", ""); states = WindowsComponents.Parse(result.Message); if (states.Count == 0) throw new IOException("Windows не вернула список компонентов"); Activity.Text = "Проверка завершена"; }
            catch (Exception ex) { states.Clear(); Failure(ex); }
            finally { Lock(false); }
        }
        private async Task Apply()
        {
            if (Busy || preview) return;
            var selected = rows.Where(r => r.Choice.Enabled && r.Choice.Checked).ToArray();
            if (selected.Length == 0) { Activity.Text = "Отметь компоненты"; return; }
            var install = String.Join(",", selected.Where(r => WindowsComponents.Classify(r.Profile, states) != WindowsComponents.ComponentState.Installed).Select(r => r.Profile.Id));
            var remove = String.Join(",", selected.Where(r => WindowsComponents.Classify(r.Profile, states) == WindowsComponents.ComponentState.Installed).Select(r => r.Profile.Id));
            string question = install.Length > 0 && remove.Length > 0 ? "Установить одни и удалить другие выбранные компоненты Windows?"
                : install.Length > 0 ? "Установить выбранные компоненты Windows? Может потребоваться доступ к Windows Update или установочному носителю."
                : "Удалить выбранные компоненты Windows? Зависящие от них приложения перестанут работать.";
            if (MessageBox.Show(this, question + "\r\nАвтоматическая перезагрузка не выполняется.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Lock(true); bool changed = false, restart = false;
            try
            {
                var steps = new List<string[]>();
                if (install.Length > 0) steps.Add(new[] { "features-enable", install, "" });
                if (remove.Length > 0) steps.Add(new[] { "features-disable", remove, "" });
                var results = await ExecuteAll(steps);
                restart = results.Any(r => r.Reboot); changed = results.Any(r => LibraryPlan.Success(r.ExitCode));
                var observed = await Execute("features-query", "", ""); states = WindowsComponents.Parse(observed.Message);
                Activity.Text = restart ? "Изменения применены. Нужна перезагрузка" : "Проверка после изменения завершена";
            }
            catch (Exception ex) { states.Clear(); Failure(ex); }
            finally { Lock(false); }
            // Offer a restart after installation and after removal alike.
            if (changed) await Restart();
        }
    }
    internal sealed class ToolsDeploymentForm : UtilityModuleForm
    {
        private readonly Label sourceLabel;
        private readonly BrandTextBox destination;
        private readonly BrandButton chooseSource, chooseTarget, deploy;
        private readonly bool preview;
        private string source;
        internal ToolsDeploymentForm(bool previewMode) : base("Распаковка утилит BARS", new Size(820, 470))
        {
            preview = previewMode;
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 0, 0, 8) };
            var content = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            // Two label/field pairs; the fixed rows must stay shorter than the card's content area.
            foreach (float h in new[] { 28F, 36F, 28F, 36F }) content.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            content.Controls.Add(Label("Комплект утилит (папка _tools)", false), 0, 0);
            sourceLabel = Label("Папка _tools будет найдена в комплекте «Библиотеки»", true); sourceLabel.AutoEllipsis = true; content.Controls.Add(sourceLabel, 0, 1);
            chooseSource = Button("Выбрать комплект…", false); chooseSource.Dock = DockStyle.Fill; chooseSource.Margin = new Padding(8, 0, 0, 6); chooseSource.Click += delegate { Choose(); };
            content.Controls.Add(chooseSource, 1, 0); content.SetRowSpan(chooseSource, 2);
            content.Controls.Add(Label("Куда распаковать (полный путь)", false), 0, 2);
            destination = new BrandTextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 0), Palette = Colors, Text = ToolsDeployment.DefaultTarget };
            content.Controls.Add(destination, 0, 3);
            chooseTarget = Button("Выбрать путь…", false); chooseTarget.Dock = DockStyle.Fill; chooseTarget.Margin = new Padding(8, 0, 0, 0); chooseTarget.Click += delegate { ChooseTarget(); };
            content.Controls.Add(chooseTarget, 1, 3);
            card.Controls.Add(content); Body.Controls.Add(card, 0, 1);
            var actions = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 6, 0, 0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var clear = Button("Путь по умолчанию", false); clear.Click += delegate { destination.Text = ToolsDeployment.DefaultTarget; }; actions.Controls.Add(clear, 0, 0);
            deploy = Button("Скопировать и создать ярлыки", true); deploy.Margin = Padding.Empty; deploy.Click += async delegate { await Deploy(); }; actions.Controls.Add(deploy, 1, 0);
            Body.Controls.Add(actions, 0, 2);
            if (!preview) try { source = ToolsDeployment.FindSource(); if (source != null && Directory.Exists(source)) sourceLabel.Text = source; } catch (Exception ex) { Activity.Text = ex.Message; }
            SetTheme(Colors.Dark, false);
        }
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
        private void ChooseTarget()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Выбери папку для распаковки утилит", ShowNewFolderButton = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string path = dialog.SelectedPath;
                destination.Text = Path.GetFileName(path).Equals("_tools", StringComparison.OrdinalIgnoreCase) ? path : Path.Combine(path, "_tools");
            }
        }
        private async Task Deploy()
        {
            if (Busy || preview) return;
            if (!(source != null && Directory.Exists(source)) && !Choose()) return;
            string target;
            try { target = ToolsDeployment.TargetFolder(destination.Text); } catch (Exception ex) { Failure(ex); return; }
            try { ToolsDeployment.Discover(source); } catch (Exception ex) { Failure(ex); return; }
            if (MessageBox.Show(this, "Скопировать утилиты в " + target + " и создать четыре ярлыка на твоём рабочем столе?\r\nСуществующая папка будет сохранена в резервной копии.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Busy = true; chooseSource.Enabled = chooseTarget.Enabled = deploy.Enabled = destination.Enabled = Back.Enabled = false;
            try
            {
                await Execute("tools-deploy", source, target);
                ToolsDeployment.Shortcuts(source, target, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                Activity.Text = "Готово: утилиты скопированы, запуск от администратора настроен, ярлыки созданы";
            }
            catch (Exception ex) { Failure(ex); }
            finally { Busy = false; chooseSource.Enabled = chooseTarget.Enabled = deploy.Enabled = destination.Enabled = Back.Enabled = true; }
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
