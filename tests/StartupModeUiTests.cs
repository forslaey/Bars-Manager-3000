using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class StartupModeUiTests
    {
        private static int count;
        private static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;}
        private static Bitmap Capture(Control control){var bitmap=new Bitmap(control.Width,control.Height);control.DrawToBitmap(bitmap,control.ClientRectangle);return bitmap;}
        private static bool Same(Bitmap a,Bitmap b){if(a.Size!=b.Size)return false;for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))return false;return true;}
        [STAThread]public static int Main()
        {
            try
            {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                foreach(bool dark in new[]{false,true})foreach(float scale in new[]{1F,1.25F,1.5F,2F})using(var form=new MainForm(null,true))
                {
                    form.SetTheme(dark,false);form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-22000,-22000);form.ShowInTaskbar=false;form.Show();Application.DoEvents();
                    var combo=(BrandComboBox)typeof(MainForm).GetField("startupCombo",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                    combo.SelectedIndex=2;combo.Enabled=true;if(scale!=1F)form.Scale(new SizeF(scale,scale));Application.DoEvents();
                    Check(!combo.HoverHighlight,"Startup-mode hover effects disabled only on its own control");
                    Check(Math.Abs(combo.Width-combo.Parent.ClientSize.Width*0.8)<=1,"Width follows marked 80% extent");
                    Check(combo.Parent.ClientRectangle.Contains(combo.Bounds),"Taller field stays inside its row");
                    Check(Math.Abs(combo.Top-(combo.Parent.ClientSize.Height-combo.Bottom))<=1,"Height grows evenly around vertical center");
                    Check(combo.ItemHeight>=combo.Font.Height+8,"More space above and below mode text");
                    Check(combo.DropDownWidth>=combo.Width,"Popup retains full choice width");
                    var bounds=combo.Bounds;
                    using(var before=Capture(combo))
                    {
                        typeof(BrandComboBox).GetMethod("OnMouseEnter",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(combo,new object[]{EventArgs.Empty});Application.DoEvents();
                        using(var hovered=Capture(combo))Check(Same(before,hovered),"Hover does not change final rendering");
                        typeof(BrandComboBox).GetMethod("OnMouseLeave",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(combo,new object[]{EventArgs.Empty});Application.DoEvents();
                        using(var after=Capture(combo))Check(Same(before,after),"Leaving field does not change final rendering");
                    }
                    Check(combo.Bounds==bounds,"Hover/leave cannot move or resize field");
                    if(scale==1F){string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"previews");Directory.CreateDirectory(output);using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"services-mode-"+(dark?"dark":"light")+".png"));}}
                    form.Close();
                }
                Console.WriteLine("PASS: "+count+" startup-mode geometry/static-hover assertions; both themes, simulated 100/125/150/200%. No service operation.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
