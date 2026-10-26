using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class SystemSettingsRegressionTests
    {
        static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
        static void Call(object value,string name,params object[] args){value.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,args);}
        [STAThread] static int Main()
        {
            try
            {
                // Real Windows PowerShell 5.1 process, but no system-setting commands.
                string large="#"+new string('x',100000)+"\r\n'Проверка длинного сценария выполнена'";
                Check(WindowsComponents.Run(large,delegate{}).Message.Contains("Проверка длинного сценария выполнена"),"Large stdin script and Cyrillic output");
                bool rejected=false;try{WindowsComponents.Run("throw 'Тестовая ошибка'",delegate{});}catch(InvalidOperationException ex){rejected=ex.Message.Contains("Тестовая ошибка");}Check(rejected,"Readable Cyrillic error propagated");
                Check(WindowsComponents.Run("'BARS_RESTART_REQUIRED'",delegate{}).ExitCode==3010,"Restart marker preserved");
                string streams="[Console]::Out.Write(('o'*100000));[Console]::Error.Write(('e'*100000));' Готово'";
                Check(WindowsComponents.Run(streams,delegate{}).Message.EndsWith("Готово"),"Both pipes drained without deadlock");
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                foreach(bool dark in new[]{false,true})using(var form=new InitialSystemSettingsForm(true))
                {
                    form.SetTheme(dark,false);form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
                    var grid=Field<ServiceGrid>(form,"grid");Check(grid.RowCount==7&&grid.TouchScrollingEnabled,"Seven settings, native touch scrolling");
                    for(int pass=0;pass<8;pass++)
                    {
                        form.WindowState=FormWindowState.Maximized;Application.DoEvents();form.WindowState=FormWindowState.Normal;form.ClientSize=pass%2==0?new Size(1000,740):new Size(1200,900);Application.DoEvents();
                        Check(grid.Parent.ClientRectangle.Contains(grid.Bounds),"Grid stays inside viewport on resize");
                        var log=Field<JournalTextBox>(form,"journal");Check(log.Parent.ClientRectangle.Contains(log.Bounds),"Journal stays inside card");
                        Check(grid.PointToScreen(new Point(0,grid.Height)).Y<=log.PointToScreen(Point.Empty).Y,"Table does not overlap journal");
                        var cell=grid.GetCellDisplayRectangle(0,grid.FirstDisplayedScrollingRowIndex,false);var point=new Point(cell.Left+20,cell.Top+20);
                        grid.TouchPan(point,true,false);grid.TouchPan(new Point(point.X,point.Y-500),false,true);Check(grid.FirstDisplayedScrollingRowIndex>=0,"Swipe remains valid after resize");
                    }
                    for(int row=0;row<6;row++){grid.CurrentCell=grid.Rows[row].Cells[0];Application.DoEvents();Check(Field<BrandButton>(form,"applyButton").Enabled,"Selected setting has action");Check(Field<BrandButton>(form,"standardButton").Text==(row==5?"Проверить папки":row==3?"Включить защиту":row==2?"Включить все профили":"Вернуть стандартные политики"),"Secondary action matches selected row");}
                    Call(form,"ShowState","admin.EnableLUA=0\r\nfirewall.Domain=True\r\nfirewall.Private=False\r\nfirewall.Public=True\r\ndefender.Error=Недоступен\r\nsmartscreen.Mode=Включён");
                    Check(Convert.ToString(grid.Rows[1].Cells[2].Value).Contains("Отключено"),"UAC state updated in grid");Check(Convert.ToString(grid.Rows[3].Cells[2].Value).Contains("не выполнена"),"Query error not reported as disabled");
                    Call(form,"Lock",true);Check(!Field<BrandButton>(form,"applyButton").Enabled&&!Field<BrandButton>(form,"refresh").Enabled,"Busy blocks actions");Call(form,"Lock",false);
                    using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings-grid-"+dark+".png"));}
                    form.Close();
                }
                Console.WriteLine("SystemSettingsRegressionTests: "+count+" passed; actual PowerShell transport, preview resize/touch. No system settings changed.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
