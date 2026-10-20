using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
class Net6Integration {
 static Assembly app;
 static StreamWriter report;
 static readonly BindingFlags Flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static object Call(string type,string method,params object[] args) {return app.GetType("ServiceTool."+type).GetMethods(Flags).Single(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(null,args);}
 static string Field(object value,string name){return Convert.ToString(value.GetType().GetField(name).GetValue(value));}
 static void Log(string text){string line=DateTime.Now.ToString("O")+" "+text;Console.WriteLine(line);report.WriteLine(line);report.Flush();}
 static bool RuntimePresent(string architecture,string family){
  string exe=architecture=="x64"?@"C:\Program Files\dotnet\dotnet.exe":@"C:\Program Files (x86)\dotnet\dotnet.exe";
  if(!File.Exists(exe))return false;
  using(var process=Process.Start(new ProcessStartInfo(exe,"--list-runtimes"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})) {
   string output=process.StandardOutput.ReadToEnd();process.WaitForExit();
   return output.Split('\n').Any(line=>line.StartsWith(family+" 6.0.36 ",StringComparison.Ordinal));
  }
 }
 static int Main(){
  if(!String.Equals(Environment.MachineName,"BARS-LAB-01",StringComparison.OrdinalIgnoreCase)){Console.Error.WriteLine("Only run in the dedicated BARS test VM.");return 87;}
  Directory.CreateDirectory(@"C:\Lab\Reports");
  using(report=new StreamWriter(@"C:\Lab\Reports\net6-integration.log",false,new UTF8Encoding(false)))try{
   app=Assembly.LoadFrom(@"C:\Lab\Input\Bars Manager 3000.exe");
   Log("REAL integration test in "+Environment.MachineName+"; manager "+app.GetName().Version);
   var packages=((IEnumerable)Call("LibraryDiscovery","Scan",@"C:\Lab\Input",new Action<string>(Log),null)).Cast<object>().Where(p=>Field(p,"Version")=="6.0.36").ToList();
   if(packages.Count!=4)throw new Exception("Expected exactly four .NET 6 packages, got "+packages.Count);
   foreach(var p in packages){
    string arch=Field(p,"Architecture"),family=Field(p,"RuntimeFamily");
    string detected=Field(Call("LibraryDetection","Check",p),"State");
    Log("BEFORE "+Field(p,"Name")+" state="+detected+" runtime="+RuntimePresent(arch,family));
    if(detected!="Missing"||RuntimePresent(arch,family))throw new Exception("Baseline is not clean.");
   }
   var operation=app.GetType("ServiceTool.LibraryOperation");
   foreach(string action in new[]{"Install","Install","Reinstall","Remove","Install","Remove"}){
    var ordered=packages.OrderBy(p=>action=="Install"?(Field(p,"RuntimeFamily")=="Microsoft.NETCore.App"?0:1):(Field(p,"RuntimeFamily")=="Microsoft.AspNetCore.App"?0:1)).ThenBy(p=>Field(p,"Architecture"));
    foreach(var p in ordered){
     string label=Field(p,"Name");Log("START "+action+" "+label);
     object result=Call("LibrarySession","InstallVerified",p,@"C:\Lab\Input","integration-"+Guid.NewGuid().ToString("N"),new Action<string>(Log),new Func<bool>(()=>true),Enum.Parse(operation,action));
     object after=Call("LibraryDetection","Check",p);
     bool installed=action!="Remove";
     bool runtime=RuntimePresent(Field(p,"Architecture"),Field(p,"RuntimeFamily"));
     Log("RESULT "+action+" "+label+" code="+Field(result,"ExitCode")+" managerState="+Field(after,"State")+" runtime="+runtime+" message="+Field(result,"Message")+" logs="+Field(result,"LogPath"));
     string expected=installed?"Installed":"Missing";
     if(Field(result,"ExitCode")!="0"||Field(after,"State")!=expected||runtime!=installed)throw new Exception("Actual state did not match "+action+" for "+label);
    }
   }
   Log("PASS: 24 operations (install, repeated install, reinstall, remove, install, remove) confirmed by manager detection and independent dotnet --list-runtimes.");return 0;
  }catch(Exception ex){while(ex.InnerException!=null)ex=ex.InnerException;Log("FAIL: "+ex);return 1;}
 }
}
