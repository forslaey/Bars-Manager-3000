using System;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class DotNetWholeRowsTests
 {
 static System.Collections.Generic.IEnumerable<Control> All(Control c){foreach(Control n in c.Controls){yield return n;foreach(var x in All(n))yield return x;}}
 [STAThread]static int Main(){int checks=0;try{foreach(bool dark in new[]{false,true})using(var f=new DotNetUpdatesForm(()=>null)){f.SetTheme(dark,false);f.Show();var g=All(f).OfType<ServiceGrid>().Single();g.Rows.Clear();for(int i=0;i<60;i++)g.Rows.Add(false,"Runtime "+i,"8.0.1","8.0.2","Обновить");foreach(int height in new[]{620,701,783}){f.Height=height;for(int p=0;p<12;p++)Application.DoEvents();foreach(int first in new[]{0,1,17,40}){g.FirstDisplayedScrollingRowIndex=first;Application.DoEvents();int sum=g.ColumnHeadersHeight;foreach(DataGridViewRow r in g.Rows){if(sum+r.Height>g.ClientSize.Height)break;sum+=r.Height;}if(sum!=g.ClientSize.Height)throw new Exception("Non-whole viewport");foreach(DataGridViewRow r in g.Rows)if(r.Displayed){var rect=g.GetRowDisplayRectangle(r.Index,false);if(rect.Bottom>g.ClientSize.Height)throw new Exception("Partial bottom row at "+first);}checks++;}}f.Close();}Console.WriteLine("PASS: "+checks+" intermediate .NET scroll/whole-row checks, both themes. No network.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 }
}
