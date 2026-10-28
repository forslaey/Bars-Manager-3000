using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Threading;
using System.Web.Script.Serialization;
namespace ServiceTool
{
 internal sealed class DotNetUpdate
 {
  internal LibraryPackage Local;
  internal string Version,Url,Hash,FileName,SupportInfo;
  internal bool Newer {get{return LibraryVersions.Compare(Version,Local.Version)>0;}}
 }
 internal static class DotNetUpdates
 {
  internal static bool Supported(LibraryPackage p){Version v;return p.Kind==LibraryKind.DotNet&&(!p.Preview||p.Version.StartsWith("11.0.0-rc.",StringComparison.OrdinalIgnoreCase))&&Version.TryParse(p.Version.Split('-')[0],out v)&&(p.Architecture=="x86"||p.Architecture=="x64")&&(p.RuntimeFamily=="Microsoft.NETCore.App"||p.RuntimeFamily=="Microsoft.AspNetCore.App");}
  static Dictionary<string,object> Obj(object x){return (Dictionary<string,object>)x;}
  internal static List<DotNetUpdate> Parse(string json,IEnumerable<LibraryPackage> packages)
  {
   var serializer=new JavaScriptSerializer {MaxJsonLength=32*1024*1024};var root=Obj(serializer.DeserializeObject(json));var output=new List<DotNetUpdate>();
   foreach(var p in packages.Where(Supported)){
    Version local=new Version(p.Version.Split('-')[0]);string channel=local.Major+"."+local.Minor;if(Convert.ToString(root["channel-version"])!=channel)continue;
    foreach(var release in ((object[])root["releases"])){
     var r=Obj(release);object component;if(!r.TryGetValue(p.RuntimeFamily=="Microsoft.NETCore.App"?"runtime":"aspnetcore-runtime",out component))continue;
     var c=Obj(component);string version=Convert.ToString(c["version"]);Version number;if(!Version.TryParse(version.Split('-')[0],out number)||(p.Preview?!version.Contains("-rc."):version.Contains("-"))||number.Major!=local.Major||number.Minor!=local.Minor)continue;
     foreach(var item in (object[])c["files"]){var f=Obj(item);string rid="win-"+p.Architecture;string name=Convert.ToString(f["name"]);
      if(Convert.ToString(f["rid"])!=rid||!name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||name.IndexOf("hosting",StringComparison.OrdinalIgnoreCase)>=0)continue;
      string prefix=p.RuntimeFamily=="Microsoft.NETCore.App"?"dotnet-runtime-":"aspnetcore-runtime-";if(!name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))continue;
      string url=Convert.ToString(f["url"]),hash=Convert.ToString(f["hash"]);ValidateUrl(url);
      if(hash.Length!=128||!hash.All(Uri.IsHexDigit))throw new InvalidDataException("Некорректная контрольная сумма Microsoft");
      var candidate=new DotNetUpdate {Local=p,Version=version,Url=url,Hash=hash,FileName=prefix+version+"-"+rid+".exe",SupportInfo=Support(root)};
      var old=output.FirstOrDefault(x=>x.Local.Id==p.Id);if(old==null)output.Add(candidate);else if(LibraryVersions.Compare(version,old.Version)>0){output.Remove(old);output.Add(candidate);}
     }
    }
   }return output;
  }
  static string Support(Dictionary<string,object> root){Func<string,string> get=k=>root.ContainsKey(k)?Convert.ToString(root[k]):"Нет сведений";string type=get("release-type");string phase=get("support-phase");return "Тип выпуска: "+(type=="lts"?"Долгосрочная поддержка (LTS)":type=="sts"?"Стандартная поддержка (STS)":type)+"\r\nПоследняя дата выпуска: "+get("latest-release-date")+"\r\nЭтап поддержки: "+(phase=="active"?"Активный":phase=="maintenance"?"Обслуживание":phase=="eol"?"Поддержка завершена":phase=="go-live"?"Go-live":phase)+"\r\nОкончание поддержки: "+get("eol-date");}
  internal static void ValidateUrl(string value){Uri u;if(!Uri.TryCreate(value,UriKind.Absolute,out u)||u.Scheme!="https"||u.Port!=443||u.UserInfo.Length!=0||!(u.Host=="builds.dotnet.microsoft.com"||u.Host=="download.visualstudio.microsoft.com"||u.Host=="dotnetcli.azureedge.net"))throw new InvalidDataException("Неожиданный адрес загрузки Microsoft");}
  static byte[] Receive(string url,int limit,CancellationToken cancel,Action<long> progress)
  {
   ValidateUrl(url);ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
   var request=(HttpWebRequest)WebRequest.Create(url);request.Timeout=20000;request.ReadWriteTimeout=20000;
   using(cancel.Register(()=>request.Abort()))using(var response=(HttpWebResponse)request.GetResponse()){
    ValidateUrl(response.ResponseUri.AbsoluteUri);using(var stream=response.GetResponseStream())using(var output=new MemoryStream()){
     byte[] buffer=new byte[65536];int n;while((n=stream.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();if(output.Length+n>limit)throw new InvalidDataException("Превышен допустимый размер загрузки");output.Write(buffer,0,n);if(progress!=null)progress(output.Length);}return output.ToArray();
    }
   }
  }
  internal static List<DotNetUpdate> Check(IEnumerable<LibraryPackage> packages,CancellationToken cancel)
  {
   var all=packages.Where(Supported).ToList();var result=new List<DotNetUpdate>();
   foreach(var channel in all.GroupBy(p=>{var v=new Version(p.Version.Split('-')[0]);return v.Major+"."+v.Minor;})){
    cancel.ThrowIfCancellationRequested();if(channel.All(p=>p.Version=="6.0.36"||p.Version=="7.0.20")){foreach(var p in channel)result.Add(new DotNetUpdate {Local=p,Version=p.Version});continue;}string json=Encoding.UTF8.GetString(Receive("https://builds.dotnet.microsoft.com/dotnet/release-metadata/"+channel.Key+"/releases.json",32*1024*1024,cancel,null));result.AddRange(Parse(json,channel));
   }return result;
  }
  internal static bool Download(DotNetUpdate update,string root,CancellationToken cancel,Action<long> progress)
  {
   if(!update.Newer)throw new InvalidOperationException("Обновление не требуется");
   cancel.ThrowIfCancellationRequested();
   root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   string old=Path.GetFullPath(Path.Combine(root,update.Local.RelativePath));if(!old.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Путь вне библиотеки");
   string folder=Path.GetDirectoryName(old);Directory.CreateDirectory(folder);
   for(string ancestor=folder;!String.IsNullOrEmpty(ancestor);ancestor=Path.GetDirectoryName(ancestor))if((File.GetAttributes(ancestor)&FileAttributes.ReparsePoint)!=0)throw new IOException("Папка-ссылка не поддерживается");
   string destination=Path.Combine(folder,update.FileName),temporary=Path.Combine(folder,".bars-"+Guid.NewGuid().ToString("N")+".exe");
   bool retained=false;
   try{
    byte[] bytes=Receive(update.Url,256*1024*1024,cancel,progress);
    using(var sha=SHA512.Create())if(!String.Equals(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-",""),update.Hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Контрольная сумма SHA-512 не совпадает");
    File.WriteAllBytes(temporary,bytes);using(var certificate=new System.Security.Cryptography.X509Certificates.X509Certificate2(System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(temporary))){
     string publisher=certificate.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName,false);
     if((publisher!="Microsoft Corporation"&&publisher!=".NET")||!System.Text.RegularExpressions.Regex.IsMatch(certificate.Subject,@"(?:^|,\s*)O=Microsoft Corporation(?:,|$)"))throw new InvalidDataException("Неожиданный подписант .NET");
     SoftwareTrust.Signature(temporary,publisher);
    }
    var described=LibraryDiscovery.Describe(temporary,"Microsoft .NET\\"+update.FileName);
    if(described.Kind!=LibraryKind.DotNet||described.RuntimeFamily!=update.Local.RuntimeFamily||described.Architecture!=update.Local.Architecture||described.Version!=update.Version)throw new InvalidDataException("Метаданные установщика не совпадают с выбранным компонентом");
    cancel.ThrowIfCancellationRequested();
    if(File.Exists(destination))throw new IOException("Файл новой версии уже существует: "+destination);
    File.Move(temporary,destination);
    // Publication is committed first; cancellation never deletes the old installer.
    try{if(File.Exists(old)&&!String.Equals(old,destination,StringComparison.OrdinalIgnoreCase)){using(var original=new FileStream(old,FileMode.Open,FileAccess.Read,FileShare.Read)){if(!String.Equals(LibraryCancellation.Hash(original,null),update.Local.Sha256,StringComparison.OrdinalIgnoreCase))retained=true;}if(!retained)File.Delete(old);}}catch(IOException){retained=true;}catch(UnauthorizedAccessException){retained=true;}
    return !retained;
   }finally{if(File.Exists(temporary))try{File.Delete(temporary);}catch(IOException){}}
  }
 }
}
