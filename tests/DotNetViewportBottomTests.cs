using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class DotNetViewportBottomTests
 {
  static System.Collections.Generic.IEnumerable<Control> All(Control p){foreach(Control c in p.Controls){yield return c;foreach(var n in All(c))yield return n;}}
  [STAThread]static int Main(){int count=0;try{foreach(bool dark in new[]{false,true})foreach(float scale in new[]{1F,1.25F,1.5F,2F})using(var form=new DotNetUpdatesForm(()=>null)){
   form.SetTheme(dark,false);form.Show();Application.DoEvents();if(scale!=1)form.Scale(new SizeF(scale,scale));Application.DoEvents();var grid=All(form).OfType<ServiceGrid>().Single();var palette=ThemePalette.Create(dark);if(grid.BackgroundColor!=palette.Canvas||grid.DefaultCellStyle.BackColor!=palette.Surface)throw new Exception("Unused viewport must be canvas, rows must retain surface");count++;
   foreach(int rows in new[]{1,80,2}){grid.Rows.Clear();for(int n=0;n<rows;n++)grid.Rows.Add(false,"Fixture "+n,"1.0","1.1","Обновить");Application.DoEvents();var scroll=All(form).OfType<LibraryGridScrollBar>().Single();scroll.UpdateViewport();Application.DoEvents();using(var image=new Bitmap(grid.Width,grid.Height)){grid.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));if(rows==1&&grid.Height>grid.ColumnHeadersHeight+grid.Rows[0].Height)throw new Exception("Viewport extends beyond complete short list");}count++;}
   using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dotnet-bottom-"+(dark?"dark":"light")+"-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png"));}form.Close();
  }Console.WriteLine("PASS: "+count+" .NET bottom viewport/theme/raster assertions; no network.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
