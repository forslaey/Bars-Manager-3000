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
        protected bool Scanning;
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
            var heading = ModuleHeading(title); heading.Margin = new Padding(12, 8, 12, 12); Body.Controls.Add(heading, 0, 0);
            var footer = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 8, 0, 0), Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Activity = Label("Готово к работе", true); Activity.AutoEllipsis = false; Activity.Font = new Font("Segoe UI", 9); footer.Controls.Add(Activity, 0, 0);
            Back = Button("В меню", false); ModuleNavigation.Configure(Back); Back.Click += delegate { if (!Busy||Scanning) { if(Scanning){CancelRequested=true;if(Session!=null)Session.Cancel();Busy=false;} MenuRequested = true; Close(); } }; footer.Controls.Add(Back, 1, 0);
            Body.Controls.Add(footer, 0, 3); Root.Controls.Add(Body, 0, 1);
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!Busy) return;
                if(Scanning){CancelRequested=true;if(Session!=null)Session.Cancel();Busy=false;return;}
                e.Cancel = true;
                BrandMessageBox.Show(this, "Дождись завершения операции. Обслуживание компонентов Windows нельзя безопасно оборвать закрытием окна.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
        }
        protected virtual void Progress(string text)
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
        protected async Task<List<LibraryInstallResult>> ExecuteAll(IList<string[]> steps, Action<LibraryInstallResult> completed = null)
        {
            var results = new List<LibraryInstallResult>();
            using (LibrarySession session = await Task.Run(() => LibrarySession.Start(() => CancelRequested)))
            {
                Session = session;
                try
                {
                    foreach (var step in steps)
                    {
                        var result = await Task.Run(() => session.ExecuteSystem(step[0], step[1], step[2], Progress));
                        results.Add(result);
                        if (completed != null) completed(result);
                    }
                }
                finally { Session = null; }
            }
            return results;
        }
        protected void Failure(Exception ex) { Activity.Text = "Не выполнено"; BrandMessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
        internal bool RestartRequired { get; private set; }

        internal void RecordRestartResults(IEnumerable<LibraryInstallResult> results)
        {
            RestartRequired |= results.Any(result => result.Reboot);
        }

        private void RecordFeatureStates(string message)
        {
            states = WindowsComponents.Parse(message);
            RestartRequired |= states.Values.Any(state =>
                String.Equals(state, "EnablePending", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(state, "DisablePending", StringComparison.OrdinalIgnoreCase));
        }
        internal WindowsFeaturesForm(bool previewMode, bool returnToComponents = false) : base("Компоненты Windows", new Size(760, 590))
        {
            if(returnToComponents) Back.Text="Назад";
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
            Body.Controls.Add(actions, 0, 2); LimitContent(new Size(760,590)); SetTheme(Colors.Dark, false);
            Shown += async delegate
            {
                if (preview)
                {
                    states = WindowsComponents.Profiles.SelectMany(p => p.Features).ToDictionary(f => f, f => "Enabled");
                    states["TFTP"] = "Disabled"; ApplyStates();
                } else await Check();
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
            var scope=selected.Length>0?selected:rows.Where(r=>r.Choice.Enabled).ToArray();
            bool anyInstall = scope.Any(r => WindowsComponents.Classify(r.Profile, states) != WindowsComponents.ComponentState.Installed);
            bool anyRemove = scope.Any(r => WindowsComponents.Classify(r.Profile, states) == WindowsComponents.ComponentState.Installed);
            action.Enabled = !Busy && selected.Length > 0;
            action.Text = !anyRemove ? "Установить выбранные" : !anyInstall ? "Удалить выбранные" : "Установить и удалить";
        }
        private async Task Check()
        {
            if (Busy || preview) return; Lock(true); Activity.Text = "Проверяем Windows…";
            try { var result = await Execute("features-query", "", ""); RecordFeatureStates(result.Message); if (states.Count == 0) throw new IOException("Windows не вернула список компонентов"); Activity.Text = RestartRequired ? "Windows ожидает перезагрузки после изменения компонентов" : "Проверка завершена"; }
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
            if (BrandMessageBox.Show(this, question + "\r\nАвтоматическая перезагрузка не выполняется.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Lock(true); bool restart = false;
            try
            {
                var steps = new List<string[]>();
                if (install.Length > 0) steps.Add(new[] { "features-enable", install, "" });
                if (remove.Length > 0) steps.Add(new[] { "features-disable", remove, "" });
                var results = await ExecuteAll(steps, result => RecordRestartResults(new[] { result }));
                RecordRestartResults(results);
                restart = RestartRequired;
                var observed = await Execute("features-query", "", ""); RecordFeatureStates(observed.Message);
                restart = RestartRequired;
                Activity.Text = restart ? "Изменения применены. Нужна перезагрузка" : "Проверка завершена. Компоненты Windows не запросили перезагрузку";
            }
            catch (Exception ex) { restart = RestartRequired; states.Clear(); Failure(ex); }
            finally { Lock(false); }
            // A successful change alone does not mean that Windows needs a reboot.
            if (restart) await Restart();
        }
    }
    internal sealed class ToolsDeploymentForm : UtilityModuleForm
    {
        private readonly BrandTextBox sourceLabel;
        private readonly BrandTextBox destination;
        private readonly BrandButton chooseSource,chooseTarget,deploy,remove;
        private readonly BrandCheckBox shortcuts=new BrandCheckBox();
        private readonly bool preview;
        private string source;
        internal ToolsDeploymentForm(bool previewMode) : base("Распаковка утилит",new Size(640,500))
        {
            preview=previewMode; MinimumSize=SizeFromClientSize(new Size(620,500));ClientSize=new Size(640,500);
            Root.RowStyles[0].Height=96;Root.Padding=new Padding(26,12,26,12);
            Body.RowStyles[0].Height=52;Body.RowStyles[2].Height=48;Body.RowStyles[3].Height=54;
            Body.GetControlFromPosition(0,0).Margin=new Padding(12,8,12,12);
            var card=new Card {Dock=DockStyle.Fill,Padding=new Padding(16,16,16,8),Margin=new Padding(0,0,0,8)};
            var content=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=6,Margin=Padding.Empty};
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));
            foreach(float height in new[]{(float)PathFieldMetrics.CaptionHeight,(float)PathFieldMetrics.PickerHeight,12F,(float)PathFieldMetrics.CaptionHeight,(float)PathFieldMetrics.PickerHeight,44F})content.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            var sourceCaption=Caption("Будет взято из:",false);content.Controls.Add(sourceCaption,0,0);content.SetColumnSpan(sourceCaption,2);
            sourceLabel=new BrandTextBox {Text=preview?@"C:\Библиотеки\_tools":"Выбери папку с утилитами",ReadOnly=true,AutoSize=false,Palette=Colors};
            chooseSource=Button("Выбрать папку…",false);chooseSource.Click+=delegate{Choose();};var sourceRow=new PathPickerRow(sourceLabel,chooseSource,168){Dock=DockStyle.Fill};content.Controls.Add(sourceRow,0,1);content.SetColumnSpan(sourceRow,2);
            var targetCaption=Caption("Путь распаковки:",false);content.Controls.Add(targetCaption,0,3);content.SetColumnSpan(targetCaption,2);
            destination=new BrandTextBox {AutoSize=false,Palette=Colors,Text=ToolsDeployment.DefaultTarget};
            chooseTarget=Button("Выбрать путь…",false);chooseTarget.Click+=delegate{ChooseTarget();};var targetRow=new PathPickerRow(destination,chooseTarget,168){Dock=DockStyle.Fill};content.Controls.Add(targetRow,0,4);content.SetColumnSpan(targetRow,2);
            shortcuts.Text=PathFieldMetrics.ShortcutCaption;shortcuts.Checked=true;shortcuts.Dock=DockStyle.Fill;shortcuts.Margin=new Padding(0,16,0,0);shortcuts.Palette=Colors;content.Controls.Add(shortcuts,0,5);content.SetColumnSpan(shortcuts,2);
            card.Controls.Add(content);Body.Controls.Add(card,0,1);
            var actions=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,4,0,0)};
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            deploy=Button("Установить",true);deploy.Click+=async delegate{await Deploy();};actions.Controls.Add(deploy,0,0);
            remove=Button("Удалить",false);remove.Destructive=true;remove.Margin=Padding.Empty;remove.Click+=async delegate{await Remove();};actions.Controls.Add(remove,1,0);Body.Controls.Add(actions,0,2);
            if(!preview)try{source=ToolsDeployment.FindSource();if(source!=null&&Directory.Exists(source))sourceLabel.Text=source;}catch(Exception ex){Activity.Text=ex.Message;}
            LimitContent(new Size(640, 500));
            SetTheme(Colors.Dark,false);
        }
        protected override void OnThemeChanged(){shortcuts.Palette=Colors;}
        private void Lock(bool busy)
        {Busy=busy;chooseSource.Enabled=chooseTarget.Enabled=deploy.Enabled=remove.Enabled=destination.Enabled=shortcuts.Enabled=Back.Enabled=!busy;}
        private bool Choose()
        {
            using(var dialog=new FolderBrowserDialog {Description="Выбери папку _tools или папку Библиотеки",ShowNewFolderButton=false})
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return false;string path=dialog.SelectedPath;if(Directory.Exists(Path.Combine(path,"_tools")))path=Path.Combine(path,"_tools");
                try{ToolsDeployment.Discover(path);source=path;sourceLabel.Text=source;Activity.Text="Комплект утилит найден";return true;}catch(Exception ex){Failure(ex);return false;}
            }
        }
        private void ChooseTarget()
        {
            using(var dialog=new FolderBrowserDialog {Description="Выбери папку для утилит",ShowNewFolderButton=true})
            {if(dialog.ShowDialog(this)!=DialogResult.OK)return;string path=dialog.SelectedPath;destination.Text=Path.GetFileName(path).Equals("_tools",StringComparison.OrdinalIgnoreCase)?path:Path.Combine(path,"_tools");}
        }
        private async Task Deploy()
        {
            if(Busy||preview)return;if(!(source!=null&&Directory.Exists(source))&&!Choose())return;
            string target;try{target=ToolsDeployment.TargetFolder(destination.Text);ToolsDeployment.Discover(source);}catch(Exception ex){Failure(ex);return;}
            if(BrandMessageBox.Show(this,"Установить утилиты в "+target+"?\r\n"+(shortcuts.Checked?"Создадим ярлыки на рабочем столе.":"Ярлыки не создаются.")+"\r\nСуществующие настройки сохраняются, прежняя папка остаётся в резервной копии.",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            Lock(true);
            try
            {
                var result=await Execute("tools-deploy",source,target);if(result.ExitCode!=0)throw new IOException(result.Message);
                ToolsDeployment.EnableAdministratorLaunch(source,target);
                ToolsDeployment.ShortcutsAt(source,target,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),shortcuts.Checked);
                Activity.Text=shortcuts.Checked?"Утилиты установлены, ярлыки созданы":"Утилиты установлены без создания ярлыков";
            }
            catch(Exception ex){Failure(ex);}finally{Lock(false);}
        }
        private async Task Remove()
        {
            if(Busy||preview)return;string target;List<BarsTool> tools;
            try
            {
                target=ToolsDeployment.TargetFolder(destination.Text);
                if(source!=null&&Path.GetFullPath(source).TrimEnd('\\').Equals(target.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new IOException("Выбран исходный комплект. Укажи место установленных утилит");
                tools=ToolsDeployment.Installed(target);if(tools.Count==0)throw new IOException("В выбранной папке утилиты не найдены");
            }
            catch(Exception ex){Failure(ex);return;}
            if(BrandMessageBox.Show(this,"Удалить из "+target+":\r\n"+String.Join("\r\n",tools.Select(t=>t.Directory))+"?\r\n\r\nНастройки и журналы внутри этих папок тоже будут удалены. Посторонние файлы и резервные копии сохранятся.","Удаление утилит",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            Lock(true);
            try
            {
                var result=await Execute("tools-remove",String.Join("\n",tools.Select(t=>t.Directory)),target);if(result.ExitCode!=0)throw new IOException(result.Message);
                ToolsDeployment.RemoveReferences(target,tools,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));Activity.Text="Утилиты и их ярлыки удалены";
            }
            catch(Exception ex){Failure(ex);}finally{Lock(false);}
        }
    }
    internal static class RestartOffer
    {
        internal static async Task Show(IWin32Window owner)
        {
            if (BrandMessageBox.Show(owner, "Для завершения изменений нужна перезагрузка Windows. Сохрани открытые документы.\r\n\r\nПерезагрузить компьютер сейчас? Нажми «Нет», чтобы сделать это позже.", "Перезагрузка Windows", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { using (var session = await Task.Run(() => LibrarySession.Start())) await Task.Run(() => session.ExecuteSystem("system-restart", "restart-confirmed", "", ignored => { })); }
            catch (Exception ex) { BrandMessageBox.Show(owner, ex.Message, "Перезагрузка Windows", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
