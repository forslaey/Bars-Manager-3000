using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal abstract class UtilityModuleForm : ManagerModuleForm, IModuleOperation
    {
        protected readonly BufferedLayoutPanel Body;
        protected readonly Label Activity;
        protected readonly BrandButton Back;
        protected DownloadProgressView OperationView;
        private bool busy, closeAfterOperation;
        protected bool ClosingAfterOperation { get { return closeAfterOperation; } }
        protected bool Busy
        {
            get { return busy; }
            set
            {
                busy = value;
                if (!busy && closeAfterOperation && IsHandleCreated && !IsDisposed)
                    BeginInvoke((MethodInvoker)Close);
            }
        }
        string IModuleOperation.ActiveOperation { get { return Busy && !Scanning ? Text : null; } }
        protected bool Scanning, ReturnToParent;
        protected LibrarySession Session;
        protected volatile bool CancelRequested = false;
        internal bool MenuRequested { get; private set; }
        internal override void ResumeSession() { MenuRequested=false; base.ResumeSession(); }
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
            var heading = ModuleHeading(title); heading.Margin = new Padding(0, ModuleHeadingStyle.Top, 0, 12); Body.Controls.Add(heading, 0, 0);
            var footer = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 8, 0, 0), Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Activity = Label("Готово к работе", true); Activity.AutoEllipsis = false; Activity.Font = new Font("Segoe UI", 9); footer.Controls.Add(Activity, 0, 0);
            Back = Button("В меню", false); ModuleNavigation.Configure(Back); Back.Click += delegate { if (!ReturnToParent && ModuleNavigation.ReturnToHub()) return; if (!Busy||Scanning) { if(Scanning){CancelRequested=true;if(Session!=null)Session.Cancel();Busy=false;} MenuRequested = true; Close(); } }; footer.Controls.Add(Back, 1, 0);
            if (!(this is WindowsFeaturesForm)) ModuleNavigation.AddCloseButton(footer, Back, Button("Закрыть", false), 0);
            if (this is WindowsFeaturesForm) ModuleNavigation.StandardFooter(footer, Back);
            Body.Controls.Add(footer, 0, 3); Root.Controls.Add(Body, 0, 1);
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!Busy) return;
                if(Scanning){CancelRequested=true;if(Session!=null)Session.Cancel();Busy=false;return;}
                e.Cancel = true;
                if (closeAfterOperation) return;
                if (!ModuleNavigation.ExitConfirmed && BrandMessageBox.ConfirmStop(this, "Действие Windows нужно завершить. Закрыть окно после завершения?") != DialogResult.Yes) return;
                closeAfterOperation = true;
            };
        }
        protected virtual void Progress(string text)
        {
            if (IsDisposed || !IsHandleCreated) return;
            long revision=OperationView==null?0:OperationView.OperationRevision;
            try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed&&Busy&&(OperationView==null||OperationView.OperationRevision==revision)) Activity.Text = text; }); } catch (InvalidOperationException) { }
        }
        protected async Task<LibraryInstallResult> Execute(string action, string data, string path)
        {
            bool readOnly = action == "features-query" || action == "initial-settings" && data.StartsWith("query:", StringComparison.Ordinal);
            using (readOnly ? null : await WindowsOperationQueue.EnterAsync(() => Progress(WindowsOperationQueue.WaitingMessage), () => CancelRequested))
            using (LibrarySession session = await Task.Run(() => LibrarySession.Start(() => CancelRequested)))
            { Session = session; try { return await Task.Run(() => session.ExecuteSystem(action, data, path, CaptureProgress())); } finally { Session = null; } }
        }
        // Runs several privileged actions inside one elevation prompt.
        protected async Task<List<LibraryInstallResult>> ExecuteAll(IList<string[]> steps, Action<LibraryInstallResult> completed = null)
        {
            var results = new List<LibraryInstallResult>();if(OperationView!=null)OperationView.SetWork(steps.Count);
            using (await WindowsOperationQueue.EnterAsync(() => Progress(WindowsOperationQueue.WaitingMessage), () => CancelRequested))
            using (LibrarySession session = await Task.Run(() => LibrarySession.Start(() => CancelRequested)))
            {
                Session = session;
                try
                {
                    foreach (var step in steps)
                    {
                        var result = await Task.Run(() => session.ExecuteSystem(step[0], step[1], step[2], CaptureProgress()));
                        results.Add(result);if(OperationView!=null&&LibraryPlan.Success(result.ExitCode))OperationView.CompleteStep();
                        if (completed != null) completed(result);
                    }
                }
                finally { Session = null; }
            }
            return results;
        }
        protected virtual bool UseHelperProgress { get { return true; } }
        Action<string> CaptureProgress(){if(!UseHelperProgress)return text=>{};long revision=OperationView==null?0:OperationView.OperationRevision;var report=OperationView==null?(Action<string>)(text=>{}):OperationView.OperationReporter;return text=>{if(OperationView!=null&&OperationView.OperationRevision!=revision)return;report(text);Progress(OperationView==null?text:OperationView.Snapshot.Stage);};}
        protected override void OnThemeChanged(){base.OnThemeChanged();if(OperationView!=null){OperationView.Palette=Colors;OperationView.Invalidate();}}
        protected void Failure(Exception ex) { Activity.Text = "Не выполнено."; if(OperationView!=null)OperationView.FinishOperation("Не выполнено: "+ex.Message,failed:true);if (!ClosingAfterOperation) BrandMessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        protected async Task Restart()
        {
            if (!ClosingAfterOperation) await RestartOffer.Show(this);
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
            HideThemeSelector();Root.RowStyles[0].Height=60;MinimumSize=new Size(0,0);ClientSize=new Size(650,500);MinimumSize=SizeFromClientSize(ClientSize);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;
            ReturnToParent = returnToComponents;
            if (ReturnToParent) Back.Text = "Назад";
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
                    content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                    var row = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
                    row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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
                list.RowStyles.Add(new RowStyle(SizeType.Absolute, 54+42*WindowsComponents.InGroup(group).Length));
            Body.Controls.Add(list, 0, 1);
            var actions = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 6, 0, 0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            check = Button("Проверить компоненты", false); check.Click += async delegate { await Check(); }; actions.Controls.Add(check, 0, 0);
            action = Button("Установить выбранные", true); action.Margin = Padding.Empty; action.Enabled = false; action.Click += async delegate { await Apply(); }; actions.Controls.Add(action, 1, 0);
            Body.Controls.Add(actions, 0, 2); LimitContent(new Size(650,500)); SetTheme(Colors.Dark, false);
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
        { Busy = busy; check.Enabled = !busy; Back.Enabled = !busy || ManagerApplicationContext.ActiveContext != null; if (busy) foreach (var row in rows) row.Choice.Enabled = false; else ApplyStates(); }
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
            if (Busy || preview) return;
            Scanning = true; CancelRequested = false; Lock(true); Activity.Text = "Проверяем Windows…";
            try { var result = await Execute("features-query", "", ""); if (IsDisposed || CancelRequested) return; RecordFeatureStates(result.Message); if (states.Count == 0) throw new IOException("Windows не вернула список компонентов"); Activity.Text = RestartRequired ? "Для завершения изменений нужна перезагрузка." : "Проверка завершена."; }
            catch (Exception ex) { if (!IsDisposed && !CancelRequested) { states.Clear(); Failure(ex); } }
            finally { Scanning = false; if (!IsDisposed) Lock(false); }
        }
        private async Task Apply()
        {
            if (Busy || preview) return;
            var selected = rows.Where(r => r.Choice.Enabled && r.Choice.Checked).ToArray();
            if (selected.Length == 0) { Activity.Text = "Отметь компоненты."; return; }
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
                Activity.Text = restart ? "Изменения применены. Нужна перезагрузка." : "Проверка завершена. Компоненты Windows не запросили перезагрузку.";
            }
            catch (Exception ex) { restart = RestartRequired; states.Clear(); Failure(ex); }
            finally { Lock(false); }
            // A successful change alone does not mean that Windows needs a reboot.
            if (restart) await Restart();
        }
    }
    internal sealed class ToolsDeploymentForm : BundleDownloadForm
    {
        private readonly bool preview;
        private volatile bool CancelRequested;
        private LibrarySession Session;
        private System.Threading.CancellationTokenSource archiveCancellation;
        internal ToolsDeploymentForm(bool previewMode)
            : base(BundleFolders.DefaultRoot,toolsOnly:true,localSource:previewMode?null:ToolsDeployment.FindSource())
        {
            preview=previewMode;
            deployFromWorkspace=Deploy;
            cancelToolDeployment=CancelDeployment;
            RemoveTools=Remove;
            UpdateState();
        }

        private async Task<LibraryInstallResult> Execute(string action,string data,string path)
        {
            var report=ToolProgress.OperationReporter;
            using(await WindowsOperationQueue.EnterAsync(()=>report(WindowsOperationQueue.WaitingMessage),()=>CancelRequested))
            using(var session=await Task.Run(()=>LibrarySession.Start(()=>CancelRequested)))
            {
                Session=session;
                try{return await Task.Run(()=>session.ExecuteSystem(action,data,path,report));}
                finally{Session=null;}
            }
        }
        internal static string[] LocalPackages(string folder)
        {
            var archives=Directory.GetFiles(folder).Where(path=>ToolArchives.Supports(path)&&!Path.GetFileName(path).StartsWith(".")).ToArray();
            var archiveFolders=new HashSet<string>(archives.Select(Path.GetFileNameWithoutExtension),StringComparer.OrdinalIgnoreCase);
            return Directory.GetDirectories(folder).Where(path=>!archiveFolders.Contains(Path.GetFileName(path)))
                .Concat(archives).Select(Path.GetFileName).Where(name=>!name.StartsWith("."))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name=>name).ToArray();
        }

        private void CancelDeployment()
        {
            CancelRequested=true;
            if(archiveCancellation!=null)archiveCancellation.Cancel();
            if(Session!=null)Session.Cancel();
        }

        private async Task Deploy(string packageSource,string[] packages,Dictionary<string,HashSet<string>> shortcutSelection,DownloadProgressView progress)
        {
            if(preview||packages.Length==0)return;
            string target;
            try
            {
                target=ToolsDeployment.TargetFolder(ToolDestination.Text);
                string from=Path.GetFullPath(packageSource).TrimEnd((char)92)+(char)92;
                string to=target.TrimEnd((char)92)+(char)92;
                if(from.StartsWith(to,StringComparison.OrdinalIgnoreCase)||to.StartsWith(from,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Папка комплекта и путь распаковки должны быть раздельными.");
                ToolsDeployment.SafePath(target);
            }
            catch(Exception ex){progress.Stage(OperationMessage.FinishSentence(ex.Message),failed:true);return;}


            CancelRequested=false;progress.BeginOperation("Подготавливаем распаковку…");
            archiveCancellation=new System.Threading.CancellationTokenSource();
            ToolProgress.SetWork(3+packages.Count(name=>ToolArchives.Supports(name)&&File.Exists(Path.Combine(packageSource,name))));
            try
            {
                var folders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var variants=ToolVariants;
                var exclusions=new List<string>();
                var launchers=new List<BarsTool>();
                var missingShortcuts=new List<string>();
                var cleanupWarnings=new List<string>();
                foreach(string package in packages)
                {
                    string path=YandexBundleSource.LocalPath(packageSource,"/"+package);
                    if(File.Exists(path)&&ToolArchives.Supports(path))
                    {
                        string folder=Path.Combine(packageSource,Path.GetFileNameWithoutExtension(path));
                        var report=ToolProgress.OperationReporter;
                        folder=await Task.Run(()=>ToolArchives.Extract(path,archiveCancellation.Token,p=>{
                            if(p.CleanupWarning){cleanupWarnings.Add(p.Stage);return;}
                            LibraryCancellation.Check(()=>CancelRequested);
                            if(p.Total<=0)report(p.Stage);
                            else report("BARS_FRACTION|"+p.Bytes+"|"+p.Total+"|Распаковываем "+package+"…");
                        },replace:true));
                        folders.Add(Path.GetFileName(folder));ToolProgress.CompleteStep();
                    }
                    else folders.Add(package);
                    string folderName=File.Exists(path)?Path.GetFileNameWithoutExtension(path):package;
                    HashSet<string> selectedVersions;
                    if(variants.TryGetValue(package,out selectedVersions))
                    {
                        var candidates=ToolsDeployment.ShortcutCandidates(Path.Combine(packageSource,folderName),ToolsDeployment.ShortcutExecutable(package))
                            .Select(candidate=>candidate.Replace((char)92,(char)47)).ToArray();
                        var roots=selectedVersions.Select(version=>Path.GetDirectoryName(version).Replace((char)92,(char)47)).ToArray();
                        foreach(var candidate in candidates.Where(candidate=>!selectedVersions.Contains(candidate)))
                        {
                            string branch=Path.GetDirectoryName(candidate).Replace((char)92,(char)47);
                            if(branch.Length!=0&&!roots.Any(root=>root.Equals(branch,StringComparison.OrdinalIgnoreCase)||root.StartsWith(branch+"/",StringComparison.OrdinalIgnoreCase)))
                                exclusions.Add(folderName+"/"+branch);
                            else if(branch.Length==0)exclusions.Add(folderName+"/"+candidate);
                        }
                    }
                    HashSet<string> selectedShortcuts;
                    if(shortcutSelection.TryGetValue(package,out selectedShortcuts))
                    {
                        string directory=File.Exists(path)?Path.GetFileNameWithoutExtension(path):package;
                        string executable=ToolsDeployment.ShortcutExecutable(package);
                        var candidates=await Task.Run(()=>ToolsDeployment.ShortcutCandidates(Path.Combine(packageSource,directory),executable));
                        var chosen=candidates.Where(candidate=>selectedShortcuts.Contains(candidate.Replace(Path.DirectorySeparatorChar,(char)47))).ToArray();
                        if(chosen.Length<selectedShortcuts.Count)missingShortcuts.Add(package);
                        foreach(string candidate in chosen)
                        {
                            string name=directory;
                            if(candidates.Length>1)name+=" - "+Path.GetDirectoryName(candidate).Replace(Path.DirectorySeparatorChar,' ');
                            launchers.Add(new BarsTool{Name=name,Directory=directory,Executable=candidate});
                        }
                    }
                }
                LibraryCancellation.Check(()=>CancelRequested);
                ToolsDeployment.Discover(packageSource,folders);
                string request=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new ToolCopyRequest{Source=packageSource,Packages=folders.ToArray(),ExcludedDirectories=exclusions.ToArray()});
                var result=await Execute("tools-deploy-selected",request,target);

                if(result.ExitCode==1602)throw new OperationCanceledException();if(result.ExitCode!=0)throw new IOException(result.Message);
                ToolProgress.CompleteStep();ToolsDeployment.EnableAdministratorLaunch(packageSource,target,folders);ToolProgress.CompleteStep();
                ToolsDeployment.ShortcutsAt(packageSource,target,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),true,folders,launchers);
                ToolProgress.CompleteStep();ToolProgress.FinishOperation(cleanupWarnings.Count>0?String.Join(" ",cleanupWarnings):missingShortcuts.Count>0?"Утилиты распакованы. Без ярлыков: "+String.Join(", ",missingShortcuts)+".":launchers.Count>0?"Утилиты распакованы. Ярлыки созданы.":"Утилиты распакованы без создания ярлыков.",success:true);
            }
            catch(OperationCanceledException)
            {

                ToolProgress.FinishOperation("Распаковка отменена.",cancelled:true);
            }
            catch(Exception ex){progress.FinishOperation("Не выполнено: "+ex.Message,failed:true);}finally{archiveCancellation.Dispose();archiveCancellation=null;}
        }
        private async Task Remove()
        {
            if(preview)return;string target;List<BarsTool> tools;
            try
            {
                target=ToolsDeployment.TargetFolder(ToolDestination.Text);
                if(ToolSource!=null&&Path.GetFullPath(ToolSource).TrimEnd('\\').Equals(target.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new IOException("Выбран исходный комплект. Укажи место установленных утилит");
                tools=ToolsDeployment.Installed(target);if(tools.Count==0)throw new IOException("В выбранной папке утилиты не найдены");
            }
            catch(Exception ex){ToolProgress.Stage(OperationMessage.FinishSentence(ex.Message),failed:true);return;}
            if(BrandMessageBox.Show(this,"Удалить из "+target+":\r\n"+String.Join("\r\n",tools.Select(t=>t.Directory))+"?\r\n\r\nНастройки и журналы внутри этих папок тоже будут удалены. Посторонние файлы и резервные копии сохранятся.","Удаление утилит",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            CancelRequested=false;ToolProgress.BeginOperation("Удаляем утилиты…",true,3);
            try
            {
                var result=await Execute("tools-remove",String.Join("\n",tools.Select(t=>t.Directory)),target);if(result.ExitCode==1602)throw new OperationCanceledException();if(result.ExitCode!=0)throw new IOException(result.Message);
                ToolProgress.CompleteStep();ToolsDeployment.RemoveReferences(target,tools,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));ToolProgress.CompleteStep();ToolProgress.CompleteStep();ToolProgress.FinishOperation("Утилиты и их ярлыки удалены.",success:true);
            }
            catch(OperationCanceledException){ToolProgress.FinishOperation("Удаление отменено.",cancelled:true);}
            catch(Exception ex){ToolProgress.FinishOperation("Не выполнено: "+ex.Message,failed:true);}
        }
    }
    internal static class RestartOffer
    {
        internal static async Task Show(IWin32Window owner)
        {
            IDisposable turn;
            if (!WindowsOperationQueue.TryEnter(out turn))
            {
                BrandMessageBox.Show(owner, "Дождись завершения другой системной операции перед перезагрузкой.", "Перезагрузка Windows", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                using (turn)
                {
                    if (BrandMessageBox.Show(owner, "Для завершения изменений нужна перезагрузка Windows. Сохрани открытые документы.\r\n\r\nПерезагрузить компьютер сейчас? Нажми «Нет», чтобы сделать это позже.", "Перезагрузка Windows", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    using (var session = await Task.Run(() => LibrarySession.Start()))
                        await Task.Run(() => session.ExecuteSystem("system-restart", "restart-confirmed", "", ignored => { }));
                }
            }
            catch (Exception ex) { BrandMessageBox.Show(owner, ex.Message, "Перезагрузка Windows", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
