using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool {
 internal static class TouchGridTests {
  static int count;
  static void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
  static T Field<T>(object x,string name){return (T)x.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(x);}
  [STAThread]public static int Main(){try{
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   Check(ServiceGrid.IsTouchMouse(unchecked((long)0xFF515780)),"Touch signature");Check(!ServiceGrid.IsTouchMouse(unchecked((long)0xFF515700)),"Pen not touch");Check(!ServiceGrid.IsTouchMouse(0),"Mouse not touch");
   foreach(bool dark in new[]{false,true})using(var form=new LibraryManagerForm(true)){
    form.SetTheme(dark,false);form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
    var grid=Field<ServiceGrid>(form,"grid");Check(grid.TouchScrollingEnabled,"Only library opts into touch scrolling");var restart=Field<BrandButton>(form,"restart");Check(restart.Enabled,"Manual restart allowed idle without requirement");
    var flags=BindingFlags.Instance|BindingFlags.NonPublic;foreach(string state in new[]{"checking","installing"}){form.GetType().GetField(state,flags).SetValue(form,true);form.GetType().GetMethod("UpdateRestartAvailability",flags).Invoke(form,null);Check(!restart.Enabled,"Restart disabled during "+state);form.GetType().GetField(state,flags).SetValue(form,false);}form.GetType().GetMethod("UpdateRestartAvailability",flags).Invoke(form,null);Check(restart.Enabled,"Restart returns to enabled idle");
    var cell=grid.GetCellDisplayRectangle(0,0,false);var point=new Point(cell.Left+cell.Width/2,cell.Top+cell.Height/2);bool before=Convert.ToBoolean(grid.Rows[0].Cells[0].Value);
    Check(grid.TouchMouse(0x201,point,true),"Touch press delayed");Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value)==before,"Touch press does not prematurely toggle");
    grid.TouchMouse(0x202,point,true);Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value)!=before,"Tap toggles once on release");grid.TouchMouse(0x202,point,true);Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value)!=before,"Extra release does not duplicate toggle");
    grid.TouchMouse(0x201,point,true);grid.TouchPan(point,true,false);grid.TouchPan(new Point(point.X,point.Y-180),false,false);grid.TouchMouse(0x202,new Point(point.X,point.Y-180),true);Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value)!=before,"Swipe starting over checkbox never toggles it");Check(grid.FirstDisplayedScrollingRowIndex>0,"Finger upward scrolls list down");
    int first=grid.FirstDisplayedScrollingRowIndex;grid.TouchPan(new Point(point.X,point.Y-450),false,true);Check(grid.FirstDisplayedScrollingRowIndex>=first,"Inertia continues same direction");
    grid.TouchPan(point,true,false);grid.TouchPan(new Point(point.X,100000),false,true);Check(grid.FirstDisplayedScrollingRowIndex==0,"Top clamp");
    grid.TouchPan(point,true,false);grid.TouchPan(new Point(point.X,-100000),false,true);Check(grid.FirstDisplayedScrollingRowIndex<grid.RowCount-1&&grid.GetRowDisplayRectangle(grid.RowCount-1,false).Height>0,"Bottom visible without empty-page overscroll");
    Check(!grid.TouchMouse(0x201,point,false),"Mouse press not intercepted");Check(!grid.TouchMouse(0x202,point,false),"Mouse release not intercepted");
    grid.FirstDisplayedScrollingRowIndex=0;grid.TouchMouse(0x201,point,true);grid.ResetTouch();grid.TouchMouse(0x202,point,true);Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value)!=before,"Cancelled touch cannot click");
    form.Close();
   }
   using(var g=new ServiceGrid()){Check(!g.TouchScrollingEnabled,"Other grids unchanged by default");Check(!g.TouchMouse(0x201,new Point(1,1),true),"No global touch interception");}
   Console.WriteLine("TouchGridTests: "+count+" passed; synthetic touch and idle/busy restart tests. No reboot or global input injection.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
