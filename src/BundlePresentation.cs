using System;
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
  internal BundleDifferencesForm(System.Collections.Generic.IEnumerable<string[]> rows):base("Результат проверки комплекта",new Size(780,480))
  {
   MinimumSize=new Size(640,380);Root.RowStyles[0].Height=70;
   var body=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,40));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,50));body.Controls.Add(ModuleHeading("Файлы, требующие внимания"),0,0);
   grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=false;grid.RowHeadersVisible=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.DefaultCellStyle.Padding=new Padding(6);grid.Columns.Add("file","Файл");grid.Columns.Add("action","Результат проверки");grid.Columns[0].FillWeight=180;grid.TouchScrollingEnabled=true;foreach(var row in rows)grid.Rows.Add(row);body.Controls.Add(grid,0,1);
   var close=Button("Закрыть",false);ModuleNavigation.Configure(close);close.Click+=delegate{Close();};body.Controls.Add(close,0,2);Root.Controls.Add(body,0,1);SetTheme(Colors.Dark,false);
  }
  protected override void OnThemeChanged(){if(grid==null)return;grid.BackgroundColor=grid.DefaultCellStyle.BackColor=Colors.Surface;grid.DefaultCellStyle.ForeColor=Colors.Text;grid.DefaultCellStyle.SelectionBackColor=Colors.Selection;grid.DefaultCellStyle.SelectionForeColor=Colors.SelectionText;grid.GridColor=Colors.Border;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.ForeColor=Colors.Text;}
 }
}
