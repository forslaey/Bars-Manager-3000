using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class HeaderStyleTests
    {
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle,int attribute,out int value,int size);
        private static int count, captions;
        private static IEnumerable<Control> Children(Control parent)
        { foreach(Control child in parent.Controls) { yield return child; foreach(var item in Children(child))yield return item; } }
        private static void Check(bool ok,string message) { if(!ok)throw new Exception(message);count++; }
        private static void Caption(Form form,bool dark)
        {
            int value;int result=DwmGetWindowAttribute(form.Handle,20,out value,sizeof(int));
            if(result<0)result=DwmGetWindowAttribute(form.Handle,19,out value,sizeof(int));
            if(result>=0){Check(value==(dark?1:0),"Native caption: "+form.GetType().Name+" dark="+dark);captions++;}
        }
        [STAThread] private static int Main()
        {
            try
            {
                Application.EnableVisualStyles();
                foreach(bool dark in new[]{false,true})
                {
                    Rectangle? expected=null;
                    string[] names={"Менеджер служб","Менеджер системных библиотек Windows","Программы для рабочего места пользователя","Первичные системные настройки"};
                    Form[] modules={new MainForm(null,true),new LibraryManagerForm(true),new SoftwareManagerForm(true),new InitialSystemSettingsForm(true),new ManagerHub(),new ToolsDeploymentForm(true),new WindowsFeaturesForm(true,true),new UserGuideForm()};
                    for(int i=0;i<modules.Length;i++)using(var form=modules[i])
                    {
                        var module=form as ManagerModuleForm;var services=form as MainForm;
                        if(module!=null)module.SetTheme(dark,false);if(services!=null)services.SetTheme(dark,false);
                        form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.ShowInTaskbar=false;
                        // Compare equal usable widths above every module's minimum;
                        // portrait/minimum-width behavior is covered separately.
                        form.Show();form.ClientSize=new Size(2400,1800);Application.DoEvents();form.PerformLayout();Application.DoEvents();Caption(form,dark);
                        var root=Children(form).OfType<TableLayoutPanel>().First();
                        var logo=Children(form).OfType<PictureBox>().First();
                        Check(root.PointToClient(logo.PointToScreen(Point.Empty)).Y>=20,"Logo top inset: "+form.GetType().Name);
                        if(i<4)
                        {
                            var title=Children(form).OfType<Label>().Single(l=>l.Text==names[i]);
                            var bounds=new Rectangle(root.PointToClient(title.PointToScreen(Point.Empty)),title.Size);
                            if(expected==null)expected=bounds;else Check(bounds.X==expected.Value.X&&bounds.Width==expected.Value.Width,"Common horizontal title alignment with compact module heights: "+form.GetType().Name+" "+bounds+" expected "+expected);
                            Check(title.Font.SizeInPoints==15&&title.Font.Bold,"Common heading font");
                        }
                        if(form is SoftwareManagerForm)
                        {
                            var grid=Children(form).OfType<ServiceGrid>().Single();
                            Check(!(grid.Parent is Card)&&grid.Parent.Padding==Padding.Empty,"Software table has no surrounding card");
                        }
                        if(module!=null){module.SetTheme(!dark,false);Caption(form,!dark);module.SetTheme(dark,false);Caption(form,dark);}
                        if(services!=null){services.SetTheme(!dark,false);Caption(form,!dark);services.SetTheme(dark,false);Caption(form,dark);}
                        Padding beforeRecreate=root.Padding;
                        typeof(Control).GetMethod("RecreateHandle",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(form,null);
                        Application.DoEvents();Check(root.Padding==beforeRecreate,"Recreated handle must not rescale header twice: "+form.GetType().Name);
                        Caption(form,dark);
                    }
                    var colors=ThemePalette.Create(dark);
                    Form[] dialogs={new AboutDialog(colors,null),new LibraryActionDialog(colors,null,new LibraryCheck[0]),new ServiceAccountDialog(colors,ServiceInstallProfile.Default(@"C:\fixture\HttpService.exe"),false),new LogArchiveDialog(colors,"BARS",new List<LogDateFolder>(),DateTime.Today),new SoftwareRemoteDialog(colors,null,"anydesk",false)};
                    foreach(var form in dialogs)using(form){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.ShowInTaskbar=false;form.Show();Application.DoEvents();Caption(form,dark);}
                }
                Console.WriteLine("HeaderStyleTests: "+count+" passed; "+captions+" supported native caption checks. No system changes.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
