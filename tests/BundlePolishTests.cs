using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class BundlePolishTests
 {
  static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
  static System.Collections.Generic.IEnumerable<Control> All(Control p){foreach(Control c in p.Controls){yield return c;foreach(var n in All(c))yield return n;}}
  [STAThread]static int Main(){try{
   foreach(bool dark in new[]{false,true}){
    var rows=Enumerable.Range(0,80).Select(i=>new BundleDifferenceItem{Path="_tools/test/file-"+i+".exe",State="Отсутствует — скачать",Allowed=true}).ToArray();
    using(var form=new BundleDifferencesForm(rows)){form.SetTheme(dark,false);form.Show();Application.DoEvents();var grid=All(form).OfType<ServiceGrid>().Single();Check(grid.SelectionMode==DataGridViewSelectionMode.FullRowSelect&&!grid.MultiSelect,"Full row selection");Check(grid.TouchScrollingEnabled,"Touch scrolling enabled");Check(All(form).OfType<LibraryGridScrollBar>().Count()==1,"Branded scrollbar");Check(grid.BorderStyle==BorderStyle.None,"No native black border");Check(grid.ColumnHeadersDefaultCellStyle.Alignment==DataGridViewContentAlignment.MiddleCenter,"Shared header alignment");grid.CurrentCell=grid.Rows[1].Cells[1];typeof(DataGridView).GetMethod("OnCellClick",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(grid,new object[]{new DataGridViewCellEventArgs(1,1)});Check(rows[1].Selected&&grid.Rows[1].Selected,"Click selects entire row and toggles mark");grid.FirstDisplayedScrollingRowIndex=20;Check(grid.FirstDisplayedScrollingRowIndex==20,"Long list scrolls");using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"results-"+(dark?"dark":"light")+".png"));}form.Close();}
    using(var dialog=new BundleCloudDialog(false)){dialog.SetTheme(dark,false);dialog.Show();Application.DoEvents();Check(dialog.ClientSize.Height<300,"Compact cloud dialog");using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cloud-"+(dark?"dark":"light")+".png"));}}
   }
   using(var form=new BundleDownloadForm()){typeof(BundleDownloadForm).GetField("scannedRoot",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,"C:\\fixture");typeof(BundleDownloadForm).GetMethod("UpdateState",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,null);Check(!All(form).OfType<BrandButton>().Single(b=>b.Text=="Результаты").Enabled,"Zero results disabled");}
   Console.WriteLine("PASS: "+count+" result style/selection/scroll/empty-result checks. No network.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
