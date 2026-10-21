using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class RapidSelectionTests
 {
  private static int count;
  private static void Check(bool ok,string text){count++;if(!ok)throw new Exception(text);}
  private static T Field<T>(object form,string name){return (T)form.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);}
  [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
  private static void Press(DataGridView grid,Point point,bool twice){SendMessage(grid.Handle,twice?0x203:0x201,new IntPtr(1),new IntPtr((point.Y<<16)|point.X));}
  private static void Release(DataGridView grid,Point point){SendMessage(grid.Handle,0x202,IntPtr.Zero,new IntPtr((point.Y<<16)|point.X));Application.DoEvents();}
  [STAThread]internal static int Main()
  {
   try
   {
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
    foreach(bool dark in new[]{false,true})foreach(bool library in new[]{false,true})using(ManagerModuleForm form=library?(ManagerModuleForm)new LibraryManagerForm(true):new SoftwareManagerForm(true))
    {
     form.SetTheme(dark,false);form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-22000,-22000);form.ShowInTaskbar=false;form.Show();Application.DoEvents();
     var grid=Field<ServiceGrid>(form,"grid");var selected=Field<HashSet<string>>(form,"selection");var rect=grid.GetCellDisplayRectangle(0,0,false);var point=new Point(rect.Right-4,rect.Bottom-4);
     bool before=Convert.ToBoolean(grid.Rows[0].Cells[0].Value);
     Press(grid,point,false);Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value)!=before,"First mouse-down immediately toggles "+form.GetType().Name);Release(grid,point);
     Press(grid,point,true);Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value)==before,"Second rapid/double mouse-down immediately clears "+form.GetType().Name);Release(grid,point);
     for(int i=0;i<8;i++){bool last=Convert.ToBoolean(grid.Rows[0].Cells[0].Value);Press(grid,point,i%2==1);Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value)!=last,"No dropped rapid click");Release(grid,point);}
     var busy=form.GetType().GetField(library?"installing":"busy",BindingFlags.Instance|BindingFlags.NonPublic);busy.SetValue(form,true);Press(grid,point,false);Release(grid,point);busy.SetValue(form,false);Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value)==before,"Busy operation locks selection");
     var key=new KeyEventArgs(Keys.Space);typeof(Control).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(grid,new object[]{key});Check(key.Handled&&key.SuppressKeyPress&&Convert.ToBoolean(grid.Rows[0].Cells[0].Value)!=before,"Space toggles once");
     Check(grid.Columns[0].Width==46&&grid.Columns[0].MinimumWidth==46,"Identical selection-column width");form.Close();
    }
    Console.WriteLine("PASS: "+count+" immediate/rapid/double-click/keyboard/busy checks, both grids and themes. No system operation.");return 0;
   }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  }
 }
}
