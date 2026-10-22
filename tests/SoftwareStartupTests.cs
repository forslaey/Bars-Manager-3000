using System;
using System.Diagnostics;
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
    internal static class SoftwareStartupTests
    {
        private static int count,requests;
        private static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
        private sealed class BlockInternet : IWebRequestCreate
        {
            public WebRequest Create(Uri uri){Interlocked.Increment(ref requests);throw new WebException("Test: internet unavailable");}
        }
        private static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target);}
        private static void PumpUntil(Func<bool> done)
        {
            var timer=Stopwatch.StartNew();while(!done()&&timer.Elapsed.TotalSeconds<20){Application.DoEvents();Thread.Sleep(10);}
            Check(done(),"Read-only startup/check exceeded 20 seconds");
        }
        [STAThread]public static int Main()
        {
            string temp=Path.Combine(Path.GetTempPath(),"bars-startup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            try
            {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                // Do not trigger an elevation prompt on a host whose ARP keys require it.
                foreach(var app in SoftwareCatalog.Apps){var check=SoftwareDetection.Check(app.Id,true);Check(!(check.State==LibraryState.Unknown&&check.Detail.Contains("доступ")),"Read-only inventory requires elevated VM test on this machine");}
                Check(WebRequest.RegisterPrefix("https://",new BlockInternet()),"HTTPS interceptor installed");
                foreach(bool dark in new[]{false,true})for(int repeat=0;repeat<2;repeat++)
                using(var form=new SoftwareManagerForm(false))
                {
                    string source=repeat==1&&Directory.Exists(@"C:\Библиотеки\_soft")?@"C:\Библиотеки\_soft":temp;
                    form.SetTheme(dark,false);form.GetType().GetField("source",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,source);
                    var grid=Field<ServiceGrid>(form,"grid");Check(grid.Rows.Count==5,"All four programs exist before Shown and before any asynchronous scan");
                    Check(!Field<BrandButton>(form,"install").Enabled&&!Field<BrandButton>(form,"remove").Enabled,"Unverified startup rows cannot install or remove anything");
                    var timer=Stopwatch.StartNew();form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-22000,-22000);form.Show();
                    Application.DoEvents();PumpUntil(()=>!Field<bool>(form,"busy"));
                    Check(requests==0,"Opening module must not request any internet resource");
                    Check(grid.Rows.Cast<DataGridViewRow>().All(r=>Convert.ToString(r.Cells[2].Value)!="Не проверено"),"Windows inventory populated: "+Field<JournalTextBox>(form,"journal").Text);
                    Check(Field<BrandButton>(form,"refresh").Enabled&&Field<BrandButton>(form,"back").Enabled,"Version check and menu available after local scan");
                    Console.WriteLine("STARTUP "+(dark?"dark":"light")+" source="+(source==temp?"empty":"real local _soft")+" seconds="+timer.Elapsed.TotalSeconds.ToString("F3")+" http="+requests);
                    if(dark&&repeat==1)
                    {
                        var task=(Task)form.GetType().GetMethod("RefreshPackages",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,new object[]{true});
                        PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();Check(requests>=3,"Explicit version check still queries official metadata");
                        Check(grid.Rows.Count==5&&Field<BrandButton>(form,"refresh").Enabled,"Unavailable internet leaves visible rows and usable controls");
                    }
                    form.Close();
                }
                // Unapproved unsigned packages cannot silently perform an online digest lookup at startup.
                string file=Path.Combine(temp,"unsigned-fixture.bin");File.WriteAllText(file,"inert startup test");
                var package=new SoftwarePackage{Id="7zip",Version="99.0",File=file,Sha256=SoftwareCatalog.Hash(file)};
                int previous=requests;bool rejected=false;try{SoftwareTrust.Verify(package,false);}catch(InvalidDataException){rejected=true;}
                Check(rejected&&requests==previous,"Unknown unsigned local package rejected without a network lookup");
                Console.WriteLine("PASS: "+count+" startup/network policy assertions. No installer or system configuration executed.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            finally{Directory.Delete(temp,true);}
        }
    }
}
