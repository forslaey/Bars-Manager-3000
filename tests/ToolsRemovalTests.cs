using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
namespace ServiceTool
{
 internal static class ToolsRemovalTests
 {
  private static int count;
  private static void Check(bool ok,string text){count++;if(!ok)throw new Exception(text);}
  private static void Reject(Action action){try{action();}catch(IOException){count++;return;}throw new Exception("Unsafe removal accepted");}
  [STAThread]internal static int Main()
  {
   string root=Path.Combine(Path.GetTempPath(),"bars-tools-remove-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   string registry=@"Software\BARS\Tests\"+Guid.NewGuid().ToString("N");
   try
   {
    string source=Path.Combine(root,"source","_tools"),target=Path.Combine(root,"target","_tools"),desktop=Path.Combine(root,"desktop");Directory.CreateDirectory(source);
    string[] folders={"fiLinn_V0.3","Piglet_V0.12","WinSCP v6.6.2"},exes={"cspPanel.exe","DK-QStart.exe","WinSCP.exe"};
    for(int i=0;i<3;i++){string d=Path.Combine(source,folders[i]);Directory.CreateDirectory(d);File.WriteAllText(Path.Combine(d,exes[i]),"inert fixture");File.WriteAllText(Path.Combine(d,"user.ini"),"settings");}
    File.WriteAllText(Path.Combine(source,folders[0],"tftpd32.exe"),"inert fixture");
    Reject(()=>ToolsDeployment.CopyToTarget(source,Path.Combine(source,"child","_tools"),s=>{},()=>false));
    var progress=new DownloadProgressView();progress.BeginOperation("Распаковка",true,3);int reports=0;double last=0;var report=progress.OperationReporter;var result=ManagerSystemActions.Execute("tools-deploy",source,target,s=>{report(s);if(s.StartsWith("BARS_FRACTION|")){Check(progress.Snapshot.Overall>=last,"Real copied-file progress never decreases");last=progress.Snapshot.Overall;reports++;}});Check(reports>=7&&last>0&&last<1D/3,"Real files contribute within copy task");progress.CompleteStep();Check(Math.Abs(progress.Snapshot.Overall-1D/3)<.0001,"Copy task commits only after worker success");progress.Dispose();Check(result.ExitCode==0&&File.Exists(Path.Combine(target,folders[0],exes[0])),"Actual worker action accepts full target path");
    Directory.CreateDirectory(Path.Combine(target,"OtherTool"));File.WriteAllText(Path.Combine(target,"OtherTool","keep.txt"),"keep");File.WriteAllText(Path.Combine(target,"unrelated.txt"),"keep");string backup=target+"-backup-test";Directory.CreateDirectory(backup);File.WriteAllText(Path.Combine(backup,"keep.txt"),"keep");
    var tools=ToolsDeployment.Installed(target);Check(tools.Count==3,"Only known utilities selected");string confirmed=String.Join("\n",tools.Select(t=>t.Directory));
    Reject(()=>ToolsDeployment.Remove(target,confirmed+"\nOtherTool",s=>{}));Check(Directory.Exists(Path.Combine(target,folders[0])),"Changed plan rejected before mutation");
    Reject(()=>ToolsDeployment.Installed(Path.Combine(root,"not_tools")));
    Reject(()=>ToolsDeployment.TargetFolder("C:_tools"));
    Reject(()=>ToolsDeployment.TargetFolder(@"\\server\share\_tools"));
    ToolsDeployment.ShortcutsAt(source,target,desktop,false);Check(!Directory.Exists(desktop),"Unchecked shortcut option creates no desktop directory or links");
    ToolsDeployment.ShortcutsAt(source,target,desktop);Check(Directory.GetFiles(desktop,"*.lnk").Length==4,"Four own shortcut fixtures");File.WriteAllText(Path.Combine(desktop,"keep.txt"),"keep");
    using(var key=Registry.CurrentUser.CreateSubKey(registry))
    {
     var paths=ToolsDeployment.InstalledLaunchers(target,tools);key.SetValue(paths[0],"~ RUNASADMIN HIGHDPIAWARE");key.SetValue(paths[1],"~ RUNASADMIN");key.SetValue("unrelated","~ RUNASADMIN");
     ToolsDeployment.RemoveAdministratorSettings(key,paths);Check(Convert.ToString(key.GetValue(paths[0]))=="~ HIGHDPIAWARE"&&key.GetValue(paths[1])==null&&Convert.ToString(key.GetValue("unrelated"))=="~ RUNASADMIN","Only target admin flags removed, other flags preserved");
    }
    var removed=ManagerSystemActions.Execute("tools-remove",confirmed,target,s=>{});Check(removed.ExitCode==0,"Worker removal completes");
    Check(ToolsDeployment.Installed(target).Count==0,"Recognized tools removed");Check(File.Exists(Path.Combine(target,"OtherTool","keep.txt"))&&File.Exists(Path.Combine(target,"unrelated.txt"))&&File.Exists(Path.Combine(backup,"keep.txt")),"Other content and backups preserved");
    ToolsDeployment.RemoveShortcuts(ToolsDeployment.InstalledLaunchers(target,tools),desktop);Check(Directory.GetFiles(desktop,"*.lnk").Length==0&&File.Exists(Path.Combine(desktop,"keep.txt")),"Own shortcuts removed without touching other files");
    Check(File.Exists(Path.Combine(source,folders[0],exes[0])),"Source never deleted");Check(!Directory.GetDirectories(target,".bars-remove-*").Any(),"No leftover removal stage");
    Reject(()=>ToolsDeployment.Remove(target,confirmed,s=>{}));
    Console.WriteLine("PASS: "+count+" deployment/uninstall/shortcut/preservation checks. Only generated temporary files and isolated registry fixture changed; no real tool installed or removed.");return 0;
   }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
   finally{Registry.CurrentUser.DeleteSubKeyTree(registry,false);Clean(root);}
  }
  private static void Clean(string root){if(!Directory.Exists(root))return;foreach(var d in Directory.GetDirectories(root))Clean(d);foreach(var f in Directory.GetFiles(root)){File.SetAttributes(f,FileAttributes.Normal);File.Delete(f);}File.SetAttributes(root,FileAttributes.Normal);Directory.Delete(root);}
 }
}
