using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace ServiceTool
{
 internal static class BundleFullDownloadTests
 {
  [STAThread] static int Main()
  {
   try
   {
    var source=new YandexBundleSource();var files=source.Scan(CancellationToken.None);
    string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"full-download");Directory.CreateDirectory(root);
    var errors=new System.Collections.Concurrent.ConcurrentBag<string>();long bytes=0;
    Parallel.ForEach(files,new ParallelOptions {MaxDegreeOfParallelism=3},file=>{
     try{string target=BundleFolders.PackageDestination(root,file.Path);
      if(!File.Exists(target)||YandexBundleSource.Hash(target,CancellationToken.None)!=file.Sha256)new BundleTransfer(source).Download(file,root,null,x=>{},CancellationToken.None);
      new BundleReceipts(root).Record(file);Interlocked.Add(ref bytes,file.Size);
     }catch(Exception ex){errors.Add(file.Path+": "+ex.Message);}
    });
    File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"full-download-result.txt"),new[]{"Files="+files.Count,"VerifiedBytes="+bytes,"Errors="+errors.Count,"No downloaded files executed."}.Concat(errors));
    Console.WriteLine("Full bundle: "+files.Count+" files; verified "+bytes+" bytes; errors "+errors.Count+". No installers executed.");return errors.IsEmpty?0:1;
   }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  }
 }
}
