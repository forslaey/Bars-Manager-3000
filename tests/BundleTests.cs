using System;
using System.IO;
using System.Threading;
using System.Linq;
namespace ServiceTool
{
 internal static class BundleTests
 {
  static int count;
  static void Check(bool ok,string message){count++;if(!ok)throw new Exception(message);}
  [STAThread] static int Main()
  {
   string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bundle-fixtures-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   try
   {
    foreach(string bad in new[]{"/../escape.exe","/a/../../escape.exe","/a:stream","/CON.exe","/a./b","/a//b","relative.exe","/LPT1.txt"})
    {bool denied=false;try{YandexBundleSource.LocalPath(root,bad);}catch(InvalidDataException){denied=true;}Check(denied,"Unsafe path accepted: "+bad);}
    string local=YandexBundleSource.LocalPath(root,"/Microsoft .NET/test.exe");Check(local.StartsWith(root,StringComparison.OrdinalIgnoreCase),"Path containment");Directory.CreateDirectory(Path.GetDirectoryName(local));File.WriteAllText(local,"fixture only");
    string hash=YandexBundleSource.Hash(local,CancellationToken.None);Check(hash.Length==64,"SHA256");
    var file=new BundleRemoteFile {Path="/Microsoft .NET/test.exe",Sha256=hash,Size=new FileInfo(local).Length};var receipts=new BundleReceipts(root);
    Check(!receipts.CanReplace(file,hash),"Unknown file must not be replaceable");receipts.Record(file);Check(receipts.CanReplace(file,hash),"Unchanged managed installer");
    Check(!receipts.CanReplace(file,new string('0',64)),"Edited managed installer protected");
    var config=new BundleRemoteFile {Path="/_tools/test.ini",Sha256=hash};receipts.Record(config);Check(!receipts.CanReplace(config,hash),"Configuration protected");
    var utility=new BundleRemoteFile {Path="/_tools/tool.exe",Sha256=hash};receipts.Record(utility);Check(!receipts.CanReplace(utility,hash),"Utility tree protected");
    Check(BundleTransfer.PartPath(root,file).Contains(hash),"Part keyed by remote content");
    var same=new BundleRemoteFile {Path="/another.exe",Sha256=hash,Size=file.Size};
    Check(BundleTransfer.PartPath(root,file)!=BundleTransfer.PartPath(root,same),"Identical content independent parts");
    string part=BundleTransfer.PartPath(root,same);Directory.CreateDirectory(Path.GetDirectoryName(part));File.Copy(local,part);
    new BundleTransfer(new YandexBundleSource()).Download(same,root,null,bytes=>{},CancellationToken.None);
    Check(File.Exists(YandexBundleSource.LocalPath(root,same.Path)),"Verified part published without network");
    var conflict=new BundleRemoteFile {Path="/conflict.exe",Sha256=hash,Size=file.Size};
    string target=YandexBundleSource.LocalPath(root,conflict.Path);File.WriteAllText(target,"user changed");
    string conflictPart=BundleTransfer.PartPath(root,conflict);Directory.CreateDirectory(Path.GetDirectoryName(conflictPart));File.Copy(local,conflictPart);
    bool blocked=false;try{new BundleTransfer(new YandexBundleSource()).Download(conflict,root,hash,bytes=>{},CancellationToken.None);}catch(IOException){blocked=true;}
    Check(blocked&&File.ReadAllText(target)=="user changed","Destination conflict preserves original");
    Check(File.Exists(conflictPart),"Conflict preserves verified replacement");
    using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool cancelled=false;try{new BundleTransfer(new YandexBundleSource()).Download(file,root,hash,bytes=>{},cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Cancelled before network");}
    using(var form=new BundleDownloadForm()){form.Show();System.Windows.Forms.Application.DoEvents();Check(form.Visible,"Window opens without network");}
    BundleSession.Save(root,new[]{file,same});var saved=BundleSession.Load(root);Check(saved.Contains(file.Path+"|"+hash)&&saved.Count==2,"Queue persists content identity");
    BundleSession.Save(root,new BundleRemoteFile[0]);Check(BundleSession.Load(root).Count==0,"Completed queue cleared atomically");
    string owned=BundleTransfer.PartPath(root,new BundleRemoteFile {Path="/cleanup.bin",Sha256=hash});Directory.CreateDirectory(Path.GetDirectoryName(owned));File.WriteAllText(owned,"partial");
    string unrelated=Path.Combine(root,".bars-download","keep.txt");File.WriteAllText(unrelated,"user");
    Check(BundleMaintenance.Parts(root).Contains(owned),"Owned partial found");BundleMaintenance.CleanParts(root);
    Check(!File.Exists(owned)&&File.Exists(unrelated),"Cleanup preserves unknown service files");
    Check(receipts.CanReplace(file,hash),"Cleanup preserves receipts");
    var msi=new BundleRemoteFile {Path="/fixture.msi",Sha256=hash};receipts.Record(msi);Check(receipts.CanReplace(msi,hash),"Managed MSI eligible for metadata validation");
    bool unknownDenied=false;try{BundlePackageIdentity.ValidateUpgrade(local,local,file.Path);}catch(IOException){unknownDenied=true;}Check(unknownDenied,"Unknown installer identity cannot authorize replacement");
    Console.WriteLine("PASS: "+count+" bundle path/receipt/UI checks. Temporary fixtures only; no network/installers.");return 0;
   }
   catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  }
 }
}
