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
    internal sealed class SoftwareManagerForm : ManagerModuleForm, IModuleOperation
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
        private readonly DownloadProgressView transfer=new DownloadProgressView{Dock=DockStyle.Fill,Margin=new Padding(0,4,0,8)};
        private readonly JournalTextBox journal=new JournalTextBox();
        private readonly bool preview;
        private readonly Func<string,SoftwarePackage> latestSource;
        private string source;
        private bool busy,checking,closeAfter,fittingRows,fitRowsPending;
        private volatile bool cancelled;
        private LibrarySession session;
        private BrandButton sqlTab;
        private SqlManagerPage sqlPage;
        internal bool MenuRequested {get;private set;}
        internal override void ResumeSession() { MenuRequested=false; closeAfter=false; base.ResumeSession(); }
        internal SoftwareManagerForm(bool previewMode) : this(previewMode,SoftwareSources.Latest) {}
        internal SoftwareManagerForm(bool previewMode,Func<string,SoftwarePackage> versions) : base("Установка программ BARS",new Size(1120,880))
        {
            if(versions==null)throw new ArgumentNullException("versions");latestSource=versions;
            FormBorderStyle=FormBorderStyle.FixedSingle;preview=previewMode; MinimumSize=new Size(1000,700); Root.AutoScroll=false; Root.RowStyles[0].Height=76;
            var body=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty,MinimumSize=Size.Empty}; body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(float height in new[]{96F,0F,44F,80F}) body.RowStyles.Add(new RowStyle(height==0 ? SizeType.Percent : SizeType.Absolute,height==0 ? 100 : height));
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
            ConfigureGrid();gridScroll.Dock=DockStyle.Right;gridScroll.Width=14;gridScroll.AlignToRows=true;gridScroll.AutoHideWhenUnused=false;gridScroll.Bind(grid);tableCard.Controls.Add(grid);tableCard.Controls.Add(gridScroll);
            // Measure against the narrow lane first, then reclaim it only when all rows fit.
            tableCard.SizeChanged+=delegate{QueueFitRows();};
            body.Controls.Add(tableCard,0,1);
            grid.ColumnWidthChanged+=delegate{QueueFitRows();};
            var options=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=1,Margin=Padding.Empty,Padding=new Padding(0,8,0,8)};
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));options.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            remote.Dock=DockStyle.Fill;remote.Margin=Padding.Empty;remote.Palette=Colors;
            remote.Text="Настроить неконтролируемый доступ AnyDesk / RuDesktop";remote.Checked=false;
            options.ColumnCount=2;options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,196));options.Controls.Add(remote,0,0);body.Controls.Add(options,0,2);
            var actions=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=2,Margin=new Padding(0,0,0,10)};for(int i=0;i<3;i++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));actions.RowStyles.Add(new RowStyle(SizeType.Percent,50));actions.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            install=Button("Установить",true);install.Click+=async delegate{await Install();};remove=Button("Удалить",false);remove.Destructive=false;remove.Click+=async delegate{await RemoveSelected();};access=Button("Настроить удалённый доступ",false);access.Margin=Padding.Empty;access.Click+=async delegate{await ConfigureSelected();};
            refresh=Button("Проверить версии в сети",false);refresh.Click+=async delegate{await RefreshPackages(true);};associations=Button("Приложения по умолчанию",false);associations.Click+=delegate{OpenDefaultApps();};
            cancel=Button("Отменить",false);cancel.Margin=Padding.Empty;cancel.Enabled=false;cancel.Click+=delegate{if(busy&&BrandMessageBox.Show(this,LibraryManagerForm.CancelWarning,"Отмена",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes)RequestCancel(false);};
            foreach(var button in new[]{install,remove,access})button.Margin=new Padding(button.Margin.Left,0,button.Margin.Right,6);
            actions.Controls.Add(install,0,0);actions.Controls.Add(remove,1,0);actions.Controls.Add(access,2,0);actions.Controls.Add(refresh,0,1);actions.Controls.Add(associations,1,1);actions.Controls.Add(cancel,2,1);body.Controls.Add(actions,0,3);UniformActionButtons.Bind(actions);
            var logCard=JournalPanel.Create(journal,"Журнал установки программ",Colors);
            var footer=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=new Padding(0,10,0,0)};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            activity=Label("Установка начнётся только по кнопке",true);activity.Font=new Font("Segoe UI",9);footer.Controls.Add(activity,0,0);

            back=Button("В меню",false);ModuleNavigation.Configure(back);back.Click+=delegate{if(ModuleNavigation.ReturnToHub())return;MenuRequested=true;Close();};footer.Controls.Add(back,1,0);
            ModuleNavigation.AddCloseButton(footer, back, Button("Закрыть", false), 0);
            // Navigation and the journal stay outside the scrolling work area.
            // Only unusually short screens or large text require scrolling it.
            var journalArea=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};journalArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));journalArea.RowStyles.Add(new RowStyle(SizeType.Absolute,112));journalArea.RowStyles.Add(new RowStyle(SizeType.Percent,100));journalArea.Controls.Add(transfer,0,0);journalArea.Controls.Add(logCard,0,1);
            var outer=new CompactJournalLayout(body,1,grid,journalArea,footer,176,54);Root.Controls.Add(outer,0,1);
            sqlTab=Button("SQL Server и SQL Server Management Studio",false);
            sqlTab.Dock=DockStyle.Fill;
            sqlTab.Margin=Padding.Empty;
            options.Controls.Add(sqlTab,1,0);
            access.SizeChanged+=delegate
            {
                if(access.Width>0&&options.ColumnStyles[1].Width!=access.Width)options.ColumnStyles[1].Width=access.Width;
            };
            sqlTab.Click+=async delegate
            {
                if(busy&&!checking)return;
                if(sqlPage==null)
                {
                    sqlPage=new SqlManagerPage(this,()=>source,delegate
                    {
                        Root.Controls.Remove(sqlPage);Root.Controls.Add(outer,0,1);SetTheme(Colors.Dark,false);
                    },Colors);
                    float scale=CurrentAutoScaleDimensions.Width/96F;
                    if(scale>0&&Math.Abs(scale-1F)>.01F)sqlPage.Scale(new SizeF(scale,scale));
                    Disposed+=delegate{if(!sqlPage.IsDisposed)sqlPage.Dispose();if(!outer.IsDisposed)outer.Dispose();};
                }
                Root.Controls.Remove(outer);Root.Controls.Add(sqlPage,0,1);SetTheme(Colors.Dark,false);
                await sqlPage.RefreshLocal(source);
            };
            try{if(!preview)source=SoftwareCatalog.FindFolder();}catch(Exception ex){transfer.Stage("Не выполнено: "+ex.Message,failed:true);Log(ex.Message);}
            SetTheme(Colors.Dark,false);
            UpdateRows();
            Shown+=async delegate {grid.PerformLayout();FitRows();if(preview)FillPreview();else {if(String.IsNullOrEmpty(source)||!Directory.Exists(source)){string root=await BundleDownloadForm.Offer(this,null,programs:true);if(IsDisposed||ModuleNavigation.ExitConfirmed)return;if(!String.IsNullOrEmpty(root))source=SoftwarePackageStorage.Folder(root);}await RefreshPackages();}grid.PerformLayout();body.PerformLayout();};
            FormClosing+=delegate(object sender,FormClosingEventArgs e)
            {
                if(sqlPage!=null&&!sqlPage.CanClose(ModuleNavigation.ExitConfirmed)){e.Cancel=true;return;}
                if(!busy)return;if(checking){cancelled=true;if(session!=null)session.Cancel();return;}e.Cancel=true;
                if(cancelled){closeAfter=true;return;}
                if(ModuleNavigation.ExitConfirmed || BrandMessageBox.Show(this,LibraryManagerForm.CancelWarning+"\r\n\r\nЗакрыть окно после отмены?","Завершение работы",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes)RequestCancel(true);else MenuRequested=false;
            };
        }
        string IModuleOperation.ActiveOperation { get { return sqlPage!=null&&sqlPage.Installing?"SQL Server и SSMS: мастер установки":busy && !checking ? "Установка программ: операция с программами" : null; } }
        private void ConfigureGrid()
        {
            grid.Dock=DockStyle.Fill;grid.Margin=Padding.Empty;grid.BorderStyle=BorderStyle.None;
            grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=grid.AllowUserToResizeRows=grid.AllowUserToResizeColumns=grid.AllowUserToOrderColumns=false;
            grid.ReadOnly=true;grid.TouchScrollingEnabled=true;grid.RowHeadersVisible=false;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;
            grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersHeight=40;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
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
            if(sqlPage!=null)sqlPage.ApplyTheme(Colors);
            transfer.Palette=Colors;transfer.Invalidate();remote.Palette=Colors;journal.BackColor=Colors.Surface;journal.ForeColor=Colors.Text;
            gridScroll.Palette=Colors;gridScroll.BackColor=Colors.Canvas;grid.BackgroundColor=Colors.Canvas;grid.GridColor=Colors.Border;
            grid.DefaultCellStyle.BackColor=Colors.Surface;grid.DefaultCellStyle.ForeColor=Colors.Text;grid.DefaultCellStyle.SelectionBackColor=Colors.Selection;grid.DefaultCellStyle.SelectionForeColor=Colors.SelectionText;
            grid.ColumnHeadersDefaultCellStyle.BackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.ForeColor=Colors.Text;grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=Colors.Text;
            grid.Invalidate();
        }
        private void Log(string text){if(IsDisposed)return;journal.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+OperationMessage.FinishSentence(text)+Environment.NewLine);}
        private void Progress(string text){if(busy&&!checking&&!transfer.Snapshot.Complete&&!transfer.Snapshot.Failed)transfer.Stage(text);if(IsDisposed||!IsHandleCreated)return;try{BeginInvoke((MethodInvoker)delegate{if(!IsDisposed&&busy&&!transfer.Snapshot.Complete&&!transfer.Snapshot.Failed)activity.Text=text;});}catch(InvalidOperationException){}}
        private void LogProgress(string text){Progress(text);if(IsDisposed||!IsHandleCreated)return;try{BeginInvoke((MethodInvoker)delegate{Log(text);});}catch(InvalidOperationException){}}
        private bool ChooseSource()
        {
            using(var dialog=new FolderBrowserDialog{Description="Выбери папку комплекта BarsManager3000 или папку _soft",SelectedPath=BundleFolders.PickerStart(source),ShowNewFolderButton=false})
            {

                if(ScopedDialogs.Show(this, dialog)!=DialogResult.OK)return false;
                source=SoftwarePackageStorage.Folder(dialog.SelectedPath);sourceLabel.Text=source;return true;
            }
        }
        private void RequestCancel(bool close){closeAfter|=close;cancelled=true;cancel.Enabled=false;if(session!=null)session.Cancel();activity.Text="Отменяем операцию…";}
        private void Lock(bool value)
        {
            if(value){if(checking)transfer.BeginOperation("Проверяем список программ…",false);else transfer.BeginOperation("Подготавливаем работу с выбранными программами…");}
            busy=value;if(sqlTab!=null)sqlTab.Enabled=!value||checking;folderButton.Enabled=refresh.Enabled=install.Enabled=remove.Enabled=access.Enabled=associations.Enabled=back.Enabled=!value;cancel.Enabled=value;

            back.Enabled=!value||checking||ManagerApplicationContext.ActiveContext!=null;UpdateActions();
            if(!value&&!IsDisposed){if(transfer.Snapshot.Active)transfer.FinishOperation(cancelled?"Операция отменена":"Операция остановлена. Результат — в журнале.",cancelled:cancelled);if(transfer.Snapshot.Complete||transfer.Snapshot.Failed)activity.Text=transfer.Snapshot.Stage;UpdateRows();if(closeAfter)BeginInvoke((MethodInvoker)delegate{Close();});}
        }
        private async Task RefreshPackages(bool resolveUnknownVersion=false)
        {
            if(busy||preview)return;bool allowAny=true;checking=true;cancelled=false;Lock(true);if(resolveUnknownVersion)transfer.BeginOperation("Проверяем программы и версии…",true,SoftwareCatalog.Apps.Length*2+1);bool scanSucceeded=false;sourceLabel.Text=source??"Папка не выбрана";
            try
            {
                offline.Clear();packages.Clear();checks.Clear();selection.Clear();UpdateRows();
                var installed=await Task.Run(()=>PerformanceTrace.Measure("software.installed.worker",()=>SoftwareCatalog.Apps.Select(app=>{var found=SoftwareDetection.Check(app.Id,allowAny);if(resolveUnknownVersion)transfer.CompleteStep();return found;}).ToArray()));
                if(cancelled||IsDisposed)return;foreach(var check in installed)checks[check.Id]=check;
                UpdateRows();
                if(checks.Values.Any(c=>c.State==LibraryState.Unknown && c.Detail.Contains("доступ")))
                {
                    Progress("Уточняем регистрацию с правами администратора");
                    using(var elevated=await Task.Run(()=>LibrarySession.Start(()=>cancelled))){session=elevated;var snapshot=await Task.Run(()=>elevated.ExecuteSystem("software-query",allowAny?"allow-anydesk32":"x64-only","",Progress));if(snapshot.ExitCode!=0)throw new InvalidOperationException(snapshot.Message);if(cancelled||IsDisposed)return;foreach(var check in SoftwareDetection.ParseQuery(snapshot.Message))checks[check.Id]=check;session=null;}
                }
                var local=await Task.Run(()=>PerformanceTrace.Measure("software.local-packages.worker",()=>{LibraryCancellation.Current=()=>cancelled; try {var list=SoftwareCatalog.Local(source,Progress,"Локальный _soft",resolveUnknownVersion);if(Directory.Exists(SoftwareSources.CacheFolder))foreach(string folder in Directory.GetDirectories(SoftwareSources.CacheFolder))list.AddRange(SoftwareCatalog.Local(folder,Progress,"Кэш менеджера",resolveUnknownVersion));return list;} finally {LibraryCancellation.Current=null;}}));
                if(resolveUnknownVersion)transfer.CompleteStep();if(cancelled||IsDisposed)return;foreach(var app in SoftwareCatalog.Apps){var candidate=SoftwareSources.Best(local,app.Id);if(candidate!=null){offline[app.Id]=candidate;packages[app.Id]=candidate;}}
                UpdateRows();
                var updates=resolveUnknownVersion ? await Task.WhenAll(SoftwareCatalog.Apps.Select(app=>Task.Run(()=>
                {
                    LibraryCancellation.Current=()=>cancelled;
                    try{var found=PerformanceTrace.Measure("software.online."+app.Id,()=>latestSource(app.Id));if(found!=null)transfer.CompleteStep();return found;}catch(Exception ex){Progress(app.Name+": официальный источник недоступен. "+ex.Message);return null;}finally{LibraryCancellation.Current=null;}
                }))) : new SoftwarePackage[0];
                if(cancelled||IsDisposed)return;foreach(var candidate in updates.Where(p=>p!=null)){SoftwarePackage old;if(!packages.TryGetValue(candidate.Id,out old)||SoftwareCatalog.Compare(candidate.Version,old.Version)>=0)packages[candidate.Id]=candidate;}
                if(cancelled)throw new OperationCanceledException();
                selection.Clear();foreach(var app in SoftwareCatalog.Apps){if(packages.ContainsKey(app.Id)&&(checks[app.Id].State==LibraryState.Missing || checks[app.Id].State==LibraryState.Installed && packages[app.Id].Version!="0.0" && SoftwareCatalog.Compare(packages[app.Id].Version,checks[app.Id].Version)>0))selection.Add(app.Id);}
                scanSucceeded=true;Log(resolveUnknownVersion&&updates.Any(u=>u==null)?"Проверка завершена. Официальный источник в сети недоступен, предлагается установка проверенных локальных копий программ.":"Проверка завершена.");activity.Text="Выбери программы и проверь источник установщиков.";
            }
            catch(OperationCanceledException){if(!IsDisposed)Log("Проверка отменена");}catch(Exception ex){if(!IsDisposed)Log("Проверка не завершена: "+ex.Message);}
            finally{transfer.FinishOperation(cancelled?"Проверка отменена":scanSucceeded?"Проверка списка завершена. Выбери действие.":"Проверка не завершена — прочитай журнал",success:resolveUnknownVersion&&scanSucceeded&&transfer.WorkFinished,failed:!scanSucceeded&&!cancelled,cancelled:cancelled);checking=false;if(!IsDisposed)Lock(false);}
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
            if(install==null)return;
            bool remoteSelected=SoftwareCatalog.Apps.Any(app=>app.Remote&&selection.Contains(app.Id));
            remote.Enabled=!busy&&remoteSelected;
            if(!remoteSelected)remote.Checked=false;
            install.Enabled=!busy&&ChosenInstallable().Length>0;remove.Enabled=!busy&&ChosenInstalled().Length>0;access.Enabled=!busy&&ChosenInstalled(true).Length>0;
        }
        private async Task ConfigureSelected()
        {
            if(busy||preview)return;string[] ids=ChosenInstalled(true);if(ids.Length==0)return;var secrets=new Dictionary<string,string>();
            foreach(string id in ids)using(var dialog=new SoftwareRemoteDialog(Colors,Icon,id)){if(await ModuleNavigation.ShowOwnedAsync(this, dialog)!=DialogResult.OK)return;secrets[id]=dialog.Password;}
            cancelled=false;Lock(true);int completed=0;transfer.SetWork(ids.Length);
            try
            {
                using (await WindowsOperationQueue.EnterAsync(() => Progress(WindowsOperationQueue.WaitingMessage), () => cancelled))
                using(var active=await Task.Run(()=>LibrarySession.Start(()=>cancelled)))
                {
                    session=active;foreach(string id in ids){if(cancelled)throw new OperationCanceledException();var request=new SoftwareInstallRequest {Id=id,Version=checks[id].Version,Sha256="",AllowAnyDesk32=true,ConfigureRemote=true,Password=secrets[id]};var result=await Task.Run(()=>active.ExecuteSystem("software-access",request.Serialize(),"",Progress));Log(SoftwareCatalog.Find(id).Name+": "+result.Message);if(result.ExitCode!=0){transfer.FinishOperation(result.Message,failed:true);break;}completed++;transfer.CompleteStep();}
                }
            }catch(OperationCanceledException){transfer.FinishOperation("Настройка отменена",cancelled:true);Log("Настройка отменена");}catch(Exception ex){transfer.FinishOperation("Настройка прервана: "+ex.Message,failed:true);Log("Настройка прервана: "+ex.Message);}finally{if(!cancelled&&!transfer.Snapshot.Failed&&completed==ids.Length)transfer.FinishOperation("Настройка доступа завершена.",success:true);session=null;secrets.Clear();Lock(false);}
        }
        private async Task RemoveSelected()
        {
            if(busy||preview)return;string[] ids=ChosenInstalled();if(ids.Length==0)return;
            if(BrandMessageBox.Show(this,"Удалить программы и их настройки?\r\n"+String.Join("\r\n",ids.Select(id=>SoftwareCatalog.Find(id).Name))+"\r\n\r\nНастройки текущего пользователя и общие настройки будут очищены. Параметры удалённого доступа и ID могут сброситься. Текущий удалённый сеанс может прерваться. Документы не удаляются.","Удаление программ",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            cancelled=false;Lock(true);transfer.Report(new TransferProgress{Stage="Подготавливаем удаление выбранных программ…"});bool reboot=false;int completed=0;transfer.SetWork(ids.Length);
            try
            {
                using (await WindowsOperationQueue.EnterAsync(() => Progress(WindowsOperationQueue.WaitingMessage), () => cancelled))
                using(var active=await Task.Run(()=>LibrarySession.Start(()=>cancelled)))
                {
                    session=active;
                    foreach(string id in ids)
                    {
                        if(cancelled)throw new OperationCanceledException();SoftwareMaintenance.PreserveUserDocuments(id,Log);var result=await Task.Run(()=>active.ExecuteSystem("software-remove",id,"",Progress));Log(SoftwareCatalog.Find(id).Name+": "+result.Message);
                        if(result.Reboot){transfer.FinishOperation("Для продолжения нужна перезагрузка.");reboot=true;break;}if(result.ExitCode!=0||result.State!=LibraryState.Missing){transfer.FinishOperation(result.Message,failed:true);break;}
                        try{SoftwareMaintenance.CleanupUser(id,Log);}finally{checks[id]=new SoftwareCheck {Id=id,Version="",Executable="",State=LibraryState.Missing,Detail="Не установлена"};selection.Remove(id);UpdateRows();}completed++;transfer.CompleteStep();
                    }
                }
            }catch(OperationCanceledException){transfer.FinishOperation("Удаление отменено. Проверь состояние программ",cancelled:true);Log("Удаление отменено. Проверь состояние программ");}catch(Exception ex){transfer.FinishOperation("Удаление не завершено: "+ex.Message,failed:true);Log("Удаление или очистка настроек не завершены: "+ex.Message);}finally{if(!cancelled&&completed==ids.Length)transfer.FinishOperation("Удаление подтверждено.",success:true);session=null;Lock(false);}
            if(reboot&&!closeAfter)await RestartOffer.Show(this);
        }
        internal string InstallConfirmation(string[] ids)
        {
            string text="Установить или обновить выбранные программы?";
            var remoteUpdates=ids.Where(id=>SoftwareCatalog.Find(id).Remote&&checks.ContainsKey(id)&&checks[id].State==LibraryState.Installed).Select(id=>SoftwareCatalog.Find(id).Name).ToArray();
            if(remoteUpdates.Length>0)text+="\r\n\r\nОбновление "+String.Join(" / ",remoteUpdates)+" может прервать текущий удалённый сеанс.";
            if(remote.Checked&&ids.Any(id=>SoftwareCatalog.Find(id).Remote))text+="\r\n\r\nДля выбранных AnyDesk и RuDesktop будет настроен постоянный пароль.";
            return text;
        }
        private async Task Install()
        {
            if(busy||preview)return;var ids=ChosenInstallable();if(ids.Length==0){activity.Text="Отметь программы для установки.";return;}
            if(String.IsNullOrWhiteSpace(source)&&ids.Any(id=>!Path.IsPathRooted(packages[id].File)||!File.Exists(packages[id].File))&&!ChooseSource())return;
            var secrets=new Dictionary<string,string>();
            if(remote.Checked)foreach(string id in ids.Where(id=>SoftwareCatalog.Find(id).Remote))using(var dialog=new SoftwareRemoteDialog(Colors,Icon,id)) {if(await ModuleNavigation.ShowOwnedAsync(this, dialog)!=DialogResult.OK)return;secrets[id]=dialog.Password;}
            if(BrandMessageBox.Show(this,InstallConfirmation(ids),Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            cancelled=false;Lock(true);bool reboot=false;
            transfer.SetWork(ids.Length*2);var ready=new List<SoftwarePackage>();
            try
            {
                foreach(string id in ids)
                {
                    if(cancelled)break;var package=packages[id];
                    var current=await Task.Run(()=>SoftwareDetection.Check(id,true));checks[id]=current;
                    if(!SoftwareMaintenance.CanInstall(current,package)){if(current.State!=LibraryState.Installed)throw new IOException(SoftwareCatalog.Find(id).Name+": состояние требует проверки");Log(SoftwareCatalog.Find(id).Name+": установка не требуется или состояние требует проверки");UpdateRows();transfer.CompleteStep();transfer.CompleteStep();continue;}
                    SoftwarePackage cached;
                    if((!Path.IsPathRooted(package.File)||!File.Exists(package.File))&&offline.TryGetValue(id,out cached)&&SoftwareCatalog.Compare(cached.Version,package.Version)==0&&(String.IsNullOrEmpty(package.Sha256)||String.Equals(cached.Sha256,package.Sha256,StringComparison.OrdinalIgnoreCase)))package=cached;
                    if(!Path.IsPathRooted(package.File)||!File.Exists(package.File))
                    {
                        try{package=await Task.Run(()=>SoftwarePackageStorage.Download(package,source,LogProgress,()=>cancelled,p=>{p.Stage+=" · файл "+(Array.IndexOf(ids,id)+1)+" из "+ids.Length;transfer.Report(p);}));packages[id]=package;offline[id]=package;}
                        catch(OperationCanceledException){throw;}
                        catch(Exception ex)
                        {
                            SoftwarePackage fallback;if(!offline.TryGetValue(id,out fallback))throw new IOException(SoftwareCatalog.Find(id).Name+": загрузка не удалась, проверенного локального пакета нет. "+ex.Message);
                            Log(SoftwareCatalog.Find(id).Name+": загрузка не удалась. Локальный пакет "+fallback.Version);
                            if(BrandMessageBox.Show(this,"Использовать локальный "+SoftwareCatalog.Find(id).Name+" "+fallback.Version+" вместо сетевого пакета?",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)throw new OperationCanceledException();package=fallback;
                        }
                    }
                    SoftwareCatalog.Validate(package,true);
                    ready.Add(package);transfer.CompleteStep();
                }
                if(cancelled)throw new OperationCanceledException();
                using (await WindowsOperationQueue.EnterAsync(() => Progress(WindowsOperationQueue.WaitingMessage), () => cancelled))
                using(var active=await Task.Run(()=>LibrarySession.Start(()=>cancelled)))
                {
                    session=active;
                    foreach(var package in ready)
                    {
                        if(cancelled){break;}string id=package.Id;
                        var request=new SoftwareInstallRequest {Id=id,Version=package.Version,Sha256=package.Sha256,AllowAnyDesk32=true,ConfigureRemote=secrets.ContainsKey(id),Password=secrets.ContainsKey(id)?secrets[id]:""};
                        transfer.Report(new TransferProgress{Stage="Устанавливаем "+SoftwareCatalog.Find(id).Name+"…",File=Path.GetFileName(package.File)});
                        var result=await Task.Run(()=>active.ExecuteSystem("software-install",request.Serialize(),package.File,Progress));
                        if(LibraryPlan.Success(result.ExitCode)&&result.State==LibraryState.Installed)transfer.CompleteStep();else transfer.FinishOperation(result.Message,failed:true);Log(SoftwareCatalog.Find(id).Name+": "+result.Message);bool allow=true;checks[id]=await Task.Run(()=>SoftwareDetection.Check(id,allow));UpdateRows();
                        if(result.Reboot){reboot=true;break;}if(!LibraryPlan.Success(result.ExitCode)||result.State!=LibraryState.Installed){break;}
                    }
                }
            }
            catch(OperationCanceledException){transfer.Stage("Операция отменена",cancelled:true);Log("Отменено. Проверь программы перед повтором");}catch(Exception ex){transfer.Stage("Очередь остановлена: "+ex.Message,failed:true);Log("Очередь остановлена: "+ex.Message);}
            finally{session=null;if(!cancelled&&transfer.WorkFinished&&!transfer.Snapshot.Failed)transfer.FinishOperation("Установка выбранных программ завершена.",success:true);var state=transfer.Snapshot;if(state.Active&&!state.Complete&&!state.Failed)transfer.FinishOperation(cancelled?"Операция отменена.":"Обработка выбранных программ завершена. Подробности в журнале.",cancelled:cancelled);Lock(false);secrets.Clear();}
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
    internal sealed class SoftwareRemoteDialog : BrandWindow
    {
        private readonly BrandTextBox input=new BrandTextBox(),confirmation=new BrandTextBox();
        private readonly string id;
        private readonly System.Windows.Forms.Timer keyboardTimer=new System.Windows.Forms.Timer();
        private readonly ToolTip keyboardTips=new ToolTip();
        private readonly BrandButton ok;private readonly Label validation;private readonly Image logo;
        internal string Password { get; private set; }
        internal static bool PasswordsMatch(string password,string repeated){return password!=null&&password.Length>=8&&password.Length<=128&&!password.Any(Char.IsControl)&&String.Equals(password,repeated,StringComparison.Ordinal);}
        internal SoftwareRemoteDialog(ThemePalette colors,Icon icon,string appId)
        {
            NativeWindowTheme.Bind(this,()=>colors.Dark);
            SuspendLayout();id=appId;Text="Неконтролируемый доступ к "+SoftwareCatalog.Find(id).Name;Icon=icon==null?BrandAssets.LoadIcon():(Icon)icon.Clone();AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(660,400);Font=new Font("Segoe UI",10);BackColor=colors.Canvas;ForeColor=colors.Text;StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;
            var layout=new BufferedLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(24,18,24,18),ColumnCount=1,RowCount=10};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int h in new[]{70,24,38,24,38,24,26,0,76,44})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            var heading=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,64));heading.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            heading.Controls.Add(new FlushLabel {WrapLines=true,InkLeft=g=>0,Text="Постоянный пароль для "+SoftwareCatalog.Find(id).Name+".\r\nПодключение без подтверждения на этом компьютере.",Dock=DockStyle.Fill,ForeColor=colors.Text,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty},0,0);
            logo=BrandAssets.LoadSoftwareIcon(id);PictureBox appIcon = id == "rudesktop" ? new SmoothSoftwareIcon() : new PictureBox();
            appIcon.Image = logo;
            appIcon.SizeMode = PictureBoxSizeMode.Zoom;
            appIcon.Dock = DockStyle.Fill;
            appIcon.Margin = new Padding(8,8,0,8);
            heading.Controls.Add(appIcon, 1, 0);layout.Controls.Add(heading,0,0);
            layout.Controls.Add(new InputCaptionLabel {Text="Пароль:",Dock=DockStyle.Fill,ForeColor=colors.Text,Margin=Padding.Empty,TextAlign=ContentAlignment.BottomLeft,Padding=new Padding(0,0,0,2)},0,1);layout.Controls.Add(new InputCaptionLabel {Text="Подтверди пароль:",Dock=DockStyle.Fill,ForeColor=colors.Text,Margin=Padding.Empty,TextAlign=ContentAlignment.BottomLeft,Padding=new Padding(0,0,0,2)},0,3);
            foreach(var box in new[]{input,confirmation}){box.Dock=DockStyle.Fill;box.UseSystemPasswordChar=true;box.MaxLength=128;box.Palette=colors;box.BackColor=colors.SurfaceAlt;box.ForeColor=colors.Text;box.Margin=new Padding(0,0,0,4);}
            layout.Controls.Add(PasswordRow(input,colors),0,2);layout.Controls.Add(PasswordRow(confirmation,colors),0,4);
            var languageRow=new BufferedLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};
            languageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int width in new[]{10,40,28})languageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,width));languageRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var language=new KeyboardLanguageLabel{Name="keyboardLanguage",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,Padding=new Padding(0,0,1,0),ForeColor=colors.Dark?colors.Warning:Color.FromArgb(190,35,35),Font=new Font("Segoe UI",10F,FontStyle.Bold),Margin=Padding.Empty};languages.Add(language);languageRow.Controls.Add(language,0,0);layout.Controls.Add(languageRow,0,5);
            validation=new FlushLabel {InkLeft=g=>0,Text="От 8 до 128 символов",TextAlign=ContentAlignment.MiddleLeft,Dock=DockStyle.None,ForeColor=colors.Muted,Margin=Padding.Empty,Font=new Font("Segoe UI",9)};
            layout.Controls.Add(new Label {Text=id=="rudesktop"?"Во время настройки администратор этого компьютера может увидеть пароль в параметрах запуска RuDesktop.":"Пароль передаётся напрямую в AnyDesk. В журнале менеджера он не сохраняется.",Dock=DockStyle.Fill,Name="passwordNotice",TextAlign=ContentAlignment.MiddleLeft,ForeColor=colors.Dark?colors.Warning:Color.FromArgb(190,35,35),Font=new Font("Segoe UI",9),Margin=new Padding(0,12,0,16)},0,8);
            var actions=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,12));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var cancel=new BrandButton {Text="Отмена",Palette=colors,Dock=DockStyle.Fill,DialogResult=DialogResult.Cancel,Margin=Padding.Empty};actions.Controls.Add(cancel,0,0);
            ok=new BrandButton {Text="Продолжить",Palette=colors,Primary=true,Dock=DockStyle.Fill,Margin=Padding.Empty,Enabled=false};
            Action validate=delegate{ok.Enabled=PasswordsMatch(input.Text,confirmation.Text);validation.Text=input.Text.Length<8?"Введи не менее 8 символов":!String.Equals(input.Text,confirmation.Text,StringComparison.Ordinal)?"Пароли не совпадают":"Пароли совпадают";};input.TextChanged+=delegate{validate();};confirmation.TextChanged+=delegate{validate();};
            ok.Click += delegate
            {
                if (!PasswordsMatch(input.Text, confirmation.Text)) return;
                Password = input.Text;
                DialogResult = DialogResult.OK;
            };
            actions.Controls.Add(ok,2,0);layout.Controls.Add(actions,0,9);Controls.Add(layout);var validationHost=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};layout.Controls.Remove(languageRow);languageRow.Controls.Remove(language);validationHost.Controls.Add(language);language.Dock=DockStyle.None;language.Anchor=AnchorStyles.Top|AnchorStyles.Right;language.Padding=Padding.Empty;languageRow.Dispose();layout.Controls.Add(validationHost,0,5);layout.SetRowSpan(validationHost,2);validationHost.Controls.Add(validation);validation.BringToFront();bool validationQueued=false;Action placeValidation=()=>{if(!confirmation.IsHandleCreated||!validationHost.IsHandleCreated||IsDisposed)return;var confirmationFrame=confirmation.Parent;int upper=PointToClient(confirmationFrame.PointToScreen(new Point(0,confirmationFrame.Height))).Y;int lower=PointToClient(validationHost.PointToScreen(new Point(0,validationHost.Height))).Y;int height=validation.Font.Height;int left=PointToClient(confirmationFrame.PointToScreen(Point.Empty)).X;validation.Bounds=new Rectangle(validationHost.PointToClient(PointToScreen(new Point(left,upper+(lower-upper-height)/2))).X,validationHost.PointToClient(PointToScreen(new Point(left,upper+(lower-upper-height)/2))).Y,confirmationFrame.Width,height);using(var graphics=language.CreateGraphics()){int languageWidth=TextRenderer.MeasureText(graphics,"ENG",language.Font,Size.Empty,TextFormatFlags.SingleLine).Width+12;int languageHeight=language.Font.Height+6;var fieldBottom=validationHost.PointToClient(confirmationFrame.PointToScreen(new Point(confirmationFrame.Width,confirmationFrame.Height)));language.Bounds=new Rectangle(fieldBottom.X-languageWidth,Math.Max(0,fieldBottom.Y+2),languageWidth,languageHeight);validation.Width=Math.Max(1,language.Left-validation.Left-8);}language.BringToFront();};Action queueValidation=()=>{if(validationQueued||!IsHandleCreated||IsDisposed)return;validationQueued=true;BeginInvoke((MethodInvoker)delegate{validationQueued=false;placeValidation();});};layout.Layout+=delegate{queueValidation();};Shown+=delegate{queueValidation();};CancelButton=cancel;AcceptButton=ok;
            keyboardTips.IsBalloon=true;keyboardTips.ToolTipIcon=ToolTipIcon.Warning;keyboardTips.ToolTipTitle="Caps Lock включён";keyboardTimer.Interval=200;keyboardTimer.Tick+=delegate{UpdateKeyboard();};Shown+=delegate{keyboardTimer.Start();};InputLanguageChanged+=delegate{UpdateKeyboard();};
            AutoScaleMode=AutoScaleMode.Dpi;ResumeLayout(true);
        }
        private sealed class KeyboardLanguageLabel : Label
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        private sealed class SmoothSoftwareIcon : PictureBox
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                if (Image == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
                float scale = Math.Min((float)ClientSize.Width / Image.Width, (float)ClientSize.Height / Image.Height);
                int width = (int)Math.Round(Image.Width * scale), height = (int)Math.Round(Image.Height * scale);
                var bounds = new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                using (var options = new System.Drawing.Imaging.ImageAttributes())
                {
                    options.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                    e.Graphics.DrawImage(Image, bounds, 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel, options);
                }
            }
        }

        private readonly List<PasswordEyeButton> eyes=new List<PasswordEyeButton>();
        private readonly List<Label> languages=new List<Label>(),caps=new List<Label>();
        private bool capsWasOn;
        private Control PasswordRow(BrandTextBox box,ThemePalette colors)
        {
            var row=new BufferedLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,10));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,40));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,36));row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            box.Anchor=AnchorStyles.Left|AnchorStyles.Right;box.Dock=DockStyle.None;row.Controls.Add(new PathFieldFrame(box),0,0);
            var eye=new PasswordEyeButton{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,4),Palette=colors};eyes.Add(eye);eye.Click+=delegate{eye.Revealed=!eye.Revealed;box.UseSystemPasswordChar=!eye.Revealed;eye.AccessibleName=eye.Revealed?"Скрыть пароль":"Показать пароль";eye.Invalidate();box.Focus();};row.Controls.Add(eye,2,0);
            var warning=new Label{Dock=DockStyle.Fill,Text="⚠",Font=new Font("Segoe UI Symbol",13),ForeColor=Color.Goldenrod,TextAlign=ContentAlignment.MiddleCenter,Margin=Padding.Empty,Visible=false};caps.Add(warning);keyboardTips.SetToolTip(warning,"Caps Lock включён. Проверь регистр букв пароля.");row.Controls.Add(warning,3,0);
            return row;
        }
        internal static string LanguageLabel(string code){return code=="ru"?"РУС":code=="en"?"ENG":(code??"").ToUpperInvariant();}
        private void UpdateKeyboard() {UpdateKeyboardState(InputLanguage.CurrentInputLanguage.Culture.TwoLetterISOLanguageName,Control.IsKeyLocked(Keys.CapsLock));}
        internal void UpdateKeyboardState(string code,bool capsLocked)
        {
            string text=LanguageLabel(code);foreach(var label in languages)label.Text=text;
            bool on=capsLocked;foreach(var label in caps){if(label.Visible!=on)label.Visible=on;if(!on&&capsWasOn)keyboardTips.Hide(label);}
            bool showWarning = on && !capsWasOn && (input.Focused || confirmation.Focused);
            capsWasOn = on;
            if (showWarning && Visible)
            {
                var label = caps[input.Focused ? 0 : 1];
                keyboardTips.Show("Caps Lock включён: проверь регистр букв", label, 0, label.Height, 3000);
            }
        }
        protected override void Dispose(bool disposing){if(disposing){keyboardTimer.Dispose();keyboardTips.Dispose();}base.Dispose(disposing);if(disposing&&logo!=null)logo.Dispose();}
    }
}
