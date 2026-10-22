using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool {
 internal static class GuideDpiTests {
  static int count;
  static void Check(bool value,string text){if(!value)throw new Exception(text);count++;}
  [STAThread] public static int Main(){try{
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   foreach(bool dark in new[]{false,true})using(var form=new UserGuideForm()){
    form.SetTheme(dark,false);form.ShowInTaskbar=false;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
    var list=(GuideSectionListBox)typeof(UserGuideForm).GetField("sections",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
    using(var g=list.CreateGraphics())Console.WriteLine("Native DPI: "+g.DpiX+" / "+g.DpiY);
    foreach(float points in new[]{10F,12.5F,15F,20F})foreach(int width in new[]{240,310,400}){
     list.Dock=DockStyle.None;list.Width=width;list.Font=new Font("Segoe UI",points);Application.DoEvents();
     using(var g=list.CreateGraphics())for(int i=0;i<list.Items.Count;i++){
      var bounds=list.TitleBounds(g,list.GetItemRectangle(i));
      var measured=TextRenderer.MeasureText(g,list.Items[i].ToString(),list.Font,new Size(bounds.Width,Int32.MaxValue),GuideSectionListBox.TitleFlags);
      Check(measured.Height<=bounds.Height,"Clipped section: "+list.Items[i]+" at font "+points+" width "+width);
      Check(list.GetItemHeight(i)==list.RequiredHeight(g,list.Items[i].ToString()),"Native row height was not refreshed");
     }
    }
    list.Font=new Font("Segoe UI",10F);list.Dock=DockStyle.Fill;form.ClientSize=new Size(940,660);Application.DoEvents();
    list.SelectedIndex=list.Items.Count-1;Application.DoEvents();Check(list.GetItemRectangle(list.SelectedIndex).IntersectsWith(list.ClientRectangle),"Last section inaccessible");
    list.SelectedIndex=0;list.TopIndex=0;
    using(var bmp=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dpi-"+(dark?"dark":"light")+".png"));}
    form.Close();
   }
   Console.WriteLine("GuideDpiTests: "+count+" passed");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 }
}
