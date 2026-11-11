using System;
using System.IO;
using System.Linq;
using System.Threading;
namespace ServiceTool
{
 internal static class BundleFailureTests
 {
  static int count;static string root;
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
  static void Reject(Action action,string name){bool denied=false;try{action();}catch(IOException){denied=true;}catch(UnauthorizedAccessException){denied=true;}catch(OperationCanceledException){denied=true;}Check(denied,name);}
  static string FileIn(string name,string text){string p=Path.Combine(root,name);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,text);return p;}
  [STAThread] static int Main(){root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"failures-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{
   foreach(string failure in new[]{"before-publish","before-new-rename"})foreach(bool cancel in new[]{false,true}){
    string old=FileIn(Guid.NewGuid()+"/file.exe","original"),part=FileIn(Guid.NewGuid()+"/file.part","new");string hash=YandexBundleSource.Hash(old,CancellationToken.None);
    using(var cts=new CancellationTokenSource())Reject(()=>BundlePublication.Publish(part,old,hash,cts.Token,stage=>{if(stage==failure){if(cancel)cts.Cancel();else throw new IOException("Injected volume failure");}}),"Failure/cancel at "+failure);
    Check(File.ReadAllText(old)=="original"&&File.ReadAllText(part)=="new","Rollback preserves both copies");
   }
   string destination=FileIn("locked/file.exe","original"),source=FileIn("locked/file.part","new");string expected=YandexBundleSource.Hash(destination,CancellationToken.None);
   using(var held=new FileStream(destination,FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>BundlePublication.Publish(source,destination,expected,CancellationToken.None,null),"Locked destination rejected");
   Check(File.ReadAllText(destination)=="original"&&File.Exists(source),"Lock preserves files");
   Reject(()=>BundlePublication.Publish(source,destination,new string('0',64),CancellationToken.None,null),"Changed destination rejected");
   File.Delete(destination);Reject(()=>BundlePublication.Publish(source,destination,expected,CancellationToken.None,null),"Removed destination rejected");
   BundlePublication.Publish(source,destination,null,CancellationToken.None,null);Check(File.ReadAllText(destination)=="new","Fresh publication succeeds");
   string deniedSource=FileIn("denied/source.part","new"),deniedTarget=FileIn("denied/destination.exe","old");string deniedHash=YandexBundleSource.Hash(deniedTarget,CancellationToken.None);
   Reject(()=>BundlePublication.Publish(deniedSource,deniedTarget,deniedHash,CancellationToken.None,s=>{if(s=="before-new-rename")throw new UnauthorizedAccessException("Injected access denied");}),"Access loss rolls back publication");
   Check(File.ReadAllText(deniedTarget)=="old"&&File.Exists(deniedSource),"Access loss preserves original and stage");
   string fullSource=FileIn("full/source.part","new"),fullTarget=Path.Combine(root,"full","destination.exe");
   Reject(()=>BundlePublication.Publish(fullSource,fullTarget,null,CancellationToken.None,s=>{throw new IOException("Injected disk full");}),"Disk full prevents fresh publication");
   Check(!File.Exists(fullTarget)&&File.Exists(fullSource),"Disk full preserves stage");
   string receiptRoot=Path.Combine(root,"receipts");Directory.CreateDirectory(receiptRoot);
   var file=new BundleRemoteFile {Path="/package.exe",Size=3,Sha256=YandexBundleSource.Hash(destination,CancellationToken.None)};
   File.Copy(destination,BundleFolders.PackageDestination(receiptRoot,file.Path));
   foreach(string phase in new[]{"before-receipt-write","before-receipt-publish"}){
    string warning=BundlePublication.Record(receiptRoot,file,s=>{if(s==phase)throw new IOException("Injected disk full");});
    Check(warning!=null&&BundlePublication.Matches(receiptRoot,file,CancellationToken.None),"Receipt failure leaves published content intact");
    Check(!Directory.GetFiles(receiptRoot,"*.tmp",SearchOption.AllDirectories).Any(),"Receipt temporary file cleaned");
   }
   Check(BundlePublication.Record(receiptRoot,file,null)==null,"Receipt recovery succeeds");Check(new BundleReceipts(receiptRoot).CanReplace(file,file.Sha256),"Recovered ownership");
   string receipt=Directory.GetFiles(receiptRoot,"*.xml",SearchOption.AllDirectories).Single();File.WriteAllText(receipt,"<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///C:/Windows/win.ini'>]><file>&e;</file>");
   Check(!new BundleReceipts(receiptRoot).CanReplace(file,file.Sha256),"Malicious receipt denied");
   File.WriteAllText(receipt,"broken");Check(!new BundleReceipts(receiptRoot).CanReplace(file,file.Sha256),"Corrupt receipt denied");
   string repaired=BundlePublication.Record(receiptRoot,file,null);Check(repaired==null,"Corrupt receipt repair: "+repaired);
   FileIn("nested/other/new.exe","fixture");FileIn("_tools/private/tool.exe","keep");FileIn(".bars-download/hidden.exe","keep");
   var files=BundleMaintenance.PackageFiles(root,CancellationToken.None).ToArray();Check(files.Any(p=>p.EndsWith("new.exe")),"Recursive package discovery");Check(!files.Any(p=>p.Contains("_tools")||p.Contains(".bars-download")),"Reserved trees excluded");
   using(var cts=new CancellationTokenSource()){cts.Cancel();Reject(()=>BundleMaintenance.PackageFiles(root,cts.Token).ToArray(),"Cancelled recursive traversal");}
   Console.WriteLine("PASS: "+count+" deterministic publication/receipt/media failure assertions. Inert temporary files only.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
