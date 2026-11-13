using System;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace ServiceTool
{
 internal sealed class BundleProgressBar:DownloadProgressView {}
 internal class BundleDifferencesForm:ManagerModuleForm
 {
  readonly ServiceGrid grid=new ServiceGrid();
  internal BundleDifferencesForm(System.Collections.Generic.IEnumerable<string[]> rows):this(rows.Select(r=>new BundleDifferenceItem {Path=r[0],State=r[1]}).ToArray()){}
  internal BundleDifferencesForm(BundleDifferenceItem[] rows,bool sql=false,Func<System.Threading.CancellationToken,System.Threading.Tasks.Task> recheck=null,string pickerTitle=null,bool shortcuts=false,bool single=false):base(sql?(pickerTitle??"Наборы SQL в облачном хранилище"):"Результат проверки комплекта",sql?new Size(570,460):new Size(897,570))
  {
   MinimumSize=SizeFromClientSize(sql?new Size(420,190):new Size(520,240));ClientSize=sql?new Size(570,460):new Size(897,570);FormBorderStyle=FormBorderStyle.Sizable;MaximizeBox=true;MinimizeBox=false;Root.RowStyles[0].Height=60;HideThemeSelector();
   var body=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,sql?40:0));if(sql)body.Controls.Add(ModuleHeading(pickerTitle??"Наборы SQL в облачном хранилище"),0,0);body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
   grid.Dock=DockStyle.Fill;grid.Margin=Padding.Empty;grid.BorderStyle=BorderStyle.None;grid.CellBorderStyle=DataGridViewCellBorderStyle.Single;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.Single;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.MultiSelect=false;grid.EditMode=DataGridViewEditMode.EditProgrammatically;grid.ColumnHeadersDefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;grid.ReadOnly=true;grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=grid.AllowUserToResizeRows=false;grid.RowHeadersVisible=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;grid.ColumnHeadersHeight=36;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;grid.ColumnHeadersDefaultCellStyle.Font=new Font("Segoe UI",10,FontStyle.Bold);grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;grid.DefaultCellStyle.WrapMode=DataGridViewTriState.False;grid.DefaultCellStyle.Padding=new Padding(6);grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="choose",HeaderText="",Width=36,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});grid.Columns.Add("file",sql?"Название набора":"Файлы");if(!sql){grid.Columns.Add("action","Действие");grid.Columns[2].MinimumWidth=120;}if(sql)grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.TouchScrollingEnabled=true;if(shortcuts){grid.Columns[0].HeaderText="Распаковать";grid.Columns[0].Width=115;grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="shortcut",HeaderText="Ярлык",Width=86,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});}if(shortcuts||single)grid.SelectionMode=DataGridViewSelectionMode.CellSelect;
   foreach(var item in rows)
   {
    int index=sql?grid.Rows.Add(item.Selected,item.Path):grid.Rows.Add(item.Selected,item.Path,item.State=="Отсутствует — скачать"?"Отсутствует - будет скачано":item.State);
    grid.Rows[index].Tag=item;grid.Rows[index].Cells[1].ToolTipText=item.Path;
   }
   Action selectionChanged=delegate{};
   Action<int,int> toggle=(index,column)=>{
    if(index<0)return;
    var row=grid.Rows[index];var item=(BundleDifferenceItem)row.Tag;
    if(!item.Allowed)return;
    if(shortcuts&&column==2)
    {
     if(item.Selected&&item.ShortcutAvailable){item.Shortcut=!item.Shortcut;row.Cells[2].Value=item.Shortcut;grid.InvalidateCell(2,index);}
     return;
    }
    if(single)foreach(DataGridViewRow other in grid.Rows)
    {
     if(other==row)continue;
     ((BundleDifferenceItem)other.Tag).Selected=false;other.Cells[0].Value=false;
    }
    item.Selected=!item.Selected;row.Cells[0].Value=item.Selected;
    if(shortcuts&&!item.Selected){item.Shortcut=false;row.Cells[2].Value=false;}
    if(shortcuts)grid.InvalidateCell(2,index);
    selectionChanged();
   };
   grid.CellMouseDown+=delegate(object sender,DataGridViewCellMouseEventArgs e){if(e.Button==MouseButtons.Left&&e.ColumnIndex>=0)toggle(e.RowIndex,e.ColumnIndex);};
   grid.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Space&&grid.CurrentCell!=null){toggle(grid.CurrentCell.RowIndex,grid.CurrentCell.ColumnIndex);e.Handled=e.SuppressKeyPress=true;}};
   grid.CellPainting+=delegate(object sender,DataGridViewCellPaintingEventArgs e){if(e.RowIndex<0||(e.ColumnIndex!=0&&(!shortcuts||e.ColumnIndex!=2)))return;var item=(BundleDifferenceItem)grid.Rows[e.RowIndex].Tag;if(item==null)return;bool selected=e.ColumnIndex==0?item.Selected:item.Shortcut;bool allowed=e.ColumnIndex==0?item.Allowed:item.Selected&&item.ShortcutAvailable;e.Paint(e.CellBounds,DataGridViewPaintParts.Background|DataGridViewPaintParts.SelectionBackground);int size=(int)Math.Round(18*e.Graphics.DpiX/96F);var rect=new Rectangle(e.CellBounds.Left+(e.CellBounds.Width-size)/2,e.CellBounds.Top+(e.CellBounds.Height-size)/2,size,size);var saved=e.Graphics.Save();try{e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var fill=new SolidBrush(selected?Colors.Accent:Colors.Surface))e.Graphics.FillEllipse(fill,rect);using(var pen=new Pen(allowed?Colors.Accent:Colors.Border,Math.Max(1,e.Graphics.DpiX/96F)))e.Graphics.DrawEllipse(pen,rect);if(selected)using(var dot=new SolidBrush(Colors.AccentText))e.Graphics.FillEllipse(dot,Rectangle.Inflate(rect,-size/3,-size/3));}finally{e.Graphics.Restore(saved);}e.Paint(e.CellBounds,DataGridViewPaintParts.Border);e.Handled=true;};var slot=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};var scroll=new LibraryGridScrollBar {Dock=DockStyle.None,Width=14,Palette=Colors,AlignToRows=true};grid.ScrollBars=ScrollBars.None;scroll.Bind(grid);slot.Controls.Add(grid);slot.Controls.Add(scroll);grid.Dock=DockStyle.None;grid.Anchor=AnchorStyles.Top|AnchorStyles.Left;Action fitColumns=()=>{if(sql)return;int available=grid.ClientSize.Width-grid.Columns[0].Width;if(available<=grid.Columns[2].MinimumWidth)return;int action=Math.Max(grid.Columns[2].MinimumWidth,(int)Math.Round(available*.28));grid.Columns[1].Width=available-action;grid.Columns[2].Width=action;};bool fitting=false,queued=false;Action fit=()=>{if(fitting||grid.IsDisposed)return;fitting=true;try{slot.BackColor=Colors.Canvas;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;grid.Width=Math.Max(1,slot.ClientSize.Width-(scroll.Visible?scroll.Width:0));fitColumns();GridViewportLayout.UniformRows(grid);grid.Height=GridViewportLayout.WholeRowsHeight(grid,Math.Max(1,slot.ClientSize.Height));scroll.UpdateViewport();grid.Width=Math.Max(1,slot.ClientSize.Width-(scroll.Visible?scroll.Width:0));fitColumns();scroll.Bounds=new Rectangle(grid.Right,grid.Top,scroll.Width,grid.Height);}finally{fitting=false;}};Action queue=()=>{if(queued||!slot.IsHandleCreated||slot.IsDisposed)return;queued=true;slot.BeginInvoke((MethodInvoker)delegate{queued=false;fit();});};slot.SizeChanged+=delegate{queue();};grid.ColumnWidthChanged+=delegate{queue();};scroll.VisibleChanged+=delegate{queue();};Shown+=delegate{queue();};body.Controls.Add(slot,0,1);
   var actions=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(0,10,0,0),Padding=Padding.Empty};for(int i=0;i<3;i++)actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/3));actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));var all=Button("Выбрать всё",false);var none=Button(sql?"Отмена":"Снять выбор",false);var apply=Button("Применить",sql);selectionChanged=delegate{if(single){all.Enabled=false;apply.Enabled=rows.Any(item=>item.Selected);}else if(sql)all.Text=rows.Length>0&&rows.All(item=>item.Selected)?"Снять выбор":"Выбрать всё";};all.Click+=delegate{bool select=!sql||!rows.All(item=>item.Selected);foreach(DataGridViewRow row in grid.Rows){var item=(BundleDifferenceItem)row.Tag;if(item.Allowed){item.Selected=select;row.Cells[0].Value=select;if(shortcuts&&!select){item.Shortcut=false;row.Cells[2].Value=false;}}}selectionChanged();};none.Click+=delegate{if(sql){DialogResult=DialogResult.Cancel;Close();return;}foreach(DataGridViewRow row in grid.Rows){var item=(BundleDifferenceItem)row.Tag;if(item.Allowed){item.Selected=false;row.Cells[0].Value=false;}}};apply.Click+=delegate{if(sql)DialogResult=DialogResult.OK;Close();};if(sql){CancelButton=none;AcceptButton=apply;}selectionChanged();actions.Controls.Add(all,0,0);actions.Controls.Add(none,1,0);actions.Controls.Add(apply,2,0);if(recheck==null)ModuleNavigation.StandardFooter(actions,all,none,apply);
   else
   {
    var refresh=Button("Перепроверить файлы",false);
    System.Threading.CancellationTokenSource localCheck=null;
    FormClosing+=delegate{if(localCheck!=null)localCheck.Cancel();};
    refresh.Click+=async delegate
    {
     if(localCheck!=null){localCheck.Cancel();return;}
     localCheck=new System.Threading.CancellationTokenSource();
     refresh.Text="Отменить проверку";all.Enabled=none.Enabled=apply.Enabled=grid.Enabled=false;
     try
     {
      await recheck(localCheck.Token);
      if(IsDisposed)return;
      foreach(DataGridViewRow row in grid.Rows){var item=(BundleDifferenceItem)row.Tag;row.Cells[0].Value=item.Selected;row.Cells[2].Value=item.State;}
      grid.Invalidate();
     }
     catch(OperationCanceledException){}
     catch(Exception ex){if(!IsDisposed)BrandMessageBox.Show(this,"Не удалось перепроверить файлы. "+ex.Message);}
     finally{localCheck.Dispose();localCheck=null;if(!IsDisposed){refresh.Text="Перепроверить файлы";all.Enabled=none.Enabled=apply.Enabled=grid.Enabled=true;}}
    };
    ModuleNavigation.StandardFooter(actions,all,none,refresh,apply);
   }Action alignFooter=()=>{actions.Controls[0].Padding=new Padding(0,0,scroll.Visible?scroll.Width:0,0);actions.PerformLayout();};scroll.VisibleChanged+=delegate{alignFooter();};Shown+=delegate{alignFooter();};Shown+=delegate{BeginInvoke((MethodInvoker)delegate{fit();if(sql){int limit=Screen.FromControl(this).WorkingArea.Height-(Height-ClientSize.Height)-16;int desired=Math.Min(limit,ClientSize.Height-slot.ClientSize.Height+grid.ColumnHeadersHeight+grid.Rows.Cast<DataGridViewRow>().Sum(row=>row.Height));ClientSize=new Size(ClientSize.Width,Math.Max(1,desired));fit();}int unused=slot.ClientSize.Height-grid.Height;if(unused>0){ClientSize=new Size(ClientSize.Width,ClientSize.Height-unused);}if(sql){var area=Screen.FromControl(Owner??this).WorkingArea;var anchor=Owner==null?Bounds:Owner.Bounds;Bounds=WindowPlacement.Fit(new Rectangle(anchor.Left+(anchor.Width-Width)/2,anchor.Top+(anchor.Height-Height)/2,Width,Height),area);}});};body.Controls.Add(actions,0,2);Root.Controls.Add(body,0,1);SetTheme(Colors.Dark,false);
  }
  protected override void OnThemeChanged(){if(grid==null)return;grid.BackgroundColor=Colors.Canvas;grid.DefaultCellStyle.BackColor=Colors.Surface;grid.DefaultCellStyle.ForeColor=Colors.Text;grid.DefaultCellStyle.SelectionBackColor=Colors.Selection;grid.DefaultCellStyle.SelectionForeColor=Colors.SelectionText;grid.GridColor=Colors.Border;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.ForeColor=Colors.Text;grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=Colors.Text;if(grid.Parent!=null)foreach(var scroll in grid.Parent.Controls.OfType<LibraryGridScrollBar>()){scroll.Palette=Colors;scroll.BackColor=Colors.Surface;}}
 }
}
