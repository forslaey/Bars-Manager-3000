using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class GridViewportTests
 {
  static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
  static System.Collections.Generic.IEnumerable<Control> All(Control p){foreach(Control c in p.Controls){yield return c;foreach(var n in All(c))yield return n;}}
  static void Pump(){for(int i=0;i<8;i++)Application.DoEvents();}
  [STAThread]static int Main(){try{
   foreach(bool dark in new[]{false,true})foreach(float scale in new[]{1F,1.25F,1.5F,2F})using(var form=new Form {ClientSize=new Size(620,380)}){
    var palette=ThemePalette.Create(dark);var slot=new Panel {Dock=DockStyle.Fill};var grid=new ServiceGrid {Dock=DockStyle.Fill,AllowUserToAddRows=false,RowHeadersVisible=false,BackgroundColor=palette.Surface,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,ScrollBars=ScrollBars.None};grid.Columns.Add("a","Компонент");var scroll=new LibraryGridScrollBar {Dock=DockStyle.Right,Width=14,Palette=palette};scroll.Bind(grid);slot.Controls.Add(grid);slot.Controls.Add(scroll);form.Controls.Add(slot);form.Show();if(scale!=1)form.Scale(new SizeF(scale,scale));Pump();
    foreach(int rows in new[]{0,1,80,2,0,100,1}){grid.Rows.Clear();for(int n=0;n<rows;n++)grid.Rows.Add("Fixture "+n);Pump();scroll.UpdateViewport();Pump();bool needed=rows>10;Check(scroll.Visible==needed,"Scrollbar follows row count "+rows);Check(slot.BackColor==palette.Canvas&&scroll.BackColor==palette.Canvas,"Matched viewport backgrounds");Check(grid.Width+(scroll.Visible?scroll.Width:0)==slot.ClientSize.Width,"No unclaimed horizontal lane");}
    form.Close();
   }
   foreach(bool dark in new[]{false,true})foreach(Form form in new Form[]{new MainForm(null,true),new LibraryManagerForm(true),new InitialSystemSettingsForm(true),new SoftwareManagerForm(true),new DotNetUpdatesForm(()=>null),new BundleDifferencesForm(new[]{new[]{"one.exe","Нет в папке"}})})using(form){
    var module=form as ManagerModuleForm;if(module!=null)module.SetTheme(dark,false);form.Show();Pump();var bars=All(form).OfType<LibraryGridScrollBar>().ToArray();Check(bars.Length>0,"Module has tested viewport: "+form.GetType().Name);foreach(var bar in bars){bar.UpdateViewport();Pump();var grid=bar.Parent.Controls.OfType<DataGridView>().Single();Check(bar.BackColor==bar.Palette.Canvas&&grid.Parent.BackColor==bar.Palette.Canvas,"Module colours "+form.GetType().Name);Check(grid.Width+(bar.Visible?bar.Width:0)==grid.Parent.ClientSize.Width,"Module viewport width "+form.GetType().Name);}
    using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"viewport-"+form.GetType().Name+"-"+(dark?"dark":"light")+".png"));}form.Close();
   }
   Console.WriteLine("PASS: "+count+" viewport row transitions/theme/module width checks. No installers or network.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
