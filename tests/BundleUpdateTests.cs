using System;
using System.IO;
using System.Linq;
using System.Threading;
namespace ServiceTool
{
 internal static class BundleUpdateTests
 {
  static int count;static string root,fixtures;
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
  static void Reject(Action action,string name){bool fail=false;try{action();}catch(IOException){fail=true;}Check(fail,name);}
  static string Copy(string fixture,string relative){string path=YandexBundleSource.LocalPath(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.Copy(Path.Combine(fixtures,fixture),path,true);return path;}
  static BundleRemoteFile Remote(string fixture,string relative){string source=Path.Combine(fixtures,fixture);return new BundleRemoteFile {Path=relative,Size=new FileInfo(source).Length,Sha256=YandexBundleSource.Hash(source,CancellationToken.None)};}
  static void Stage(string fixture,BundleRemoteFile file){string path=BundleTransfer.PartPath(root,file);Directory.CreateDirectory(Path.GetDirectoryName(path));File.Copy(Path.Combine(fixtures,fixture),path,true);}
  [STAThread] static int Main(){fixtures=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"package-fixtures");try{
   foreach(string ext in new[]{".exe",".msi"}){
    root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"updates-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
    string target=Copy("old"+ext,"/current/package"+ext);string hash=YandexBundleSource.Hash(target,CancellationToken.None);
    var update=Remote("new"+ext,"/current/package"+ext);Stage("new"+ext,update);
    new BundleTransfer((f,t)=>{throw new Exception("Network not expected");}).Download(update,root,hash,x=>{},CancellationToken.None);
    Check(BundlePublication.Matches(root,update,CancellationToken.None),"Verified update publishes "+ext);
    new BundleReceipts(root).Record(update);
    var downgrade=Remote("old"+ext,"/elsewhere/renamed"+ext);Stage("old"+ext,downgrade);
    Reject(()=>new BundleTransfer((f,t)=>{throw new Exception("Network not expected");}).Download(downgrade,root,null,x=>{},CancellationToken.None),"Cross-directory downgrade denied "+ext);
    Check(!File.Exists(YandexBundleSource.LocalPath(root,downgrade.Path)),"No downgrade destination "+ext);
    string old=Copy("old"+ext,"/archive/old"+ext);var oldFile=Remote("old"+ext,"/archive/old"+ext);new BundleReceipts(root).Record(oldFile);
    var candidates=BundleMaintenance.OldFiles(root,new[]{update},CancellationToken.None);Check(candidates.Any(c=>c.Path==oldFile.Path),"Cross-directory old managed file recognized "+ext);
    File.AppendAllText(old,"edited");Check(!BundleMaintenance.OldFiles(root,new[]{update},CancellationToken.None).Any(c=>c.Path==oldFile.Path),"Edited old file protected "+ext);
    foreach(string other in new[]{"otherarch",ext==".exe"?"otherbranch":"otherproduct"}){
     string stage=Path.Combine(fixtures,other+ext);Reject(()=>BundlePackageIdentity.ValidateUpgrade(target,stage,update.Path),"Identity mismatch blocked "+other+ext);
    }
    using(var held=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read)){
     var replacement=Remote("new"+ext,update.Path);Stage("new"+ext,replacement);Reject(()=>new BundleTransfer((f,t)=>null).Download(replacement,root,update.Sha256,x=>{},CancellationToken.None),"Locked real-format target retained "+ext);
    }
    Check(BundlePublication.Matches(root,update,CancellationToken.None),"Locked update preserved original "+ext);
   }
   Console.WriteLine("PASS: "+count+" EXE/MSI real-format metadata/update assertions; generated inert packages never executed or installed.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
