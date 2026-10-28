using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ServiceTool
{
    internal sealed class BundleRemoteFile
    {
        internal string Path;
        internal long Size;
        internal string Sha256;
        internal string Group { get { return Path.StartsWith("/_soft/",StringComparison.OrdinalIgnoreCase)?"Программы":Path.StartsWith("/_tools/",StringComparison.OrdinalIgnoreCase)?"Утилиты":"Библиотеки"; } }
    }

    // Public metadata only. No OAuth, no execution of downloaded content.
    internal sealed class YandexBundleSource
    {
        internal const string PublicLink="https://disk.yandex.ru/d/ZviB19HcrDuQ2w";
        const string Api="https://cloud-api.yandex.net/v1/disk/public/resources";
        readonly JavaScriptSerializer json=new JavaScriptSerializer { MaxJsonLength=8*1024*1024 };
        internal static string LocalPath(string root,string remote)
        {
            if(String.IsNullOrWhiteSpace(root)||String.IsNullOrEmpty(remote)||!remote.StartsWith("/",StringComparison.Ordinal))throw new InvalidDataException("Некорректный путь комплекта");
            string[] parts=remote.Substring(1).Split('/');
            foreach(string part in parts)
            {
                if(String.IsNullOrEmpty(part)||part=="."||part==".."||part.IndexOfAny(Path.GetInvalidFileNameChars())>=0||part.EndsWith(".")||part.EndsWith(" "))throw new InvalidDataException("Небезопасный путь комплекта");
                string name=part.Split('.')[0].ToUpperInvariant();
                if(name=="CON"||name=="PRN"||name=="AUX"||name=="NUL"||Enumerable.Range(1,9).Any(n=>name=="COM"+n||name=="LPT"+n))throw new InvalidDataException("Зарезервированное имя файла");
            }
            string fullRoot=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string result=Path.GetFullPath(Path.Combine(fullRoot,String.Join(Path.DirectorySeparatorChar.ToString(),parts)));
            if(!result.StartsWith(fullRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Путь за пределами папки комплекта");
            for(string current=Path.GetDirectoryName(result);!String.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
                if(Directory.Exists(current)&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Папка комплекта содержит ссылку: "+current);
            if(File.Exists(result)&&(File.GetAttributes(result)&FileAttributes.ReparsePoint)!=0)throw new IOException("Файл комплекта является ссылкой");
            return result;
        }
        internal static string Hash(string file,CancellationToken token)
        {
            using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))using(var hash=SHA256.Create())
            {
                var buffer=new byte[128*1024];int count;
                while((count=input.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();hash.TransformBlock(buffer,0,count,buffer,0);}
                hash.TransformFinalBlock(buffer,0,0);return BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant();
            }
        }
        Dictionary<string,object> Get(string suffix,string path,int offset,CancellationToken token)
        {
            for(int attempt=0;;attempt++)
            {
                try{return GetOnce(suffix,path,offset,token);}
                catch(WebException ex)
                {
                    token.ThrowIfCancellationRequested();var response=ex.Response as HttpWebResponse;int code=response==null?0:(int)response.StatusCode;
                    bool transient=code==408||code==429||code>=500||ex.Status==WebExceptionStatus.Timeout||ex.Status==WebExceptionStatus.ConnectFailure||ex.Status==WebExceptionStatus.ReceiveFailure||ex.Status==WebExceptionStatus.ConnectionClosed||ex.Status==WebExceptionStatus.NameResolutionFailure;
                    int delay=(1<<attempt)*1000,seconds;
                    if(response!=null&&Int32.TryParse(response.Headers["Retry-After"],out seconds))delay=Math.Max(delay,Math.Min(60,Math.Max(0,seconds))*1000);
                    if(response!=null)response.Dispose();if(!transient||attempt>=3)throw;
                    if(token.WaitHandle.WaitOne(delay))token.ThrowIfCancellationRequested();
                }
            }
        }
        Dictionary<string,object> GetOnce(string suffix,string path,int offset,CancellationToken token)
        {
            var uri=Api+suffix+"?public_key="+Uri.EscapeDataString(PublicLink)+"&path="+Uri.EscapeDataString(path)+"&limit=100&offset="+offset;
            var request=(HttpWebRequest)WebRequest.Create(uri);request.Timeout=30000;request.ReadWriteTimeout=30000;
            request.UserAgent="BARS-Manager-3000";
            using(token.Register(()=>request.Abort()))
            {
                token.ThrowIfCancellationRequested();
                using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))
                {
                    var text=new StringBuilder();var buffer=new char[4096];int count;
                    while((count=reader.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();if(text.Length+count>8*1024*1024)throw new InvalidDataException("Слишком большой ответ Яндекс Диска");text.Append(buffer,0,count);}
                    return json.Deserialize<Dictionary<string,object>>(text.ToString());
                }
            }
        }
        internal List<BundleRemoteFile> Scan(CancellationToken token)
        {
            var queue=new Queue<string>();queue.Enqueue("/");var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var files=new List<BundleRemoteFile>();
            while(queue.Count>0)
            {
                token.ThrowIfCancellationRequested();string directory=queue.Dequeue();if(!visited.Add(directory))throw new InvalidDataException("Повтор каталога в ответе источника");
                for(int offset=0;;offset+=100)
                {
                    var page=Get("",directory,offset,token);var embedded=(Dictionary<string,object>)page["_embedded"];
                    foreach(var value in (System.Collections.IEnumerable)embedded["items"])
                    {
                        var item=(Dictionary<string,object>)value;string path=(string)item["path"];
                        if(path.Equals("/.bars-download",StringComparison.OrdinalIgnoreCase)||path.StartsWith("/.bars-download/",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Источник использует служебную папку загрузчика");
                        // Validate metadata paths independently of the actual destination.
                        LocalPath(Path.Combine(Path.GetTempPath(),"bars-bundle-path-check"),path);
                        if(!path.StartsWith(directory=="/"?"/":directory+"/",StringComparison.Ordinal)||path.Substring(directory=="/"?1:directory.Length+1).Contains("/"))throw new InvalidDataException("Некорректная вложенность каталога");
                        if(!paths.Add(path))throw new InvalidDataException("Повтор пути в комплекте");
                        if((string)item["type"]=="dir")
                        {
                            if(!path.Equals("/Журнал установки библиотек",StringComparison.OrdinalIgnoreCase))queue.Enqueue(path);
                        }
                        else if((string)item["type"]=="file")
                        {
                            string sha=item.ContainsKey("sha256")?item["sha256"] as string:null;long size=Convert.ToInt64(item["size"]);
                            if(size<0||sha==null||sha.Length!=64||sha.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("Нет надёжных метаданных файла: "+path);
                            files.Add(new BundleRemoteFile {Path=path,Size=size,Sha256=sha.ToLowerInvariant()});
                        }
                        else throw new InvalidDataException("Неизвестный тип ресурса");
                        if(paths.Count>20000)throw new InvalidDataException("Превышен предел размера каталога");
                    }
                    int total=Convert.ToInt32(embedded["total"]);if(total<0||total>20000)throw new InvalidDataException("Некорректный размер каталога");if(offset+100>=total)break;
                }
            }
            return files.OrderBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).ToList();
        }
        internal Uri DownloadLink(BundleRemoteFile file,CancellationToken token)
        {
            var item=Get("/download",file.Path,0,token);Uri uri;
            if(!Uri.TryCreate(item["href"] as string,UriKind.Absolute,out uri)||uri.Scheme!=Uri.UriSchemeHttps||!String.IsNullOrEmpty(uri.UserInfo))throw new InvalidDataException("Некорректная ссылка скачивания");
            return uri;
        }
    }
}
