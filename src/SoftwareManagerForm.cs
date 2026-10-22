using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class SoftwareManagerForm : ManagerModuleForm
    {
        private readonly ServiceGrid grid=new ServiceGrid();
        private readonly HashSet<string> selection=new HashSet<string>(), selectable=new HashSet<string>();
        private readonly Font headerFont=new Font("Segoe UI",10.5F,FontStyle.Bold);
        private readonly Dictionary<string,SoftwarePackage> packages=new Dictionary<string,SoftwarePackage>(), offline=new Dictionary<string,SoftwarePackage>();
        private readonly Dictionary<string,SoftwareCheck> checks=new Dictionary<string,SoftwareCheck>();
        private readonly BrandCheckBox defaults=new BrandCheckBox(), remote=new BrandCheckBox();
        private readonly BrandTextBox sourceLabel;
        private readonly Label activity;
        private readonly BrandButton refresh,install,cancel,folderButton,back,associations,remove,access;
        private readonly JournalTextBox journal=new JournalTextBox();
        private readonly bool preview;
        private readonly Func<string,SoftwarePackage> latestSource;
        private string source;
        private bool busy,checking,closeAfter,applyingDefaults;
        private volatile bool cancelled;
        private LibrarySession session;
        internal bool MenuRequested {get;private set;}
        internal SoftwareManagerForm(bool previewMode) : this(previewMode,SoftwareSources.Latest) {}
        internal SoftwareManagerForm(bool previewMode,Func<string,SoftwarePackage> versions) : base("Установка программ BARS",new Size(1120,920))
        {
            if(versions==null)throw new ArgumentNullException("versions");latestSource=versions;
            preview=previewMode; MinimumSize=new Size(1080,860); Root.AutoScroll=true;
            var body=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Margin=Padding.Empty,MinimumSize=new Size(0,788)}; body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(float height in new[]{134F,326F,108F,100F,0F,52F}) body.RowStyles.Add(new RowStyle(height==0 ? SizeType.Percent : SizeType.Absolute,height==0 ? 100 : height));
            var heading=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=new Padding(0,0,0,10)};heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.RowStyles.Add(new RowStyle(SizeType.Absolute,38));heading.RowStyles.Add(new RowStyle(SizeType.Absolute,20));heading.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var title=Label("Программы для рабочего места пользователя",false);title.Font=new Font("Segoe UI",15,FontStyle.Bold);heading.Controls.Add(title,0,0);
            var folderGroup=new BufferedLayoutPanel {Dock=DockStyle.Right,Width=520,ColumnCount=1,RowCount=2,Margin=Padding.Empty};folderGroup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));folderGroup.RowStyles.Add(new RowStyle(SizeType.Absolute,24));folderGroup.RowStyles.Add(new RowStyle(SizeType.Percent,100));folderGroup.Controls.Add(Label("Папка установщиков:",true),0,0);
            sourceLabel=new BrandTextBox {Text="Папка не выбрана",ReadOnly=true,AutoSize=false,Palette=Colors};
            folderButton=Button("Выбрать папку...",false);folderButton.Click+=async delegate {if(!busy&&ChooseSource())await RefreshPackages();};
            var sourceRow=new PathPickerRow(sourceLabel,folderButton,176){Dock=DockStyle.Fill};folderGroup.Controls.Add(sourceRow,0,1);
            folderGroup.Layout+=delegate{int width=Math.Min(heading.ClientSize.Width,sourceRow.PreferredRowWidth);if(folderGroup.Width!=width)folderGroup.Width=width;};
            heading.Controls.Add(folderGroup,0,2);body.Controls.Add(heading,0,0);
            var tableCard=new Card {Dock=DockStyle.Fill,Padding=new Padding(12,10,12,12),Margin=Padding.Empty};
            ConfigureGrid();tableCard.Controls.Add(grid);body.Controls.Add(tableCard,0,1);
            bool fitting=false;
            Action fitTable=delegate {
                if(fitting||grid.RowCount==0)return;fitting=true;
                try{int required=grid.ColumnHeadersHeight+grid.Rows.Cast<DataGridViewRow>().Sum(r=>r.Height)+tableCard.Padding.Vertical+2;if(Math.Abs(body.RowStyles[1].Height-required)>0.5F)body.RowStyles[1].Height=required;}
                finally{fitting=false;}
            };
            grid.RowHeightChanged+=delegate{fitTable();};grid.RowsAdded+=delegate{fitTable();};grid.ColumnWidthChanged+=delegate{fitTable();};tableCard.SizeChanged+=delegate{fitTable();};
            var options=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(4,0,0,0),Padding=new Padding(0,16,0,16)};options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(var item in new[]{defaults,remote}) {options.RowStyles.Add(new RowStyle(SizeType.Percent,50F));item.Dock=DockStyle.Fill;item.Margin=Padding.Empty;item.Palette=Colors;}
            defaults.Text="Применить ассоциации Notepad++ и 7-Zip для всех пользователей при входе";defaults.Checked=true;
            remote.Text="Настроить неконтролируемый доступ AnyDesk / RuDesktop — пароль перед установкой";remote.Checked=true;
            options.Controls.Add(defaults,0,0);options.Controls.Add(remote,0,1);body.Controls.Add(options,0,2);
            var actions=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=2,Margin=new Padding(0,0,0,10)};for(int i=0;i<3;i++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));actions.RowStyles.Add(new RowStyle(SizeType.Percent,50));actions.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            install=Button("Установить",true);install.Click+=async delegate{await Install();};remove=Button("Удалить",false);remove.Destructive=true;remove.Click+=async delegate{await RemoveSelected();};access=Button("Настроить доступ",false);access.Margin=Padding.Empty;access.Click+=async delegate{await ConfigureSelected();};
            refresh=Button("Проверить версии",false);refresh.Click+=async delegate{await RefreshPackages(true);};associations=Button("Ассоциации файлов",false);associations.Click+=async delegate{await ApplyDefaults();};
            cancel=Button("Отменить",false);cancel.Margin=Padding.Empty;cancel.Enabled=false;cancel.Click+=delegate{if(busy&&!applyingDefaults&&MessageBox.Show(this,LibraryManagerForm.CancelWarning,"Отмена",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes)RequestCancel(false);};
            foreach(var button in new[]{install,remove,access})button.Margin=new Padding(button.Margin.Left,0,button.Margin.Right,6);
            actions.Controls.Add(install,0,0);actions.Controls.Add(remove,1,0);actions.Controls.Add(access,2,0);actions.Controls.Add(refresh,0,1);actions.Controls.Add(associations,1,1);actions.Controls.Add(cancel,2,1);body.Controls.Add(actions,0,3);
            var logCard=new Card {Dock=DockStyle.Fill,Padding=new Padding(12,8,12,8),Margin=Padding.Empty};journal.Dock=DockStyle.Fill;journal.Font=new Font("Consolas",9);var scroll=new JournalScrollBar {Dock=DockStyle.Right,Width=14};scroll.Bind(journal);logCard.Controls.Add(journal);logCard.Controls.Add(scroll);body.Controls.Add(logCard,0,4);
            var footer=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=new Padding(0,10,0,0)};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            activity=Label("Установка начнётся только по кнопке",true);activity.Font=new Font("Segoe UI",9);footer.Controls.Add(activity,0,0);

            back=Button("В меню",false);back.Margin=Padding.Empty;back.Click+=delegate{MenuRequested=true;Close();};footer.Controls.Add(back,1,0);body.Controls.Add(footer,0,5);Root.Controls.Add(body,0,1);
            try{if(!preview)source=SoftwareCatalog.FindFolder();}catch(Exception ex){Log(ex.Message);}
            SetTheme(Colors.Dark,false);
            UpdateRows();
            Shown+=async delegate {if(preview)FillPreview();else await RefreshPackages();};
            FormClosing+=delegate(object sender,FormClosingEventArgs e)
            {
                if(!busy)return;e.Cancel=true;
                if(applyingDefaults){MenuRequested=false;MessageBox.Show(this,"Дождись завершения применения системного профиля",Text);return;}
                if(cancelled){closeAfter=true;return;}
                if(checking || MessageBox.Show(this,LibraryManagerForm.CancelWarning+"\r\n\r\nЗакрыть менеджер после отмены?","Завершение работы",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes)RequestCancel(true);else MenuRequested=false;
            };
        }
        private void ConfigureGrid()
        {
            grid.Dock=DockStyle.Fill;grid.Margin=Padding.Empty;grid.BorderStyle=BorderStyle.None;
            grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=grid.AllowUserToResizeRows=grid.AllowUserToResizeColumns=grid.AllowUserToOrderColumns=false;
            grid.ReadOnly=true;grid.RowHeadersVisible=false;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
            grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersHeight=56;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.Font=headerFont;grid.ColumnHeadersDefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Padding=new Padding(6);grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleLeft;
            grid.CellBorderStyle=DataGridViewCellBorderStyle.Single;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.Single;grid.ScrollBars=ScrollBars.None;
            grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="Selected",HeaderText="",Width=46,MinimumWidth=46,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});
            string[] names={"Программа","В системе","Доступно / источник"};int[] weights={24,20,56};
            for(int i=0;i<names.Length;i++){int column=grid.Columns.Add("c"+i,names[i]);grid.Columns[column].FillWeight=weights[i];grid.Columns[column].MinimumWidth=130;}
            foreach(DataGridViewColumn column in grid.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;
            grid.EditMode=DataGridViewEditMode.EditProgrammatically;
            grid.CellMouseDown+=delegate(object sender,DataGridViewCellMouseEventArgs e){if(e.Button==MouseButtons.Left&&e.ColumnIndex==0)ToggleSelection(e.RowIndex);};
            grid.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Space&&!e.Alt&&!e.Control&&grid.CurrentRow!=null){ToggleSelection(grid.CurrentRow.Index);e.Handled=e.SuppressKeyPress=true;}};
            grid.CellMouseEnter+=delegate(object sender,DataGridViewCellEventArgs e){grid.Cursor=e.RowIndex>=0&&e.ColumnIndex==0?Cursors.Hand:Cursors.Default;};
            grid.CellPainting+=delegate(object sender,DataGridViewCellPaintingEventArgs e)
            {
                if(e.RowIndex<0||e.ColumnIndex!=0)return;
                e.Paint(e.CellBounds,DataGridViewPaintParts.Background|DataGridViewPaintParts.SelectionBackground);var saved=e.Graphics.Save();
                try
                {
                    e.Graphics.SetClip(e.CellBounds,CombineMode.Intersect);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                    int diameter=(int)Math.Round(18*e.Graphics.DpiX/96F);var circle=new Rectangle(e.CellBounds.Left+(e.CellBounds.Width-diameter)/2,e.CellBounds.Top+(e.CellBounds.Height-diameter)/2,diameter,diameter);
                    bool chosen=Convert.ToBoolean(e.Value);
                    using(var fill=new SolidBrush(chosen?Colors.Accent:Colors.Surface))e.Graphics.FillEllipse(fill,circle);
                    using(var edge=new Pen(chosen?Colors.Accent:Colors.Border))e.Graphics.DrawEllipse(edge,circle);
                    if(chosen)using(var point=new SolidBrush(Colors.AccentText))e.Graphics.FillEllipse(point,Rectangle.Inflate(circle,-diameter/3,-diameter/3));
                }
                finally{e.Graphics.Restore(saved);}
                e.Paint(e.CellBounds,DataGridViewPaintParts.Border|DataGridViewPaintParts.Focus);e.Handled=true;
            };
        }
        private void ToggleSelection(int index)
        {
            if(busy||index<0||index>=grid.Rows.Count)return;string id=Convert.ToString(grid.Rows[index].Tag);if(!selectable.Contains(id))return;
            if(!selection.Add(id))selection.Remove(id);grid.Rows[index].Cells[0].Value=selection.Contains(id);grid.InvalidateCell(0,index);UpdateActions();
        }
        protected override void OnThemeChanged()
        {
            foreach(var item in new[]{defaults,remote})item.Palette=Colors;journal.BackColor=Colors.Surface;journal.ForeColor=Colors.Text;
            grid.BackgroundColor=Colors.Surface;grid.GridColor=Colors.Border;
            grid.DefaultCellStyle.BackColor=Colors.Surface;grid.DefaultCellStyle.ForeColor=Colors.Text;grid.DefaultCellStyle.SelectionBackColor=Colors.Selection;grid.DefaultCellStyle.SelectionForeColor=Colors.SelectionText;
            grid.ColumnHeadersDefaultCellStyle.BackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.ForeColor=Colors.Text;grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=Colors.Text;
            grid.Invalidate();
        }
        private void Log(string text){if(IsDisposed)return;journal.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);}
        private void Progress(string text){if(IsDisposed||!IsHandleCreated)return;try{BeginInvoke((MethodInvoker)delegate{if(!IsDisposed)activity.Text=text;});}catch(InvalidOperationException){}}
        private void LogProgress(string text){Progress(text);if(IsDisposed||!IsHandleCreated)return;try{BeginInvoke((MethodInvoker)delegate{Log(text);});}catch(InvalidOperationException){}}
        private bool ChooseSource()
        {
            using(var dialog=new FolderBrowserDialog{Description="Выбери _soft или папку Библиотеки",ShowNewFolderButton=false})
            {
                if(!String.IsNullOrWhiteSpace(source))dialog.SelectedPath=Directory.Exists(source)?source:Path.GetDirectoryName(source);
                if(dialog.ShowDialog(this)!=DialogResult.OK)return false;
                source=SoftwarePackageStorage.Folder(dialog.SelectedPath);sourceLabel.Text=source;return true;
            }
        }
        private void RequestCancel(bool close){closeAfter|=close;cancelled=true;cancel.Enabled=false;if(session!=null)session.Cancel();activity.Text="Отменяем операцию…";}
        private void Lock(bool value)
        {
            busy=value;folderButton.Enabled=refresh.Enabled=install.Enabled=remove.Enabled=access.Enabled=associations.Enabled=back.Enabled=defaults.Enabled=remote.Enabled=!value;cancel.Enabled=value&&!applyingDefaults;

            if(!value){UpdateRows();if(closeAfter)BeginInvoke((MethodInvoker)delegate{Close();});}
        }
        private async Task RefreshPackages(bool resolveUnknownVersion=false)
        {
            if(busy||preview)return;bool allowAny=true;checking=true;cancelled=false;Lock(true);sourceLabel.Text=source??"Папка не выбрана";
            try
            {
                offline.Clear();packages.Clear();checks.Clear();selection.Clear();UpdateRows();
                var installed=await Task.Run(()=>SoftwareCatalog.Apps.Select(app=>SoftwareDetection.Check(app.Id,allowAny)).ToArray());
                foreach(var check in installed)checks[check.Id]=check;
                UpdateRows();
                if(checks.Values.Any(c=>c.State==LibraryState.Unknown && c.Detail.Contains("доступ")))
                {
                    Progress("Уточняем регистрацию с правами администратора");
                    using(var elevated=await Task.Run(()=>LibrarySession.Start(()=>cancelled))){session=elevated;var snapshot=await Task.Run(()=>elevated.ExecuteSystem("software-query",allowAny?"allow-anydesk32":"x64-only","",Progress));if(snapshot.ExitCode!=0)throw new InvalidOperationException(snapshot.Message);foreach(var check in SoftwareDetection.ParseQuery(snapshot.Message))checks[check.Id]=check;session=null;}
                }
                var local=await Task.Run(()=>{LibraryCancellation.Current=()=>cancelled; try {var list=SoftwareCatalog.Local(source,Progress,"Локальный _soft",resolveUnknownVersion);if(Directory.Exists(SoftwareSources.CacheFolder))foreach(string folder in Directory.GetDirectories(SoftwareSources.CacheFolder))list.AddRange(SoftwareCatalog.Local(folder,Progress,"Кэш менеджера",resolveUnknownVersion));return list;} finally {LibraryCancellation.Current=null;}});
                foreach(var app in SoftwareCatalog.Apps){var candidate=SoftwareSources.Best(local,app.Id);if(candidate!=null){offline[app.Id]=candidate;packages[app.Id]=candidate;}}
                UpdateRows();
                var updates=resolveUnknownVersion ? await Task.WhenAll(SoftwareCatalog.Apps.Select(app=>Task.Run(()=>
                {
                    LibraryCancellation.Current=()=>cancelled;
                    try{return latestSource(app.Id);}catch(Exception ex){Progress(app.Name+": официальный источник недоступен. "+ex.Message);return null;}finally{LibraryCancellation.Current=null;}
                }))) : new SoftwarePackage[0];
                foreach(var candidate in updates.Where(p=>p!=null)){SoftwarePackage old;if(!packages.TryGetValue(candidate.Id,out old)||SoftwareCatalog.Compare(candidate.Version,old.Version)>=0)packages[candidate.Id]=candidate;}
                if(cancelled)throw new OperationCanceledException();
                selection.Clear();foreach(var app in SoftwareCatalog.Apps){if(packages.ContainsKey(app.Id)&&(checks[app.Id].State==LibraryState.Missing || checks[app.Id].State==LibraryState.Installed && packages[app.Id].Version!="0.0" && SoftwareCatalog.Compare(packages[app.Id].Version,checks[app.Id].Version)>0))selection.Add(app.Id);}
                Log("Проверка завершена. Если официальный источник недоступен, предлагается проверенный _soft или кэш");activity.Text="Выбери программы и проверь источник установщиков";
            }
            catch(OperationCanceledException){Log("Проверка отменена");}catch(Exception ex){Log("Проверка не завершена: "+ex.Message);}
            finally{checking=false;Lock(false);}
        }
        private void UpdateRows()
        {
            if(grid.Rows.Count==0)foreach(var app in SoftwareCatalog.Apps){int index=grid.Rows.Add(false,app.Name,"Не проверено","");grid.Rows[index].Tag=app.Id;grid.Rows[index].MinimumHeight=60;}
            selectable.Clear();
            foreach(DataGridViewRow row in grid.Rows)
            {
                string id=Convert.ToString(row.Tag);SoftwareCheck check;SoftwarePackage package;checks.TryGetValue(id,out check);packages.TryGetValue(id,out package);
                row.Cells[2].Value=check==null?"Не проверено":check.State==LibraryState.Installed?"Установлена, "+(check.Detail.Contains("x86")?"x86":check.Detail.Contains("x64")?"x64":"разрядность уточняется")+"\r\nВерсия "+check.Version:check.State==LibraryState.Missing?"Не установлена":"Требуется проверка";
                row.Cells[3].Value=package==null?"Пакет не найден\r\nВыбери _soft или проверь доступ к сайту":(package.Version=="0.0"?"Версия после получения":package.Version)+", разрядность "+package.Architecture+"\r\n"+package.Source;
                bool allowed=check!=null&&check.State==LibraryState.Installed||package!=null&&check!=null&&check.State==LibraryState.Missing&&(package.Architecture=="x64"||id=="anydesk");
                if(allowed)selectable.Add(id);else selection.Remove(id);row.Cells[0].Value=selection.Contains(id);
                row.Cells[0].ToolTipText=allowed?"Щёлкни, чтобы выбрать или снять отметку":"Сначала проверь установленную программу и пакет";
            }
            UpdateActions();
        }
        private string[] ChosenInstalled(bool remoteOnly=false)
        {return SoftwareCatalog.Apps.Where(a=>selection.Contains(a.Id)&&(!remoteOnly||a.Remote)&&checks.ContainsKey(a.Id)&&checks[a.Id].State==LibraryState.Installed).Select(a=>a.Id).ToArray();}
        private string[] ChosenInstallable()
        {return SoftwareCatalog.Apps.Where(a=>selection.Contains(a.Id)&&checks.ContainsKey(a.Id)&&packages.ContainsKey(a.Id)&&SoftwareMaintenance.CanInstall(checks[a.Id],packages[a.Id])).Select(a=>a.Id).ToArray();}
        private void UpdateActions()
        {
            if(install==null)return;install.Enabled=!busy&&ChosenInstallable().Length>0;remove.Enabled=!busy&&ChosenInstalled().Length>0;access.Enabled=!busy&&ChosenInstalled(true).Length>0;
        }
        private async Task ConfigureSelected()
        {
            if(busy||preview)return;string[] ids=ChosenInstalled(true);if(ids.Length==0)return;var secrets=new Dictionary<string,string>();
            foreach(string id in ids)using(var dialog=new SoftwareRemoteDialog(Colors,Icon,id)){if(dialog.ShowDialog(this)!=DialogResult.OK)return;secrets[id]=dialog.Password;}
            cancelled=false;Lock(true);
            try
            {
                using(var active=await Task.Run(()=>LibrarySession.Start(()=>cancelled)))
                {
                    session=active;foreach(string id in ids){if(cancelled)throw new OperationCanceledException();var request=new SoftwareInstallRequest {Id=id,Version=checks[id].Version,Sha256="",AllowAnyDesk32=true,ConfigureRemote=true,Password=secrets[id]};var result=await Task.Run(()=>active.ExecuteSystem("software-access",request.Serialize(),"",Progress));Log(SoftwareCatalog.Find(id).Name+": "+result.Message);if(result.ExitCode!=0)break;}
                }
            }catch(OperationCanceledException){Log("Настройка отменена");}catch(Exception ex){Log("Настройка прервана: "+ex.Message);}finally{session=null;secrets.Clear();Lock(false);}
        }
        private async Task RemoveSelected()
        {
            if(busy||preview)return;string[] ids=ChosenInstalled();if(ids.Length==0)return;
            if(MessageBox.Show(this,"Удалить программы и их настройки?\r\n"+String.Join("\r\n",ids.Select(id=>SoftwareCatalog.Find(id).Name))+"\r\n\r\nНастройки текущего пользователя и общие настройки будут очищены. Параметры удалённого доступа и ID могут сброситься. Текущий удалённый сеанс может прерваться. Документы не удаляются.","Удаление программ",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            cancelled=false;Lock(true);bool reboot=false;
            try
            {
                using(var active=await Task.Run(()=>LibrarySession.Start(()=>cancelled)))
                {
                    session=active;
                    foreach(string id in ids)
                    {
                        if(cancelled)throw new OperationCanceledException();SoftwareMaintenance.PreserveUserDocuments(id,Log);var result=await Task.Run(()=>active.ExecuteSystem("software-remove",id,"",Progress));Log(SoftwareCatalog.Find(id).Name+": "+result.Message);
                        if(result.Reboot){reboot=true;break;}if(result.ExitCode!=0||result.State!=LibraryState.Missing)break;
                        try{SoftwareMaintenance.CleanupUser(id,Log);}finally{checks[id]=new SoftwareCheck {Id=id,Version="",Executable="",State=LibraryState.Missing,Detail="Не установлена"};selection.Remove(id);UpdateRows();}
                    }
                }
            }catch(OperationCanceledException){Log("Удаление отменено. Проверь состояние программ");}catch(Exception ex){Log("Удаление или очистка настроек не завершены: "+ex.Message);}finally{session=null;Lock(false);}
            if(reboot&&!closeAfter)await RestartOffer.Show(this);
        }
        private async Task Install()
        {
            if(busy||preview)return;var ids=ChosenInstallable();if(ids.Length==0){activity.Text="Отметь программы для установки";return;}
            if(String.IsNullOrWhiteSpace(source)&&ids.Any(id=>!Path.IsPathRooted(packages[id].File)||!File.Exists(packages[id].File))&&!ChooseSource())return;
            var secrets=new Dictionary<string,string>();
            if(remote.Checked)foreach(string id in ids.Where(id=>SoftwareCatalog.Find(id).Remote))using(var dialog=new SoftwareRemoteDialog(Colors,Icon,id)) {if(dialog.ShowDialog(this)!=DialogResult.OK)return;secrets[id]=dialog.Password;}
            if(MessageBox.Show(this,"Установить или обновить выбранные программы?\r\n\r\nОбновление AnyDesk / RuDesktop может прервать текущий удалённый сеанс. Изменение ассоциаций потребует повторного входа пользователей в Windows.",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            cancelled=false;Lock(true);bool reboot=false;
            var ready=new List<SoftwarePackage>();
            try
            {
                foreach(string id in ids)
                {
                    if(cancelled)break;var package=packages[id];
                    var current=await Task.Run(()=>SoftwareDetection.Check(id,true));checks[id]=current;
                    if(!SoftwareMaintenance.CanInstall(current,package)){Log(SoftwareCatalog.Find(id).Name+": установка не требуется или состояние требует проверки");UpdateRows();continue;}
                    SoftwarePackage cached;
                    if((!Path.IsPathRooted(package.File)||!File.Exists(package.File))&&offline.TryGetValue(id,out cached)&&SoftwareCatalog.Compare(cached.Version,package.Version)==0&&(String.IsNullOrEmpty(package.Sha256)||String.Equals(cached.Sha256,package.Sha256,StringComparison.OrdinalIgnoreCase)))package=cached;
                    if(!Path.IsPathRooted(package.File)||!File.Exists(package.File))
                    {
                        try{package=await Task.Run(()=>SoftwarePackageStorage.Download(package,source,LogProgress,()=>cancelled));packages[id]=package;offline[id]=package;}
                        catch(OperationCanceledException){throw;}
                        catch(Exception ex)
                        {
                            SoftwarePackage fallback;if(!offline.TryGetValue(id,out fallback))throw new IOException(SoftwareCatalog.Find(id).Name+": загрузка не удалась, проверенного локального пакета нет. "+ex.Message);
                            Log(SoftwareCatalog.Find(id).Name+": загрузка не удалась. Локальный пакет "+fallback.Version);
                            if(MessageBox.Show(this,"Использовать локальный "+SoftwareCatalog.Find(id).Name+" "+fallback.Version+" вместо сетевого пакета?",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)throw new OperationCanceledException();package=fallback;
                        }
                    }
                    SoftwareCatalog.Validate(package,true);
                    ready.Add(package);
                }
                if(cancelled)throw new OperationCanceledException();
                using(var active=await Task.Run(()=>LibrarySession.Start(()=>cancelled)))
                {
                    session=active;bool complete=true;
                    foreach(var package in ready)
                    {
                        if(cancelled){complete=false;break;}string id=package.Id;
                        var request=new SoftwareInstallRequest {Id=id,Version=package.Version,Sha256=package.Sha256,AllowAnyDesk32=true,ConfigureRemote=secrets.ContainsKey(id),Password=secrets.ContainsKey(id)?secrets[id]:""};
                        var result=await Task.Run(()=>active.ExecuteSystem("software-install",request.Serialize(),package.File,Progress));
                        Log(SoftwareCatalog.Find(id).Name+": "+result.Message);bool allow=true;checks[id]=await Task.Run(()=>SoftwareDetection.Check(id,allow));UpdateRows();
                        if(result.Reboot){reboot=true;complete=false;break;}if(!LibraryPlan.Success(result.ExitCode)||result.State!=LibraryState.Installed){complete=false;break;}
                    }
                    if(complete&&!cancelled&&!reboot&&defaults.Checked)await DefaultsCore(active);
                }
            }
            catch(OperationCanceledException){Log("Отменено. Проверь программы перед повтором");}catch(Exception ex){Log("Очередь остановлена: "+ex.Message);}
            finally{session=null;Lock(false);secrets.Clear();}
            if(reboot&&!closeAfter)await RestartOffer.Show(this);
        }
        private async Task DefaultsCore(LibrarySession existing=null)
        {
            var ids=new[]{"notepad","7zip"}.Where(id=>checks.ContainsKey(id)&&checks[id].State==LibraryState.Installed).ToArray();if(ids.Length==0){Log("Ассоциации не изменены: Notepad++ и 7-Zip x64 не подтверждены");return;}
            applyingDefaults=true;cancel.Enabled=false;
            try
            {
                LibraryInstallResult result;
                if(existing!=null)result=await Task.Run(()=>existing.ExecuteSystem("software-defaults",String.Join(",",ids),"",Progress));
                else using(var active=await Task.Run(()=>LibrarySession.Start())){session=active;result=await Task.Run(()=>active.ExecuteSystem("software-defaults",String.Join(",",ids),"",Progress));}
                if(result.ExitCode!=0)throw new InvalidOperationException(result.Message);Log(result.Message);activity.Text=result.Message;
            }
            finally{session=null;applyingDefaults=false;}
        }
        private async Task ApplyDefaults()
        {
            if(busy||preview)return;if(MessageBox.Show(this,"Применить системный профиль Notepad++ и 7-Zip для пользователей компьютера? Сохраним исходные ассоциации. Для применения существующим пользователям нужен повторный вход в Windows.",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            Lock(true);try{await DefaultsCore();}catch(Exception ex){Log(ex.Message);}finally{Lock(false);}
        }
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)headerFont.Dispose();}
        private void FillPreview()
        {
            foreach(var app in SoftwareCatalog.Apps){checks[app.Id]=new SoftwareCheck {Id=app.Id,Version=app.Id=="notepad"?"8.9.8.1":"26.04",State=app.Id=="notepad"?LibraryState.Installed:LibraryState.Missing,Detail=app.Id=="notepad"?"Установлена, x64":"Не установлено"};packages[app.Id]=new SoftwarePackage {Id=app.Id,Version=app.Id=="notepad"?"8.9.8.1":app.Id=="7zip"?"26.04":app.Id=="rudesktop"?"3.0.1563":"9.8",Architecture=app.Id=="anydesk"?"x86":"x64",Source=app.Id=="notepad"?"Официальный релиз на GitHub":"Локальный _soft"};}
            UpdateRows();Log("Проверка завершена. Для отсутствующих программ доступны установщики");
        }
    }
    internal sealed class SoftwareRemoteDialog : Form
    {
        private readonly BrandTextBox input=new BrandTextBox(),confirmation=new BrandTextBox();
        private readonly BrandCheckBox remember=new BrandCheckBox();private readonly string id;
        private readonly BrandButton ok;private readonly Label validation;private readonly Image logo;
        internal string Password {get{return input.Text;}}
        internal static bool PasswordsMatch(string password,string repeated){return password!=null&&password.Length>=8&&password.Length<=128&&!password.Any(Char.IsControl)&&String.Equals(password,repeated,StringComparison.Ordinal);}
        internal SoftwareRemoteDialog(ThemePalette colors,Icon icon,string appId,bool loadSaved=true)
        {
            id=appId;Text="Неконтролируемый доступ к "+SoftwareCatalog.Find(id).Name;Icon=icon==null?null:(Icon)icon.Clone();ClientSize=new Size(620,410);Font=new Font("Segoe UI",10);BackColor=colors.Canvas;ForeColor=colors.Text;StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;
            var layout=new BufferedLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(24,18,24,18),ColumnCount=1,RowCount=9};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int h in new[]{76,24,36,24,36,30,36,48,48})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            var heading=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,64));heading.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            heading.Controls.Add(new Label {Text="Постоянный пароль для "+SoftwareCatalog.Find(id).Name+"\r\nПодключение без подтверждения на этом компьютере",Dock=DockStyle.Fill,ForeColor=colors.Text,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty},0,0);
            logo=BrandAssets.LoadSoftwareIcon(id);heading.Controls.Add(new PictureBox {Image=logo,SizeMode=PictureBoxSizeMode.Zoom,Dock=DockStyle.Fill,Margin=new Padding(8,8,0,8)},1,0);layout.Controls.Add(heading,0,0);
            layout.Controls.Add(new Label {Text="Пароль",Dock=DockStyle.Fill,ForeColor=colors.Text,Margin=Padding.Empty},0,1);layout.Controls.Add(new Label {Text="Подтверди пароль",Dock=DockStyle.Fill,ForeColor=colors.Text,Margin=Padding.Empty},0,3);
            foreach(var box in new[]{input,confirmation}){box.Dock=DockStyle.Fill;box.UseSystemPasswordChar=true;box.MaxLength=128;box.Palette=colors;box.BackColor=colors.SurfaceAlt;box.ForeColor=colors.Text;box.Margin=new Padding(0,0,0,4);}
            layout.Controls.Add(input,0,2);layout.Controls.Add(confirmation,0,4);
            validation=new Label {Text="От 8 до 128 символов",Dock=DockStyle.Fill,ForeColor=colors.Muted,Margin=Padding.Empty,Font=new Font("Segoe UI",9)};layout.Controls.Add(validation,0,5);
            remember.Text="Запомнить на этом компьютере для моей учётной записи";remember.Dock=DockStyle.Fill;remember.Palette=colors;remember.Margin=Padding.Empty;layout.Controls.Add(remember,0,6);
            layout.Controls.Add(new Label {Text=id=="rudesktop"?"RuDesktop передаёт пароль параметром процесса; он может быть виден локальному администратору во время настройки":"Пароль передаётся AnyDesk через стандартный ввод и не записывается в журнал",Dock=DockStyle.Fill,ForeColor=colors.Muted,Font=new Font("Segoe UI",9),Margin=Padding.Empty},0,7);
            var actions=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var cancel=new BrandButton {Text="Отмена",Palette=colors,Dock=DockStyle.Fill,DialogResult=DialogResult.Cancel,Margin=new Padding(0,0,10,0)};actions.Controls.Add(cancel,0,0);
            ok=new BrandButton {Text="Продолжить",Palette=colors,Primary=true,Dock=DockStyle.Fill,Margin=Padding.Empty,Enabled=false};
            Action validate=delegate{ok.Enabled=PasswordsMatch(input.Text,confirmation.Text);validation.Text=input.Text.Length<8?"Введи не менее 8 символов":!String.Equals(input.Text,confirmation.Text,StringComparison.Ordinal)?"Пароли не совпадают":"Пароли совпадают";};input.TextChanged+=delegate{validate();};confirmation.TextChanged+=delegate{validate();};
            ok.Click+=delegate{if(!PasswordsMatch(input.Text,confirmation.Text))return;try{if(remember.Checked)SoftwarePasswordStore.Save(id,input.Text);else SoftwarePasswordStore.Forget(id);DialogResult=DialogResult.OK;}catch(Exception){validation.Text="Не удалось сохранить пароль: отключи запоминание и повтори";}};
            actions.Controls.Add(ok,1,0);layout.Controls.Add(actions,0,8);Controls.Add(layout);CancelButton=cancel;AcceptButton=ok;
            if(loadSaved)try{input.Text=SoftwarePasswordStore.Load(id);remember.Checked=input.Text.Length>0;}catch(Exception){}
        }
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing&&logo!=null)logo.Dispose();}
    }
}
