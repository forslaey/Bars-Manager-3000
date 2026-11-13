using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
namespace ServiceTool
{
 internal enum BundleCloud {Yandex,Google,Both}
 internal sealed class GoogleBundleSource
 {
  internal const string FolderId="1Zj2h8ImggKBSRuAv3e_X4ZIvLeAbvdL6";
  readonly Dictionary<string,string> ids=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  internal static string ReadText(string url,CancellationToken token)
  {
   for(int attempt=0;;attempt++){
    var request=(HttpWebRequest)WebRequest.Create(url);request.Timeout=request.ReadWriteTimeout=30000;request.UserAgent="BARS-Manager-3000";request.ServicePoint.ConnectionLimit=6;request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
    try{using(token.Register(()=>request.Abort())){token.ThrowIfCancellationRequested();using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){var builder=new StringBuilder();var buffer=new char[8192];int n;while((n=reader.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();if(builder.Length+n>16*1024*1024)throw new InvalidDataException("Слишком большой ответ Google");builder.Append(buffer,0,n);}return builder.ToString();}}}
    catch(WebException ex){token.ThrowIfCancellationRequested();var response=ex.Response as HttpWebResponse;int status=response==null?0:(int)response.StatusCode;if(response!=null)response.Dispose();if(attempt>=3||(status!=0&&status!=429&&status<500))throw;if(token.WaitHandle.WaitOne((1<<attempt)*1000))token.ThrowIfCancellationRequested();}
   }
  }
  internal static object[] Parse(string html)
  {
   var match=Regex.Match(html,@"window\['_DRIVE_ivd'\]\s*=\s*'((?:\\.|[^'])*)'",RegexOptions.Singleline);
   if(!match.Success)throw new InvalidDataException("Google не предоставил публичный каталог. Проверьте доступ к папке.");
   string data=Regex.Replace(match.Groups[1].Value,@"\\x([0-9a-fA-F]{2})",m=>((char)Convert.ToInt32(m.Groups[1].Value,16)).ToString());
   data=data.Replace(@"\'","'").Replace(@"\/","/");
   var all=new JavaScriptSerializer {MaxJsonLength=16*1024*1024,RecursionLimit=256}.DeserializeObject(data) as object[];
   if(all==null||all.Length<2||all[1]!=null||(all[0]!=null&&!(all[0] is object[])))throw new InvalidDataException("Каталог Google неполный или требует дополнительную страницу; загрузка остановлена");
   return all[0] as object[]??new object[0];
  }
  internal List<BundleRemoteFile> Scan(IEnumerable<BundleRemoteFile> manifest,CancellationToken token,Action<string> log,bool independentTools=false,bool toolsOnly=false)
  {
   ids.Clear();var expected=manifest.ToDictionary(f=>f.Path,StringComparer.OrdinalIgnoreCase);var result=new List<BundleRemoteFile>();
   var pending=new Queue<string[]>();pending.Enqueue(new[]{FolderId,""});var seen=new HashSet<string>();var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int count=0;
   while(pending.Count>0){token.ThrowIfCancellationRequested();var batch=new List<string[]>();while(batch.Count<4&&pending.Count>0)batch.Add(pending.Dequeue());
    var pages=batch.Select(directory=>System.Threading.Tasks.Task.Run(()=>ReadText("https://drive.google.com/drive/folders/"+directory[0],token),token)).ToArray();System.Threading.Tasks.Task.WaitAll(pages);
    for(int page=0;page<batch.Count;page++){var directory=batch[page];if(!seen.Add(directory[0]))throw new InvalidDataException("Повтор папки Google");
    foreach(object value in Parse(pages[page].Result)){
     var row=value as object[];if(row==null||row.Length<14)throw new InvalidDataException("Неизвестный формат каталога Google");
     string id=row[0] as string,name=row[2] as string,type=row[3] as string;if(id==null||!Regex.IsMatch(id,@"^[a-zA-Z0-9_-]+$")||String.IsNullOrEmpty(name)||name.Contains("/"))throw new InvalidDataException("Некорректное имя Google");
     string path=directory[1]+"/"+name;YandexBundleSource.LocalPath(Path.Combine(Path.GetTempPath(),"bars-google-check"),path);
     if(!paths.Add(path)||++count>20000)throw new InvalidDataException("Повтор или слишком большой каталог Google");
     if(path.Equals("/.bars-download",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Источник использует служебную папку");
     if(type=="application/vnd.google-apps.folder"){if(toolsOnly&&!(directory[1].Length==0&&name.Equals("_tools",StringComparison.OrdinalIgnoreCase)))continue;if(!path.Equals("/Журнал установки библиотек",StringComparison.OrdinalIgnoreCase))pending.Enqueue(new[]{id,path});continue;}

          long size=Convert.ToInt64(row[13]);BundleRemoteFile reference;
     if(independentTools&&path.StartsWith("/_tools/",StringComparison.OrdinalIgnoreCase))
     {
      if(directory[1].Equals("/_tools",StringComparison.OrdinalIgnoreCase)&&ToolArchives.Supports(path))
      {
       ids[path]=id;
       result.Add(new BundleRemoteFile {Path=path,Size=size,TransferId=Guid.NewGuid().ToString("N")});
      }
      continue;
     }
     if(!expected.TryGetValue(path,out reference)||reference.Size!=size){if(log!=null)log("Google: файл не подтверждён каталогом SHA-256 и пропущен: "+path);continue;}
     ids[path]=id;result.Add(new BundleRemoteFile {Path=path,Size=size,Sha256=reference.Sha256});
    }
   }
   }
   return result;
  }
  static string ReadLimited(StreamReader reader,CancellationToken token){var builder=new StringBuilder();var buffer=new char[4096];int n;while((n=reader.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();if(builder.Length+n>1024*1024)throw new IOException("Слишком большой ответ подтверждения Google");builder.Append(buffer,0,n);}return builder.ToString();}
  internal static Uri ConfirmationLink(string html)
  {
     var form=Regex.Match(html,@"<form[^>]*action=""([^""]+)""[^>]*>(.*?)</form>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
     if(!form.Success)throw new IOException("Google ограничил скачивание или требует вход. Можно выбрать Яндекс.");
     Uri action;if(!Uri.TryCreate(WebUtility.HtmlDecode(form.Groups[1].Value),UriKind.Absolute,out action)||action.Scheme!="https"||action.Host!="drive.usercontent.google.com")throw new IOException("Неподдерживаемое подтверждение Google");
     var fields=new List<string>();foreach(Match input in Regex.Matches(form.Groups[2].Value,@"<input\b[^>]*>",RegexOptions.IgnoreCase)){
      var name=Regex.Match(input.Value,@"\bname=""([^""]+)""");var val=Regex.Match(input.Value,@"\bvalue=""([^""]*)""");if(name.Success&&val.Success)fields.Add(Uri.EscapeDataString(WebUtility.HtmlDecode(name.Groups[1].Value))+"="+Uri.EscapeDataString(WebUtility.HtmlDecode(val.Groups[1].Value)));
     }if(fields.Count==0)throw new IOException("Google не предоставил параметры скачивания");return new Uri(action.GetLeftPart(UriPartial.Path)+"?"+String.Join("&",fields));
  }
  internal Uri DownloadLink(BundleRemoteFile file,CancellationToken token)
  {
   string id;if(!ids.TryGetValue(file.Path,out id))throw new IOException("Файл не найден на Google; требуется проверка каталога");
   string url="https://drive.google.com/uc?export=download&id="+id;
   var request=(HttpWebRequest)WebRequest.Create(url);request.Method="GET";request.AddRange(0,0);request.Timeout=60000;request.ReadWriteTimeout=30000;request.ServicePoint.ConnectionLimit=6;
   using(token.Register(()=>request.Abort())){token.ThrowIfCancellationRequested();using(var response=(HttpWebResponse)request.GetResponse()){
    if(!(response.ContentType??"").StartsWith("text/html",StringComparison.OrdinalIgnoreCase))return response.ResponseUri;
    using(var reader=new StreamReader(response.GetResponseStream())){
     return ConfirmationLink(ReadLimited(reader,token));
    }
   }}
  }
 }
 internal sealed class BundleCloudSource
 {
  readonly GoogleBundleSource google=new GoogleBundleSource();readonly YandexBundleSource yandex=new YandexBundleSource();
  internal List<BundleRemoteFile> YandexFiles,GoogleFiles;
  internal static List<BundleRemoteFile> Baseline()
  {
   using(var stream=typeof(BundleCloudSource).Assembly.GetManifestResourceStream("BARS.BundleManifest.json")){
    if(stream==null)throw new IOException("Не найден проверенный каталог SHA-256");using(var reader=new StreamReader(stream)){
     var raw=new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()) as object[];
     return raw.Select(value=>{var item=(Dictionary<string,object>)value;return new BundleRemoteFile {Path=(string)item["Path"],Size=Convert.ToInt64(item["Size"]),Sha256=(string)item["Sha256"]};}).ToList();
    }
   }
  }
  internal List<BundleRemoteFile> Scan(BundleCloud cloud,CancellationToken token,Action<string> log,IEnumerable<BundleRemoteFile> expected=null)
  {
   if(cloud!=BundleCloud.Google)YandexFiles=yandex.Scan(token);
   if(cloud!=BundleCloud.Yandex)
   {
    var reference=(cloud==BundleCloud.Both?YandexFiles:expected??YandexFiles??Baseline()).ToList();
    GoogleFiles=google.Scan(reference,token,log,expected==null&&cloud==BundleCloud.Google);
    if(cloud==BundleCloud.Google)
    {
     var confirmed=new HashSet<string>(GoogleFiles.Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
     foreach(var file in reference.Where(f=>(expected!=null||f.Group!="Утилиты")&&!confirmed.Contains(f.Path)))
      log("На Google Диске нет подтверждённой копии. Проверь Яндекс Диск: "+file.Path);
    }
   }
   if(cloud==BundleCloud.Both){var g=GoogleFiles.ToDictionary(f=>f.Path,StringComparer.OrdinalIgnoreCase);foreach(var file in YandexFiles)if(!g.ContainsKey(file.Path))log("Нет подтверждённой копии на Google: "+file.Path);log("Каталоги зеркал сверены по путям и размерам. Содержимое Google проверяется по SHA-256 после скачивания.");}
   if(cloud==BundleCloud.Google)log("Для разделов комплекта со сверкой зеркал используется эталонный SHA-256. Архивы утилит Google читаются из его собственного каталога.");
   return cloud==BundleCloud.Google?GoogleFiles:YandexFiles;
  }
  internal List<BundleRemoteFile> ScanGoogleSql(CancellationToken token,Action<string> log)
  {
   List<BundleRemoteFile> reference;
   try{reference=yandex.Scan(token);YandexFiles=reference;}
   catch(Exception ex){token.ThrowIfCancellationRequested();if(!IsNetworkFailure(ex))throw;log("Яндекс недоступен. Google проверяется по встроенному каталогу SHA-256; новые наборы могут отсутствовать.");var baseline=Baseline();Scan(BundleCloud.Google,token,log);return baseline.Where(f=>f.Group!="Утилиты").Concat(GoogleFiles.Where(f=>f.Group=="Утилиты")).ToList();}
   Scan(BundleCloud.Google,token,log);
   return reference.Where(f=>f.Group!="Утилиты").Concat(GoogleFiles.Where(f=>f.Group=="Утилиты")).ToList();
  }
  internal List<BundleRemoteFile> ScanGoogleTools(CancellationToken token,Action<string> log)
  {
   GoogleFiles=google.Scan(new BundleRemoteFile[0],token,null,true,true);
   return GoogleFiles;
  }
  internal static bool IsNetworkFailure(Exception ex){return ex is WebException||ex is IOException;}
  internal bool Ready(BundleCloud cloud){return cloud==BundleCloud.Google?GoogleFiles!=null:YandexFiles!=null;}
  internal bool Contains(BundleCloud cloud,BundleRemoteFile file){var list=cloud==BundleCloud.Google?GoogleFiles:YandexFiles;return list!=null&&list.Any(f=>f.Path==file.Path&&f.Size==file.Size&&f.Sha256==file.Sha256);}
  internal BundleTransfer Transfer(BundleCloud cloud){return cloud==BundleCloud.Google?new BundleTransfer(google.DownloadLink):new BundleTransfer(yandex);}
 }
}
