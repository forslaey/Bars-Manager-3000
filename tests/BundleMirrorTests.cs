using System;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class BundleMirrorTests
 {
  static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
  static System.Collections.Generic.IEnumerable<Control> Children(Control c){foreach(Control child in c.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
  [STAThread]static int Main(){try{
   var baseline=BundleCloudSource.Baseline();Check(baseline.Count==187&&baseline.All(f=>f.Path!=null&&f.Sha256.Length==64),"Embedded hash manifest complete");
   string html="window['_DRIVE_ivd'] = '[[[\"abc\",[\"parent\"],\"file.txt\",\"text/plain\",0,null,0,0,0,0,0,null,null,10]],null,null]';";Check(GoogleBundleSource.Parse(html).Length==1,"Public Google listing parser");
   bool denied=false;try{GoogleBundleSource.Parse(html.Replace("null,null]","\"next\",null]"));}catch(System.IO.InvalidDataException){denied=true;}Check(denied,"Incomplete listing fails closed");
   using(var check=new BundleCloudDialog(true)){var choices=Children(check).OfType<BrandCheckBox>().ToArray();choices[1].Checked=true;Check(choices.All(c=>c.Checked),"Both clouds allowed for checking");}
   using(var download=new BundleCloudDialog(false)){var choices=Children(download).OfType<BrandCheckBox>().ToArray();choices[1].Checked=true;Check(!choices[0].Checked&&choices[1].Checked,"Download cloud exclusive");}
   var item=new BundleDifferenceItem {Path="missing.exe",State="Нет в папке",Allowed=true,Selected=true};using(var result=new BundleDifferencesForm(new[]{item})){result.Show();Application.DoEvents();var grid=Children(result).OfType<ServiceGrid>().Single();typeof(DataGridView).GetMethod("OnCellClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(grid,new object[]{new DataGridViewCellEventArgs(1,0)});Check(!item.Selected,"Whole row toggles saved selection");}
   var source=new BundleCloudSource();source.GoogleFiles=new System.Collections.Generic.List<BundleRemoteFile>{new BundleRemoteFile {Path="/a",Size=10,Sha256=new string('a',64)}};Check(!source.Contains(BundleCloud.Google,new BundleRemoteFile {Path="/a",Size=10,Sha256=new string('b',64)}),"Mirror mismatch cannot reuse part");
   Console.WriteLine("PASS: "+count+" mirror/parser/cloud choice/result selection checks. No network.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
