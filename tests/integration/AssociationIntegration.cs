using System;
using System.IO;
using System.Xml;
namespace ServiceTool { internal static class AssociationIntegration {
[STAThread] static int Main(string[] args) { try {
if(Environment.MachineName!="BARS-LAB-01")throw new Exception("Dedicated VM only");
if(args.Length>0){var result=SoftwareDefaults.Apply(args[0],Console.WriteLine);Console.WriteLine(result.Message);return result.ExitCode;}
foreach(string id in new[]{"chrome","notepad","7zip"}) {
string name=id=="chrome"?"googlechromestandaloneenterprise64.msi":id=="notepad"?"npp.8.9.8.1.Installer.x64.exe":"7z2604-x64.exe";
var p=SoftwareCatalog.Describe(id,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name),"VM test");
var r=SoftwareInstaller.Install(new SoftwareInstallRequest{Id=id,Version=p.Version,Sha256=p.Sha256,AllowAnyDesk32=true}.Serialize(),p.File,Console.WriteLine);
if(!LibraryPlan.Success(r.ExitCode))throw new Exception(id+" install: "+r.Message);
}
foreach(string profile in new[]{"chrome,notepad,7zip","chrome,notepad,7zip","chrome"}) {
var r=SoftwareDefaults.Apply(profile,Console.WriteLine);if(r.ExitCode!=0)throw new Exception(r.Message);
string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"BARS\Manager\DefaultAssociations");
var scoped=new XmlDocument();scoped.Load(Path.Combine(dir,"BARS.xml"));var full=new XmlDocument();full.Load(Path.Combine(dir,"ImageDefaults.xml"));
if(full.SelectNodes("/DefaultAssociations/Association").Count<=scoped.SelectNodes("/DefaultAssociations/Association").Count)throw new Exception("Full profile not preserved");
if(profile=="chrome" && scoped.OuterXml.Contains("BARS."))throw new Exception("Unchecked profiles remain");
Console.WriteLine("PASS profile "+profile+" scoped="+scoped.SelectNodes("/DefaultAssociations/Association").Count+" full="+full.SelectNodes("/DefaultAssociations/Association").Count);
}
return 0;
}catch(Exception ex){Console.WriteLine(ex);return 1;}}
}}
