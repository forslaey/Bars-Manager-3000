using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class BundleUiRevisionTests
 {
  static int count;static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
  static System.Collections.Generic.IEnumerable<Control> Children(Control p){foreach(Control c in p.Controls){yield return c;foreach(var child in Children(c))yield return child;}}
  [STAThread] static int Main(){try{
   foreach(bool dark in new[]{false,true})using(var form=new BundleDownloadForm()){
    form.SetTheme(dark,false);form.Show();Application.DoEvents();var controls=Children(form).ToArray();
    Check(!controls.OfType<ServiceGrid>().Any(),"No per-file grid in primary module");Check(!controls.OfType<NumericUpDown>().Any(),"No concurrency selector");
    var choices=controls.OfType<BrandCheckBox>().ToArray();Check(choices.Length==2&&choices.Any(c=>c.Text=="Библиотеки и .NET")&&choices.Any(c=>c.Text=="Утилиты BARS"),"Exactly two requested sections");
    Check(form.FormBorderStyle==FormBorderStyle.FixedSingle&&!form.MaximizeBox,"Fixed compact main window");Check(controls.OfType<BrandButton>().Any(b=>b.Text=="Результаты"),"Reopen results button");Check(controls.OfType<JournalTextBox>().Count()==1,"Embedded journal");Check(controls.OfType<BundleProgressBar>().Count()==1&&!controls.OfType<ProgressBar>().Any(),"Branded progress only");
    var buttons=controls.OfType<BrandButton>().Where(b=>new[]{"Проверить","Скачать","Результаты","Пауза","Отменить","В меню","Закрыть"}.Contains(b.Text)).ToArray();Check(buttons.Length==7&&buttons.Max(b=>b.Height)-buttons.Min(b=>b.Height)<=1,"Equal lower action button heights (DPI rounding <=1px)");
    controls.OfType<BrandButton>().Single(b=>b.Text=="В меню").PerformClick();Check(form.MenuRequested,"Module menu request");
   }
   using(var hub=new ManagerHub()){
    hub.Show();Application.DoEvents();var all=Children(hub).ToArray();Check(!all.OfType<Label>().Any(l=>l.Text.StartsWith("Версия ")),"No launcher version label");
    var bundle=all.OfType<BrandButton>().Single(b=>b.Text=="Скачать комплект");var guide=all.OfType<BrandButton>().Single(b=>b.Text=="Инструкция");var about=all.OfType<BrandButton>().Single(b=>b.AccessibleName=="О программе");var exit=all.OfType<BrandButton>().Single(b=>b.Text=="Закрыть программу");Check(about.Width==about.Height&&about.Height==exit.Height,"Square About button");Check(about.Left<guide.Left&&guide.Left<bundle.Left&&bundle.Left<exit.Left,"Launcher footer order");Check(Math.Abs(bundle.Width-guide.Width)<=1&&Math.Abs(guide.Width-exit.Width)<=1,"Equal text button widths");Check(hub.FormBorderStyle==FormBorderStyle.FixedSingle&&!hub.MaximizeBox,"Launcher fixed border and maximize");
    bundle.PerformClick();Check(hub.RequestedModuleType==typeof(BundleDownloadForm)&&!hub.Visible,"Launcher closes for downloader");
   }
   using(var software=new SoftwareManagerForm(true)){Check(software.FormBorderStyle==FormBorderStyle.FixedSingle&&software.MaximizeBox,"Software fixed border and maximize");Check(!Children(software).OfType<Label>().Any(l=>l.Cursor==Cursors.Hand&&l.Text.Contains("комплект")),"No clickable software status");}
   using(var tools=new ToolsDeploymentForm(true)){Check(tools.FormBorderStyle==FormBorderStyle.FixedSingle&&!tools.MaximizeBox,"Tools fixed border without maximize");}
   using(var library=new LibraryManagerForm(true)){var all=Children(library).ToArray();Check(!all.OfType<Label>().Any(l=>l.Cursor==Cursors.Hand&&l.Text.Contains("комплект")),"No clickable library status");var journal=all.OfType<BrandButton>().Single(b=>b.Text=="Открыть журнал");var exit=all.OfType<BrandButton>().Single(b=>b.Text=="Закрыть");Check(journal.Parent==exit.Parent&&journal.Left<exit.Left,"Journal and close are in the same footer");}
   using(var result=new BundleDifferencesForm(new[]{new[]{"test.exe","Нет в папке — скачать"}})){result.Show();Application.DoEvents();Check(result.FormBorderStyle==FormBorderStyle.FixedSingle&&!result.MaximizeBox,"Fixed results window");Check(Children(result).OfType<ServiceGrid>().Single().Rows.Count==1,"Only supplied differences appear");}
   Console.WriteLine("PASS: "+count+" compact downloader/navigation/window policy checks. No network.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
