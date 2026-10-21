using System;
using System.IO;
using System.Linq;

namespace ServiceTool
{
    // Explicit read-only inventory + download audit. Vendor EXE/MSI are never executed.
    internal static class SoftwareSmokeTests
    {
        public static int Main()
        {
            string temp=Path.Combine(Path.GetTempPath(),"bars-software-source-audit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            try
            {
                var packages=SoftwareCatalog.Local(@"C:\Библиотеки\_soft",Console.WriteLine);
                if(packages.Count!=4)throw new Exception("Expected four validated local packages, found "+packages.Count);
                foreach(var package in packages)
                {
                    Console.WriteLine(package.Id+" "+package.Version+" "+package.Architecture+" "+package.Sha256);
                    SoftwareTrust.Verify(package,false);
                    var check=SoftwareDetection.Check(package.Id,package.Id=="anydesk");Console.WriteLine("Installed: "+package.Id+" "+check.State+" "+check.Version);
                }
                foreach(string id in new[]{"notepad","7zip","rudesktop"})
                {
                    var remote=SoftwareSources.Latest(id);Console.WriteLine("Official: "+id+" "+remote.Version+" "+remote.Url);
                    var received=SoftwareSources.Download(remote,temp,ignored=>{},()=>false);SoftwareCatalog.Validate(received,false);
                    if(!File.Exists(received.File))throw new Exception("Download missing");Console.WriteLine("Verified download: "+id+" "+received.Version+" "+received.Sha256);
                    var cached=SoftwareCatalog.Local(Path.GetDirectoryName(received.File),Console.WriteLine);if(cached.Count!=1)throw new Exception("Downloaded package not reusable offline");
                }
                Console.WriteLine("PASS: 4 actual local packages, 3 official latest x64 downloads, signatures/digests, cache reuse and read-only inventory. No vendor installer, remote-access configuration or Windows defaults policy executed.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
        }
    }
}
