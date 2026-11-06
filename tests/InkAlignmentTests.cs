using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class InkAlignmentTests
 {
  static int count;
  static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
  static IEnumerable<Control> Children(Control p){foreach(Control c in p.Controls){yield return c;foreach(Control d in Children(c))yield return d;}}
  static void Pump(){for(int i=0;i<4;i++)Application.DoEvents();}
  static int InkX(Control c)
  {
   Form form=c.FindForm();
   using(var b=new Bitmap(form.Width,form.Height)){
    form.DrawToBitmap(b,new Rectangle(Point.Empty,form.Size));Color bg=c.BackColor;
    Point origin=c.PointToScreen(Point.Empty);origin.Offset(-form.Left,-form.Top);
    for(int x=0;x<c.Width;x++)for(int y=0;y<c.Height;y++){
     if(origin.X+x<0||origin.Y+y<0||origin.X+x>=b.Width||origin.Y+y>=b.Height)continue;
     Color pixel=b.GetPixel(origin.X+x,origin.Y+y);
     if(Math.Abs(pixel.R-bg.R)>32||Math.Abs(pixel.G-bg.G)>32||Math.Abs(pixel.B-bg.B)>32)return c.PointToScreen(Point.Empty).X+x;
    }
   }throw new Exception("No visible ink in "+c.Text);
  }
  static T Field<T>(Form f,string name){return (T)f.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(f);}
  [STAThread]static int Main()
  {
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   try{
    foreach(bool dark in new[]{false,true})foreach(float scale in new[]{1F,1.25F,1.5F,2F})
    foreach(Func<Form> factory in new Func<Form>[] {()=>new MainForm(null,true),()=>new LibraryManagerForm(true),()=>new SoftwareManagerForm(true),()=>new ToolsDeploymentForm(true),()=>new InitialSystemSettingsForm(true),()=>new WindowsFeaturesForm(true,true)})
    using(Form form=factory()){
     var m=form as ManagerModuleForm;if(m!=null)m.SetTheme(dark,false);var services=form as MainForm;if(services!=null)services.SetTheme(dark,false);
     form.ShowInTaskbar=false;form.Show();Pump();float native=form.CurrentAutoScaleDimensions.Width/96F;
     if(Math.Abs(native-1F)>.001F)form.Scale(new SizeF(1F/native,1F/native));if(scale!=1F)form.Scale(new SizeF(scale,scale));
     bool compact=form is ToolsDeploymentForm||form is WindowsFeaturesForm;
     if(!compact)form.ClientSize=new Size((int)(1120*scale),(int)(860*scale));Pump();
     string id=form.GetType().Name+" theme="+dark+" scale="+scale;
     var labels=Children(form).OfType<Label>().ToArray();
     if(services!=null){
      var title=labels.Single(l=>l.Text=="Менеджер служб");var drop=Field<FlushLabel>(form,"fileTitle");var status=Field<Label>(form,"statusTitle");
      int x=InkX(title);Check(InkX(drop)==x,id+" drop ink "+InkX(drop)+" vs heading "+x);Check(InkX(status)==x,id+" status ink "+InkX(status)+" vs heading "+x);
      string original=drop.Text;foreach(string sample in new[]{"DK.License.Server.Host.exe","HttpService.exe","Bars.BackendWidget.exe"}){drop.Text=sample;Pump();Check(InkX(drop)==x,id+" dynamic selected file ink "+sample);}drop.Text=original;
      var install=Children(form).OfType<Button>().Single(b=>b.Text=="Установить службу");var grid=Field<DataGridView>(form,"servicesGrid");
      Check(install.PointToScreen(new Point(0,install.Height)).Y<=status.PointToScreen(Point.Empty).Y,id+" actions above status row");
      Check(status.PointToScreen(new Point(0,status.Height)).Y<=grid.PointToScreen(Point.Empty).Y,id+" status row immediately before list");
      var toggle=Children(form).OfType<RefreshToggle>().Single();Check(toggle.PointToScreen(Point.Empty).Y>=install.PointToScreen(new Point(0,install.Height)).Y,id+" toggle moved with status");
     }else if(form is LibraryManagerForm){
      var title=labels.Single(l=>l.Text=="Менеджер системных библиотек Windows");var summary=Field<Label>(form,"summary");
      Check(InkX(title)==InkX(summary),id+" title ink "+InkX(title)+" vs summary "+InkX(summary));
      var source=Field<Label>(form,"sourceLabel");if(!String.IsNullOrEmpty(source.Text))Check(InkX(title)==InkX(source),id+" title and library folder ink");
     }else{
      string name=form is SoftwareManagerForm?"Программы для рабочего места пользователя":form is ToolsDeploymentForm?"Распаковка утилит":form is WindowsFeaturesForm?"Компоненты Windows":"Первичные системные настройки";
      var title=labels.Single(l=>l.Text==name);int target;
      if(form is SoftwareManagerForm||form is InitialSystemSettingsForm)target=Children(form).OfType<ServiceGrid>().Single().PointToScreen(Point.Empty).X;
      else target=Children(form).OfType<Card>().Min(c=>c.PointToScreen(Point.Empty).X);
      Check(InkX(title)==target,id+" visible title ink "+InkX(title)+" vs content edge "+target);
      if(form is SoftwareManagerForm){
       var caption=labels.Single(l=>l.Text=="Папка установщиков:");
       Check(title.PointToScreen(Point.Empty).Y==caption.PointToScreen(Point.Empty).Y&&title.Height==caption.Height,id+" shared heading/caption row");
       int needed=TextRenderer.MeasureText(title.Text,title.Font,new Size(title.Width,Int32.MaxValue),TextFormatFlags.NoPadding|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height;
       Check(needed<=title.Height,id+" full heading fits shared row, wrapped when necessary: needed="+needed+" height="+title.Height);
      }
     }
     if(scale==1.25F){string folder=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ink-previews");System.IO.Directory.CreateDirectory(folder);
      using(var bmp=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bmp,new Rectangle(Point.Empty,form.Size));bmp.Save(System.IO.Path.Combine(folder,form.GetType().Name+(dark?"-dark":"-light")+".png"));}
     }
    }
    Console.WriteLine("PASS: "+count+" raster glyph-edge/row-order/header-caption checks, both themes and simulated 100/125/150/200%. Preview only.");return 0;
   }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  }
 }
}
