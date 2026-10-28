using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class ToolsCompactTests
 {
  static int count;static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;}
  static System.Collections.Generic.IEnumerable<Control> All(Control p){foreach(Control c in p.Controls){yield return c;foreach(var n in All(c))yield return n;}}
  [STAThread]static int Main(){try{foreach(bool dark in new[]{false,true})foreach(float scale in new[]{1F,1.25F,1.5F,2F})using(var form=new ToolsDeploymentForm(true)){
   form.SetTheme(dark,false);form.Show();Application.DoEvents();if(scale!=1)form.Scale(new SizeF(scale,scale));Application.DoEvents();var all=All(form).ToArray();var install=all.OfType<BrandButton>().Single(b=>b.Text=="Установить");var remove=all.OfType<BrandButton>().Single(b=>b.Text=="Удалить");Check(Math.Abs(install.Width-remove.Width)<=1&&install.Height==remove.Height,"Equal compact actions");Check(!remove.Destructive,"No special delete outline");Check(remove.Right==remove.Parent.ClientSize.Width,"Delete reaches right action edge");Check(remove.PointToScreen(new Point(remove.Width,0)).X==all.OfType<Card>().Single().PointToScreen(new Point(all.OfType<Card>().Single().Width,0)).X,"Delete aligns to card right edge");Check(!form.MaximizeBox&&form.FormBorderStyle==FormBorderStyle.FixedSingle,"Fixed compact window");foreach(var picker in all.OfType<PathPickerRow>()){var button=picker.Controls.OfType<BrandButton>().Single();Check(picker.ClientSize.Width-button.Right<=20*scale,"No large unused area after picker: "+(picker.ClientSize.Width-button.Right)+" scale="+scale);}using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tools-"+(dark?"dark":"light")+"-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png"));}form.Close();
   }Console.WriteLine("PASS: "+count+" compact utility geometry assertions, both themes and 100/125/150/200%. No deployment.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
