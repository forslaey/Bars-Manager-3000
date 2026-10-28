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
        readonly ServiceGrid grid=new ServiceGrid();
        readonly TextBox folder=new TextBox();
        readonly Label summary;
        readonly ProgressBar total=new ProgressBar {Dock=DockStyle.Fill,Maximum=1000};
        readonly BrandButton scan,start,pause,back,choose,stop;
        readonly ProgressBar current=new ProgressBar {Dock=DockStyle.Fill,Maximum=1000};
        readonly Label activeLabel;
        readonly BrandCheckBox libraries=new BrandCheckBox {Text="Библиотеки и .NET",Checked=true},software=new BrandCheckBox {Text="Программы",Checked=true},tools=new BrandCheckBox {Text="Утилиты",Checked=true};
        List<Entry> batch;
        long previousBytes,networkBytes;
        DateTime sampled;
        bool cancelRequested;
        readonly NumericUpDown parallel=new NumericUpDown {Minimum=1,Maximum=4,Value=3,Width=50};
        readonly List<Entry> entries=new List<Entry>();
        readonly object sync=new object();
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=250};
        CancellationTokenSource cancellation;bool busy,paused,closeAfter,downloading;
        string scannedRoot;
        DateTime started;
        internal string LibraryRoot {get{return folder.Text;}}
        internal static string Offer(IWin32Window owner,string root)
        {
            if(BrandMessageBox.Show(owner,"Папка Библиотеки не найдена. Открыть загрузку комплекта с Яндекс Диска? Проверка и скачивание запускаются отдельно.","Комплект библиотек",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return root;
            using(var dialog=new BundleDownloadForm(root)){dialog.ShowDialog(owner);return Directory.Exists(dialog.LibraryRoot)?dialog.LibraryRoot:root;}
        }
        internal BundleDownloadForm(string libraryRoot=null):base("Загрузка и обновление комплекта",new Size(1120,780))
        {
            MinimumSize=new Size(940,650);
            var body=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(float height in new[]{44F,48F,40F,0F,52F,46F,24F,52F})body.RowStyles.Add(new RowStyle(height==0?SizeType.Percent:SizeType.Absolute,height==0?100:height));
            body.Controls.Add(ModuleHeading("Загрузка и обновление комплекта"),0,0);
            var pathRow=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170));
            pathRow.Controls.Add(Label("Папка комплекта:",false),0,0);folder.Dock=DockStyle.Fill;folder.ReadOnly=true;folder.Text=libraryRoot??Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Библиотеки");pathRow.Controls.Add(folder,1,0);
            choose=Button("Выбрать папку…",false);choose.Click+=delegate {using(var dialog=new FolderBrowserDialog {SelectedPath=folder.Text})if(dialog.ShowDialog(this)==DialogResult.OK){folder.Text=dialog.SelectedPath;entries.Clear();grid.Rows.Clear();scannedRoot=null;UpdateState();}};pathRow.Controls.Add(choose,2,0);body.Controls.Add(pathRow,0,1);
            grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=grid.AllowUserToResizeRows=false;grid.RowHeadersVisible=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.MultiSelect=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.TouchScrollingEnabled=true;grid.EditMode=DataGridViewEditMode.EditProgrammatically;
            grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="Selected",HeaderText="",Width=40,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});grid.Columns.Add("Group","Раздел");grid.Columns.Add("File","Файл");grid.Columns.Add("Size","Размер");grid.Columns.Add("State","Состояние");grid.Columns[2].FillWeight=300;
            foreach(DataGridViewColumn column in grid.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;
            grid.CellMouseDown+=delegate(object sender,DataGridViewCellMouseEventArgs e){if(!busy&&e.Button==MouseButtons.Left&&e.RowIndex>=0&&entries[e.RowIndex].Allowed&&!entries[e.RowIndex].Done){var entry=entries[e.RowIndex];entry.Selected=!entry.Selected;grid.Rows[e.RowIndex].Cells[0].Value=entry.Selected;UpdateState();}};
            grid.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Space&&!busy&&grid.CurrentRow!=null){var entry=entries[grid.CurrentRow.Index];if(entry.Allowed&&!entry.Done){entry.Selected=!entry.Selected;grid.CurrentRow.Cells[0].Value=entry.Selected;UpdateState();}e.Handled=e.SuppressKeyPress=true;}};
            var groups=new FlowLayoutPanel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            foreach(var option in new[]{libraries,software,tools}){option.Width=230;option.Height=36;option.Palette=Colors;option.CheckedChanged+=delegate{if(!busy){foreach(var entry in entries){entry.Selected=entry.Allowed&&!entry.Done&&GroupSelected(entry.File.Group);}RefreshProgress();UpdateState();}};groups.Controls.Add(option);}body.Controls.Add(groups,0,2);
            body.Controls.Add(grid,0,3);summary=Label("Источник: публичная папка Яндекс Диска. Проверка не скачивает установщики.",true);body.Controls.Add(summary,0,4);
            var active=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};active.RowStyles.Add(new RowStyle(SizeType.Percent,50));active.RowStyles.Add(new RowStyle(SizeType.Percent,50));activeLabel=Label("Активные файлы появятся здесь",true);active.Controls.Add(activeLabel,0,0);active.Controls.Add(current,0,1);body.Controls.Add(active,0,5);body.Controls.Add(total,0,6);
            var actions=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=6,RowCount=1,Margin=Padding.Empty};for(int i=0;i<6;i++)actions.ColumnStyles.Add(new ColumnStyle(i==4?SizeType.Absolute:SizeType.Percent,i==4?60:20));
            scan=Button("Проверить комплект",false);start=Button("Скачать выбранное",true);pause=Button("Пауза",false);back=Button("Назад",false);stop=Button("Отменить",false);
            scan.Click+=async delegate{await Scan();};start.Click+=async delegate{await Download();};pause.Click+=delegate{if(!downloading)return;if(paused){paused=false;pause.Text="Пауза";}else{paused=true;pause.Enabled=false;pause.Text="Приостановка…";if(cancellation!=null)cancellation.Cancel();}};stop.Click+=delegate{if(busy&&BrandMessageBox.Show(this,"Прервать загрузку? Проверенные файлы и данные для продолжения сохранятся.","Отмена",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes){cancelRequested=true;paused=false;if(cancellation!=null)cancellation.Cancel();}};
            back.Click+=delegate{Close();};parallel.AccessibleName="Одновременных загрузок (1–4)";
            actions.Controls.Add(scan,0,0);actions.Controls.Add(start,1,0);actions.Controls.Add(pause,2,0);actions.Controls.Add(stop,3,0);actions.Controls.Add(parallel,4,0);actions.Controls.Add(back,5,0);body.Controls.Add(actions,0,7);Root.Controls.Add(body,0,1);
            timer.Tick+=delegate{RefreshProgress();};timer.Start();FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){e.Cancel=true;if(BrandMessageBox.Show(this,"Остановить загрузку и закрыть окно? Готовые файлы сохранятся.","Загрузка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes){closeAfter=true;cancelRequested=true;paused=false;if(cancellation!=null)cancellation.Cancel();}}};
            SetTheme(Colors.Dark,false);UpdateState();
        }
        bool GroupSelected(string group){return group=="Программы"?software.Checked:group=="Утилиты"?tools.Checked:libraries.Checked;}
        static string SizeText(long bytes){return (bytes/1048576.0).ToString("0.0")+" МиБ";}
        void UpdateState(){scan.Enabled=choose.Enabled=parallel.Enabled=!busy;start.Enabled=!busy&&entries.Any(e=>e.Selected&&e.Allowed&&!e.Done);pause.Enabled=downloading;stop.Enabled=busy;back.Enabled=!busy;foreach(var option in new[]{libraries,software,tools})option.Enabled=!busy;}
        async Task Scan()
        {
            if(busy)return;busy=true;cancelRequested=false;paused=false;batch=null;cancellation=new CancellationTokenSource();UpdateState();string root=Path.GetFullPath(folder.Text);summary.Text="Читаем каталоги Яндекс Диска и проверяем локальные файлы…";
            try
            {
                var token=cancellation.Token;
                var result=await Task.Run(()=>{
                    var files=new YandexBundleSource().Scan(token);var list=new List<Entry>();
                    foreach(var file in files){token.ThrowIfCancellationRequested();string local=YandexBundleSource.LocalPath(root,file.Path);var entry=new Entry {File=file};
                        if(!File.Exists(local)){entry.Allowed=entry.Selected=true;entry.State="Отсутствует — скачать";}
                        else{entry.OriginalHash=YandexBundleSource.Hash(local,token);entry.Done=entry.OriginalHash==file.Sha256;entry.Allowed=!entry.Done&&new BundleReceipts(root).CanReplace(file,entry.OriginalHash);entry.Selected=entry.Allowed;entry.State=entry.Done?"Совпадает":entry.Allowed?"Изменён в источнике — обновить":"Отличается — сохранить локальный файл";}
                        list.Add(entry);
                    }return list;
                });
                foreach(var entry in result)entry.Selected=entry.Selected&&GroupSelected(entry.File.Group);
                entries.Clear();entries.AddRange(result);scannedRoot=root;grid.Rows.Clear();foreach(var entry in entries)grid.Rows.Add(entry.Selected,entry.File.Group,entry.File.Path.TrimStart('/'),SizeText(entry.File.Size),entry.State);
                summary.Text="Проверено: "+entries.Count+" файлов. Недостающие и ранее загруженные неизменённые установщики выбраны; остальные локальные файлы защищены.";
            }
            catch(Exception ex){summary.Text=cancellation.IsCancellationRequested?"Проверка отменена":"Не удалось проверить: "+ex.Message;}
            finally{busy=false;cancellation.Dispose();cancellation=null;UpdateState();if(closeAfter)Close();}
        }
        async Task Download()
        {
            if(busy||scannedRoot==null)return;
            var selected=entries.Where(e=>e.Selected&&e.Allowed&&!e.Done).ToList();if(selected.Count==0)return;
            if(BrandMessageBox.Show(this,"Скачать "+selected.Count+" файлов ("+SizeText(selected.Sum(e=>e.File.Size))+")?\r\nУстановщики не будут запущены.","Загрузка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            busy=true;downloading=true;cancelRequested=false;paused=false;batch=selected;previousBytes=selected.Sum(e=>e.Bytes);networkBytes=0;sampled=DateTime.UtcNow;started=DateTime.UtcNow;UpdateState();
            try
            {
                int workers=(int)parallel.Value;
                Directory.CreateDirectory(scannedRoot);
                string probe=YandexBundleSource.LocalPath(scannedRoot,"/.bars-download/write-"+Guid.NewGuid().ToString("N")+".tmp");
                Directory.CreateDirectory(Path.GetDirectoryName(probe));
                using(var output=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose)){output.WriteByte(0);}
                var drive=new DriveInfo(Path.GetPathRoot(scannedRoot));
                if(drive.IsReady&&drive.AvailableFreeSpace<selected.Sum(e=>e.File.Size)+16L*1024*1024)throw new IOException("Недостаточно свободного места для выбранных файлов и безопасной загрузки.");
                do
                {
                    cancellation=new CancellationTokenSource();var token=cancellation.Token;
                    var queue=new Queue<Entry>(selected.Where(e=>!e.Done));
                    var tasks=Enumerable.Range(0,workers).Select(worker=>Task.Run(()=>{
                        while(true){Entry entry;lock(sync){if(queue.Count==0||token.IsCancellationRequested)return;entry=queue.Dequeue();entry.State="Скачивание";}
                            try{new BundleTransfer(new YandexBundleSource()).Download(entry.File,scannedRoot,entry.OriginalHash,bytes=>{lock(sync)entry.Bytes=bytes;},token);new BundleReceipts(scannedRoot).Record(entry.File);lock(sync){entry.Done=true;entry.Selected=false;entry.State="Готово";}}
                            catch(Exception ex){lock(sync)entry.State=token.IsCancellationRequested?"Приостановлено / прервано":"Ошибка: "+ex.Message;if(token.IsCancellationRequested)return;}
                        }
                    })).ToArray();
                    await Task.WhenAll(tasks);cancellation.Dispose();cancellation=null;
                    if(!cancelRequested&&paused){pause.Text="Продолжить";pause.Enabled=true;summary.Text="Пауза. Уже полученные данные сохранены.";while(paused&&!closeAfter)await Task.Delay(100);}
                    else break;
                }while(!closeAfter&&!cancelRequested);
                RefreshProgress();summary.Text="Готово: "+selected.Count(e=>e.Done)+" из "+selected.Count+". Компоненты Windows не устанавливались. Остальные выбранные файлы можно повторить.";
            }
            catch(Exception ex){summary.Text="Загрузка не завершена: "+ex.Message;}
            finally{busy=false;downloading=false;paused=false;pause.Text="Пауза";if(cancellation!=null){cancellation.Dispose();cancellation=null;}UpdateState();if(closeAfter)Close();}
        }
        void RefreshProgress()
        {
            if(IsDisposed)return;
            lock(sync)
            {
                long size=(batch??entries.Where(e=>e.Selected).ToList()).Sum(e=>e.File.Size),bytes=(batch??entries.Where(e=>e.Selected).ToList()).Sum(e=>e.Bytes);
                total.Value=size<=0?0:(int)Math.Min(1000,bytes*1000.0/size);
                for(int i=0;i<entries.Count&&i<grid.Rows.Count;i++){var entry=entries[i];grid.Rows[i].Cells[0].Value=entry.Selected;grid.Rows[i].Cells[4].Value=entry.State+(entry.State=="Скачивание"?" · "+(entry.File.Size==0?100:Math.Min(100,entry.Bytes*100.0/entry.File.Size)).ToString("0")+"%":"");}
                var active=entries.Where(e=>e.State=="Скачивание").ToList();
                long activeSize=active.Sum(e=>e.File.Size),activeBytes=active.Sum(e=>e.Bytes);
                current.Value=activeSize<=0?0:(int)Math.Min(1000,activeBytes*1000.0/activeSize);
                activeLabel.Text=active.Count==0?"Нет активных загрузок":String.Join(" | ",active.Select(e=>System.IO.Path.GetFileName(e.File.Path)+" "+(e.File.Size==0?100:e.Bytes*100.0/e.File.Size).ToString("0")+"%"));
                if(downloading&&!paused&&cancellation!=null){double interval=(DateTime.UtcNow-sampled).TotalSeconds;if(interval>=1){networkBytes=(long)(Math.Max(0,bytes-previousBytes)/interval);previousBytes=bytes;sampled=DateTime.UtcNow;}double speed=networkBytes;summary.Text="Получено "+SizeText(bytes)+" / "+SizeText(size)+" · "+(size==0?0:bytes*100.0/size).ToString("0.0")+"% · "+SizeText((long)speed)+"/с"+(speed>0?" · осталось примерно "+TimeSpan.FromSeconds(Math.Min(864000,(size-bytes)/speed)).ToString(@"hh\:mm\:ss"):"");}
            }
        }
        protected override void OnThemeChanged(){if(grid==null)return;grid.BackgroundColor=Colors.Surface;grid.DefaultCellStyle.BackColor=Colors.Surface;grid.DefaultCellStyle.ForeColor=Colors.Text;grid.DefaultCellStyle.SelectionBackColor=Colors.Selection;grid.DefaultCellStyle.SelectionForeColor=Colors.SelectionText;grid.GridColor=Colors.Border;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.ForeColor=Colors.Text;}
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
}
