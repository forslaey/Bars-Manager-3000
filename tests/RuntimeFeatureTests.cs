using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class RuntimeFeatureTests
 {
  static int count;static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;}
  static IEnumerable<Control> Children(Control p){foreach(Control c in p.Controls){yield return c;foreach(Control n in Children(c))yield return n;}}
  static void ArchitectureFixture(ushort machine,uint flags,string expected){string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".exe");try{
   using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write))using(var writer=new BinaryWriter(file)){
    file.SetLength(1024);writer.Write((ushort)0x5a4d);file.Position=60;writer.Write(128U);file.Position=128;writer.Write(0x4550U);writer.Write(machine);writer.Write((ushort)1);file.Position=148;writer.Write((ushort)224);file.Position=152;writer.Write((ushort)0x10b);
    file.Position=360;writer.Write(0x2000U);file.Position=384;writer.Write(512U);writer.Write(0x2000U);writer.Write(512U);writer.Write(512U);file.Position=528;writer.Write(flags);
   }Check(ExecutableArchitecture.Caption(path)==expected,"PE CLR flags "+flags);
  }finally{File.Delete(path);}}
  [STAThread]static int Main(){try{
   Check(ExecutableArchitecture.Caption(typeof(RuntimeFeatureTests).Assembly.Location)=="AnyCPU","Managed AnyCPU");
   Check(ExecutableArchitecture.Caption(Path.Combine(Environment.SystemDirectory,"cmd.exe"))=="64-разрядная","Native x64 (64-bit test host)");
   Check(ExecutableArchitecture.Caption(@"Z:\not-existing-bars-fixture.exe")=="разрядность не определена","Missing file safe");
   string file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".exe");try{File.WriteAllText(file,"invalid");Check(ExecutableArchitecture.Caption(file)=="разрядность не определена","Truncated file safe");}finally{File.Delete(file);}
   ArchitectureFixture(0x14c,1,"AnyCPU");ArchitectureFixture(0x14c,3,"32-разрядная");ArchitectureFixture(0x14c,0x20001,"AnyCPU, предпочтительно 32-разрядная");ArchitectureFixture(0x14c,0,"32-разрядная");
   var local=new LibraryPackage {Id="fixture",Kind=LibraryKind.DotNet,Version="8.0.31",Architecture="x64",RuntimeFamily="Microsoft.NETCore.App"};
   string hash=new string('a',128);string json="{\"channel-version\":\"8.0\",\"releases\":[{\"runtime\":{\"version\":\"8.0.33\",\"files\":[{\"rid\":\"win-x64\",\"name\":\"dotnet-runtime-win-x64.exe\",\"url\":\"https://builds.dotnet.microsoft.com/a.exe\",\"hash\":\""+hash+"\"}]}}]}";
   var results=DotNetUpdates.Parse(json,new[]{local});Check(results.Count==1&&results[0].Newer,"New stable patch found");
   local.Version="8.0.34";Check(!DotNetUpdates.Parse(json,new[]{local})[0].Newer,"Never downgrade");
   local.Preview=true;Check(DotNetUpdates.Parse(json,new[]{local}).Count==0,"No implicit preview update");local.Preview=false;
   local.Architecture="x86";Check(DotNetUpdates.Parse(json,new[]{local}).Count==0,"Architecture isolation");local.Architecture="x64";
   local.Version="10.0.1";Check(DotNetUpdates.Parse(json,new[]{local}).Count==0,"Branch isolation");local.Version="8.0.31";
   bool rejected=false;try{DotNetUpdates.Parse(json.Replace("https://builds.dotnet.microsoft.com","https://example.org"),new[]{local});}catch(InvalidDataException){rejected=true;}Check(rejected,"Unofficial URL rejected");
   Application.EnableVisualStyles();
   foreach(bool dark in new[]{false,true})using(var form=new DotNetUpdatesForm(()=>null)){
    form.SetTheme(dark,false);form.Show();Application.DoEvents();Check(Children(form).OfType<DataGridView>().Single().Rows.Count==0,"No automatic query/download");
    foreach(var b in Children(form).OfType<BrandButton>())Check(b.Parent.ClientRectangle.Contains(b.Bounds),"Update button fits: "+b.Text);
    var table=Children(form).OfType<DataGridView>().Single();table.Rows.Add(false,".NET Runtime 8.0, x64","8.0.31","8.0.33","Доступно обновление");
    string proof=@"C:\ServiceManager3000\work\runtime-updates";Directory.CreateDirectory(proof);using(var image=new System.Drawing.Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new System.Drawing.Rectangle(System.Drawing.Point.Empty,form.Size));image.Save(Path.Combine(proof,"updates-"+dark+".png"));}
   }
   foreach(bool dark in new[]{false,true})using(var form=new LibraryManagerForm(true)){
    form.SetTheme(dark,false);form.Show();Application.DoEvents();var filter=Children(form).OfType<BrandComboBox>().Single(c=>c.Items.Contains("Microsoft .NET"));filter.SelectedIndex=1;Application.DoEvents();
    var table=Children(form).OfType<ServiceGrid>().Single();Check(table.Rows.Count>0&&table.Rows.Cast<DataGridViewRow>().All(row=>((LibraryPackage)row.Tag).Kind==LibraryKind.DotNet||((LibraryPackage)row.Tag).Kind==LibraryKind.Framework),".NET filter isolates families");
    Check(table.Rows.Cast<DataGridViewRow>().All(row=>!Convert.ToBoolean(row.Cells[0].Value)),"Filter clears previous selection");
    using(var image=new System.Drawing.Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new System.Drawing.Rectangle(System.Drawing.Point.Empty,form.Size));image.Save(@"C:\ServiceManager3000\work\runtime-updates\library-"+dark+".png");}
   }
   Console.WriteLine("PASS: "+count+" architecture, metadata isolation and update-dialog checks. No downloads or installations.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
