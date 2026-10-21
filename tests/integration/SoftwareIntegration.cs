using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

// Explicit, destructive integration runner: refuses to run outside the dedicated VM.
class SoftwareIntegration
{
 static readonly BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static readonly string Root=@"C:\Lab\Software239",Reports=@"C:\Lab\Reports\Software239";
 static Assembly app;static StreamWriter log;static int failures;static readonly List<object> results=new List<object>();
 static object Call(string type,string name,params object[] args){return app.GetType("ServiceTool."+type).GetMethods(Static).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(null,args);}
 static object Invoke(object obj,string name,params object[] args){return obj.GetType().GetMethods(Instance).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(obj,args);}
 static string Value(object obj,string field){return Convert.ToString(obj.GetType().GetField(field,Instance).GetValue(obj));}
 static void Set(object obj,string field,object value){obj.GetType().GetField(field,Instance).SetValue(obj,value);}
 static void Log(string text){string line=DateTime.Now.ToString("O")+" "+text;if(!text.StartsWith("Операция выполняется.")&&!text.Contains(" · 00:"))Console.WriteLine(line);log.WriteLine(line);log.Flush();}
 static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Case(string name,Action action){var clock=Stopwatch.StartNew();Log("START "+name);try{action();results.Add(new{name=name,status="PASS",seconds=clock.Elapsed.TotalSeconds});Log("PASS "+name);}catch(Exception ex){while(ex.InnerException!=null)ex=ex.InnerException;failures++;results.Add(new{name=name,status="FAIL",seconds=clock.Elapsed.TotalSeconds,error=ex.Message});Log("FAIL "+name+": "+ex.Message);}}
 static object Check(string id){return Call("SoftwareDetection","Check",id,true,true);}
 static bool Registered(string id)
 {
  foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view))using(var root=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
  {if(root==null)continue;foreach(string name in root.GetSubKeyNames())using(var key=root.OpenSubKey(name)){if(key==null)continue;string display=Convert.ToString(key.GetValue("DisplayName"));if(id=="notepad"?display.StartsWith("Notepad++",StringComparison.OrdinalIgnoreCase):id=="7zip"?display.StartsWith("7-Zip ",StringComparison.OrdinalIgnoreCase):display.Equals(id=="anydesk"?"AnyDesk":"RuDesktop",StringComparison.OrdinalIgnoreCase))return true;}}
  return false;
 }
 static string Password(){byte[] bytes=new byte[24];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return "Lab!"+Convert.ToBase64String(bytes);}
 static string Request(object package,bool remote)
 {
  var request=Activator.CreateInstance(app.GetType("ServiceTool.SoftwareInstallRequest"),true);foreach(string field in new[]{"Id","Sha256","Version"})Set(request,field,Value(package,field));Set(request,"AllowAnyDesk32",true);Set(request,"ConfigureRemote",remote);Set(request,"Password",remote?Password():"");return (string)Invoke(request,"Serialize");
 }
 static object Execute(object session,string action,string data,string path){if(action=="software-remove")Call("SoftwareMaintenance","PreserveUserDocuments",data,new Action<string>(Log));object result=Invoke(session,"ExecuteSystem",action,data,path,new Action<string>(Log));Log("RESULT "+action+" code="+Value(result,"ExitCode")+" state="+Value(result,"State")+" "+Value(result,"Message"));return result;}
 static void Success(object result,string state){Assert(Value(result,"ExitCode")=="0"&&Value(result,"State")==state,"Unexpected result: "+Value(result,"ExitCode")+" / "+Value(result,"State"));}
 static string Settings(string id){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),id=="notepad"?"Notepad++":id=="7zip"?"7-Zip":id=="anydesk"?"AnyDesk":"RuDesktop");}
 static string Canary(string folder){Directory.CreateDirectory(folder);string file=Path.Combine(folder,"BARS-test-settings.txt");File.WriteAllText(file,"Disposable guest integration canary");return file;}
 static void ManualRemoval(object session,object package)
 {
  Success(Execute(session,"software-install",Request(package,false),Value(package,"File")),"Installed");string exe=Value(Check("notepad"),"Executable");var start=(ProcessStartInfo)Call("SoftwareMaintenance","RemovalCommand","notepad",exe,null,Path.Combine(Reports,"manual-remove.log"));
  using(var process=Process.Start(start)){Assert(process.WaitForExit(120000),"Manual vendor uninstall timed out");Assert(process.ExitCode==0,"Vendor uninstall failed");}
  // NSIS may return from its launcher while its temporary uninstaller is finishing.
  for(int i=0;i<150&&(Registered("notepad")||File.Exists(exe));i++)Thread.Sleep(100);
  Assert(!Registered("notepad")&&Value(Check("notepad"),"State")=="Missing","Completed manual removal was still detected as installed");Call("SoftwareMaintenance","CleanupUser","notepad",new Action<string>(Log));
 }
 static void Cycle()
 {
  var packages=((IEnumerable)Call("SoftwareCatalog","Local",Path.Combine(Root,"_soft"),new Action<string>(Log),"VM local _soft")).Cast<object>().ToDictionary(p=>Value(p,"Id"));Assert(packages.Count==4,"Four verified local packages required");
  using(var session=(IDisposable)Call("LibrarySession","Start",new Func<bool>(()=>false)))
  {
   foreach(string id in new[]{"notepad","7zip","anydesk","rudesktop"})Case("local install "+id,()=>{Assert(!Registered(id)&&Value(Check(id),"State")=="Missing","Baseline not clean");object result=Execute(session,"software-install",Request(packages[id],id=="anydesk"||id=="rudesktop"),Value(packages[id],"File"));Success(result,"Installed");var check=Check(id);Assert(Registered(id)&&Value(check,"State")=="Installed"&&File.Exists(Value(check,"Executable")),"Independent ARP/file inventory disagrees");});
   Case("opening module does not download installed AnyDesk",()=>
   {
    Assert(Value(Check("anydesk"),"State")=="Installed","AnyDesk prerequisite failed");
    string cache=Convert.ToString(app.GetType("ServiceTool.SoftwareSources").GetProperty("CacheFolder",Static).GetValue(null,null));Directory.CreateDirectory(cache);int payloads=0;
    using(var watcher=new FileSystemWatcher(cache){IncludeSubdirectories=true})
    using(var form=(Form)Activator.CreateInstance(app.GetType("ServiceTool.SoftwareManagerForm"),Instance,null,new object[]{false},null))
    {
     watcher.Created+=(sender,e)=>{if(e.FullPath.EndsWith(".part",StringComparison.OrdinalIgnoreCase)&&e.FullPath.IndexOf("anydesk",StringComparison.OrdinalIgnoreCase)>=0)Interlocked.Increment(ref payloads);};watcher.EnableRaisingEvents=true;
     form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new System.Drawing.Point(-22000,-22000);form.Show();
     var busy=form.GetType().GetField("busy",Instance);var timer=Stopwatch.StartNew();do{Application.DoEvents();Thread.Sleep(50);}while((bool)busy.GetValue(form)&&timer.Elapsed.TotalSeconds<75);
     Assert(!(bool)busy.GetValue(form),"Initial module check exceeded 75 seconds");Assert(payloads==0,"Opening module downloaded AnyDesk payload");form.Close();
    }
   });
   foreach(string id in packages.Keys)Case("same-version install skipped "+id,()=>{Assert(Value(Check(id),"State")=="Installed","Installation prerequisite failed");object result=Execute(session,"software-install",Request(packages[id],false),Value(packages[id],"File"));Success(result,"Installed");Assert(Value(result,"Message").Contains("пропущен"),"Same version was not skipped");});
   foreach(string id in new[]{"anydesk","rudesktop"})Case("configure existing remote access "+id,()=>{Assert(Value(Check(id),"State")=="Installed","Remote client missing");Success(Execute(session,"software-access",Request(packages[id],true),""),"Installed");string password=Password();Call("SoftwarePasswordStore","Save",id,password);Assert((string)Call("SoftwarePasswordStore","Load",id)==password,"DPAPI remember mismatch");Call("SoftwarePasswordStore","Forget",id);Assert((string)Call("SoftwarePasswordStore","Load",id)=="","Saved secret not forgotten");});
   Case("default file associations",()=>{Success(Execute(session,"software-defaults","notepad,7zip",""),"Installed");string xml=File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"BARS\Manager\DefaultAssociations\BARS.xml"));foreach(string ext in new[]{".txt",".cfg",".json",".config",".zip",".7z",".rar"})Assert(xml.Contains("Identifier=\""+ext+"\""),"Missing association "+ext);});
   foreach(string id in new[]{"anydesk","rudesktop","notepad","7zip"})Case("clean removal "+id,()=>
   {
    Assert(Value(Check(id),"State")=="Installed","Removal prerequisite failed");var canaries=new List<string>();string user=Settings(id);canaries.Add(Canary(id=="rudesktop"?Path.Combine(user,"config"):user));foreach(string path in (string[])Call("SoftwareMaintenance","MachineSettings",id))canaries.Add(Canary(path));
    string unrelated=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"BARS-Unrelated-Test");string keep=Canary(unrelated);
    Success(Execute(session,"software-remove",id,""),"Missing");Call("SoftwareMaintenance","CleanupUser",id,new Action<string>(Log));Assert(!Registered(id)&&Value(Check(id),"State")=="Missing","Removal not independently confirmed");Assert(canaries.All(file=>!File.Exists(file)),"Settings canary survived cleanup");Assert(File.Exists(keep),"Unrelated folder changed");
   });
   foreach(string id in packages.Keys)Case("install again after clean removal "+id,()=>{Assert(Value(Check(id),"State")=="Missing","Removal prerequisite failed");Success(Execute(session,"software-install",Request(packages[id],false),Value(packages[id],"File")),"Installed");Assert(Registered(id),"Reinstalled app absent from ARP");Success(Execute(session,"software-remove",id,""),"Missing");Call("SoftwareMaintenance","CleanupUser",id,new Action<string>(Log));Assert(!Registered(id),"Second removal left registration");});
   Case("manual uninstall refresh notepad",()=>ManualRemoval(session,packages["notepad"]));
  }
 }
 static void Sources()
 {
  foreach(string id in new[]{"notepad","7zip","anydesk","rudesktop"})Case("official latest download "+id,()=>{object latest=Call("SoftwareSources","Latest",id);object package=Call("SoftwareSources","Download",latest,Path.Combine(Root,"downloads"),new Action<string>(Log),new Func<bool>(()=>false));Assert(File.Exists(Value(package,"File")),"Download absent");Log("VERIFIED "+id+" "+Value(package,"Version")+" "+Value(package,"Architecture")+" SHA256="+Value(package,"Sha256"));});
 }
 static void Cancellation()
 {
  var package=Call("SoftwareCatalog","Describe","rudesktop",Directory.GetFiles(Path.Combine(Root,"_soft"),"rudesktop-*.msi").Single(),"VM cancellation fixture");
  Case("cancel before launch",()=>{bool rejected=false;try{Call("LibrarySession","Start",new Func<bool>(()=>true));}catch(TargetInvocationException e){rejected=e.InnerException is OperationCanceledException;}Assert(rejected&&!Registered("rudesktop"),"Pre-cancel did not stop preparation");});
  Case("active installer cancellation",()=>
  {
   using(var session=(IDisposable)Call("LibrarySession","Start",new Func<bool>(()=>false)))
   using(var timer=new System.Threading.Timer(ignored=>Invoke(session,"Cancel"),null,Timeout.Infinite,Timeout.Infinite))
   {
    bool armed=false;Action<string> progress=text=>{Log(text);if(!armed&&text.Contains("Устанавливаем")){armed=true;timer.Change(400,Timeout.Infinite);}};
    var watch=Stopwatch.StartNew();object result=Invoke(session,"ExecuteSystem","software-install",Request(package,false),Value(package,"File"),progress);
    Assert(armed,"Installer phase never reached");Assert(Value(result,"ExitCode")!="0"&&Value(result,"State")!="Installed","Cancelled installer incorrectly reported success");Assert(watch.Elapsed.TotalSeconds<90,"Cancellation exceeded 90 seconds");Log("POST-CANCEL independent registration="+Registered("rudesktop")+" manager="+Value(Check("rudesktop"),"State"));
   }
  });
 }
 static void Boundaries()
 {
  string original=Directory.GetFiles(Path.Combine(Root,"_soft"),"npp.*.Installer.x64.exe").Single();
  var package=Call("SoftwareCatalog","Describe","notepad",original,"VM boundary test");
  using(var session=(IDisposable)Call("LibrarySession","Start",new Func<bool>(()=>false)))
  {
   Case("changed installer rejected before execution",()=>
   {
    Assert(!Registered("notepad"),"Notepad baseline not clean");string dir=Path.Combine(Root,"changed");Directory.CreateDirectory(dir);string changed=Path.Combine(dir,Path.GetFileName(original));File.Copy(original,changed,true);using(var stream=new FileStream(changed,FileMode.Append,FileAccess.Write))stream.WriteByte(0);
    object result=Execute(session,"software-install",Request(package,false),changed);Assert(Value(result,"ExitCode")!="0"&&Value(result,"State")!="Installed"&&!Registered("notepad"),"Changed package was accepted");
   });
   Case("spaces and Cyrillic source path plus unsaved backup preservation",()=>
   {
    string dir=Path.Combine(Root,"пакеты с пробелами");Directory.CreateDirectory(dir);string copied=Path.Combine(dir,Path.GetFileName(original));File.Copy(original,copied,true);Success(Execute(session,"software-install",Request(package,false),copied),"Installed");Assert(Registered("notepad"),"Unicode source install not registered");
    string backup=Path.Combine(Settings("notepad"),"backup");Directory.CreateDirectory(backup);string name="BARS-unsaved-fixture-"+Guid.NewGuid().ToString("N")+".txt";File.WriteAllText(Path.Combine(backup,name),"Disposable unsaved text fixture");
    Success(Execute(session,"software-remove","notepad",""),"Missing");Call("SoftwareMaintenance","CleanupUser","notepad",new Action<string>(Log));
    string storage=(string)app.GetType("ServiceTool.ApplicationStorage").GetField("DirectoryPath",Static).GetValue(null);string backups=Path.Combine(storage,"UninstallBackups");Assert(Directory.Exists(backups)&&Directory.GetFiles(backups,name,SearchOption.AllDirectories).Length==1,"Unsaved backup was lost");Assert(!Registered("notepad"),"Unicode-source app still registered after removal");
   });
  }
 }
 static void Upgrade()
 {
  Case("real Notepad update and downgrade prevention",()=>
  {
   Assert(!Registered("notepad"),"Update baseline not clean");
   // Fixed official release fixture avoids the public API rate limit; digest is published on its release page.
   object oldRelease=Activator.CreateInstance(app.GetType("ServiceTool.SoftwarePackage"),true);
   Set(oldRelease,"Id","notepad");Set(oldRelease,"Version","8.9.8");Set(oldRelease,"Architecture","x64");
   Set(oldRelease,"File","npp.8.9.8.Installer.x64.exe");Set(oldRelease,"Source","GitHub official v8.9.8 test fixture");
   Set(oldRelease,"Url","https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v8.9.8/npp.8.9.8.Installer.x64.exe");
   Set(oldRelease,"Sha256","7b2a949bf460fb37a3888c9048698f43222a185a48323023df1c51e78a3ca1c2");
   object old=Call("SoftwareSources","Download",oldRelease,Path.Combine(Root,"older"),new Action<string>(Log),new Func<bool>(()=>false));
   object current=Call("SoftwareCatalog","Describe","notepad",Directory.GetFiles(Path.Combine(Root,"_soft"),"npp.*.Installer.x64.exe").Single(),"VM current package");
   Assert((int)Call("SoftwareCatalog","Compare",Value(old,"Version"),Value(current,"Version"))<0,"Expected older signed release");
   using(var session=(IDisposable)Call("LibrarySession","Start",new Func<bool>(()=>false)))
   {
    Success(Execute(session,"software-install",Request(old,false),Value(old,"File")),"Installed");Assert(Value(Check("notepad"),"Version")==Value(old,"Version"),"Older release not confirmed");
    Success(Execute(session,"software-install",Request(current,false),Value(current,"File")),"Installed");Assert(Value(Check("notepad"),"Version")==Value(current,"Version"),"Update version not confirmed");
    object skipped=Execute(session,"software-install",Request(old,false),Value(old,"File"));Success(skipped,"Installed");Assert(Value(skipped,"Message").Contains("пропущен")&&Value(Check("notepad"),"Version")==Value(current,"Version"),"Older package caused downgrade");
    Success(Execute(session,"software-remove","notepad",""),"Missing");Call("SoftwareMaintenance","CleanupUser","notepad",new Action<string>(Log));Assert(!Registered("notepad"),"Updated app not removed");
   }
  });
 }
 [STAThread]static int Main(string[] args)
 {
  if(!String.Equals(Environment.MachineName,"BARS-LAB-01",StringComparison.OrdinalIgnoreCase))return 87;
  app=Assembly.LoadFrom(Path.Combine(Root,"Bars Manager 3000.exe"));
  if(args.Length>0&&args[0]=="--library-worker")return (int)Call("LibrarySession","RunWorker",(object)args);
  string mode=args.Length==1?args[0]:"cycle";Directory.CreateDirectory(Reports);
  using(log=new StreamWriter(Path.Combine(Reports,mode+".log"),false,new UTF8Encoding(false)))
  {
   Log("REAL SOFTWARE TEST guest="+Environment.MachineName+" manager="+app.GetName().Version+" mode="+mode);
   try{if(mode=="cycle")Cycle();else if(mode=="sources")Sources();else if(mode=="cancel")Cancellation();else if(mode=="boundaries")Boundaries();else if(mode=="upgrade")Upgrade();else if(mode=="manual")using(var session=(IDisposable)Call("LibrarySession","Start",new Func<bool>(()=>false))){Case("manual uninstall refresh notepad",()=>ManualRemoval(session,Call("SoftwareCatalog","Describe","notepad",Directory.GetFiles(Path.Combine(Root,"_soft"),"npp.*.Installer.x64.exe").Single(),"VM manual uninstall")));}else throw new Exception("Unknown mode");}catch(Exception ex){while(ex.InnerException!=null)ex=ex.InnerException;failures++;Log("FATAL "+ex.Message);}
   File.WriteAllText(Path.Combine(Reports,mode+"-results.json"),new JavaScriptSerializer().Serialize(new{mode=mode,failures=failures,cases=results}),new UTF8Encoding(false));Log("COMPLETED failures="+failures);return failures==0?0:1;
  }
 }
}
