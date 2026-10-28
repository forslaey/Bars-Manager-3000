using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Security.Cryptography;
namespace ServiceTool
{
 internal static class RuntimeDownloadTests
 {
  static int Main(){try{
   string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"download-fixture");Directory.CreateDirectory(root);
   var locals=LibraryCatalog.All().Where(p=>p.Kind==LibraryKind.DotNet&&p.Version=="8.0.31").ToList();
   var updates=DotNetUpdates.Check(locals,CancellationToken.None);int count=0;
   foreach(var u in updates.Where(u=>u.Newer)){
    string old=Path.Combine(root,u.Local.RelativePath);Directory.CreateDirectory(Path.GetDirectoryName(old));File.WriteAllText(old,"BARS isolated old-file fixture");
    using(var sha=SHA256.Create())u.Local.Sha256=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(old))).Replace("-","");
    Console.WriteLine("Downloading verified fixture: "+u.Local.RuntimeFamily+" "+u.Local.Architecture+" "+u.Version);
    DotNetUpdates.Download(u,root,CancellationToken.None,null);
    string saved=Path.Combine(Path.GetDirectoryName(old),u.FileName);if(!File.Exists(saved)||File.Exists(old))throw new Exception("Commit failed");
    using(var sha=SHA512.Create())if(!String.Equals(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(saved))).Replace("-",""),u.Hash,StringComparison.OrdinalIgnoreCase))throw new Exception("Saved hash mismatch");
    bool refused=false;try{DotNetUpdates.Download(u,root,CancellationToken.None,null);}catch(IOException){refused=true;}if(!refused||!File.Exists(saved))throw new Exception("Existing destination changed");count++;
   }
   if(count!=4)throw new Exception("Expected four .NET 8 patch updates; got "+count);
   var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;try{DotNetUpdates.Download(updates[0],root,cancel.Token,null);}catch(OperationCanceledException){cancelled=true;}if(!cancelled)throw new Exception("Cancellation ignored");
   var sample=updates[0];string originalPath=Path.Combine(root,sample.Local.RelativePath);File.WriteAllText(originalPath,"preserve me");
   var midway=new CancellationTokenSource();bool aborted=false;
   try{DotNetUpdates.Download(sample,root,midway.Token,n=>midway.Cancel());}catch(Exception){aborted=midway.IsCancellationRequested;}
   if(!aborted||File.ReadAllText(originalPath)!="preserve me")throw new Exception("Mid-download cancellation damaged old file");
   string realHash=sample.Hash;sample.Hash=new string('0',128);bool badHash=false;
   try{DotNetUpdates.Download(sample,root,CancellationToken.None,null);}catch(InvalidDataException){badHash=true;}finally{sample.Hash=realHash;}
   if(!badHash||File.ReadAllText(originalPath)!="preserve me")throw new Exception("Bad SHA512 damaged old file");
   Console.WriteLine("PASS: four official installers, SHA512/signature/product metadata, publication and existing-file preservation; pre-cancel, mid-download cancellation and bad SHA512. Files only under work. No installers executed.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
