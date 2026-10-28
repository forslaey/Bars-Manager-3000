using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class Alignment2348Tests
 {
  static int count;static void Check(bool value,string text){if(!value)throw new Exception(text);count++;}
  static System.Collections.Generic.IEnumerable<Control> All(Control parent){foreach(Control c in parent.Controls){yield return c;foreach(var child in All(c))yield return child;}}
  [STAThread]static int Main(){try{
   foreach(bool dark in new[]{false,true})foreach(float scale in new[]{1F,1.25F,1.5F,2F}){
    using(var form=new SoftwareManagerForm(true)){
     form.SetTheme(dark,false);form.Show();Application.DoEvents();if(scale!=1)form.Scale(new SizeF(scale,scale));Application.DoEvents();var controls=All(form).ToArray();var install=controls.OfType<BrandButton>().Single(b=>b.Text=="Установить");var panel=(TableLayoutPanel)install.Parent;var buttons=panel.Controls.Cast<Control>().ToArray();Check(buttons.Length==6&&buttons.Select(b=>b.Size).Distinct().Count()==1,"Six exactly equal buttons at "+scale);foreach(var button in buttons)Check(panel.ClientRectangle.Contains(button.Bounds),"Button in bounds");var remote=controls.OfType<BrandCheckBox>().Single();var grid=controls.OfType<ServiceGrid>().Single();Check(remote.PointToScreen(Point.Empty).X==grid.PointToScreen(Point.Empty).X,"Remote left matches table");Check(remote.PointToScreen(Point.Empty).X==install.PointToScreen(Point.Empty).X,"Remote left matches install");using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"software-"+(dark?"dark":"light")+"-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png"));}form.Close();
    }
    using(var form=new BundleDownloadForm()){
     form.SetTheme(dark,false);form.Show();Application.DoEvents();if(scale!=1)form.Scale(new SizeF(scale,scale));Application.DoEvents();var controls=All(form).ToArray();var field=controls.OfType<BrandTextBox>().Single();var button=controls.OfType<BrandButton>().Single(b=>b.Text=="Выбрать папку");var caption=controls.OfType<Label>().Single(l=>l.Text=="Папка комплекта:");Check(field.PointToScreen(Point.Empty).X>caption.PointToScreen(Point.Empty).X,"Horizontal picker row");Check(Math.Abs(field.PointToScreen(new Point(0,field.Height/2)).Y-button.PointToScreen(new Point(0,button.Height/2)).Y)<=1,"Picker centres aligned");var boxes=controls.OfType<BrandCheckBox>().OrderBy(c=>c.PointToScreen(Point.Empty).Y).ToArray();Check(boxes[0].Height==boxes[1].Height,"Equal checkbox slots");Check(boxes[0].Margin==Padding.Empty&&boxes[1].Margin==Padding.Empty,"Equal checkbox margins");Check(boxes[0].Parent.Padding.Top==boxes[0].Parent.Padding.Bottom,"Balanced section padding");form.Close();
    }
   }
   Console.WriteLine("PASS: "+count+" exact button/picker/checkbox alignment assertions, both themes 100/125/150/200%. No network.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
