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
        readonly BrandButton scan,start,pause,back,choose,stop;
        readonly BrandCheckBox libraries=new BrandCheckBox {Text="Библиотеки и .NET",Checked=true},tools=new BrandCheckBox {Text="Утилиты BARS",Checked=true};
        List<Entry> batch;
        long previousBytes,networkBytes,receivedBytes;
        DateTime sampled;
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
        internal BundleDownloadForm(string libraryRoot=null):base("Загрузка комплекта",new Size(800,610))
        {
            MinimumSize=new Size(760,570);Root.RowStyles[0].Height=76;
            var body=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(float height in new[]{42F,50F,46F,0F,40F,30F,50F})body.RowStyles.Add(new RowStyle(height==0?SizeType.Percent:SizeType.Absolute,height==0?100:height));
            body.Controls.Add(ModuleHeading("Загрузка комплекта"),0,0);
            var path=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};path.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));path.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));path.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            path.Controls.Add(Caption("Папка комплекта",false),0,0);folder.ReadOnly=true;folder.Text=libraryRoot??BundlePreferences.Load();folder.Dock=DockStyle.Fill;folder.Margin=new Padding(0,9,10,9);path.Controls.Add(folder,1,0);
            choose=Button("Выбрать папку",false);ModuleNavigation.Configure(choose);choose.Anchor=AnchorStyles.Right;choose.Click+=delegate{using(var dialog=new FolderBrowserDialog {SelectedPath=folder.Text})if(dialog.ShowDialog(this)==DialogResult.OK){folder.Text=dialog.SelectedPath;entries.Clear();scannedRoot=null;UpdateState();}};path.Controls.Add(choose,2,0);body.Controls.Add(path,0,1);
            var groups=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));groups.RowStyles.Add(new RowStyle(SizeType.Percent,100));int column=0;foreach(var option in new[]{libraries,tools}){option.Dock=DockStyle.Fill;option.Palette=Colors;option.CheckedChanged+=delegate{if(!busy){foreach(var entry in entries)entry.Selected=entry.Allowed&&!entry.Done&&GroupSelected(entry.File.Group);UpdateState();}};groups.Controls.Add(option,column++,0);}body.Controls.Add(groups,0,2);
            body.Controls.Add(JournalPanel.Create(journal,"Журнал загрузки комплекта",Colors),0,3);
            summary=Label("Проверка сверяет файлы; установка в Windows не выполняется.",true);body.Controls.Add(summary,0,4);total.Margin=new Padding(0,2,0,8);body.Controls.Add(total,0,5);
            var actions=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=5,RowCount=1,Margin=Padding.Empty};for(int i=0;i<5;i++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,20));actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            scan=Button("Проверить",false);start=Button("Скачать",true);pause=Button("Пауза",false);stop=Button("Отменить",false);back=Button("В меню",false);
            scan.Click+=async delegate{await Scan();};start.Click+=async delegate{await Download();};pause.Click+=delegate{if(!downloading)return;if(paused){paused=false;pause.Text="Пауза";}else{paused=true;pause.Enabled=false;pause.Text="Пауза…";if(cancellation!=null)cancellation.Cancel();}};
            stop.Click+=delegate{if(busy&&BrandMessageBox.Show(this,"Остановить загрузку? Готовые файлы и данные для продолжения сохранятся.","Загрузка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes){cancelRequested=true;paused=false;if(cancellation!=null)cancellation.Cancel();}};
            back.Click+=delegate{MenuRequested=true;Close();};int index=0;foreach(var button in new[]{scan,start,pause,stop,back}){ModuleNavigation.Configure(button);button.Dock=DockStyle.Fill;button.Margin=new Padding(0,5,index==4?0:10,5);actions.Controls.Add(button,index++,0);}body.Controls.Add(actions,0,6);Root.Controls.Add(body,0,1);
            timer.Tick+=delegate{RefreshProgress();};VisibleChanged+=delegate{timer.Enabled=Visible;};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;if(BrandMessageBox.Show(this,"Остановить загрузку и закрыть окно? Готовые файлы сохранятся.","Загрузка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes){closeAfter=true;cancelRequested=true;paused=false;if(cancellation!=null)cancellation.Cancel();}}};
            SetTheme(Colors.Dark,false);UpdateState();
        }
        bool GroupSelected(string group){return group=="Программы"?false:group=="Утилиты"?tools.Checked:libraries.Checked;}
        static string SizeText(long bytes){return (bytes/1048576.0).ToString("0.0")+" МиБ";}
        void Log(string text){if(IsDisposed)return;if(InvokeRequired){try{BeginInvoke((MethodInvoker)delegate{Log(text);});}catch(InvalidOperationException){}return;}journal.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);}
        void UpdateState(){scan.Enabled=choose.Enabled=!busy;start.Enabled=!busy&&entries.Any(e=>e.Selected&&e.Allowed&&!e.Done);pause.Enabled=downloading;stop.Enabled=busy;back.Enabled=!busy;libraries.Enabled=tools.Enabled=!busy;}
        async Task Scan()
        {
            if(busy)return;busy=true;cancelRequested=false;paused=false;batch=null;cancellation=new CancellationTokenSource();UpdateState();string root=Path.GetFullPath(folder.Text);Log("Проверяем выбранные разделы: читаем каталог облака и сверяем локальные файлы.");summary.Text="Читаем каталоги Яндекс Диска и проверяем локальные файлы…";
            try
            {
                var token=cancellation.Token;
                var result=await Task.Run(()=>{
                    var files=new YandexBundleSource().Scan(token);var list=new List<Entry>();
                    foreach(var file in files.Where(f=>GroupSelected(f.Group))){token.ThrowIfCancellationRequested();string local=YandexBundleSource.LocalPath(root,file.Path);var entry=new Entry {File=file};
                        if(!File.Exists(local)){entry.Allowed=entry.Selected=true;entry.State="Отсутствует — скачать";}
                        else{entry.OriginalHash=YandexBundleSource.Hash(local,token);entry.Done=entry.OriginalHash==file.Sha256;entry.Allowed=!entry.Done&&new BundleReceipts(root).CanReplace(file,entry.OriginalHash);entry.Selected=entry.Allowed;entry.State=entry.Done?"Обновление не требуется":entry.Allowed?"Изменён в источнике — обновить":"Отличается — сохранить локальный файл";}
                        list.Add(entry);
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
                if(differences.Length>0)using(var dialog=new BundleDifferencesForm(differences))dialog.ShowDialog(this);
                summary.Text="Проверено: "+entries.Count+" файлов. Недостающие и ранее загруженные неизменённые установщики выбраны; остальные локальные файлы защищены.";
                RootAccepted=true;BundlePreferences.Save(root);
            }
            catch(Exception ex){summary.Text=cancellation.IsCancellationRequested?"Проверка отменена":"Не удалось проверить: "+ex.Message;Log(summary.Text);}
            finally{busy=false;cancellation.Dispose();cancellation=null;UpdateState();if(closeAfter)Close();}
        }
        async Task Download()
        {
            if(busy||scannedRoot==null)return;
            var selected=entries.Where(e=>e.Selected&&e.Allowed&&!e.Done).ToList();if(selected.Count==0)return;
            if(BrandMessageBox.Show(this,"Скачать "+selected.Count+" файлов ("+SizeText(selected.Sum(e=>e.File.Size))+")?\r\nУстановщики не будут запущены.","Загрузка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            busy=true;downloading=true;cancelRequested=false;paused=false;batch=selected;previousBytes=0;receivedBytes=0;networkBytes=0;sampled=DateTime.UtcNow;started=DateTime.UtcNow;UpdateState();
            try
            {
                int workers=3;workerLimit=workers;
                Directory.CreateDirectory(scannedRoot);
                BundleSession.Save(scannedRoot,selected.Select(e=>e.File));BundleSession.Log(scannedRoot,"Начало очереди: "+selected.Count+" файлов");
                string probe=YandexBundleSource.LocalPath(scannedRoot,"/.bars-download/write-"+Guid.NewGuid().ToString("N")+".tmp");
                Directory.CreateDirectory(Path.GetDirectoryName(probe));
                using(var output=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose)){output.WriteByte(0);}
                var drive=new DriveInfo(Path.GetPathRoot(scannedRoot));
                long required=selected.Sum(e=>Math.Max(0,e.File.Size-(File.Exists(BundleTransfer.PartPath(scannedRoot,e.File))?new FileInfo(BundleTransfer.PartPath(scannedRoot,e.File)).Length:0)));
                if(drive.IsReady&&drive.AvailableFreeSpace<required+16L*1024*1024)throw new IOException("Недостаточно свободного места для выбранных файлов и безопасной загрузки.");
                do
                {
                    cancellation=new CancellationTokenSource();var token=cancellation.Token;
                    var queue=new Queue<Entry>(selected.Where(e=>!e.Done));
                    var tasks=Enumerable.Range(0,workers).Select(worker=>Task.Run(()=>{
                        while(true){Entry entry;lock(sync){if(queue.Count==0||token.IsCancellationRequested||worker>=workerLimit)return;entry=queue.Dequeue();entry.State="Скачивание";}
                            try{
                                string destination=YandexBundleSource.LocalPath(scannedRoot,entry.File.Path);
                                bool published=BundlePublication.Matches(scannedRoot,entry.File,token);
                                if(!published)new BundleTransfer(new YandexBundleSource()).Download(entry.File,scannedRoot,entry.OriginalHash,bytes=>{lock(sync)entry.Bytes=bytes;},token,bytes=>Interlocked.Add(ref receivedBytes,bytes),state=>{lock(sync){entry.State=state;if(state=="Ожидание повтора")workerLimit=Math.Max(1,workerLimit-1);}BundleSession.Log(scannedRoot,entry.File.Path+": "+state);});
                                string warning=BundlePublication.Record(scannedRoot,entry.File,null);
                                lock(sync){entry.Done=true;entry.Bytes=entry.File.Size;entry.Selected=false;entry.State=warning==null?"Готово":warning;}Log("Сохранён файл: "+entry.File.Path.TrimStart('/')+(warning==null?"":". "+warning));
                            }
                            catch(Exception ex){Log("Не удалось загрузить "+entry.File.Path+": "+ex.Message);BundleSession.Log(scannedRoot,entry.File.Path+": "+(token.IsCancellationRequested?"Прервано":ex.Message));lock(sync)entry.State=token.IsCancellationRequested?"Приостановлено / прервано":"Ошибка: "+ex.Message;if(token.IsCancellationRequested)return;}
                        }
                    })).ToArray();
                    await Task.WhenAll(tasks);cancellation.Dispose();cancellation=null;
                    if(!cancelRequested&&paused){pause.Text="Продолжить";pause.Enabled=true;summary.Text="Пауза. Уже полученные данные сохранены.";while(paused&&!closeAfter)await Task.Delay(100);}
                    else break;
                }while(!closeAfter&&!cancelRequested);
                RefreshProgress();BundleSession.Save(scannedRoot,selected.Where(e=>!e.Done).Select(e=>e.File));summary.Text="Готово: "+selected.Count(e=>e.Done)+" из "+selected.Count+". Компоненты Windows не устанавливались. Остальные выбранные файлы можно повторить.";
            }
            catch(Exception ex){summary.Text="Загрузка не завершена: "+ex.Message;Log(summary.Text);}
            finally{busy=false;downloading=false;paused=false;pause.Text="Пауза";if(cancellation!=null){cancellation.Dispose();cancellation=null;}UpdateState();if(closeAfter)Close();}
        }
        void RefreshProgress()
        {
            if(IsDisposed)return;
            lock(sync)
            {
                long size=(batch??entries.Where(e=>e.Selected).ToList()).Sum(e=>e.File.Size),bytes=(batch??entries.Where(e=>e.Selected).ToList()).Sum(e=>e.Bytes);
                total.Fraction=size<=0?0:bytes/(double)size;
                if(downloading&&!paused&&cancellation!=null){double interval=(DateTime.UtcNow-sampled).TotalSeconds;if(interval>=1){networkBytes=(long)(Math.Max(0,Interlocked.Read(ref receivedBytes)-previousBytes)/interval);previousBytes=Interlocked.Read(ref receivedBytes);sampled=DateTime.UtcNow;}double speed=networkBytes;summary.Text="Получено "+SizeText(bytes)+" / "+SizeText(size)+" · "+(size==0?0:bytes*100.0/size).ToString("0.0")+"% · "+SizeText((long)speed)+"/с"+(speed>0?" · осталось примерно "+TimeSpan.FromSeconds(Math.Min(864000,(size-bytes)/speed)).ToString(@"hh\:mm\:ss"):"");}
            }
        }
        protected override void OnThemeChanged(){if(total!=null){total.Palette=Colors;total.Invalidate();}if(libraries!=null)libraries.Palette=Colors;if(tools!=null)tools.Palette=Colors;}
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
}
