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
        private readonly LibraryGridScrollBar gridScroll=new LibraryGridScrollBar();
        private readonly HashSet<string> selection=new HashSet<string>(), selectable=new HashSet<string>();
        private readonly Font headerFont=new Font("Segoe UI",10.5F,FontStyle.Bold);
        private readonly Dictionary<string,SoftwarePackage> packages=new Dictionary<string,SoftwarePackage>(), offline=new Dictionary<string,SoftwarePackage>();
        private readonly Dictionary<string,SoftwareCheck> checks=new Dictionary<string,SoftwareCheck>();
        private readonly BrandCheckBox remote=new BrandCheckBox();
        private readonly BrandTextBox sourceLabel;
        private readonly Label activity;
        private readonly BrandButton refresh,install,cancel,folderButton,back,associations,remove,access;
        private readonly JournalTextBox journal=new JournalTextBox();
        private readonly bool preview;
        private readonly Func<string,SoftwarePackage> latestSource;
        private string source;
        private bool busy,checking,closeAfter,fittingRows,fitRowsPending;
        private volatile bool cancelled;
        private LibrarySession session;
        internal bool MenuRequested {get;private set;}
        internal override void ResumeSession() { MenuRequested=false; closeAfter=false; base.ResumeSession(); }
        internal SoftwareManagerForm(bool previewMode) : this(previewMode,SoftwareSources.Latest) {}
        internal SoftwareManagerForm(bool previewMode,Func<string,SoftwarePackage> versions) : base("Установка программ BARS",new Size(1120,780))
        {
            if(versions==null)throw new ArgumentNullException("versions");latestSource=versions;
            preview=previewMode; MinimumSize=new Size(1000,700); Root.AutoScroll=false; Root.RowStyles[0].Height=96;
            var body=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Margin=Padding.Empty,MinimumSize=Size.Empty}; body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(float height in new[]{100F,0F,56F,84F,78F,50F}) body.RowStyles.Add(new RowStyle(height==0 ? SizeType.Percent : SizeType.Absolute,height==0 ? 100 : height));
            var heading=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Margin=new Padding(0,0,0,12),Padding=new Padding(0,8,0,0)};
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,520));
            heading.RowStyles.Add(new RowStyle(SizeType.Absolute,8));heading.RowStyles.Add(new RowStyle(SizeType.Absolute,32));heading.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var title=ModuleHeading("Программы для рабочего места пользователя");((FlushLabel)title).WrapLines=true;title.AutoEllipsis=false;title.Margin=new Padding(0,0,12,0);heading.Controls.Add(title,0,1);
            var folderCaption=Caption("Папка установщиков:",true);heading.Controls.Add(folderCaption,1,1);
            sourceLabel=new BrandTextBox {Text="Папка не выбрана",ReadOnly=true,AutoSize=false,Palette=Colors};
            folderButton=Button("Выбрать папку...",false);folderButton.Click+=async delegate {if(!busy&&ChooseSource())await RefreshPackages();};
            var sourceRow=new PathPickerRow(sourceLabel,folderButton,176){Dock=DockStyle.Fill};heading.Controls.Add(sourceRow,1,2);
            heading.Layout+=delegate
            {
                int width=Math.Min(heading.ClientSize.Width,sourceRow.PreferredRowWidth);
                if(heading.ColumnStyles[1].Width!=width)heading.ColumnStyles[1].Width=width;
                float scale=sourceRow.PreferredRowHeight/40F;
                int available=Math.Max(1,heading.ClientSize.Width-width-title.Margin.Horizontal);
                int measured=TextRenderer.MeasureText(title.Text,title.Font,new Size(available,Int32.MaxValue),TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.WordBreak).Height;
                float shared=Math.Max(32*scale,measured),blank=Math.Max(0,40*scale-shared);
                if(Math.Abs(heading.RowStyles[0].Height-blank)>.1F)heading.RowStyles[0].Height=blank;
                if(Math.Abs(heading.RowStyles[1].Height-shared)>.1F)heading.RowStyles[1].Height=shared;
            };
            body.Controls.Add(heading,0,0);
            var tableCard=new Panel {Dock=DockStyle.Fill,Padding=Padding.Empty,Margin=Padding.Empty};
            ConfigureGrid();gridScroll.Dock=DockStyle.Right;gridScroll.Width=14;gridScroll.AlignToRows=true;gridScroll.Bind(grid);tableCard.Controls.Add(grid);tableCard.Controls.Add(gridScroll);
            // Measure against the narrow lane first, then reclaim it only when all rows fit.
            tableCard.SizeChanged+=delegate{QueueFitRows();};
            body.Controls.Add(tableCard,0,1);
            grid.ColumnWidthChanged+=delegate{QueueFitRows();};
            var options=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=1,Margin=new Padding(4,0,0,0),Padding=new Padding(0,10,0,10)};
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));options.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            remote.Dock=DockStyle.Fill;remote.Margin=Padding.Empty;remote.Palette=Colors;
            remote.Text="Настроить неконтролируемый доступ AnyDesk / RuDesktop — пароль перед установкой";remote.Checked=false;
            options.Controls.Add(remote,0,0);body.Controls.Add(options,0,2);
            // Align the painted left edge, not the checkbox control bounds.
            options.Layout+=delegate
            {
                if(!grid.IsHandleCreated||!options.IsHandleCreated||grid.Columns.Count==0)return;
                using(var graphics=grid.CreateGraphics())
                {
                    var cell=grid.GetCellDisplayRectangle(0,-1,false);
                    int diameter=(int)Math.Round(18*graphics.DpiX/96F);
                    int left=options.PointToClient(grid.PointToScreen(new Point(cell.Left+(cell.Width-diameter)/2,0))).X-1;
                    var margin=new Padding(Math.Max(0,left),0,0,0);
                    if(remote.Margin!=margin)remote.Margin=margin;
                }
            };
            var actions=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=2,Margin=new Padding(0,0,0,10)};for(int i=0;i<3;i++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));actions.RowStyles.Add(new RowStyle(SizeType.Percent,50));actions.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            install=Button("Установить",true);install.Click+=async delegate{await Install();};remove=Button("Удалить",false);remove.Destructive=true;remove.Click+=async delegate{await RemoveSelected();};access=Button("Настроить удалённый доступ",false);access.Margin=Padding.Empty;access.Click+=async delegate{await ConfigureSelected();};
            refresh=Button("Проверить версии в сети",false);refresh.Click+=async delegate{await RefreshPackages(true);};associations=Button("Приложения по умолчанию",false);associations.Click+=delegate{OpenDefaultApps();};
            cancel=Button("Отменить",false);cancel.Margin=Padding.Empty;cancel.Enabled=false;cancel.Click+=delegate{if(busy&&BrandMessageBox.Show(this,LibraryManagerForm.CancelWarning,"Отмена",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes)RequestCancel(false);};
            foreach(var button in new[]{install,remove,access})button.Margin=new Padding(button.Margin.Left,0,button.Margin.Right,6);
            actions.Controls.Add(install,0,0);actions.Controls.Add(remove,1,0);actions.Controls.Add(access,2,0);actions.Controls.Add(refresh,0,1);actions.Controls.Add(associations,1,1);actions.Controls.Add(cancel,2,1);body.Controls.Add(actions,0,3);
            var logCard=JournalPanel.Create(journal,"Журнал установки программ",Colors);
            var footer=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=new Padding(0,10,0,0)};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            activity=Label("Установка начнётся только по кнопке",true);activity.Font=new Font("Segoe UI",9);footer.Controls.Add(activity,0,0);

            activity.Text="Скачать / сверить комплект…";activity.Cursor=Cursors.Hand;
            activity.Click+=async delegate{if(busy)return;using(var dialog=new BundleDownloadForm(String.IsNullOrEmpty(source)?null:Path.GetDirectoryName(source))){dialog.ShowDialog(this);if(dialog.RootAccepted){source=SoftwarePackageStorage.Folder(dialog.LibraryRoot);sourceLabel.Text=source;await RefreshPackages();}}};
            back=Button("В меню",false);ModuleNavigation.Configure(back);back.Click+=delegate{MenuRequested=true;Close();};footer.Controls.Add(back,1,0);
            // Navigation and the journal stay outside the scrolling work area.
            // Only unusually short screens or large text require scrolling it.
            body.RowCount=4;while(body.RowStyles.Count>4)body.RowStyles.RemoveAt(body.RowStyles.Count-1);body.MinimumSize=new Size(0,440);
            var outer=new CompactJournalLayout(body,1,grid,logCard,footer,64,54);Root.Controls.Add(outer,0,1);
            try{if(!preview)source=SoftwareCatalog.FindFolder();}catch(Exception ex){Log(ex.Message);}
            SetTheme(Colors.Dark,false);
            UpdateRows();
            Shown+=async delegate {grid.PerformLayout();FitRows();if(preview)FillPreview();else {if(String.IsNullOrEmpty(source)||!Directory.Exists(source)){string root=BundleDownloadForm.Offer(this,null);if(!String.IsNullOrEmpty(root))source=SoftwarePackageStorage.Folder(root);}await RefreshPackages();}grid.PerformLayout();body.PerformLayout();};
            FormClosing+=delegate(object sender,FormClosingEventArgs e)
            {
                if(!busy)return;if(checking){cancelled=true;if(session!=null)session.Cancel();return;}e.Cancel=true;
                if(cancelled){closeAfter=true;return;}
                if(checking || BrandMessageBox.Show(this,LibraryManagerForm.CancelWarning+"\r\n\r\nЗакрыть менеджер после отмены?","Завершение работы",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes)RequestCancel(true);else MenuRequested=false;
            };
        }
        private void ConfigureGrid()
        {
            grid.Dock=DockStyle.Fill;grid.Margin=Padding.Empty;grid.BorderStyle=BorderStyle.None;
            grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=grid.AllowUserToResizeRows=grid.AllowUserToResizeColumns=grid.AllowUserToOrderColumns=false;
            grid.ReadOnly=true;grid.TouchScrollingEnabled=true;grid.RowHeadersVisible=false;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;
            grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersHeight=44;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.Font=headerFont;grid.ColumnHeadersDefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Font=new Font("Segoe UI",9.5F);grid.DefaultCellStyle.Padding=new Padding(4);grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleLeft;
            grid.CellBorderStyle=DataGridViewCellBorderStyle.Single;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.Single;grid.ScrollBars=ScrollBars.None;
            grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="Selected",HeaderText="",Width=46,MinimumWidth=46,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});
            string[] names={"Программа","В системе","Доступно / Источник"};int[] weights={24,20,56};
            for(int i=0;i<names.Length;i++){int column=grid.Columns.Add("c"+i,names[i]);grid.Columns[column].FillWeight=weights[i];grid.Columns[column].MinimumWidth=130;}
            foreach(DataGridViewColumn column in grid.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;
            grid.EditMode=DataGridViewEditMode.EditProgrammatically;
            grid.CellMouseDown+=delegate(object sender,DataGridViewCellMouseEventArgs e){if(e.Button==MouseButtons.Left&&e.RowIndex>=0&&e.ColumnIndex>=0)ToggleSelection(e.RowIndex);};
            grid.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Space&&!e.Alt&&!e.Control&&grid.CurrentRow!=null){ToggleSelection(grid.CurrentRow.Index);e.Handled=e.SuppressKeyPress=true;}};
            grid.CellMouseEnter+=delegate(object sender,DataGridViewCellEventArgs e){grid.Cursor=e.RowIndex>=0?Cursors.Hand:Cursors.Default;};
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
        private void QueueFitRows()
        {
            if(fittingRows||fitRowsPending||!IsHandleCreated||IsDisposed)return;fitRowsPending=true;
            BeginInvoke((MethodInvoker)delegate{fitRowsPending=false;if(!IsDisposed)FitRows();});
        }
        private void FitRows()
        {
            if(fittingRows||grid.RowCount==0)return;fittingRows=true;
            try
            {
                // Use the smaller width for measurement in either state. This is
                // deterministic even if hiding the lane unwraps a long cell.
                bool wasVisible=gridScroll.Visible;
                gridScroll.Visible=true;
                grid.Parent.PerformLayout();
                GridViewportLayout.UniformRows(grid);
                int total=grid.ColumnHeadersHeight;
                foreach(DataGridViewRow row in grid.Rows)total+=row.Height;
                gridScroll.Visible=total>grid.ClientSize.Height;
                if(wasVisible!=gridScroll.Visible)grid.Parent.PerformLayout();
            }
            finally{fittingRows=false;}
        }
        private void ToggleSelection(int index)
        {
            if(busy||index<0||index>=grid.Rows.Count)return;string id=Convert.ToString(grid.Rows[index].Tag);if(!selectable.Contains(id))return;
            if(!selection.Add(id))selection.Remove(id);grid.Rows[index].Cells[0].Value=selection.Contains(id);grid.InvalidateCell(0,index);UpdateActions();
        }
        protected override void OnThemeChanged()
        {
            remote.Palette=Colors;journal.BackColor=Colors.Surface;journal.ForeColor=Colors.Text;
            gridScroll.Palette=Colors;gridScroll.BackColor=Colors.Canvas;grid.BackgroundColor=Colors.Canvas;grid.GridColor=Colors.Border;
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
            busy=value;folderButton.Enabled=refresh.Enabled=install.Enabled=remove.Enabled=access.Enabled=associations.Enabled=back.Enabled=remote.Enabled=!value;cancel.Enabled=value;

            back.Enabled=!value||checking;
            if(!value&&!IsDisposed){UpdateRows();if(closeAfter)BeginInvoke((MethodInvoker)delegate{Close();});}
        }
        private async Task RefreshPackages(bool resolveUnknownVersion=false)
        {
            if(busy||preview)return;bool allowAny=true;checking=true;cancelled=false;Lock(true);sourceLabel.Text=source??"Папка не выбрана";
            try
            {
                offline.Clear();packages.Clear();checks.Clear();selection.Clear();UpdateRows();
                var installed=await Task.Run(()=>SoftwareCatalog.Apps.Select(app=>SoftwareDetection.Check(app.Id,allowAny)).ToArray());
                if(cancelled||IsDisposed)return;foreach(var check in installed)checks[check.Id]=check;
                UpdateRows();
                if(checks.Values.Any(c=>c.State==LibraryState.Unknown && c.Detail.Contains("доступ")))
                {
                    Progress("Уточняем регистрацию с правами администратора");
                    using(var elevated=await Task.Run(()=>LibrarySession.Start(()=>cancelled))){session=elevated;var snapshot=await Task.Run(()=>elevated.ExecuteSystem("software-query",allowAny?"allow-anydesk32":"x64-only","",Progress));if(snapshot.ExitCode!=0)throw new InvalidOperationException(snapshot.Message);if(cancelled||IsDisposed)return;foreach(var check in SoftwareDetection.ParseQuery(snapshot.Message))checks[check.Id]=check;session=null;}
                }
                var local=await Task.Run(()=>{LibraryCancellation.Current=()=>cancelled; try {var list=SoftwareCatalog.Local(source,Progress,"Локальный _soft",resolveUnknownVersion);if(Directory.Exists(SoftwareSources.CacheFolder))foreach(string folder in Directory.GetDirectories(SoftwareSources.CacheFolder))list.AddRange(SoftwareCatalog.Local(folder,Progress,"Кэш менеджера",resolveUnknownVersion));return list;} finally {LibraryCancellation.Current=null;}});
                if(cancelled||IsDisposed)return;foreach(var app in SoftwareCatalog.Apps){var candidate=SoftwareSources.Best(local,app.Id);if(candidate!=null){offline[app.Id]=candidate;packages[app.Id]=candidate;}}
                UpdateRows();
                var updates=resolveUnknownVersion ? await Task.WhenAll(SoftwareCatalog.Apps.Select(app=>Task.Run(()=>
                {
                    LibraryCancellation.Current=()=>cancelled;
                    try{return latestSource(app.Id);}catch(Exception ex){Progress(app.Name+": официальный источник недоступен. "+ex.Message);return null;}finally{LibraryCancellation.Current=null;}
                }))) : new SoftwarePackage[0];
                if(cancelled||IsDisposed)return;foreach(var candidate in updates.Where(p=>p!=null)){SoftwarePackage old;if(!packages.TryGetValue(candidate.Id,out old)||SoftwareCatalog.Compare(candidate.Version,old.Version)>=0)packages[candidate.Id]=candidate;}
                if(cancelled)throw new OperationCanceledException();
                selection.Clear();foreach(var app in SoftwareCatalog.Apps){if(packages.ContainsKey(app.Id)&&(checks[app.Id].State==LibraryState.Missing || checks[app.Id].State==LibraryState.Installed && packages[app.Id].Version!="0.0" && SoftwareCatalog.Compare(packages[app.Id].Version,checks[app.Id].Version)>0))selection.Add(app.Id);}
                Log(resolveUnknownVersion&&updates.Any(u=>u==null)?"Проверка завершена. Официальный источник в сети недоступен, предлагается установка проверенных локальных копий программ.":"Проверка завершена.");activity.Text="Выбери программы и проверь источник установщиков";
            }
            catch(OperationCanceledException){if(!IsDisposed)Log("Проверка отменена");}catch(Exception ex){if(!IsDisposed)Log("Проверка не завершена: "+ex.Message);}
            finally{checking=false;if(!IsDisposed)Lock(false);}
        }
        private void UpdateRows()
        {
            if(grid.Rows.Count==0)foreach(var app in SoftwareCatalog.Apps){int index=grid.Rows.Add(false,app.Name,"Не проверено","");grid.Rows[index].Tag=app.Id;grid.Rows[index].MinimumHeight=44;}
            selectable.Clear();
            foreach(DataGridViewRow row in grid.Rows)
            {
                string id=Convert.ToString(row.Tag);SoftwareCheck check;SoftwarePackage package;checks.TryGetValue(id,out check);packages.TryGetValue(id,out package);
                row.Cells[2].Value=check==null?"Не проверено":check.State==LibraryState.Installed?"Установлена, "+(check.Detail.Contains("x86")?"x86":check.Detail.Contains("x64")?"x64":"разрядность уточняется")+"\r\nВерсия "+check.Version:check.State==LibraryState.Missing?"Не установлена":"Требуется проверка";
                row.Cells[3].Value=package==null?"Пакет не найден\r\nВыбери _soft или проверь доступ к сайту":(package.Version=="0.0"?"Версия после получения":package.Version)+", разрядность "+package.Architecture+" / "+(package.Source=="Локальный _soft"?"Локальный источник - (папка: _soft)":package.Source);
                bool allowed=check!=null&&check.State==LibraryState.Installed||package!=null&&check!=null&&check.State==LibraryState.Missing&&(package.Architecture=="x64"||id=="anydesk");
                if(allowed)selectable.Add(id);else selection.Remove(id);row.Cells[0].Value=selection.Contains(id);
                row.Cells[0].ToolTipText=allowed?"Щёлкни, чтобы выбрать или снять отметку":"Сначала проверь установленную программу и пакет";
            }
            FitRows();UpdateActions();
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
            if(BrandMessageBox.Show(this,"Удалить программы и их настройки?\r\n"+String.Join("\r\n",ids.Select(id=>SoftwareCatalog.Find(id).Name))+"\r\n\r\nНастройки текущего пользователя и общие настройки будут очищены. Параметры удалённого доступа и ID могут сброситься. Текущий удалённый сеанс может прерваться. Документы не удаляются.","Удаление программ",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
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
        internal string InstallConfirmation(string[] ids)
        {
            string text="Установить или обновить выбранные программы?";
            var remoteUpdates=ids.Where(id=>SoftwareCatalog.Find(id).Remote&&checks.ContainsKey(id)&&checks[id].State==LibraryState.Installed).Select(id=>SoftwareCatalog.Find(id).Name).ToArray();
            if(remoteUpdates.Length>0)text+="\r\n\r\nОбновление "+String.Join(" / ",remoteUpdates)+" может прервать текущий удалённый сеанс.";
            if(remote.Checked&&ids.Any(id=>SoftwareCatalog.Find(id).Remote))text+="\r\n\r\nДля выбранных программ удалённого доступа будет настроен постоянный пароль.";
            return text;
        }
        private async Task Install()
        {
            if(busy||preview)return;var ids=ChosenInstallable();if(ids.Length==0){activity.Text="Отметь программы для установки";return;}
            if(String.IsNullOrWhiteSpace(source)&&ids.Any(id=>!Path.IsPathRooted(packages[id].File)||!File.Exists(packages[id].File))&&!ChooseSource())return;
            var secrets=new Dictionary<string,string>();
            if(remote.Checked)foreach(string id in ids.Where(id=>SoftwareCatalog.Find(id).Remote))using(var dialog=new SoftwareRemoteDialog(Colors,Icon,id)) {if(dialog.ShowDialog(this)!=DialogResult.OK)return;secrets[id]=dialog.Password;}
            if(BrandMessageBox.Show(this,InstallConfirmation(ids),Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
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
                            if(BrandMessageBox.Show(this,"Использовать локальный "+SoftwareCatalog.Find(id).Name+" "+fallback.Version+" вместо сетевого пакета?",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)throw new OperationCanceledException();package=fallback;
                        }
                    }
                    SoftwareCatalog.Validate(package,true);
                    ready.Add(package);
                }
                if(cancelled)throw new OperationCanceledException();
                using(var active=await Task.Run(()=>LibrarySession.Start(()=>cancelled)))
                {
                    session=active;
                    foreach(var package in ready)
                    {
                        if(cancelled){break;}string id=package.Id;
                        var request=new SoftwareInstallRequest {Id=id,Version=package.Version,Sha256=package.Sha256,AllowAnyDesk32=true,ConfigureRemote=secrets.ContainsKey(id),Password=secrets.ContainsKey(id)?secrets[id]:""};
                        var result=await Task.Run(()=>active.ExecuteSystem("software-install",request.Serialize(),package.File,Progress));
                        Log(SoftwareCatalog.Find(id).Name+": "+result.Message);bool allow=true;checks[id]=await Task.Run(()=>SoftwareDetection.Check(id,allow));UpdateRows();
                        if(result.Reboot){reboot=true;break;}if(!LibraryPlan.Success(result.ExitCode)||result.State!=LibraryState.Installed){break;}
                    }
                }
            }
            catch(OperationCanceledException){Log("Отменено. Проверь программы перед повтором");}catch(Exception ex){Log("Очередь остановлена: "+ex.Message);}
            finally{session=null;Lock(false);secrets.Clear();}
            if(reboot&&!closeAfter)await RestartOffer.Show(this);
        }
        private void OpenDefaultApps()
        {
            if(busy||preview)return;
            try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps"){UseShellExecute=true});Log("Открыты настройки Windows. Выбери приложения по умолчанию самостоятельно; менеджер ассоциации не меняет.");}
            catch(Exception ex){Log("Не удалось открыть настройки Windows: "+ex.Message);}
        }
        private void FillPreview()
        {
            foreach(var app in SoftwareCatalog.Apps){checks[app.Id]=new SoftwareCheck {Id=app.Id,Version=app.Id=="chrome"?"156.0.8078.48":app.Id=="notepad"?"8.9.8.1":"26.04",State=app.Id=="notepad"?LibraryState.Installed:LibraryState.Missing,Detail=app.Id=="notepad"?"Установлена, x64":"Не установлено"};packages[app.Id]=new SoftwarePackage {Id=app.Id,Version=app.Id=="chrome"?"156.0.8078.48":app.Id=="notepad"?"8.9.8.1":app.Id=="7zip"?"26.04":app.Id=="rudesktop"?"3.0.1563":"9.8",Architecture=app.Id=="anydesk"?"x86":"x64",Source=app.Id=="notepad"?"Официальный релиз на GitHub":"Локальный _soft"};}
            UpdateRows();Log("Проверка завершена. Для отсутствующих программ доступны установщики");
        }
    }
    internal sealed class SoftwareRemoteDialog : Form
    {
        private readonly BrandTextBox input=new BrandTextBox(),confirmation=new BrandTextBox();
        private readonly BrandCheckBox remember=new BrandCheckBox(),standard=new BrandCheckBox();private readonly string id;
        private readonly System.Windows.Forms.Timer keyboardTimer=new System.Windows.Forms.Timer();
        private readonly ToolTip keyboardTips=new ToolTip();
        private string customPassword="",customConfirmation="";
        private readonly BrandButton ok;private readonly Label validation;private readonly Image logo;
        internal string Password {get{return input.Text;}}
        internal static bool PasswordsMatch(string password,string repeated){return password!=null&&password.Length>=8&&password.Length<=128&&!password.Any(Char.IsControl)&&String.Equals(password,repeated,StringComparison.Ordinal);}
        internal SoftwareRemoteDialog(ThemePalette colors,Icon icon,string appId,bool loadSaved=true)
        {
            NativeWindowTheme.Bind(this,()=>colors.Dark);
            SuspendLayout();id=appId;Text="Неконтролируемый доступ к "+SoftwareCatalog.Find(id).Name;Icon=icon==null?null:(Icon)icon.Clone();AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(660,460);Font=new Font("Segoe UI",10);BackColor=colors.Canvas;ForeColor=colors.Text;StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;
            var layout=new BufferedLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(24,18,24,18),ColumnCount=1,RowCount=11};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int h in new[]{70,24,38,24,38,24,26,36,36,64,44})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            var heading=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,64));heading.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            heading.Controls.Add(new Label {Text="Постоянный пароль для "+SoftwareCatalog.Find(id).Name+"\r\nПодключение без подтверждения на этом компьютере",Dock=DockStyle.Fill,ForeColor=colors.Text,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty},0,0);
            logo=BrandAssets.LoadSoftwareIcon(id);heading.Controls.Add(new PictureBox {Image=logo,SizeMode=PictureBoxSizeMode.Zoom,Dock=DockStyle.Fill,Margin=new Padding(8,8,0,8)},1,0);layout.Controls.Add(heading,0,0);
            layout.Controls.Add(new InputCaptionLabel {Text="Пароль:",Dock=DockStyle.Fill,ForeColor=colors.Text,Margin=Padding.Empty,TextAlign=ContentAlignment.BottomLeft,Padding=new Padding(0,0,0,2)},0,1);layout.Controls.Add(new InputCaptionLabel {Text="Подтверди пароль:",Dock=DockStyle.Fill,ForeColor=colors.Text,Margin=Padding.Empty,TextAlign=ContentAlignment.BottomLeft,Padding=new Padding(0,0,0,2)},0,3);
            foreach(var box in new[]{input,confirmation}){box.Dock=DockStyle.Fill;box.UseSystemPasswordChar=true;box.MaxLength=128;box.Palette=colors;box.BackColor=colors.SurfaceAlt;box.ForeColor=colors.Text;box.Margin=new Padding(0,0,0,4);}
            layout.Controls.Add(PasswordRow(input,colors),0,2);layout.Controls.Add(PasswordRow(confirmation,colors),0,4);
            var languageRow=new BufferedLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};
            languageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int width in new[]{10,40,28})languageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,width));languageRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var language=new Label{Name="keyboardLanguage",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,ForeColor=colors.Dark?colors.Warning:Color.FromArgb(190,35,35),Font=new Font("Segoe UI",10F,FontStyle.Bold),Margin=Padding.Empty};languages.Add(language);languageRow.Controls.Add(language,0,0);layout.Controls.Add(languageRow,0,5);
            validation=new Label {Text="От 8 до 128 символов",Dock=DockStyle.Fill,ForeColor=colors.Muted,Margin=Padding.Empty,Font=new Font("Segoe UI",9)};layout.Controls.Add(validation,0,6);
            remember.Text="Запомнить на этом компьютере для моей учётной записи";remember.Dock=DockStyle.Fill;remember.Palette=colors;remember.Margin=Padding.Empty;layout.Controls.Add(remember,0,8);
            standard.Text="Использовать стандартный пароль BARS";standard.Dock=DockStyle.Fill;standard.Palette=colors;standard.Margin=Padding.Empty;layout.Controls.Add(standard,0,7);
            standard.CheckedChanged+=delegate{if(standard.Checked){customPassword=input.Text;customConfirmation=confirmation.Text;input.Text=confirmation.Text="Dk3651340";}else{input.Text=customPassword;confirmation.Text=customConfirmation;}input.Enabled=confirmation.Enabled=!standard.Checked;input.UseSystemPasswordChar=confirmation.UseSystemPasswordChar=true;foreach(var eye in eyes){eye.Enabled=!standard.Checked;eye.Revealed=false;eye.AccessibleName="Показать пароль";eye.Invalidate();}};
            layout.Controls.Add(new Label {Text=id=="rudesktop"?"RuDesktop передаёт пароль параметром процесса; он может быть виден локальному администратору во время настройки":"Пароль передаётся AnyDesk через стандартный ввод и не записывается в журнал",Dock=DockStyle.Fill,Name="passwordNotice",TextAlign=ContentAlignment.MiddleLeft,ForeColor=colors.Dark?colors.Warning:Color.FromArgb(190,35,35),Font=new Font("Segoe UI",9),Margin=new Padding(0,12,0,4)},0,9);
            var actions=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var cancel=new BrandButton {Text="Отмена",Palette=colors,Dock=DockStyle.Fill,DialogResult=DialogResult.Cancel,Margin=new Padding(0,0,10,0)};actions.Controls.Add(cancel,0,0);
            ok=new BrandButton {Text="Продолжить",Palette=colors,Primary=true,Dock=DockStyle.Fill,Margin=Padding.Empty,Enabled=false};
            Action validate=delegate{ok.Enabled=PasswordsMatch(input.Text,confirmation.Text);validation.Text=input.Text.Length<8?"Введи не менее 8 символов":!String.Equals(input.Text,confirmation.Text,StringComparison.Ordinal)?"Пароли не совпадают":"Пароли совпадают";};input.TextChanged+=delegate{validate();};confirmation.TextChanged+=delegate{validate();};
            ok.Click+=delegate{if(!PasswordsMatch(input.Text,confirmation.Text))return;try{if(remember.Checked)SoftwarePasswordStore.Save(id,input.Text);else SoftwarePasswordStore.Forget(id);DialogResult=DialogResult.OK;}catch(Exception){validation.Text="Не удалось сохранить пароль: отключи запоминание и повтори";}};
            actions.Controls.Add(ok,1,0);layout.Controls.Add(actions,0,10);Controls.Add(layout);CancelButton=cancel;AcceptButton=ok;
            keyboardTips.IsBalloon=true;keyboardTips.ToolTipIcon=ToolTipIcon.Warning;keyboardTips.ToolTipTitle="Caps Lock включён";keyboardTimer.Interval=200;keyboardTimer.Tick+=delegate{UpdateKeyboard();};Shown+=delegate{keyboardTimer.Start();UpdateKeyboard();};InputLanguageChanged+=delegate{UpdateKeyboard();};
            if(loadSaved)try{input.Text=SoftwarePasswordStore.Load(id);remember.Checked=input.Text.Length>0;}catch(Exception){}
            AutoScaleMode=AutoScaleMode.Dpi;ResumeLayout(true);
        }
        private readonly List<PasswordEyeButton> eyes=new List<PasswordEyeButton>();
        private readonly List<Label> languages=new List<Label>(),caps=new List<Label>();
        private bool capsWasOn;
        private Control PasswordRow(BrandTextBox box,ThemePalette colors)
        {
            var row=new BufferedLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,10));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,40));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,28));row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            box.Anchor=AnchorStyles.Left|AnchorStyles.Right;box.Dock=DockStyle.None;row.Controls.Add(box,0,0);
            var eye=new PasswordEyeButton{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,4),Palette=colors};eyes.Add(eye);eye.Click+=delegate{eye.Revealed=!eye.Revealed;box.UseSystemPasswordChar=!eye.Revealed;eye.AccessibleName=eye.Revealed?"Скрыть пароль":"Показать пароль";eye.Invalidate();box.Focus();};row.Controls.Add(eye,2,0);
            var warning=new Label{Dock=DockStyle.Fill,Text="⚠",Font=new Font("Segoe UI Symbol",13),ForeColor=Color.Goldenrod,TextAlign=ContentAlignment.MiddleCenter,Margin=Padding.Empty,Visible=false};caps.Add(warning);keyboardTips.SetToolTip(warning,"Caps Lock включён. Проверь регистр букв пароля.");row.Controls.Add(warning,3,0);
            box.Enter+=delegate{capsWasOn=false;UpdateKeyboard();};return row;
        }
        internal static string LanguageLabel(string code){return code=="ru"?"РУС":code=="en"?"ENG":(code??"").ToUpperInvariant();}
        private void UpdateKeyboard() {UpdateKeyboardState(InputLanguage.CurrentInputLanguage.Culture.TwoLetterISOLanguageName,Control.IsKeyLocked(Keys.CapsLock));}
        internal void UpdateKeyboardState(string code,bool capsLocked)
        {
            string text=LanguageLabel(code);foreach(var label in languages)label.Text=text;
            bool on=capsLocked&&!standard.Checked;foreach(var label in caps){label.Visible=on;if(!on)keyboardTips.Hide(label);}
            if(on&&!capsWasOn&&(input.Focused||confirmation.Focused)){var label=caps[input.Focused?0:1];keyboardTips.Show("Caps Lock включён: проверь регистр букв",label,0,label.Height,3000);}capsWasOn=on;
        }
        protected override void Dispose(bool disposing){if(disposing){keyboardTimer.Dispose();keyboardTips.Dispose();}base.Dispose(disposing);if(disposing&&logo!=null)logo.Dispose();}
    }
}
