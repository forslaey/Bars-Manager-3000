using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Security.AccessControl;
namespace ServiceTool
{
    internal static class LibraryCancellationTests
    {
        private static int count;
        private static string self = Assembly.GetExecutingAssembly().Location;
        private static void Check(bool test, string message) { count++; if (!test) throw new Exception(message); }
        private static void Wait(Func<bool> ready) { var elapsed=Stopwatch.StartNew(); while(!ready() && elapsed.ElapsedMilliseconds<10000) { Application.DoEvents(); Thread.Sleep(20); } if(!ready())throw new Exception("Inert fixture did not become ready"); }
        private static ProcessStartInfo Start(string mode,string marker) { return new ProcessStartInfo(self,mode+" "+LibraryPlan.Quote(marker)) { UseShellExecute=false, CreateNoWindow=true }; }
        private static void WritePid(string path) { File.WriteAllText(path+".tmp",Process.GetCurrentProcess().Id.ToString());File.Move(path+".tmp",path); }
        private static bool Alive(int pid) { try { using(var p=Process.GetProcessById(pid)) return !p.HasExited; } catch(ArgumentException){return false;} }
        private static void Clean(string marker) { if(File.Exists(marker)) { int pid; if(Int32.TryParse(File.ReadAllText(marker),out pid) && Alive(pid)) using(var p=Process.GetProcessById(pid)){p.Kill();p.WaitForExit(3000);} } }
        [STAThread] public static int Main(string[] args)
        {
            if(args.Length==2 && args[0]=="--child") { WritePid(args[1]);Thread.Sleep(60000);return 0; }
            if(args.Length==2 && args[0]=="--parent") { using(var p=Process.Start(Start("--child",args[1]))){} return 0; }
            if(args.Length==2 && args[0]=="--controller") { return LibraryProcess.Run(Start("--parent",args[1]),s=>{}); }
            if(args.Length==2 && args[0]=="--gui")
            {
                using(var form=new Form { ShowInTaskbar=false, StartPosition=FormStartPosition.Manual, Location=new System.Drawing.Point(-20000,-20000) })
                { form.Shown+=delegate {WritePid(args[1]);};form.FormClosed+=delegate {File.WriteAllText(args[1]+".closed","normal-close");};Application.Run(form); }return 0;
            }
            string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cancel-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            string child=Path.Combine(directory,"owned.pid"), unrelated=Path.Combine(directory,"unrelated.pid"), controllerChild=Path.Combine(directory,"crash-child.pid"), gui=Path.Combine(directory,"gui.pid");
            try
            {
                using(var memory=new MemoryStream(new byte[200000]))
                {
                    bool hashCancelled=false;try{LibraryCancellation.Hash(memory,()=>memory.Position>0);}catch(OperationCanceledException){hashCancelled=true;}
                    Check(hashCancelled && memory.Position<memory.Length,"Source verification cancels between blocks before reading entire file");
                }
                string name="BARS.Library."+Guid.NewGuid().ToString("N");
                using(var signal=LibraryCancellation.Create(name))
                using(var reader=EventWaitHandle.OpenExisting(LibraryCancellation.EventName(name),EventWaitHandleRights.Synchronize))
                { Check(!reader.WaitOne(0),"New authenticated session not cancelled");signal.Set();Check(reader.WaitOne(0),"Separate worker handle observes cancel without pipe writes"); }
                bool beforeUac=false;try{LibrarySession.Start(()=>true);}catch(OperationCanceledException){beforeUac=true;}Check(beforeUac,"Cancelled queue returns before event/pipe creation and any UAC request");
                bool early=false;try{LibraryProcess.Run(new ProcessStartInfo("this-file-does-not-exist.exe"),s=>{},()=>true);}catch(OperationCanceledException){early=true;}
                Check(early,"Cancellation before launch never creates a process");
                LibraryCancellation.Current=()=>true;
                try { bool blocked=false;try{LibrarySession.InstallVerified(new LibraryPackage(),"", "fixture",s=>{},()=>true);}catch(OperationCanceledException){blocked=true;}Check(blocked,"Worker cancellation checked before mutex, staging or installer"); }
                finally {LibraryCancellation.Current=null;}
                using(var other=Process.Start(Start("--child",unrelated)))
                {
                    Wait(()=>File.Exists(unrelated));int cancel=0;
                    Task<bool> running=Task.Run(()=>{try{LibraryProcess.Run(Start("--parent",child),s=>{},()=>Volatile.Read(ref cancel)==1);return false;}catch(OperationCanceledException){return true;}});
                    Wait(()=>File.Exists(child));int pid=Int32.Parse(File.ReadAllText(child));Check(Alive(pid),"Inert grandchild remains after fast parent exit");
                    Interlocked.Exchange(ref cancel,1);Check(running.Wait(10000)&&running.Result,"Active process tree cancellation completes with cancelled outcome");
                    Wait(()=>!Alive(pid));Check(!Alive(pid),"Owned grandchild terminated; no orphan after parent exits");Check(!other.HasExited,"Unrelated process with identical EXE name survives cancellation");
                    using(var controller=Process.Start(Start("--controller",controllerChild)))
                    {Wait(()=>File.Exists(controllerChild));int orphan=Int32.Parse(File.ReadAllText(controllerChild));controller.Kill();controller.WaitForExit(3000);Wait(()=>!Alive(orphan));Check(!Alive(orphan),"Worker crash closes private job and kills only its descendants");}
                    Check(!other.HasExited,"Unrelated process survives worker crash cleanup");
                    cancel=0;Task<bool> graceful=Task.Run(()=>{try{LibraryProcess.Run(Start("--gui",gui),s=>{},()=>Volatile.Read(ref cancel)==1);return false;}catch(OperationCanceledException){return true;}});
                    Wait(()=>File.Exists(gui));Interlocked.Exchange(ref cancel,1);Check(graceful.Wait(10000)&&graceful.Result,"GUI cancellation completes");Check(File.Exists(gui+".closed"),"WM_CLOSE gives owned GUI chance to close normally before force");
                    other.Kill();other.WaitForExit(3000);
                }
                Console.WriteLine("PASS: "+count+" cancellation checks. Only isolated inert processes were terminated; no real installer, uninstall, UAC or SCM action.");return 0;
            }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
            finally{Clean(child);Clean(unrelated);Clean(controllerChild);Clean(gui);}
        }
    }
}
