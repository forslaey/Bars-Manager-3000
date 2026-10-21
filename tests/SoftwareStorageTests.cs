using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace ServiceTool
{
    internal static class SoftwareStorageTests
    {
        private static int count;
        private static string root,fixture;
        private static readonly List<string> messages=new List<string>();
        private static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
        private static void Reject(Action action,string message){bool failed=false;try{action();}catch(InvalidDataException){failed=true;}catch(IOException){failed=true;}catch(OperationCanceledException){failed=true;}Check(failed,message);}
        private static string Area(string name){string value=Path.Combine(root,name);Directory.CreateDirectory(value);return value;}
        private static SoftwarePackage Package(string area){string file=Path.Combine(area,Path.GetFileName(fixture));File.Copy(fixture,file);return SoftwareCatalog.Describe("notepad",file,"Temporary signed fixture");}
        private static string Old(string folder){Directory.CreateDirectory(folder);string old=Path.Combine(folder,"npp.1.0.Installer.x64.exe");File.Copy(fixture,old);return old;}
        private sealed class NoHttp:IWebRequestCreate{public WebRequest Create(Uri uri){throw new WebException("Test download unavailable");}}
        public static int Main(string[] args)
        {
            root=Path.Combine(Path.GetTempPath(),"bars-storage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                if(args.Length==1&&args[0]=="--live")
                {
                    string library=Area("USB path Библиотеки");var remote=SoftwareSources.Latest("anydesk");
                    var saved=SoftwarePackageStorage.Download(remote,library,Console.WriteLine,()=>false);
                    Check(saved.File==Path.Combine(library,"_soft","AnyDesk.exe")&&File.Exists(saved.File),"Live download saved directly into newly created _soft");
                    SoftwareTrust.Verify(saved,false);Check(Directory.GetDirectories(Path.Combine(library,"_soft")).Length==0,"Successful live download leaves no staging directories");
                    Console.WriteLine("PASS live save-to-_soft: "+saved.Version+" SHA256="+saved.Sha256+"; no installer executed.");return 0;
                }
                fixture=Directory.GetFiles(@"C:\Библиотеки\_soft","npp.*.Installer.x64.exe").First();
                string libraries=Area("Библиотеки");string soft=Path.Combine(libraries,"_soft");
                Check(SoftwarePackageStorage.Folder(libraries)==soft,"Library selection appends _soft even when missing");
                Check(SoftwarePackageStorage.Folder(soft+"\\")==soft,"Existing _soft path not nested again");
                Check(!Directory.Exists(soft),"Resolving a folder never creates directories during check");
                string drive=Area("removable root");Directory.CreateDirectory(Path.Combine(drive,"Библиотеки"));
                Check(SoftwarePackageStorage.Folder(drive)==Path.Combine(drive,"Библиотеки","_soft"),"Selecting drive/container finds adjacent Библиотеки");
                Check(SoftwarePackageStorage.Folder(@"Z:\Библиотеки")==@"Z:\Библиотеки\_soft","Removable drive letter independent of manager executable");
                Check(SoftwarePackageStorage.Folder(@"Z:\")==@"Z:\_soft","Drive root remains absolute");
                Reject(()=>SoftwarePackageStorage.Folder(@"C:relative"),"Drive-relative destination rejected");
                Reject(()=>SoftwarePackageStorage.Folder("relative"),"Relative destination rejected");

                var package=Package(Area("fresh"));string old=Old(soft);
                string unrelated=Path.Combine(soft,"keep.txt");File.WriteAllText(unrelated,"Keep user file");
                string corrupt=Path.Combine(soft,"npp.invalid.Installer.x64.exe");File.WriteAllText(corrupt,"Unrecognized file must remain");
                var savedPackage=SoftwarePackageStorage.Publish(package,libraries,messages.Add,()=>false);
                Check(savedPackage.File==Path.Combine(soft,Path.GetFileName(fixture))&&File.Exists(savedPackage.File),"Verified new package published flat into _soft");
                Check(!File.Exists(old),"Older recognized installer permanently removed after publication");
                Check(File.Exists(unrelated)&&File.Exists(corrupt),"Unrelated and unrecognized files preserved");
                Check(SoftwareCatalog.Hash(savedPackage.File)==savedPackage.Sha256,"Published installer hash unchanged");

                string rollback=Area("rollback");string rollbackSoft=Path.Combine(rollback,"_soft");Directory.CreateDirectory(rollbackSoft);
                string original=Path.Combine(rollbackSoft,Path.GetFileName(fixture));File.Copy(fixture,original);
                var lockedPackage=Package(Area("locked-source"));string previousHash=SoftwareCatalog.Hash(original);
                using(var held=new FileStream(lockedPackage.File,FileMode.Open,FileAccess.Read,FileShare.Read))
                    Reject(()=>SoftwarePackageStorage.Publish(lockedPackage,rollback,messages.Add,()=>false),"Failed new-file rename triggers rollback");
                Check(File.Exists(original)&&SoftwareCatalog.Hash(original)==previousHash,"Same-name previous installer restored on failure");
                Check(File.Exists(lockedPackage.File),"Failed publication retains staged file for caller cleanup");
                var replacement=Package(Area("same-name"));string replacementStage=Path.GetDirectoryName(replacement.File);SoftwarePackageStorage.Publish(replacement,rollback,messages.Add,()=>false);
                Check(File.Exists(original)&&!Directory.GetFiles(replacementStage,"*.previous").Any(),"Same-name replacement committed");

                string failed=Area("bad-hash");string failedOld=Old(Path.Combine(failed,"_soft"));var bad=Package(Area("bad-source"));bad.Sha256=new string('0',64);
                Reject(()=>SoftwarePackageStorage.Publish(bad,failed,messages.Add,()=>false),"Hash failure rejected before replacing old installer");Check(File.Exists(failedOld),"Hash failure preserves previous installer");
                var cancelled=Package(Area("cancelled-source"));int probes=0;
                Reject(()=>SoftwarePackageStorage.Publish(cancelled,failed,messages.Add,()=>++probes>=2),"Cancellation at commit boundary rejected");Check(File.Exists(failedOld)&&File.Exists(cancelled.File),"Cancellation preserves old and staged files");

                string newer=Area("newer");string newerSoft=Path.Combine(newer,"_soft");Directory.CreateDirectory(newerSoft);
                string higher=Path.Combine(newerSoft,"npp.99.0.Installer.x64.exe");File.Copy(fixture,higher);var lower=Package(Area("lower-source"));
                Reject(()=>SoftwarePackageStorage.Publish(lower,newer,messages.Add,()=>false),"Newer local installer never replaced with older download");Check(File.Exists(higher),"Newer installer retained");

                string busy=Area("locked-old");string busyOld=Old(Path.Combine(busy,"_soft"));var fresh=Package(Area("busy-source"));
                using(var held=new FileStream(busyOld,FileMode.Open,FileAccess.Read,FileShare.Read))SoftwarePackageStorage.Publish(fresh,busy,messages.Add,()=>false);
                Check(File.Exists(fresh.File)&&File.Exists(busyOld),"Busy old file does not destroy verified new installer");
                Check(messages.Any(m=>m.Contains("прежний удалить не удалось")),"Incomplete old-file cleanup reported");

                Check(WebRequest.RegisterPrefix("https://",new NoHttp()),"Failure interceptor registered");
                var unavailable=new SoftwarePackage{Id="anydesk",Version="9.8.2",Architecture="x86",File="AnyDesk.exe",Url="https://download.anydesk.com/AnyDesk.exe",Sha256=""};
                bool downloadFailed=false;try{SoftwarePackageStorage.Download(unavailable,failed,messages.Add,()=>false);}catch(WebException){downloadFailed=true;}
                Check(downloadFailed&&File.Exists(failedOld),"Network failure preserves prior installers");
                Check(Directory.GetDirectories(Path.Combine(failed,"_soft"),".bars-download-*").Length==0,"Network failure removes own empty staging directories");
                Console.WriteLine("PASS: "+count+" portable _soft publication/rollback/cleanup assertions. Only temporary copies touched; no installer executed.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            finally
            {
                if(root.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Directory.Exists(root))Directory.Delete(root,true);
            }
        }
    }
}
