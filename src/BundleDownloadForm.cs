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
    internal sealed class BundleDownloadForm : ManagerModuleForm
    {
        sealed class Entry
        {
            internal BundleRemoteFile File;internal string OriginalHash,State;internal bool Selected,Allowed,Done;internal long Bytes;
        }
        readonly JournalTextBox journal=new JournalTextBox {Multiline=true,ReadOnly=true};
        readonly BrandTextBox folder=new BrandTextBox();
        readonly Label summary;
        readonly BundleProgressBar total=new BundleProgressBar {Dock=DockStyle.Fill};
        readonly BrandButton scan,start,pause,back,choose,stop,results;
        readonly BundleCloudSource cloudSource=new BundleCloudSource();
        readonly BrandCheckBox libraries=new BrandCheckBox {Text="Библиотеки и .NET",Checked=true},tools=new BrandCheckBox {Text="Утилиты BARS",Checked=true};
        List<Entry> batch;
        long receivedBytes;

        bool cancelRequested;
        int workerLimit;
        readonly List<Entry> entries=new List<Entry>();
        readonly object sync=new object();
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=250};
        CancellationTokenSource cancellation;bool busy,paused,closeAfter,downloading;
        string scannedRoot;
        DateTime started;
        internal bool RootAccepted {get;private set;}
        internal string LibraryRoot {get{return folder.Text;}}
        internal static string Offer(IWin32Window owner,string root)
        {
            if(BrandMessageBox.Show(owner,"Папка Библиотеки не найдена. Открыть загрузку комплекта с Яндекс Диска? Проверка и скачивание запускаются отдельно.","Комплект библиотек",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return root;
            using(var dialog=new BundleDownloadForm(root)){dialog.ShowDialog(owner);return dialog.RootAccepted?dialog.LibraryRoot:root;}
        }
        internal bool MenuRequested {get;private set;}
        internal override void ResumeSession(){MenuRequested=false;base.ResumeSession();}
        internal BundleDownloadForm(string libraryRoot=null):base("Загрузка комплекта",new Size(650,680))
        {
            MinimumSize=SizeFromClientSize(new Size(650,680));ClientSize=new Size(650,680);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;Root.RowStyles[0].Height=60;
            var body=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(float height in new[]{50F,40F,104F,0F,40F,112F,100F})body.RowStyles.Add(new RowStyle(height==0?SizeType.Percent:SizeType.Absolute,height==0?100:height));
            body.Controls.Add(ModuleHeading("Загрузка комплекта"),0,0);
            var path=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};path.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));path.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,168));path.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var caption=Caption("Папка комплекта",false);path.Controls.Add(caption,0,0);folder.ReadOnly=true;folder.AutoSize=false;folder.Text=libraryRoot??BundlePreferences.Load();folder.Dock=DockStyle.None;folder.Anchor=AnchorStyles.Left|AnchorStyles.Right;folder.Margin=Padding.Empty;
            choose=Button("Выбрать папку",false);choose.Dock=DockStyle.None;choose.Anchor=AnchorStyles.Right;choose.Size=new Size(168,40);choose.Margin=Padding.Empty;choose.Click+=delegate{using(var dialog=new FolderBrowserDialog {SelectedPath=folder.Text})if(dialog.ShowDialog(this)==DialogResult.OK){folder.Text=dialog.SelectedPath;entries.Clear();scannedRoot=null;UpdateState();}};
            path.Controls.Add(folder,1,0);path.Controls.Add(choose,2,0);path.Layout+=delegate{
                using(var graphics=path.CreateGraphics()){
                    int textWidth=TextRenderer.MeasureText(graphics,caption.Text,caption.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width;
                    int gap=(int)Math.Round(12*graphics.DpiX/96F);
                    float wanted=textWidth+gap;
                    if(Math.Abs(path.ColumnStyles[0].Width-wanted)>.5F)path.ColumnStyles[0].Width=wanted;
                    folder.Margin=new Padding(0,0,gap,0);folder.Height=PathFieldMetrics.Measure(folder).Height;
                }
            };body.Controls.Add(path,0,1);
            var groups=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty,Padding=new Padding(0,16,0,16)};groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));groups.RowStyles.Add(new RowStyle(SizeType.Percent,50));groups.RowStyles.Add(new RowStyle(SizeType.Percent,50));int row=0;foreach(var option in new[]{libraries,tools}){option.Dock=DockStyle.Fill;option.Margin=Padding.Empty;option.Palette=Colors;option.CheckedChanged+=delegate{if(!busy){foreach(var entry in entries)entry.Selected=entry.Allowed&&!entry.Done&&GroupSelected(entry.File.Group);UpdateState();}};groups.Controls.Add(option,0,row++);}body.Controls.Add(groups,0,2);
            body.Controls.Add(JournalPanel.Create(journal,"Журнал загрузки комплекта",Colors),0,3);
            summary=Label("Проверка сверяет файлы; установка в Windows не выполняется.",true);summary.Margin=new Padding(0,4,0,4);body.Controls.Add(summary,0,4);total.Margin=new Padding(0,8,0,8);body.Controls.Add(total,0,5);
            var actions=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=2,Margin=Padding.Empty};for(int i=0;i<3;i++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));actions.RowStyles.Add(new RowStyle(SizeType.Percent,50));actions.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            scan=Button("Проверить",false);start=Button("Скачать",true);pause=Button("Пауза",false);stop=Button("Отменить",false);back=Button("В меню",false);results=Button("Результаты",false);results.Click+=delegate{ShowResults();};
            scan.Click+=async delegate{await Scan();};start.Click+=async delegate{await Download();};pause.Click+=delegate{if(!downloading)return;if(paused){paused=false;pause.Text="Пауза";}else{paused=true;pause.Enabled=false;pause.Text="Пауза…";if(cancellation!=null)cancellation.Cancel();}};
            stop.Click+=delegate{if(busy&&BrandMessageBox.ConfirmStop(this,"Остановить загрузку? Готовые файлы и данные для продолжения сохранятся.")==DialogResult.Yes){cancelRequested=true;paused=false;if(cancellation!=null)cancellation.Cancel();}};
            back.Click+=delegate{MenuRequested=true;Close();};int index=0;foreach(var button in new[]{scan,start,results,pause,stop,back}){ModuleNavigation.Configure(button);button.Dock=DockStyle.Fill;button.Margin=new Padding(0,5,index%3==2?0:10,5);actions.Controls.Add(button,index%3,index/3);index++;}body.RowCount=6;body.RowStyles.RemoveAt(6);body.MinimumSize=new Size(0,480);
            var content=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));content.RowStyles.Add(new RowStyle(SizeType.Percent,100));content.RowStyles.Add(new RowStyle(SizeType.Absolute,100));content.Controls.Add(new FillContentPanel(body){Margin=Padding.Empty,PreserveScrollOnFocus=true},0,0);content.Controls.Add(actions,0,1);Root.Controls.Add(content,0,1);
            timer.Tick+=delegate{RefreshProgress();};VisibleChanged+=delegate{timer.Enabled=Visible;};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;if(BrandMessageBox.Show(this,"Остановить загрузку и закрыть окно? Готовые файлы сохранятся.","Загрузка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes){closeAfter=true;cancelRequested=true;paused=false;if(cancellation!=null)cancellation.Cancel();}}};
            SetTheme(Colors.Dark,false);UpdateState();
        }
        bool GroupSelected(string group){return group=="Программы"?false:group=="Утилиты"?tools.Checked:libraries.Checked;}
        static string SizeText(long bytes){return (bytes/1048576.0).ToString("0.0")+" МиБ";}
        void Log(string text){if(IsDisposed)return;if(InvokeRequired){try{BeginInvoke((MethodInvoker)delegate{Log(text);});}catch(InvalidOperationException){}return;}journal.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);}
        void UpdateState(){results.Enabled=!busy&&scannedRoot!=null&&entries.Any(e=>!e.Done);scan.Enabled=choose.Enabled=!busy;start.Enabled=!busy&&entries.Any(e=>e.Selected&&e.Allowed&&!e.Done);pause.Enabled=downloading;stop.Enabled=busy;back.Enabled=!busy;libraries.Enabled=tools.Enabled=!busy;}
        void ShowResults()
        {
            var source=entries.Where(e=>!e.Done).ToArray();if(source.Length==0)return;var rows=source.Select(e=>new BundleDifferenceItem {Path=e.File.Path.TrimStart('/'),State=e.State,Allowed=e.Allowed,Selected=e.Selected}).ToArray();
            using(var dialog=new BundleDifferencesForm(rows))dialog.ShowDialog(this);
            for(int i=0;i<rows.Length;i++)source[i].Selected=rows[i].Selected;UpdateState();
        }
        async Task Scan(BundleCloud? forcedCloud=null)
        {
            if(busy)return;bool suggestAlternative=false;BundleCloud cloud;if(forcedCloud.HasValue)cloud=forcedCloud.Value;else using(var picker=new BundleCloudDialog(true)){if(picker.ShowDialog(this)!=DialogResult.OK)return;cloud=picker.Cloud;}busy=true;cancelRequested=false;paused=false;batch=null;cancellation=new CancellationTokenSource();UpdateState();string root=Path.GetFullPath(folder.Text);Log("Проверяем выбранные разделы: читаем каталог облака и сверяем локальные файлы.");summary.Text="Читаем выбранное облако и проверяем локальные файлы…";total.BeginOperation(summary.Text);
            try
            {
                var token=cancellation.Token;
                bool selectLibraries=libraries.Checked,selectTools=tools.Checked;
                var result=await Task.Run(()=>{
                    var files=cloudSource.Scan(cloud,token,Log);var list=new List<Entry>();total.SetWork(files.Count(f=>f.Group=="Утилиты"?selectTools:f.Group!="Программы"&&selectLibraries));
                    foreach(var file in files.Where(f=>f.Group=="Утилиты"?selectTools:f.Group!="Программы"&&selectLibraries)){token.ThrowIfCancellationRequested();string local=YandexBundleSource.LocalPath(root,file.Path);var entry=new Entry {File=file};
                        if(!File.Exists(local)){entry.Allowed=entry.Selected=true;entry.State="Отсутствует - будет скачано";}
                        else{entry.OriginalHash=YandexBundleSource.Hash(local,token);entry.Done=entry.OriginalHash==file.Sha256;entry.Allowed=!entry.Done&&new BundleReceipts(root).CanReplace(file,entry.OriginalHash);entry.Selected=entry.Allowed;entry.State=entry.Done?"Обновление не требуется":entry.Allowed?"Изменён в источнике — обновить":"Отличается — сохранить локальный файл";}
                        list.Add(entry);total.OperationReporter("Проверен файл: "+file.Path.TrimStart('/'));total.CompleteStep();
                    }return list;
                });
                var saved=BundleSession.Load(root);
                if(saved!=null&&saved.Count>0&&BrandMessageBox.Show(this,"Найдена сохранённая очередь. Восстановить выбор файлов, которые по-прежнему совпадают с актуальным источником?","Продолжение загрузки",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)
                    foreach(var entry in result)entry.Selected=entry.Allowed&&!entry.Done&&saved.Contains(entry.File.Path+"|"+entry.File.Sha256);
                foreach(var entry in result)entry.Selected=entry.Selected&&GroupSelected(entry.File.Group);
                entries.Clear();entries.AddRange(result);scannedRoot=root;
                foreach(var entry in entries.Where(e=>!e.Done))Log(entry.File.Path.TrimStart('/')+": "+entry.State);
                var differences=entries.Where(e=>!e.Done).Select(e=>new[]{e.File.Path.TrimStart('/'),e.State}).ToArray();
                Log("Проверка завершена. Требуют внимания: "+differences.Length+"; актуальных файлов: "+entries.Count(e=>e.Done)+".");
                ShowResults();
                total.FinishOperation("Сверка завершена",success:true);summary.Text="Проверено: "+entries.Count+" файлов. Недостающие и ранее загруженные неизменённые установщики выбраны; остальные локальные файлы защищены.";
                RootAccepted=true;BundlePreferences.Save(root);
            }
            catch(Exception ex){summary.Text=cancellation.IsCancellationRequested?"Проверка отменена":"Не удалось проверить: "+ex.Message;total.Stage(summary.Text,failed:true);Log(summary.Text);suggestAlternative=!cancellation.IsCancellationRequested;}
            finally{busy=false;cancellation.Dispose();cancellation=null;UpdateState();if(closeAfter)Close();}
            if(suggestAlternative&&!closeAfter&&BrandMessageBox.Show(this,"Не удалось проверить выбранное облако. Попробовать альтернативное?","Проверка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)await Scan(cloud==BundleCloud.Google?BundleCloud.Yandex:BundleCloud.Google);
        }
        internal static string CloudName(BundleCloud cloud){return cloud==BundleCloud.Google?"Google Диск":"Яндекс Диск";}
        internal static string AlternativeMessage(BundleCloud cloud,int remaining){return "Не удалось скачать часть файлов из "+CloudName(cloud)+".\r\n\r\nДокачать оставшиеся файлы ("+remaining+") из "+CloudName(cloud==BundleCloud.Google?BundleCloud.Yandex:BundleCloud.Google)+"?\r\n\r\nГотовые файлы сохранятся и не будут скачиваться заново. Недокачанные файлы продолжим с сохранённого места, если облако поддерживает докачку.";}
        async Task Download(BundleCloud? forcedCloud=null, bool alternativeAttempt=false)
        {
            if(busy||scannedRoot==null)return;BundleCloud downloadCloud;if(forcedCloud.HasValue)downloadCloud=forcedCloud.Value;else using(var picker=new BundleCloudDialog(false)){if(picker.ShowDialog(this)!=DialogResult.OK)return;downloadCloud=picker.Cloud;}
            var selected=entries.Where(e=>e.Selected&&e.Allowed&&!e.Done).ToList();if(selected.Count==0)return;
            if(BrandMessageBox.Show(this,"Скачать "+selected.Count+" файлов ("+SizeText(selected.Sum(e=>e.File.Size))+")?\r\nУстановщики не будут запущены.","Загрузка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            Log("Начинаем загрузку из "+CloudName(downloadCloud)+". В очереди: "+selected.Count+" файлов. Уже готовые файлы повторно не скачиваем.");
            busy=true;downloading=true;cancelRequested=false;paused=false;batch=selected;receivedBytes=0;started=DateTime.UtcNow;UpdateState();
            try
            {
                int workers=3;workerLimit=workers;
                cancellation=new CancellationTokenSource();
                if(alternativeAttempt||!cloudSource.Ready(downloadCloud))await Task.Run(()=>cloudSource.Scan(downloadCloud,cancellation.Token,Log,selected.Select(e=>e.File).ToList()));
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
                do
                {
                    if(cancellation!=null)cancellation.Dispose();cancellation=new CancellationTokenSource();var token=cancellation.Token;
                    var queue=new Queue<Entry>(selected.Where(e=>!e.Done));
                    var tasks=Enumerable.Range(0,workers).Select(worker=>Task.Run(()=>{
                        while(true){Entry entry;lock(sync){if(queue.Count==0||token.IsCancellationRequested||worker>=workerLimit)return;entry=queue.Dequeue();entry.State="Скачивание";}
                            try{
                                string destination=YandexBundleSource.LocalPath(scannedRoot,entry.File.Path);
                                bool published=BundlePublication.Matches(scannedRoot,entry.File,token);
                                if(!cloudSource.Contains(downloadCloud,entry.File))throw new IOException("Выбранное облако не содержит подтверждённую копию файла");
                                if(!published)cloudSource.Transfer(downloadCloud).Download(entry.File,scannedRoot,entry.OriginalHash,bytes=>{lock(sync)entry.Bytes=bytes;},token,bytes=>Interlocked.Add(ref receivedBytes,bytes),state=>{lock(sync){entry.State=state;if(state=="Ожидание повтора")workerLimit=Math.Max(1,workerLimit-1);}BundleSession.Log(scannedRoot,entry.File.Path+": "+state);});
                                string warning=BundlePublication.Record(scannedRoot,entry.File,null);
                                lock(sync){entry.Done=true;entry.Bytes=entry.File.Size;entry.Selected=false;entry.State=warning==null?"Готово":warning;}Log("Сохранён файл: "+entry.File.Path.TrimStart('/')+(warning==null?"":". "+warning));
                            }
                            catch(Exception ex){Log("Не удалось скачать файл из "+CloudName(downloadCloud)+": "+entry.File.Path.TrimStart('/')+". "+(token.IsCancellationRequested?"Передача остановлена; полученные данные сохранены.":"Готовые файлы не затронуты. Подробности: "+ex.Message));BundleSession.Log(scannedRoot,entry.File.Path+": "+(token.IsCancellationRequested?"Прервано":ex.Message));lock(sync)entry.State=token.IsCancellationRequested?"Приостановлено / прервано":"Ошибка: "+ex.Message;if(token.IsCancellationRequested)return;}
                        }
                    })).ToArray();
                    await Task.WhenAll(tasks);cancellation.Dispose();cancellation=null;
                    if(!cancelRequested&&paused){pause.Text="Продолжить";pause.Enabled=true;summary.Text="Пауза. Уже полученные данные сохранены.";while(paused&&!closeAfter)await Task.Delay(100);}
                    else break;
                }while(!closeAfter&&!cancelRequested);
                RefreshProgress();BundleSession.Save(scannedRoot,selected.Where(e=>!e.Done).Select(e=>e.File));summary.Text="Готово: "+selected.Count(e=>e.Done)+" из "+selected.Count+". Компоненты Windows не устанавливались. Остальные выбранные файлы можно повторить.";
            }
            catch(Exception ex){summary.Text="Загрузка не завершена: "+ex.Message;total.Stage(summary.Text,failed:true);Log(summary.Text);}
            finally{busy=false;downloading=false;paused=false;pause.Text="Пауза";if(cancellation!=null){cancellation.Dispose();cancellation=null;}UpdateState();if(closeAfter)Close();}
            int remaining=selected.Count(e=>!e.Done);
            if(!cancelRequested&&!closeAfter&&remaining>0)
            {
                Log("Загрузка из "+CloudName(downloadCloud)+" завершена не полностью. Готово: "+selected.Count(e=>e.Done)+"; осталось: "+remaining+". Готовые файлы сохранены.");
                if(!alternativeAttempt)
                {
                    var other=downloadCloud==BundleCloud.Google?BundleCloud.Yandex:BundleCloud.Google;
                    if(BrandMessageBox.Show(this,AlternativeMessage(downloadCloud,remaining),"Докачать из другого облака?",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)
                    {Log("По вашему выбору переключаемся на "+CloudName(other)+". Проверим копии оставшихся файлов и докачаем только их.");await Download(other,true);}
                    else Log("Вы оставили текущий источник. Остаток очереди сохранён; загрузку можно повторить позже.");
                }
                else Log("Резервное облако тоже не смогло выдать все файлы. Очередь сохранена. Проверьте интернет и повторите загрузку позже.");
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
                    if(stage=="Скачивание")stage="Скачивание · готово "+batch.Count(e=>e.Done)+" из "+batch.Count;
                    total.Report(new TransferProgress{Key="Комплект",NetworkBytes=Interlocked.Read(ref receivedBytes),Stage=stage,File=active==null?"":active.File.Path.TrimStart('/'),Bytes=bytes,Total=size,Complete=complete,Paused=paused||cancelRequested,Failed=failed&&!complete});
                }
            }
        }
        protected override void OnThemeChanged(){if(total!=null){total.Palette=Colors;total.Invalidate();}if(libraries!=null)libraries.Palette=Colors;if(tools!=null)tools.Palette=Colors;}
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
}
