using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal class BundleDownloadForm : ManagerModuleForm, IModuleOperation
    {
        sealed class Entry
        {
            internal BundleRemoteFile File;internal string OriginalHash,State;internal bool Selected,Allowed,Done;internal long Bytes;internal BundleCloud Source;
        }
        readonly JournalTextBox journal=new JournalTextBox {Multiline=true,ReadOnly=true};
        readonly BrandTextBox folder=new BrandTextBox();
        readonly Label summary;
        readonly BundleProgressBar total=new BundleProgressBar {Dock=DockStyle.Fill};
        readonly BrandButton scan,start,pause,back,choose,stop,results;
        readonly BundleCloudSource cloudSource=new BundleCloudSource();
        readonly BrandCheckBox libraries=new BrandCheckBox {Text="Библиотеки и .NET",Checked=true},tools=new BrandCheckBox {Text="Утилиты BARS",Checked=true};
        readonly BrandCheckBox software=new BrandCheckBox {Text="Программы"},wallpapers=new BrandCheckBox {Text="Обои для рабочего стола"},sql=new BrandCheckBox {Text="SQL Server и SSMS"};
        HashSet<string> sqlFolders;
        readonly bool toolsOnly;
        private readonly ToolsWorkspaceGrid toolGrid;
        private readonly BrandComboBox toolCloud;
        private readonly BrandButton deployTools;
        protected Func<string,string[],Dictionary<string,HashSet<string>>,DownloadProgressView,Task> deployFromWorkspace;
        protected Action cancelToolDeployment;
        protected Func<Task> RemoveTools;
        protected readonly BrandTextBox ToolDestination;
        private readonly BrandButton removeTools, targetPicker;
        protected string ToolSource {get{return ToolsFolder;}}
        protected Dictionary<string,HashSet<string>> ToolVariants {get{return toolGrid.Variants;}}
        protected DownloadProgressView ToolProgress {get{return total;}}
        private string localTools;
        private bool extracting;
        private BundleCloud verifiedCloud;
        private string ToolsFolder {get{return localTools??Path.Combine(folder.Text,"_tools");}}
        internal static string ToolPackage(BundleRemoteFile file)
        {
            if(file.Group!="Утилиты")return null;
            string[] parts=file.Path.Split('/');
            if(parts.Length>3)return parts[2];
            return parts.Length==3&&ToolArchives.Supports(parts[2])?parts[2]:null;
        }
        internal static List<BundleRemoteFile> ConfirmedToolPackages(IEnumerable<BundleRemoteFile> reference,Func<BundleRemoteFile,bool> confirmed,out int missing)
        {
            var groups=reference.Where(file=>ToolPackage(file)!=null)
                .GroupBy(ToolPackage,StringComparer.OrdinalIgnoreCase).ToArray();
            var available=groups.Where(group=>group.All(confirmed)).ToArray();
            missing=groups.Length-available.Length;
            return available.SelectMany(group=>group).ToList();
        }
        List<Entry> batch;
        long receivedBytes;

        bool cancelRequested;
        int workerLimit;
        readonly List<Entry> entries=new List<Entry>();
        readonly object sync=new object();
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=250};
        string IModuleOperation.ActiveOperation { get { return extracting?"Распаковка утилит":downloading?"Скачивание комплекта BARS":null; } }
        CancellationTokenSource cancellation;bool busy,paused,closeAfter,downloading,preparingDownload;
        string scannedRoot;
        DateTime started;
        internal bool RootAccepted {get;private set;}
        internal string DownloadedToolsRoot {get;private set;}
        internal string LibraryRoot {get{return folder.Text;}}
        internal static async Task<string> Offer(Form owner,string root,bool programs=false)
        {
            if(BrandMessageBox.Show(owner,"Папка установщиков не найдена. Открыть загрузку комплекта с Яндекс Диска или Google Диска? Проверка и скачивание запускаются отдельно.","Комплект библиотек",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return root;
            using(var dialog=new BundleDownloadForm(root,programs)){await ModuleNavigation.ShowOwnedAsync(owner, dialog);return dialog.RootAccepted?dialog.LibraryRoot:root;}
        }
        internal bool MenuRequested {get;private set;}
        internal override void ResumeSession(){MenuRequested=false;base.ResumeSession();}
        internal BundleDownloadForm(string libraryRoot=null,bool programs=false,bool sqlOnly=false,bool toolsOnly=false,string localSource=null,Func<string,string[],Dictionary<string,HashSet<string>>,DownloadProgressView,Task> deploy=null,Action cancelDeployment=null):base(toolsOnly?"Распаковка утилит":"Загрузка комплекта",toolsOnly?new Size(1050,730):new Size(650,680))
        {
            this.toolsOnly=toolsOnly;localTools=localSource;deployFromWorkspace=deploy;cancelToolDeployment=cancelDeployment;
            MinimumSize=SizeFromClientSize(toolsOnly?new Size(660,520):new Size(650,680));ClientSize=toolsOnly?new Size(1050,730):new Size(650,680);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;Root.RowStyles[0].Height=60;if(toolsOnly){FormBorderStyle=FormBorderStyle.Sizable;MaximizeBox=true;}
            var body=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(float height in new[]{50F,40F,toolsOnly?100F:136F,0F,40F,toolsOnly?0.01F:120F,100F})body.RowStyles.Add(new RowStyle(height==0?SizeType.Percent:SizeType.Absolute,height==0?100:height));
            body.Controls.Add(ModuleHeading(toolsOnly?"Распаковка утилит":"Загрузка комплекта"),0,0);
            var path=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};path.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));path.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,168));path.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var caption=Caption("Папка комплекта",false);path.Controls.Add(caption,0,0);folder.ReadOnly=true;folder.AutoSize=false;folder.Text=libraryRoot==null?BundlePreferences.Load():BundleFolders.BundleRoot(libraryRoot);folder.Dock=DockStyle.None;folder.Anchor=AnchorStyles.Left|AnchorStyles.Right;folder.Margin=Padding.Empty;
            choose=Button("Выбрать папку",false);choose.Dock=DockStyle.None;choose.Anchor=AnchorStyles.Right;choose.Size=new Size(168,40);choose.Margin=Padding.Empty;if(toolsOnly){choose.Height=34;choose.Anchor=AnchorStyles.None;}choose.Click+=delegate{using(var dialog=new FolderBrowserDialog {Description=toolsOnly?"Выбери папку комплекта BarsManager3000 или папку _tools":"Выбери папку комплекта BarsManager3000",SelectedPath=BundleFolders.PickerStart(folder.Text)})if(ScopedDialogs.Show(this, dialog)==DialogResult.OK){string picked=dialog.SelectedPath;bool section=toolsOnly&&Path.GetFileName(picked.TrimEnd((char)92,(char)47)).Equals("_tools",StringComparison.OrdinalIgnoreCase);folder.Text=section?Path.GetDirectoryName(picked.TrimEnd((char)92,(char)47)):BundleFolders.BundleRoot(picked);entries.Clear();scannedRoot=null;localTools=null;RefreshTools();UpdateState();}};
            var folderFrame=new PathFieldFrame(folder);path.Controls.Add(folderFrame,1,0);path.Controls.Add(choose,2,0);path.Layout+=delegate{
                using(var graphics=path.CreateGraphics()){
                    int textWidth=TextRenderer.MeasureText(graphics,caption.Text,caption.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width;
                    int gap=(int)Math.Round(12*graphics.DpiX/96F);
                    float wanted=toolsOnly?Math.Max(textWidth+gap,(int)Math.Round(140*graphics.DpiX/96F)):textWidth+gap;
                    if(Math.Abs(path.ColumnStyles[0].Width-wanted)>.5F)path.ColumnStyles[0].Width=wanted;
                    folderFrame.Margin=new Padding(0,0,gap,0);folderFrame.Height=PathFieldMetrics.Measure(folder).Height;
                }
            };body.Controls.Add(path,0,1);
            var groups=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Margin=Padding.Empty,Padding=new Padding(0,16,0,16)};for(int column=0;column<2;column++)groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));for(int groupRow=0;groupRow<3;groupRow++)groups.RowStyles.Add(new RowStyle(SizeType.Percent,100F/3));int row=0;foreach(var option in new[]{libraries,tools,software,wallpapers,sql}){option.Dock=DockStyle.Fill;option.Margin=Padding.Empty;option.Palette=Colors;option.CheckedChanged+=delegate{if(!busy){foreach(var entry in entries)entry.Selected=entry.Allowed&&!entry.Done&&GroupSelected(entry.File.Group)&&(entry.Selected||entry.OriginalHash==null);UpdateState();}};groups.Controls.Add(option,row%2,row/2);row++;}if(!toolsOnly)body.Controls.Add(groups,0,2);
            if(toolsOnly)
            {
                toolCloud=new BrandComboBox{Dock=DockStyle.None,Anchor=AnchorStyles.Left,Width=230,MinimumSize=new Size(210,0),DropDownStyle=ComboBoxStyle.DropDownList,UseManagedPopup=true};
                toolCloud.Items.AddRange(new object[]{"Яндекс Диск","Google Диск"});toolCloud.SelectedIndex=0;
                var toolOptions=new BufferedLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=Padding.Empty};
                toolOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
                toolOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,168));
                toolOptions.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
                toolOptions.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
                ToolDestination=new BrandTextBox{Text=ToolsDeployment.DefaultTarget,Dock=DockStyle.None,Anchor=AnchorStyles.Left|AnchorStyles.Right,Margin=new Padding(0,0,12,0),AccessibleName="Путь распаковки"};
                var destinationRow=new BufferedLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
                destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));
                destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
                destinationRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));
                destinationRow.Controls.Add(Caption("Путь распаковки",false),0,0);
                var destinationFrame=new PathFieldFrame(ToolDestination);
                destinationRow.Controls.Add(destinationFrame,1,0);
                destinationRow.Layout+=delegate
                {
                    int height=PathFieldMetrics.Measure(ToolDestination).Height;
                    if(destinationFrame.Height!=height)destinationFrame.Height=height;
                    destinationRow.ColumnStyles[0].Width=path.ColumnStyles[0].Width;
                    destinationFrame.Margin=folderFrame.Margin;
                };
                toolOptions.Controls.Add(destinationRow,0,0);
                targetPicker=Button("Куда распаковать…",false);
                targetPicker.Dock=DockStyle.None;targetPicker.Anchor=AnchorStyles.None;targetPicker.Size=new Size(168,34);targetPicker.Margin=Padding.Empty;
                targetPicker.Click+=delegate
                {
                    using(var dialog=new FolderBrowserDialog{Description="Выбери папку назначения для утилит",SelectedPath=BundleFolders.PickerStart(ToolDestination.Text)})
                        if(ScopedDialogs.Show(this,dialog)==DialogResult.OK)
                        {
                            try{ToolDestination.Text=ToolsDeployment.TargetFolder(dialog.SelectedPath);}
                            catch(Exception ex){total.Stage(OperationMessage.FinishSentence(ex.Message),failed:true);}
                        }
                };
                toolOptions.Controls.Add(targetPicker,1,0);
                toolCloud.Margin=new Padding(0,6,0,4);
                toolOptions.Controls.Add(toolCloud,0,1);

                body.Controls.Add(toolOptions,0,2);
                toolCloud.SelectedIndexChanged+=delegate
                {
                    if(busy)return;
                    UpdateState();
                    summary.Text="Нажми «Проверить в сети», чтобы прочитать каталог выбранного облака.";
                };
                toolGrid=new ToolsWorkspaceGrid{Dock=DockStyle.Fill};
                toolGrid.SelectionChanged+=delegate{UpdateToolSelection();UpdateState();};
                var workspace=new BufferedLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
                workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
                workspace.RowStyles.Add(new RowStyle(SizeType.Percent,100));
                workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
                workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,65));
                var tableCard=new Card{Dock=DockStyle.Fill,Padding=new Padding(11),Margin=new Padding(0,0,0,10)};
                toolGrid.Margin=Padding.Empty;tableCard.Controls.Add(toolGrid);workspace.Controls.Add(tableCard,0,0);
                workspace.RowCount=3;workspace.Controls.Add(total,0,1);workspace.Controls.Add(JournalPanel.Create(journal,"Журнал загрузки комплекта",Colors),0,2);
                body.Controls.Add(workspace,0,3);
                total.InlineDetails=true;
            }
            else body.Controls.Add(JournalPanel.Create(journal,"Журнал загрузки комплекта",Colors),0,3);
            summary=Label(toolsOnly?"Отметь нужные наборы, затем нажми «Скачать» или «Распаковать».":"Проверка сверяет файлы; установка в Windows не выполняется.",!toolsOnly);summary.Margin=new Padding(0,4,0,4);body.Controls.Add(summary,0,4);total.Margin=new Padding(0,8,0,8);if(!toolsOnly)body.Controls.Add(total,0,5);
            var actions=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=12,RowCount=2,Margin=Padding.Empty};for(int i=0;i<12;i++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/12));actions.RowStyles.Add(new RowStyle(SizeType.Percent,50));actions.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            scan=Button(toolsOnly?"Проверить в сети":"Проверить",false);start=Button("Скачать",true);pause=Button("Пауза",false);stop=Button("Отменить",false);back=Button("В меню",false);results=Button(toolsOnly?"Перепроверить файлы":"Результаты",false);results.Click+=async delegate{if(toolsOnly)await RecheckTools();else await ShowResults();};
            scan.Click+=async delegate{await Scan();};start.Click+=async delegate{await Download();};pause.Click+=delegate{if(!downloading||preparingDownload)return;if(paused){paused=false;pause.Text="Пауза";}else{paused=true;pause.Enabled=false;pause.Text="Пауза…";if(cancellation!=null)cancellation.Cancel();}};
            stop.Click+=delegate{if(toolsOnly&&busy&&!downloading&&!extracting){if(cancellation!=null)cancellation.Cancel();return;}if(extracting){if(cancelToolDeployment!=null)cancelToolDeployment();return;}if(busy&&BrandMessageBox.ConfirmStop(this,"Остановить загрузку? Готовые файлы сохранятся. Недокачанные файлы будут удалены.")==DialogResult.Yes){cancelRequested=true;paused=false;if(cancellation!=null)cancellation.Cancel();}};
            back.Click+=delegate{if(ModuleNavigation.ReturnToHub())return;MenuRequested=true;Close();};var close=Button("Закрыть",false);close.Click+=delegate{Close();};var upper = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var lower = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            if(toolsOnly)
            {
                deployTools=Button("Распаковать",false);
                deployTools.Click+=async delegate{await DeployTools();};
                removeTools=Button("Удалить",false);removeTools.Click+=async delegate{await RunToolOperation(RemoveTools);};
                ModuleNavigation.StandardFooter(upper,scan,start,results,deployTools);
            }
            else ModuleNavigation.StandardFooter(upper, scan, start, results);
            if(toolsOnly)ModuleNavigation.RightAlignedFooter(lower,pause,stop,removeTools,back,close);
            else ModuleNavigation.StandardFooter(lower,pause,stop,back,close);
            actions.Controls.Add(upper, 0, 0); actions.SetColumnSpan(upper, 12);
            actions.Controls.Add(lower, 0, 1); actions.SetColumnSpan(lower, 12);body.RowCount=6;body.RowStyles.RemoveAt(6);body.MinimumSize=new Size(0,480);
            var content=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));content.RowStyles.Add(new RowStyle(SizeType.Percent,100));content.RowStyles.Add(new RowStyle(SizeType.Absolute,100));content.Controls.Add(new FillContentPanel(body){Margin=Padding.Empty,PreserveScrollOnFocus=true},0,0);content.Controls.Add(actions,0,1);Root.Controls.Add(content,0,1);
            timer.Tick+=delegate{RefreshProgress();};VisibleChanged+=delegate{timer.Enabled=Visible;};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;if(extracting){closeAfter=true;if(cancelToolDeployment!=null)cancelToolDeployment();return;}if(!downloading || closeAfter || ModuleNavigation.ExitConfirmed || BrandMessageBox.Show(this,"Остановить загрузку и закрыть окно? Готовые файлы сохранятся.","Загрузка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes){closeAfter=true;cancelRequested=true;paused=false;if(cancellation!=null)cancellation.Cancel();}}};
            if(programs||sqlOnly||toolsOnly){libraries.Checked=false;tools.Checked=toolsOnly;software.Checked=programs;sql.Checked=sqlOnly;}SetTheme(Colors.Dark,false);RefreshTools();UpdateState();
        }
        private void UpdateToolSelection()
        {
            if(toolGrid==null)return;
            foreach(var entry in entries)entry.Selected=entry.Allowed&&!entry.Done&&toolGrid.WantsDownload(ToolPackage(entry.File));
        }
        private void RefreshTools()
        {
            if(toolGrid==null)return;
            var catalog=entries.GroupBy(entry=>ToolPackage(entry.File),StringComparer.OrdinalIgnoreCase)
                .Where(group=>group.Key!=null).ToDictionary(group=>group.Key,group=>
                {
                    bool ready=group.All(entry=>entry.Done);
                    bool download=group.Any(entry=>entry.Allowed&&!entry.Done);
                    return Tuple.Create(ready?"Локальная папка":download?String.Join(" / ",group.Where(entry=>!entry.Done).Select(entry=>CloudName(entry.Source)).Distinct()):"Локальная копия отличается",ready,download);
                },StringComparer.OrdinalIgnoreCase);
            toolGrid.LoadRows(ToolsFolder,catalog);
            UpdateToolSelection();
        }
        private async Task DeployTools()
        {
            if(busy||deployFromWorkspace==null)return;
            if(toolGrid.Deployment.Length==0&&toolGrid.DamagedPackages.Length>0)
            {
                string message="Повреждённые архивы: "+String.Join(", ",toolGrid.DamagedPackages)+". Проверить исправные копии в облаке?";
                if(BrandMessageBox.Show(this,message,Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)
                    await Scan();
                return;
            }
            if(!toolGrid.HasLocal)
            {
                const string message="Распаковывать нечего, локальный комплект не обнаружен, выберите другую локальную папку или скачайте комплект с облачного сервиса.";
                using(var dialog=new BrandMessageDialog(message,Text,MessageBoxButtons.YesNoCancel,MessageBoxIcon.Information,MessageBoxDefaultButton.Button1,Colors))
                {
                    dialog.SetButtonText(DialogResult.Yes,"Выбрать папку");
                    dialog.SetButtonText(DialogResult.No,"Проверить облако");
                    var choice=ScopedDialogs.Show(this,dialog);
                    if(choice==DialogResult.Yes)choose.PerformClick();
                    if(choice==DialogResult.No)await Scan();
                }
                return;
            }
            await RunToolOperation(()=>deployFromWorkspace(ToolsFolder,toolGrid.Deployment,toolGrid.Shortcuts,total));
        }
        private async Task RunToolOperation(Func<Task> action)
        {
            if(busy||action==null)return;
            busy=extracting=true;UpdateState();
            try{await action();}catch(Exception ex){total.FinishOperation("Не выполнено: "+ex.Message,failed:true);}
            finally{busy=extracting=false;RefreshTools();UpdateState();if(closeAfter)Close();}
        }
        private async Task RecheckTools()
        {
            if(busy||scannedRoot==null)return;
            busy=true;cancellation=new CancellationTokenSource();UpdateState();
            try
            {
                var token=cancellation.Token;

                var root=scannedRoot;
                total.BeginOperation("Перепроверяем локальные файлы…");
                var prior=entries.ToArray();
                var refreshed=await Task.Run(()=>prior.Select(entry=>{var item=CheckLocal(root,entry.File,token);item.Source=entry.Source;return item;}).ToArray(),token);
                entries.Clear();entries.AddRange(refreshed);RefreshTools();
                total.FinishOperation("Локальная сверка завершена.",success:true);
            }
            catch(OperationCanceledException){total.FinishOperation("Сверка отменена.",cancelled:true);}
            catch(Exception ex){total.FinishOperation("Не удалось сверить файлы: "+ex.Message,failed:true);}
            finally{busy=false;cancellation.Dispose();cancellation=null;UpdateState();if(closeAfter)Close();}
        }
        bool GroupSelected(string group){switch(group){case "Библиотеки":return libraries.Checked;case "Утилиты":return tools.Checked;case "Программы":return software.Checked;case "Обои":return wallpapers.Checked;case "SQL":return sql.Checked;default:return false;}}
        static string SizeText(long bytes){return (bytes/1048576.0).ToString("0.0")+" МиБ";}
        void Log(string text){if(IsDisposed)return;if(InvokeRequired){try{BeginInvoke((MethodInvoker)delegate{Log(text);});}catch(InvalidOperationException){}return;}journal.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+OperationMessage.FinishSentence(text)+Environment.NewLine);}
        protected void UpdateState(){results.Enabled=!busy&&(toolsOnly||scannedRoot!=null);scan.Enabled=choose.Enabled=!busy;start.Enabled=!busy&&entries.Any(e=>e.Selected&&e.Allowed&&!e.Done);pause.Enabled=downloading&&!preparingDownload;stop.Enabled=busy;back.Enabled=!busy||ManagerApplicationContext.ActiveContext!=null;libraries.Enabled=tools.Enabled=software.Enabled=wallpapers.Enabled=sql.Enabled=!busy&&!toolsOnly;if(toolGrid!=null){toolGrid.Enabled=toolCloud.Enabled=ToolDestination.Enabled=targetPicker.Enabled=removeTools.Enabled=!busy;deployTools.Enabled=!busy&&deployFromWorkspace!=null&&(!toolGrid.HasLocal||toolGrid.Deployment.Length>0||toolGrid.DamagedPackages.Length>0);}}
        async Task ShowResults()
        {
            var source=entries.ToArray();if(scannedRoot==null)return;var rows=source.Select(e=>new BundleDifferenceItem {Path=e.File.Path.TrimStart('/'),State=e.State,Allowed=e.Allowed,Selected=e.Selected}).ToArray();
            string root=scannedRoot;
            using(var dialog=new BundleDifferencesForm(rows,recheck:async delegate(CancellationToken token)
            {
                var refreshed=await Task.Run(()=>source.Select(e=>CheckLocal(root,e.File,token)).ToArray());
                token.ThrowIfCancellationRequested();
                for(int i=0;i<source.Length;i++)
                {
                    var old=source[i];var current=refreshed[i];
                    old.Selected=current.Allowed&&GroupSelected(old.File.Group)&&(rows[i].Selected||current.Selected&&(old.Done||!old.Allowed));
                    old.OriginalHash=current.OriginalHash;old.Done=current.Done;old.Allowed=current.Allowed;old.State=current.State;old.Bytes=current.Done?old.File.Size:0;
                    rows[i].Allowed=old.Allowed;rows[i].Selected=old.Selected;rows[i].State=old.State;
                }
                Log("Локальная сверка: проверено "+source.Length+", готово "+source.Count(e=>e.Done)+", к загрузке "+source.Count(e=>e.Selected)+".");
                UpdateState();
            }))await ModuleNavigation.ShowOwnedAsync(this, dialog);
            for(int i=0;i<rows.Length;i++)source[i].Selected=rows[i].Selected;UpdateState();
        }
        static Entry CheckLocal(string root,BundleRemoteFile file,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string local=BundleFolders.PackageDestination(root,file.Path);
            var entry=new Entry {File=file};
            if(!File.Exists(local)){entry.Allowed=entry.Selected=true;entry.State="Отсутствует - будет скачано";return entry;}
            entry.OriginalHash=YandexBundleSource.Hash(local,token);
            if(file.Group=="Утилиты"&&ToolArchives.Supports(local))
            {
                bool readable=ToolArchives.HasReadableDirectory(local,null,token);
                entry.Done=readable&&(file.Sha256!=null
                    ?String.Equals(entry.OriginalHash,file.Sha256,StringComparison.OrdinalIgnoreCase)
                    :new FileInfo(local).Length==file.Size);
                entry.Allowed=!entry.Done;
                entry.Selected=!readable;
                entry.State=!readable?"Архив не читается — доступна замена":
                    !entry.Done?"Локальный архив читается — можно скачать другую копию":
                    file.Sha256==null?"Локальный архив читается, размер совпадает":"Проверено — файл на месте";
                return entry;
            }
            entry.Done=entry.OriginalHash==file.Sha256;
            entry.Selected=!entry.Done&&new BundleReceipts(root).CanReplace(file,entry.OriginalHash);
            string extension=Path.GetExtension(file.Path);
            entry.Allowed=!entry.Done&&file.Group!="Утилиты"&&(extension.Equals(".exe",StringComparison.OrdinalIgnoreCase)||extension.Equals(".msi",StringComparison.OrdinalIgnoreCase));
            entry.State=entry.Done?"Проверено — файл на месте":entry.Selected?"Изменён в источнике — обновить":entry.Allowed?"Отличается — замена по выбору":"Отличается — сохранить локальный файл";
            return entry;
        }
        async Task Scan(BundleCloud? forcedCloud=null)
        {
            if(busy)return;bool suggestAlternative=false, showResults=false;bool emptyToolCatalog=false;BundleCloud cloud;if(forcedCloud.HasValue)cloud=forcedCloud.Value;else if(toolsOnly)cloud=toolCloud.SelectedIndex==1?BundleCloud.Google:BundleCloud.Yandex;else using(var picker=new BundleCloudDialog(true)){if(await ModuleNavigation.ShowOwnedAsync(this, picker)!=DialogResult.OK)return;cloud=picker.Cloud;}string root;
            try { root=Path.GetFullPath(folder.Text); }
            catch(Exception ex)
            {
                if (!(ex is ArgumentException) && !(ex is NotSupportedException) && !(ex is IOException)) throw;
                summary.Text="Неверный путь к папке комплекта.";
                Log(summary.Text+" "+ex.Message);
                return;
            }
            busy=true;cancelRequested=false;paused=false;batch=null;cancellation=new CancellationTokenSource();UpdateState();Log("Проверяем выбранные разделы: читаем каталог облака и сверяем локальные файлы…");summary.Text="Читаем выбранное облако и проверяем локальные файлы…";total.BeginOperation(summary.Text);
            try
            {
                var token=cancellation.Token;
                var selectedGroups=new HashSet<string>(new[]{"Библиотеки","Утилиты","Программы","Обои","SQL"}.Where(GroupSelected));
                var files=await Task.Run(()=>cloud==BundleCloud.Google&&(selectedGroups.Contains("SQL")||toolsOnly)?(toolsOnly?cloudSource.ScanGoogleTools(token,Log):cloudSource.ScanGoogleSql(token,Log)):cloudSource.Scan(cloud,token,Log));
                token.ThrowIfCancellationRequested();
                if(selectedGroups.Contains("SQL"))
                {
                    var names=BundleSqlFolders.Names(files);
                    if(names.Length==0) Log("В выбранном облаке папки SQL не найдены.");
                    else using(var sqlPicker=new BundleSqlFoldersForm(names,sqlFolders))
                    {
                        sqlPicker.SetTheme(Colors.Dark,false);
                        if(await ModuleNavigation.ShowOwnedAsync(this,sqlPicker)!=DialogResult.OK)return;
                        token.ThrowIfCancellationRequested();
                        sqlFolders=sqlPicker.Selected;
                        Log(sqlFolders.Count==0?"Папки SQL не выбраны.":"Выбраны папки SQL: "+String.Join(", ",sqlFolders));
                    }
                }
                var chosenFiles=files.Where(f=>selectedGroups.Contains(f.Group)&&(f.Group!="SQL"||BundleSqlFolders.Includes(f,sqlFolders))
                    &&(!toolsOnly||ToolPackage(f)!=null)).ToArray();
                emptyToolCatalog=toolsOnly&&chosenFiles.Length==0;
                var result=await Task.Run(()=>{
                    var list=new List<Entry>();total.SetWork(chosenFiles.Length);
                    foreach(var file in chosenFiles){var entry=CheckLocal(root,file,token);entry.Source=cloud;list.Add(entry);total.OperationReporter("Проверен файл: "+file.Path.TrimStart('/'));total.CompleteStep();
                    }return list;
                });
                var saved=BundleSession.Load(root);
                if(!toolsOnly&&saved!=null&&saved.Count>0&&BrandMessageBox.Show(this,"Найдена сохранённая очередь. Восстановить выбор файлов, которые по-прежнему совпадают с актуальным источником?","Продолжение загрузки",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)
                    foreach(var entry in result)entry.Selected=entry.Allowed&&!entry.Done&&(entry.OriginalHash==null&&entry.File.Group=="SQL"||saved.Contains(entry.File.Path+"|"+entry.File.Sha256));
                foreach(var entry in result)entry.Selected=entry.Selected&&GroupSelected(entry.File.Group);
                if(toolsOnly&&String.Equals(scannedRoot,root,StringComparison.OrdinalIgnoreCase))
                {
                    entries.RemoveAll(entry=>entry.Source==cloud||result.Any(current=>String.Equals(current.File.Path,entry.File.Path,StringComparison.OrdinalIgnoreCase)));
                    var existing=new HashSet<string>(entries.Select(entry=>entry.File.Path),StringComparer.OrdinalIgnoreCase);
                    entries.AddRange(result.Where(entry=>existing.Add(entry.File.Path)));
                }
                else {entries.Clear();entries.AddRange(result);}
                scannedRoot=root;verifiedCloud=cloud;RefreshTools();
                foreach(var entry in entries.Where(e=>!e.Done))Log(entry.File.Path.TrimStart('/')+": "+entry.State);
                var differences=entries.Where(e=>!e.Done).Select(e=>new[]{e.File.Path.TrimStart('/'),e.State}).ToArray();
                Log("Проверка завершена. Требуют внимания: "+differences.Length+"; актуальных файлов: "+entries.Count(e=>e.Done)+".");
                showResults = !toolsOnly&&differences.Length>0;
                total.FinishOperation("Сверка завершена. Скачивание запускается отдельно.",success:true);summary.Text="Проверено: "+entries.Count+". На месте: "+entries.Count(e=>e.Done)+". К загрузке: "+entries.Count(e=>e.Selected)+". Защищено: "+entries.Count(e=>!e.Done&&!e.Allowed)+".";
                RootAccepted=true;BundlePreferences.Save(root);
            }
            catch(Exception ex){emptyToolCatalog=false;summary.Text=cancellation.IsCancellationRequested?"Проверка отменена.":OperationMessage.FinishSentence("Не удалось проверить: "+ex.Message);total.Stage(summary.Text,failed:!cancellation.IsCancellationRequested,cancelled:cancellation.IsCancellationRequested);Log(summary.Text);suggestAlternative=!cancellation.IsCancellationRequested;}
            finally{busy=false;cancellation.Dispose();cancellation=null;UpdateState();if(closeAfter)Close();}
            if(toolsOnly&&!forcedCloud.HasValue&&(emptyToolCatalog||suggestAlternative)&&!cancelRequested&&!closeAfter&&!IsDisposed)
            {
                var other=cloud==BundleCloud.Google?BundleCloud.Yandex:BundleCloud.Google;
                string reason=suggestAlternative?"Не удалось проверить "+(cloud==BundleCloud.Google?"Google Диск.":"Яндекс Диск."):"На "+(cloud==BundleCloud.Google?"Google Диске":"Яндекс Диске")+" нет доступных архивов утилит.";
                string question=other==BundleCloud.Yandex?"Проверить комплект на Яндекс Диске?":"Проверить комплект на Google Диске?";
                if(BrandMessageBox.Show(this,reason+" "+question,"Проверка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)
                {
                    toolCloud.SelectedIndex=other==BundleCloud.Google?1:0;
                    await Scan(other);
                }
            }
            if(showResults&&!closeAfter&&!IsDisposed)await ShowResults();
            if(!toolsOnly&&suggestAlternative&&!closeAfter&&BrandMessageBox.Show(this,"Не удалось проверить выбранное облако. Попробовать альтернативное?","Проверка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)await Scan(cloud==BundleCloud.Google?BundleCloud.Yandex:BundleCloud.Google);
        }
        internal static string CloudName(BundleCloud cloud){return cloud==BundleCloud.Google?"Google Диск":"Яндекс Диск";}
        internal static string AlternativeMessage(BundleCloud cloud,int remaining){return "Не удалось скачать часть файлов из "+CloudName(cloud)+".\r\n\r\nДокачать оставшиеся файлы ("+remaining+") из "+CloudName(cloud==BundleCloud.Google?BundleCloud.Yandex:BundleCloud.Google)+"?\r\n\r\nГотовые файлы сохранятся и не будут скачиваться заново. Недокачанные файлы продолжим с сохранённого места, если облако поддерживает докачку.";}
        internal static string FailedArchiveMessage(IEnumerable<string> names,bool damaged)
        {
            var files=names.ToArray();
            string heading=damaged?"Выбранные архивы скачаны не будут — они повреждены.":"Не удалось скачать или проверить выбранные архивы.";
            string list=String.Join("\r\n",files.Take(5));
            if(files.Length>5)list+="\r\nЕщё архивов: "+(files.Length-5)+". Полный список в журнале.";
            return heading+"\r\n\r\n"+list+"\r\n\r\nПосмотреть наборы в другом облаке?";
        }
        async Task Download(BundleCloud? forcedCloud=null, bool alternativeAttempt=false)
        {
            if(busy||scannedRoot==null)return;BundleCloud downloadCloud;if(forcedCloud.HasValue)downloadCloud=forcedCloud.Value;else if(toolsOnly)downloadCloud=verifiedCloud;else using(var picker=new BundleCloudDialog(false)){if(await ModuleNavigation.ShowOwnedAsync(this, picker)!=DialogResult.OK)return;downloadCloud=picker.Cloud;}
            var selected=entries.Where(e=>e.Selected&&e.Allowed&&!e.Done).ToList();if(selected.Count==0)return;
            if(!toolsOnly&&BrandMessageBox.Show(this,"Скачать "+selected.Count+" файлов ("+SizeText(selected.Sum(e=>e.File.Size))+")?\r\nУстановщики не будут запущены.","Загрузка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            var replacements=selected.Where(e=>e.OriginalHash!=null).ToArray();
            if(replacements.Length>0&&BrandMessageBox.Show(this,(toolsOnly?"Заменить выбранные локальные архивы (":"Заменить отличающиеся локальные установщики (")+replacements.Length+")?\r\n"+String.Join("\r\n",replacements.Take(6).Select(e=>e.File.Path)),toolsOnly?"Замена архивов":"Замена установщиков",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            Log("Начинаем загрузку из "+CloudName(downloadCloud)+". В очереди: "+selected.Count+" файлов. Уже готовые файлы повторно не скачиваем.");
            busy=true;downloading=true;preparingDownload=true;cancelRequested=false;paused=false;batch=selected;receivedBytes=0;started=DateTime.UtcNow;UpdateState();
            try
            {
            try
            {
                int workers=3;workerLimit=workers;
                cancellation=new CancellationTokenSource();
                if(toolsOnly&&alternativeAttempt)
                {
                    foreach(var mirror in selected.Select(entry=>entry.Source==BundleCloud.Google?BundleCloud.Yandex:BundleCloud.Google).Distinct())
                    {
                        try{await Task.Run(()=>cloudSource.Scan(mirror,cancellation.Token,Log,selected.Select(e=>e.File).ToList()));}
                        catch(Exception ex){cancellation.Token.ThrowIfCancellationRequested();Log("Не удалось обновить каталог "+CloudName(mirror)+": "+ex.Message);}
                    }
                }
                else if(alternativeAttempt||!cloudSource.Ready(downloadCloud))
                    await Task.Run(()=>cloudSource.Scan(downloadCloud,cancellation.Token,Log,selected.Select(e=>e.File).ToList()));
                cancellation.Token.ThrowIfCancellationRequested();
                if(cancelRequested||paused)return;
                Directory.CreateDirectory(scannedRoot);
                BundleSession.Save(scannedRoot,selected.Select(e=>e.File));BundleSession.Log(scannedRoot,"Начало очереди: "+selected.Count+" файлов");
                string probe=YandexBundleSource.LocalPath(scannedRoot,"/.bars-download/write-"+Guid.NewGuid().ToString("N")+".tmp");
                Directory.CreateDirectory(Path.GetDirectoryName(probe));
                using(var output=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose)){output.WriteByte(0);}
                var drive=new DriveInfo(Path.GetPathRoot(scannedRoot));
                foreach(var entry in selected){string part=BundleTransfer.PartPath(scannedRoot,entry.File);entry.Bytes=File.Exists(part)?Math.Min(entry.File.Size,new FileInfo(part).Length):0;}
                RefreshProgress();
                long required=selected.Sum(e=>Math.Max(0,e.File.Size-(File.Exists(BundleTransfer.PartPath(scannedRoot,e.File))?new FileInfo(BundleTransfer.PartPath(scannedRoot,e.File)).Length:0)));
                if(drive.IsReady&&drive.AvailableFreeSpace<required+16L*1024*1024)throw new IOException("Недостаточно свободного места для выбранных файлов и безопасной загрузки.");
                preparingDownload=false;UpdateState();
                do
                {
                    if(cancellation!=null)cancellation.Dispose();cancellation=new CancellationTokenSource();var token=cancellation.Token;
                    var queue=new Queue<Entry>(selected.Where(e=>!e.Done));
                    var tasks=Enumerable.Range(0,workers).Select(worker=>Task.Run(()=>{
                        while(true){Entry entry;lock(sync){if(queue.Count==0||token.IsCancellationRequested||worker>=workerLimit)return;entry=queue.Dequeue();entry.State="Скачивание";}
                            try{
                                bool published=BundlePublication.Matches(scannedRoot,entry.File,token);
                                var fileCloud=toolsOnly?(alternativeAttempt?(entry.Source==BundleCloud.Google?BundleCloud.Yandex:BundleCloud.Google):entry.Source):downloadCloud;
                                if(!published&&!cloudSource.Contains(fileCloud,entry.File))throw new IOException("Выбранное облако не содержит подтверждённую копию файла");
                                if(!published)cloudSource.Transfer(fileCloud).Download(entry.File,scannedRoot,entry.OriginalHash,bytes=>{lock(sync)entry.Bytes=bytes;},token,bytes=>Interlocked.Add(ref receivedBytes,bytes),state=>{lock(sync){entry.State=state;if(state=="Ожидание повтора")workerLimit=Math.Max(1,workerLimit-1);}BundleSession.Log(scannedRoot,entry.File.Path+": "+state);});
                                string warning=entry.File.Sha256==null?null:BundlePublication.Record(scannedRoot,entry.File,null);
                                lock(sync){entry.Done=true;entry.Bytes=entry.File.Size;entry.Selected=false;entry.State=warning==null?"Готово":warning;}Log("Сохранён файл: "+entry.File.Path.TrimStart('/')+(warning==null?"":". "+warning));
                            }
                            catch(Exception ex){Log("Не удалось скачать файл из "+CloudName(downloadCloud)+": "+entry.File.Path.TrimStart('/')+". "+(token.IsCancellationRequested?"Передача остановлена; полученные данные сохранены.":"Готовые файлы не затронуты. Подробности: "+ex.Message));BundleSession.Log(scannedRoot,entry.File.Path+": "+(token.IsCancellationRequested?"Прервано":ex.Message));lock(sync)entry.State=token.IsCancellationRequested?"Приостановлено / прервано":"Ошибка: "+ex.Message;if(token.IsCancellationRequested)return;}
                        }
                    })).ToArray();
                    await Task.WhenAll(tasks);cancellation.Dispose();cancellation=null;
                    if(!cancelRequested&&paused){pause.Text="Продолжить";pause.Enabled=true;summary.Text="Пауза. Уже полученные данные сохранены.";while(paused&&!closeAfter)await Task.Delay(100);}
                    else break;
                }while(!closeAfter&&!cancelRequested);
                int done=selected.Count(e=>e.Done);
                if(toolsOnly&&done>0){DownloadedToolsRoot=scannedRoot;localTools=null;RefreshTools();}
                summary.Text=(cancelRequested?"Загрузка отменена. ":done==selected.Count?"Загрузка завершена. ":"Загрузка не завершена. ")+"Сохранено: "+done+" из "+selected.Count+". Осталось: "+(selected.Count-done)+".";
                total.Stage(toolsOnly?(cancelRequested?"Загрузка отменена.":done==selected.Count?"Загрузка завершена.":"Загрузка не завершена."):summary.Text,complete:done==selected.Count,failed:!cancelRequested&&done<selected.Count,cancelled:cancelRequested);
                Log(summary.Text);
            }
            catch(OperationCanceledException){summary.Text="Загрузка отменена.";total.Stage(summary.Text,cancelled:true);Log(summary.Text);}
            catch(Exception ex){summary.Text=OperationMessage.FinishSentence("Загрузка не завершена: "+ex.Message);total.Stage(summary.Text,failed:true);Log(summary.Text);}
            finally{busy=false;downloading=false;preparingDownload=false;paused=false;pause.Text="Пауза";if(cancellation!=null){cancellation.Dispose();cancellation=null;}UpdateState();if(closeAfter)Close();}
            int remaining=selected.Count(e=>!e.Done);
            if(!cancelRequested&&!closeAfter&&!IsDisposed&&remaining>0)
            {
                Log("Загрузка из "+CloudName(downloadCloud)+" завершена не полностью. Готово: "+selected.Count(e=>e.Done)+"; осталось: "+remaining+". Готовые файлы сохранены.");
                if(!alternativeAttempt&&!toolsOnly&&selected.Any(entry=>!entry.Done&&entry.File.Sha256!=null))
                {
                    var other=downloadCloud==BundleCloud.Google?BundleCloud.Yandex:BundleCloud.Google;
                    if(BrandMessageBox.Show(this,AlternativeMessage(downloadCloud,remaining),"Докачать из другого облака?",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)
                    {Log("По вашему выбору переключаемся на "+CloudName(other)+". Проверим копии оставшихся файлов и докачаем только их.");await Download(other,true);}
                    else Log("Вы оставили текущий источник. Готовые файлы сохранены. Загрузку остальных можно повторить позже.");
                }
                else if(alternativeAttempt)Log("Резервное облако тоже не смогло выдать все файлы.");
                else if(toolsOnly)
                {
                    var other=downloadCloud==BundleCloud.Google?BundleCloud.Yandex:BundleCloud.Google;
                    var failed=selected.Where(entry=>!entry.Done).ToArray();
                    string message=FailedArchiveMessage(
                        failed.Select(entry=>Path.GetFileName(entry.File.Path)),
                        failed.All(entry=>entry.State=="Ошибка: Скачанный архив не удалось прочитать." ||
                            entry.State.StartsWith("Ошибка: SHA-256 не совпадает",StringComparison.Ordinal)));
                    if(BrandMessageBox.Show(this,message,"Проверка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)
                    {
                        toolCloud.SelectedIndex=other==BundleCloud.Google?1:0;
                        await Scan(other);
                    }
                }
            }
            }
            finally
            {
                if(!alternativeAttempt)
                    try{BundleSession.Cleanup(scannedRoot);}
                    catch(IOException ex){Log("Не удалось удалить временную папку загрузки. "+ex.Message);}
                    catch(UnauthorizedAccessException ex){Log("Не удалось удалить временную папку загрузки. "+ex.Message);}
            }
        }
        void RefreshProgress()
        {
            if(IsDisposed)return;
            lock(sync)
            {
                long size=(batch??entries.Where(e=>e.Selected).ToList()).Sum(e=>e.File.Size),bytes=(batch??entries.Where(e=>e.Selected).ToList()).Sum(e=>e.Bytes);
                if(downloading && batch!=null){
                    bool complete=batch.Count>0&&batch.All(e=>e.Done);
                    var active=batch.FirstOrDefault(e=>!e.Done&&e.State!=null);
                    bool failed=batch.Any(e=>e.State!=null&&e.State.StartsWith("Ошибка"));
                    string stage=paused?"Пауза":cancelRequested?"Загрузка отменена":complete?"Готово":active==null?"Подготовка…":active.State;
                    if(stage=="Скачивание")stage="Скачиваем файлы, готово "+batch.Count(e=>e.Done)+" из "+batch.Count+"…";
                    total.Report(new TransferProgress{Key="Комплект",NetworkBytes=Interlocked.Read(ref receivedBytes),Stage=stage,File=active==null?"":active.File.Path.TrimStart('/'),Bytes=bytes,Total=size,Complete=complete,Paused=paused&&!cancelRequested,Cancelled=cancelRequested&&!complete,Active=!cancelRequested&&!complete,Failed=failed&&!complete});
                }
            }
        }
        protected override void OnThemeChanged(){if(toolGrid!=null){toolGrid.SetPalette(Colors);toolCloud.Palette=Colors;}if(total!=null){total.Palette=Colors;total.Invalidate();}if(libraries!=null)libraries.Palette=Colors;if(tools!=null)tools.Palette=Colors;}
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
}
