using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class RequestedUi2322Tests
    {
        static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
        [STAThread] static int Main()
        {
            try
            {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                foreach(bool dark in new[]{false,true})
                {
                    using(var form=new SoftwareManagerForm(true))
                    {
                        form.SetTheme(dark,false);
                        var chrome=Field<BrandCheckBox>(form,"chromeDefaults");var defaults=Field<BrandCheckBox>(form,"defaults");var remote=Field<BrandCheckBox>(form,"remote");
                        Check(chrome.Checked&&!defaults.Checked&&!remote.Checked,"Only Chrome initially checked");
                        Check(((TableLayoutPanel)chrome.Parent).GetRow(chrome)==1,"Chrome option first");
                        Check(Field<BrandButton>(form,"refresh").Text=="Проверить версии в сети","Network check caption");
                        Check(Field<BrandButton>(form,"associations").Text=="Применить ассоциации","Association caption");
                        Check(Field<BrandButton>(form,"access").Text=="Настроить удалённый доступ","Remote access caption");
                    }
                    using(var form=new MainForm(null,true))
                    {
                        form.SetTheme(dark,false);form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
                        var toggle=Field<RefreshToggle>(form,"autoRefreshToggle");var button=Field<Button>(form,"servicesButton");
                        for(int pass=0;pass<6;pass++)
                        {
                            form.WindowState=pass%2==0?FormWindowState.Maximized:FormWindowState.Normal;Application.DoEvents();form.PerformLayout();Application.DoEvents();
                            Check(Math.Abs(toggle.PointToScreen(new Point(toggle.Width,0)).X-button.PointToScreen(new Point(button.Width,0)).X)<=2,"Refresh right edge matches Open services after maximize/restore");
                            using(var graphics=toggle.CreateGraphics())
                            {
                                int text=TextRenderer.MeasureText(graphics,toggle.Text,toggle.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width;
                                int prefix=1+(int)Math.Round(42*graphics.DpiX/96F)+(int)(8*graphics.DpiX/96F);
                                Check(toggle.Width>=prefix+text&&toggle.Width<=prefix+text+6,"Toggle fits text without stretching across column");
                            }
                        }
                        form.Close();
                    }
                }
                Check(SoftwareSources.ParseAnyDesk("23.10.2026 - 9.8.2 (Windows)").Source=="Релиз на официальном сайте AnyDesk","Official AnyDesk description");
                Console.WriteLine("RequestedUi2322Tests: "+count+" passed; preview only.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
