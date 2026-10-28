using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class BundleLayoutTests
 {
  static System.Collections.Generic.IEnumerable<Control> Children(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in Children(c))yield return child;}}
  [STAThread] static int Main(){int checks=0;try{foreach(bool dark in new[]{false,true})foreach(float scale in new[]{1F,1.25F,1.5F,2F})using(var form=new BundleDownloadForm()){
   form.SetTheme(dark,false);form.Show();Application.DoEvents();if(scale!=1)form.Scale(new SizeF(scale,scale));Application.DoEvents();
   foreach(var button in Children(form).OfType<BrandButton>()){if(button.Visible&&!button.Parent.ClientRectangle.Contains(button.Bounds))throw new Exception("Button clipped: "+button.Text+" at "+scale);checks++;}
   using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bundle-"+(dark?"dark":"light")+"-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png"));}
   form.Close();}
   Console.WriteLine("PASS: "+checks+" bundle button bounds checks; both themes, simulated 100/125/150/200%. No network.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
