using System;
using System.Linq;
namespace ServiceTool
{
 internal static class DotNetPresentationTests
 {
  static int count;static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;}
  [STAThread]static int Main(){try{
   foreach(string family in new[]{"Microsoft.NETCore.App","Microsoft.AspNetCore.App"})foreach(string arch in new[]{"x86","x64"})foreach(int major in new[]{7,8,9,10,11}){
    var package=new LibraryPackage {Kind=LibraryKind.DotNet,RuntimeFamily=family,Architecture=arch,Version=major+".0.1"};var check=new LibraryCheck {Package=package,State=LibraryState.Missing,Relation=LibraryRelation.Incompatible,ActualVersion="6.0.36, 8.0.33, 9.0.22, 10.0.14, 11.0.0-rc.1"};
    string before=check.ActualVersion;var expected=before.Split(',').Select(v=>v.Trim()).Where(v=>LibraryVersions.Compatible(package,v)).ToArray();Check(LibraryPresentation.SystemVersion(check)==(expected.Length==0?"Не установлено":String.Join(", ",expected)),"Only compatible displayed "+major+family+arch);Check(check.ActualVersion==before&&check.Relation==LibraryRelation.Incompatible,"Presentation leaves detection unchanged");
    check.ActualVersion=major+".0.2, "+major+".0.3";Check(LibraryPresentation.SystemVersion(check)==check.ActualVersion,"Matching patches retained");
    package.Version=major+".0.0-rc.1";check.ActualVersion=major+".0.0-preview.1, "+major+".0.0-rc.1, "+major+".0.0-rc.2, "+major+".0.1";Check(LibraryPresentation.SystemVersion(check)==major+".0.0-rc.1","Preview/stable and RC series separated");
    check.ActualVersion="";check.State=LibraryState.Unknown;Check(LibraryPresentation.SystemVersion(check)=="Нужна проверка","Unconfirmed not called missing");
   }
   Check(LibraryPresentation.SystemVersion(new LibraryCheck {Package=new LibraryPackage {Kind=LibraryKind.Msi},ActualVersion="12.0"})=="12.0","Other package display unchanged");
   Console.WriteLine("PASS: "+count+" .NET presentation assertions; no system reads or installers.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
