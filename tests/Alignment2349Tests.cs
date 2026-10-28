using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class Alignment2349Tests
 {
  static int count;static void Check(bool value,string text){if(!value)throw new Exception(text);count++;}
  static System.Collections.Generic.IEnumerable<Control> All(Control p){foreach(Control c in p.Controls){yield return c;foreach(var n in All(c))yield return n;}}
  static void Snap(Form f,string name){using(var image=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name+".png"));}}
  [STAThread]static int Main(){try{foreach(bool dark in new[]{false,true}){
   string theme=dark?"dark":"light";
   using(var f=new BundleDownloadForm()){f.SetTheme(dark,false);f.Show();Application.DoEvents();var all=All(f).ToArray();var field=all.OfType<BrandTextBox>().Single();var caption=all.OfType<Label>().Single(l=>l.Text=="Папка комплекта:");var button=all.OfType<BrandButton>().Single(b=>b.Text=="Выбрать папку");Func<Control,int> cy=c=>c.PointToScreen(new Point(0,c.Height/2)).Y;Check(Math.Abs(cy(field)-cy(caption))<=1,"Caption and field vertical centres");Check(Math.Abs(cy(field)-cy(button))<=1,"Field and picker centres");Check(field.PointToScreen(Point.Empty).X>caption.PointToScreen(Point.Empty).X,"Horizontal folder row");Snap(f,"bundle-"+theme);}
   using(var f=new SoftwareManagerForm(true)){f.SetTheme(dark,false);f.Show();Application.DoEvents();var remove=All(f).OfType<BrandButton>().Single(b=>b.Text=="Удалить");Check(!remove.Destructive,"No special delete border");var buttons=remove.Parent.Controls.Cast<Control>().ToArray();Check(buttons.Select(b=>b.Size).Distinct().Count()==1,"Six equal actions");Snap(f,"software-"+theme);}
   using(var f=new BundleDifferencesForm(new[]{new[]{"one.exe","Отсутствует — скачать"}})){f.SetTheme(dark,false);f.Show();Application.DoEvents();var grid=All(f).OfType<ServiceGrid>().Single();var bar=All(f).OfType<LibraryGridScrollBar>().Single();Check(!bar.Visible,"No empty scroll lane for short result");Check(grid.Parent.BackColor==grid.BackgroundColor,"Viewport matches grid background");Check(grid.Width==grid.Parent.ClientSize.Width,"Short table uses full viewport");Snap(f,"results-"+theme);}
   using(var f=new BundleCloudDialog(true)){f.SetTheme(dark,false);f.Show();Application.DoEvents();Snap(f,"cloud-"+theme);}
   using(var f=new BrandMessageDialog("Остановить загрузку? Готовые файлы и данные для продолжения сохранятся.","Загрузка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2,ThemePalette.Create(dark),true)){f.Show();Application.DoEvents();Check(f.ClientSize.Width==520&&f.ClientSize.Height<208,"Compact stop confirmation");var sign=All(f).OfType<Label>().Single(l=>l.Text=="!");Check(sign.TextAlign==ContentAlignment.MiddleCenter,"Warning sign centred");Snap(f,"stop-"+theme);}
  }Console.WriteLine("PASS: "+count+" folder/result/dialog/button geometry checks; both themes, screenshots. No network.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
