using System;
using System.IO;
using System.Linq;
using System.Threading;
namespace ServiceTool
{
 internal static class BundleOnlineTests
 {
  [STAThread] static int Main()
  {
   try
   {
    var source=new YandexBundleSource();var files=source.Scan(CancellationToken.None);
    var file=files.Single(f=>f.Path=="/!ПОЯСНЕНИЕ.txt");
    string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"online-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
    new BundleTransfer(source).Download(file,root,null,x=>{},CancellationToken.None);
    if(YandexBundleSource.Hash(YandexBundleSource.LocalPath(root,file.Path),CancellationToken.None)!=file.Sha256)throw new Exception("Online hash mismatch");
    Console.WriteLine("PASS: public tree "+files.Count+" files; downloaded and SHA256-verified only !ПОЯСНЕНИЕ.txt ("+file.Size+" bytes). No installer downloads or execution.");return 0;
   }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  }
 }
}
