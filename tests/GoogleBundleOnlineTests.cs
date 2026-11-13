using System;
using System.IO;
using System.Linq;
using System.Threading;
namespace ServiceTool
{
 internal static class GoogleBundleOnlineTests
 {
  [STAThread] static int Main(){try{var source=new BundleCloudSource();var files=source.Scan(BundleCloud.Google,CancellationToken.None,Console.WriteLine);var file=files.Where(f=>f.Group=="Обои"&&f.Size>0).OrderBy(f=>f.Size).First();string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"google-text");Directory.CreateDirectory(root);source.Transfer(BundleCloud.Google).Download(file,root,null,x=>{},CancellationToken.None);if(!BundlePublication.Matches(root,file,CancellationToken.None))throw new Exception("Google sample SHA mismatch");Console.WriteLine("PASS: Google recursive listing "+files.Count+" verified-manifest files; downloaded sample "+file.Size+" bytes SHA-256 verified. No installers downloaded or executed.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
