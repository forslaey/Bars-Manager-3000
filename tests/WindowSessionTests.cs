using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class WindowSessionTests
 {
  static int count;
  static void Check(bool ok,string text) { if(!ok)throw new Exception(text); count++; }
  static IEnumerable<Control> Children(Control p) {foreach(Control c in p.Controls){yield return c;foreach(Control d in Children(c))yield return d;}}
  static void Pump(){for(int i=0;i<4;i++)Application.DoEvents();}
  static void Back(Form form){Children(form).OfType<Button>().Single(b=>b.Text=="В меню").PerformClick();Pump();}
  [STAThread] static int Main()
  {
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   try {
    // Actual initial bounds and initial footer visibility, without scrolling a
    // control into view or moving a test window after Show.
    foreach(bool dark in new[]{false,true})
    foreach(Func<Form> factory in new Func<Form>[] {()=>new MainForm(null,true),()=>new LibraryManagerForm(true),()=>new SoftwareManagerForm(true),()=>new InitialSystemSettingsForm(true),()=>new ToolsDeploymentForm(true),()=>new WindowsFeaturesForm(true,true)})
    using(Form form=factory()) {
     var m=form as ManagerModuleForm;if(m!=null)m.SetTheme(dark,false);
     var service=form as MainForm;if(service!=null)service.SetTheme(dark,false);
     form.Show();Pump();Rectangle area=Screen.FromControl(form).WorkingArea;
     Check(area.Contains(form.Bounds),"Initial complete bounds: "+form.GetType().Name+" "+form.Bounds+" "+area);
     var back=Children(form).OfType<Button>().Single(b=>b.Text=="В меню"||b.Text=="Назад");
     Check(form.RectangleToScreen(form.ClientRectangle).Contains(back.RectangleToScreen(back.ClientRectangle)),"Initial footer visible: "+form.GetType().Name);
     Check(back.Visible&&back.Parent.ClientRectangle.Contains(back.Bounds),"Menu within footer");
     if(form is SoftwareManagerForm) {
      var grid=Children(form).OfType<ServiceGrid>().Single();
      var lane=Children(form).OfType<LibraryGridScrollBar>().Single();
      int total=grid.ColumnHeadersHeight;foreach(DataGridViewRow row in grid.Rows)total+=row.Height;
      if(total<=grid.ClientSize.Height)Check(!lane.Visible&&grid.Right==grid.Parent.ClientSize.Width,"No empty right scrollbar lane");
      var source=Children(form).OfType<Button>().Single(b=>b.Text=="Выбрать папку...");
      int gap=grid.PointToScreen(Point.Empty).Y-source.PointToScreen(new Point(0,source.Height)).Y;
      Check(gap>=10,"Folder group separated from table: "+gap);
      if(dark)using(var bmp=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bmp,new Rectangle(Point.Empty,form.Size));bmp.Save(@"C:\ServiceManager3000\work\current-verification\v2.3.36\programs-initial.png");}
     }
    }
    using(var context=new ManagerApplicationContext(()=>new ManagerHub())) {
     var hub=(ManagerHub)context.CurrentWindow; hub.WindowState=FormWindowState.Maximized;Pump();
     hub.OpenModule(()=>new SoftwareManagerForm(true));Pump();var software=(SoftwareManagerForm)context.CurrentWindow;
     software.WindowState=FormWindowState.Maximized;Pump();
     var grid=Children(software).OfType<ServiceGrid>().Single();grid.Rows[0].Cells[0].Value=false;grid.CurrentCell=grid.Rows[2].Cells[1];
     var journal=Children(software).OfType<JournalTextBox>().Single();journal.AppendText("SESSION-MARKER");
     Back(software);Check(((Form)context.CurrentWindow).WindowState==FormWindowState.Maximized,"Launcher maximized session retained");
     ((ManagerHub)context.CurrentWindow).OpenModule(()=>new SoftwareManagerForm(true));Pump();
     Check(Object.ReferenceEquals(context.CurrentWindow,software),"Module identity retained");
     Check(software.WindowState==FormWindowState.Maximized,"Module maximized session retained");
     Check(grid.CurrentCell.RowIndex==2&&journal.Text.Contains("SESSION-MARKER"),"Selection and journal retained");
     software.WindowState=FormWindowState.Normal;Pump();Size size=software.Size;Point location=software.Location;
     Back(software);((ManagerHub)context.CurrentWindow).OpenModule(()=>new ToolsDeploymentForm(true));Pump();Back(context.CurrentWindow);
     ((ManagerHub)context.CurrentWindow).OpenModule(()=>new SoftwareManagerForm(true));Pump();
     Check(software.Size==size&&software.Location==location,"Normal geometry retained across another module");
     var guide=context.Dialog(()=>new UserGuideForm());var search=Children(guide).OfType<BrandTextBox>().First();search.Text="журнал";
     Check(Object.ReferenceEquals(guide,context.Dialog(()=>new UserGuideForm()))&&search.Text=="журнал","Nested guide session retained");
     Size guideSize=Size.Empty;
     for(int cycle=0;cycle<2;cycle++) using(var closer=new Timer {Interval=60}) {
      int iteration=cycle;
      closer.Tick+=delegate { closer.Stop();if(iteration==0){guide.Size=new Size(guide.Width-20,guide.Height-20);guideSize=guide.Size;} else Check(guide.Size==guideSize,"Nested modal geometry retained");guide.Close(); };
      closer.Start();var shown=ModuleNavigation.ShowDialog(software,()=>new UserGuideForm());
      Check(Object.ReferenceEquals(shown,guide)&&!guide.IsDisposed,"Nested modal survives close");
     }
     software.Close();Pump();Check(software.IsDisposed&&guide.IsDisposed&&context.CurrentWindow==null,"Cross disposes all cached windows");
    }
    using(var context=new ManagerApplicationContext(()=>new MainForm(null,true))) {
     var services=(MainForm)context.CurrentWindow;
     var journal=Children(services).OfType<JournalTextBox>().Single();Back(services);
     typeof(MainForm).GetMethod("PostLog",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(services,new object[]{"HIDDEN-SERVICE-RESULT"});Pump();
     Check(journal.Text.Contains("HIDDEN-SERVICE-RESULT"),"Hidden service operation result preserved in journal");context.CurrentWindow.Close();Pump();
    }
    foreach(Rectangle area in new[]{new Rectangle(0,0,1920,1040),new Rectangle(-1080,0,1080,1880),new Rectangle(0,-900,1440,860)})
    foreach(Rectangle bounds in new[]{new Rectangle(-9999,9999,2500,2000),new Rectangle(20,40,800,600)})
    Check(area.Contains(WindowPlacement.Fit(bounds,area)),"All edges clamped on landscape/portrait/negative-origin screen");
    Check(Application.OpenForms.Count==0,"No orphan windows");
    Console.WriteLine("PASS: "+count+" initial working-area, footer, session, table-edge and placement checks. Preview only.");return 0;
   }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  }
 }
}
