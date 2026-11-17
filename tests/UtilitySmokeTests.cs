using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace ServiceTool
{
    internal static class UtilitySmokeTests
    {
        [STAThread] public static int Main()
        {
            string root=Path.Combine(Path.GetTempPath(),"bars-real-tools-smoke-"+Guid.NewGuid().ToString("N"));
            string live=Path.Combine(BundleFolders.DefaultRoot,"_tools");
            try
            {
                string source=Path.Combine(root,"source","_tools"),target=Path.Combine(root,"target","_tools");
                string desktop=Path.Combine(root,"Desktop");
                Directory.CreateDirectory(source);
                var ready=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var launchers=new List<BarsTool>();
                var archives=ToolsDeploymentForm.ArchiveDestinations(live);
                foreach(string package in ToolsDeploymentForm.LocalPackages(live))
                {
                    string original=Path.Combine(live,package);
                    string name;
                    if(archives.TryGetValue(package,out name))
                    {
                        string[] entries;
                        var state=ToolArchives.Inspect(original,CancellationToken.None,out entries);
                        if(state!=ToolArchives.ReadState.Ready)
                        {
                            Console.WriteLine("Skipped "+package+": "+state);
                            continue;
                        }
                        string copy=Path.Combine(source,package);
                        File.Copy(original,copy);
                        ToolArchives.Extract(copy,CancellationToken.None,null,false,name);
                    }
                    else
                    {
                        name=package;
                        CopyFixture(original,Path.Combine(source,name));
                    }
                    ready.Add(name);
                    string executable=ToolsDeployment.ShortcutExecutable(package);
                    if(executable==null)continue;
                    var candidates=ToolsDeployment.ShortcutCandidates(Path.Combine(source,name),executable);
                    for(int i=0;i<candidates.Length;i++)
                        launchers.Add(new BarsTool{Name=candidates.Length==1?name:name+" "+(i+1),Directory=name,Executable=candidates[i]});
                }
                if(ready.Count==0)throw new InvalidDataException("No deployable utility packages at "+live);
                ToolsDeployment.CopyToTarget(source,target,ignored=>{},()=>false,ready);
                int files=0;
                foreach(string package in ready)
                    foreach(string file in Directory.GetFiles(Path.Combine(source,package),"*",SearchOption.AllDirectories))
                    {
                        if(Path.GetFileName(file).Equals("desktop.ini",StringComparison.OrdinalIgnoreCase))continue;
                        string copied=Path.Combine(target,file.Substring(source.Length+1));
                        if(SoftwareCatalog.Hash(file)!=SoftwareCatalog.Hash(copied))throw new IOException("Copy differs: "+file);
                        files++;
                    }
                ToolsDeployment.ShortcutsAt(source,target,desktop,true,ready,launchers);
                foreach(var tool in launchers)
                {
                    if(!File.Exists(Path.Combine(desktop,tool.Name+".lnk")))throw new IOException("Missing shortcut: "+tool.Name);
                    using(Icon icon=Icon.ExtractAssociatedIcon(Path.Combine(target,tool.Directory,tool.Executable)))
                        if(icon==null)throw new IOException("No EXE icon: "+tool.Name);
                }
                Console.WriteLine("PASS: "+ready.Count+" real packages, "+files+" files verified by SHA-256, "+launchers.Count+" shortcuts and EXE icons from "+live);
                return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            finally
            {
                if(Directory.Exists(root))
                {
                    foreach(string file in Directory.GetFiles(root,"*",SearchOption.AllDirectories))File.SetAttributes(file,FileAttributes.Normal);
                    foreach(string folder in Directory.GetDirectories(root,"*",SearchOption.AllDirectories))File.SetAttributes(folder,FileAttributes.Directory);
                    Directory.Delete(root,true);
                }
            }
        }

        private static void CopyFixture(string source,string target)
        {
            ToolsDeployment.SafePath(source);
            Directory.CreateDirectory(target);
            foreach(string file in Directory.EnumerateFiles(source))
            {
                ToolsDeployment.SafePath(file);
                File.Copy(file,Path.Combine(target,Path.GetFileName(file)));
            }
            foreach(string folder in Directory.EnumerateDirectories(source))
                CopyFixture(folder,Path.Combine(target,Path.GetFileName(folder)));
        }
    }
}
