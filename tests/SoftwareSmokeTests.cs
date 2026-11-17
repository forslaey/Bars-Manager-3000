using System;
using System.IO;
using System.Linq;

namespace ServiceTool
{
    // Explicit read-only inventory + download audit. Vendor EXE/MSI are never executed.
    internal static class SoftwareSmokeTests
    {
        public static int Main(string[] args)
        {
            string temp=Path.Combine(Path.GetTempPath(),"bars-software-source-audit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            try
            {
                string source=Path.Combine(BundleFolders.DefaultRoot,"_soft");
                if(!Directory.Exists(source))throw new DirectoryNotFoundException(source);
                var packages=SoftwareCatalog.Local(source,Console.WriteLine);
                if(packages.Count==0)throw new Exception("No validated local installers at "+source);
                foreach(var package in packages)
                {
                    Console.WriteLine(package.Id+" "+package.Version+" "+package.Architecture+" "+package.Sha256);
                    SoftwareTrust.Verify(package,false);
                    var check=SoftwareDetection.Check(package.Id,package.Id=="anydesk");Console.WriteLine("Installed: "+package.Id+" "+check.State+" "+check.Version);
                }
                if(args.Contains("--local-only"))
                {
                    Console.WriteLine("PASS: "+packages.Count+" validated local installers and read-only inventory from "+source);return 0;
                }
                foreach(string id in new[]{"notepad","7zip","rudesktop"})
                {
                    var remote=SoftwareSources.Latest(id);Console.WriteLine("Official: "+id+" "+remote.Version+" "+remote.Url);
                    var received=SoftwareSources.Download(remote,temp,ignored=>{},()=>false);SoftwareCatalog.Validate(received,false);
                    if(!File.Exists(received.File))throw new Exception("Download missing");Console.WriteLine("Verified download: "+id+" "+received.Version+" "+received.Sha256);
                    var cached=SoftwareCatalog.Local(Path.GetDirectoryName(received.File),Console.WriteLine);if(cached.Count!=1)throw new Exception("Downloaded package not reusable offline");
                }
                Console.WriteLine("PASS: "+packages.Count+" actual local packages, 3 official latest x64 downloads, signatures/digests, cache reuse and read-only inventory. No vendor installer, remote-access configuration or Windows defaults policy executed.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
        }
    }
}
