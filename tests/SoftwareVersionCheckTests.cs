using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    // Explicit read-only host smoke: an installed AnyDesk exercises the old download branch.
    internal static class SoftwareVersionCheckTests
    {
        private static int count,requests,lookups;
        private static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
        private static void Reject(string text){bool rejected=false;try{SoftwareSources.ParseAnyDesk(text);}catch(InvalidDataException){rejected=true;}Check(rejected,"Unrecognized/preview/non-Windows changelog rejected");}
        private sealed class NoHttp : IWebRequestCreate
        {public WebRequest Create(Uri uri){Interlocked.Increment(ref requests);throw new WebException("Unexpected network during injected metadata test: "+uri);}}
        private static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);}
        private static void Wait(Func<bool> done){var timer=System.Diagnostics.Stopwatch.StartNew();Application.DoEvents();while(!done()&&timer.Elapsed.TotalSeconds<20){Application.DoEvents();Thread.Sleep(10);}Check(done(),"Version check timed out");}
        [STAThread]public static int Main(string[] args)
        {
            if(args.Length==1&&args[0]=="--live")
            {
                try{var actual=SoftwareSources.Latest("anydesk");Check(actual.Version!="0.0"&&!Path.IsPathRooted(actual.File),"Live AnyDesk metadata contains version without a downloaded package");Console.WriteLine("PASS: live AnyDesk Windows metadata "+actual.Version+"; installer not downloaded.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            }
            string temp=Path.Combine(Path.GetTempPath(),"bars-version-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            try
            {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                var parsed=SoftwareSources.ParseAnyDesk("16.10.2026 - 99.0.0 (macOS)\r\n15.10.2026 - 9.8.2 (Windows)\r\n06.10.2026 - 9.8.1 (Windows)\r\n16.10.2026 - 10.0.0-beta (Windows)\r\n");
                Check(parsed.Version=="9.8.2"&&parsed.Architecture=="x86","Windows stable release only; other platforms/preview ignored");
                Check(parsed.Url=="https://download.anydesk.com/AnyDesk.exe"&&parsed.File=="AnyDesk.exe","Descriptor keeps download URL without downloading");
                Check(SoftwareSources.ParseAnyDesk("01.01.2026 - 9.8.9 (Windows)\n02.01.2026 - 9.8.10 (Windows)\n").Version=="9.8.10","Numeric comparison independent of entry order");
                Reject("");Reject("15.10.2026 - 99.0.0 (Linux)");Reject("15.10.2026 - 9.8.2-preview (Windows)");Reject("<html>Access denied</html>");
                foreach(var app in SoftwareCatalog.Apps){var found=SoftwareDetection.Check(app.Id,true);Check(!(found.State==LibraryState.Unknown&&found.Detail.Contains("доступ")),"Host inventory must not trigger elevation");}
                var installed=SoftwareDetection.Check("anydesk",true);Check(installed.State==LibraryState.Installed,"This read-only regression smoke requires an installed AnyDesk");
                Check(WebRequest.RegisterPrefix("https://",new NoHttp()),"Network interceptor installed");
                foreach(bool dark in new[]{false,true})
                {
                    int round=0;
                    Func<string,SoftwarePackage> versions=id=>
                    {
                        Interlocked.Increment(ref lookups);
                        return new SoftwarePackage{Id=id,Version=id=="anydesk"?(round==0?"0.0":round==1?installed.Version:"99.0.0"):"1.0",Architecture=id=="anydesk"?"x86":"x64",File=id=="rudesktop"?"fixture.msi":"fixture.exe",Url=id=="anydesk"?"https://download.anydesk.com/AnyDesk.exe":"",Source="Metadata fixture",Sha256=""};
                    };
                    using(var form=new SoftwareManagerForm(false,versions))
                    {
                        form.SetTheme(dark,false);form.GetType().GetField("source",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,temp);
                        form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-22000,-22000);form.Show();Wait(()=>!Field<bool>(form,"busy"));Check(lookups==0,"Opening form does not request remote metadata");
                        for(round=0;round<3;round++)
                        {
                            int before=lookups;var task=(Task)form.GetType().GetMethod("RefreshPackages",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{true});Wait(()=>task.IsCompleted);task.GetAwaiter().GetResult();
                            Check(lookups-before==5,"Explicit check obtains metadata for four programs");
                            Check(requests==0,"Successful metadata check, including unknown/newer AnyDesk, never requests installer");
                            Check(Field<ServiceGrid>(form,"grid").Rows.Count==5&&Field<BrandButton>(form,"refresh").Enabled,"Rows and controls remain available");
                            var packages=Field<Dictionary<string,SoftwarePackage>>(form,"packages");
                            Check(packages.ContainsKey("anydesk"),"AnyDesk metadata available");
                            if(round==1)Check(!SoftwareMaintenance.CanInstall(installed,packages["anydesk"]),"Same installed version cannot be offered as an update");
                            if(round==2)Check(SoftwareMaintenance.CanInstall(installed,packages["anydesk"]),"Newer metadata offers update without downloading");
                        }
                        form.Close();
                    }
                    lookups=0;
                }
                Console.WriteLine("PASS: "+count+" metadata-only/version-check assertions; no installer or settings changes.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            finally{Directory.Delete(temp,true);}
        }
    }
}
