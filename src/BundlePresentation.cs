using System;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace ServiceTool
{
 internal sealed class BundleProgressBar:Control
 {
  internal ThemePalette Palette;double value;
  internal double Fraction {get{return value;}set{this.value=Math.Max(0,Math.Min(1,value));Invalidate();}}
  internal BundleProgressBar(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);Height=24;}
  protected override void OnPaint(PaintEventArgs e){var p=Palette??ThemePalette.Create(false);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var rect=new Rectangle(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1));using(var shape=BrandButton.RoundRect(rect,Math.Min(12,Height/2)))using(var track=new SolidBrush(p.SurfaceAlt)){e.Graphics.FillPath(track,shape);var state=e.Graphics.Save();e.Graphics.SetClip(shape);int w=(int)(Width*value);if(w>0)using(var brush=new LinearGradientBrush(rect,p.Accent,ControlPaint.Light(p.Accent),0F))e.Graphics.FillRectangle(brush,0,0,w,Height);e.Graphics.Restore(state);}TextRenderer.DrawText(e.Graphics,(value*100).ToString("0")+"%",Font,rect,p.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.PreserveGraphicsTranslateTransform);}
 }
 internal sealed class BundleDifferencesForm:ManagerModuleForm
 {
  readonly ServiceGrid grid=new ServiceGrid();
  internal BundleDifferencesForm(System.Collections.Generic.IEnumerable<string[]> rows):this(rows.Select(r=>new BundleDifferenceItem {Path=r[0],State=r[1]}).ToArray()){}
  internal BundleDifferencesForm(BundleDifferenceItem[] rows):base("Результат проверки комплекта",new Size(650,350))
  {
   MinimumSize=SizeFromClientSize(new Size(650,350));ClientSize=new Size(650,350);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=MinimizeBox=false;Root.RowStyles[0].Height=60;
   var body=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,36));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,50));body.Controls.Add(ModuleHeading("Выберите файлы для загрузки"),0,0);
   grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=grid.AllowUserToResizeRows=false;grid.RowHeadersVisible=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.ColumnHeadersHeight=36;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;grid.ColumnHeadersDefaultCellStyle.Font=new Font("Segoe UI",10,FontStyle.Bold);grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.DefaultCellStyle.Padding=new Padding(6);grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="choose",HeaderText="",Width=36,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});grid.Columns.Add("file","Файл");grid.Columns.Add("action","Действие");grid.Columns[1].FillWeight=180;grid.TouchScrollingEnabled=true;
   foreach(var row in rows)grid.Rows.Add(row.Selected,row.Path,row.State);
   Action<int> toggle=index=>{if(index<0||!rows[index].Allowed)return;rows[index].Selected=!rows[index].Selected;grid.Rows[index].Cells[0].Value=rows[index].Selected;};grid.CellClick+=delegate(object sender,DataGridViewCellEventArgs e){toggle(e.RowIndex);};grid.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Space&&grid.CurrentRow!=null){toggle(grid.CurrentRow.Index);e.Handled=e.SuppressKeyPress=true;}};
   grid.CellPainting+=delegate(object sender,DataGridViewCellPaintingEventArgs e){if(e.RowIndex<0||e.ColumnIndex!=0)return;e.PaintBackground(e.CellBounds,true);int size=18;var rect=new Rectangle(e.CellBounds.Left+(e.CellBounds.Width-size)/2,e.CellBounds.Top+(e.CellBounds.Height-size)/2,size,size);using(var fill=new SolidBrush(rows[e.RowIndex].Selected?Colors.Accent:Colors.Surface))e.Graphics.FillEllipse(fill,rect);using(var pen=new Pen(rows[e.RowIndex].Allowed?Colors.Accent:Colors.Border))e.Graphics.DrawEllipse(pen,rect);if(rows[e.RowIndex].Selected)using(var dot=new SolidBrush(Colors.AccentText))e.Graphics.FillEllipse(dot,Rectangle.Inflate(rect,-6,-6));e.Handled=true;};body.Controls.Add(grid,0,1);
   var close=Button("Применить выбор",false);ModuleNavigation.Configure(close);close.Width=180;close.Click+=delegate{Close();};body.Controls.Add(close,0,2);Root.Controls.Add(body,0,1);SetTheme(Colors.Dark,false);
  }
  protected override void OnThemeChanged(){if(grid==null)return;grid.BackgroundColor=grid.DefaultCellStyle.BackColor=Colors.Surface;grid.DefaultCellStyle.ForeColor=Colors.Text;grid.DefaultCellStyle.SelectionBackColor=Colors.Selection;grid.DefaultCellStyle.SelectionForeColor=Colors.SelectionText;grid.GridColor=Colors.Border;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.ForeColor=Colors.Text;}
 }
}
