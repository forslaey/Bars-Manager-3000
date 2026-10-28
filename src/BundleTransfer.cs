using System;
using System.IO;
using System.Net;
using System.Threading;

namespace ServiceTool
{
    internal sealed class BundleTransfer
    {
        readonly Func<BundleRemoteFile,CancellationToken,Uri> linkProvider;
        readonly bool testLoopback;
        internal BundleTransfer(YandexBundleSource source){linkProvider=source.DownloadLink;}
        internal BundleTransfer(Func<BundleRemoteFile,CancellationToken,Uri> provider){linkProvider=provider;testLoopback=true;}
        // Parts are owned by this manager and tied to the remote content hash.
        internal static string PartPath(string root,BundleRemoteFile file)
        {
            return YandexBundleSource.LocalPath(root,"/.bars-download/"+file.Sha256+"/"+file.Path.TrimStart('/')+".part");
        }
        internal void Download(BundleRemoteFile file,string root,string originalHash,Action<long> progress,CancellationToken token)
        {
            for(int attempt=0;;attempt++)
            {
                token.ThrowIfCancellationRequested();
                try { DownloadOnce(file,root,originalHash,progress,token);return; }
                catch(WebException ex)
                {
                    token.ThrowIfCancellationRequested();
                    var response=ex.Response as HttpWebResponse;
                    int code=response==null?0:(int)response.StatusCode;
                    bool transient=code==429||code==408||code>=500||ex.Status==WebExceptionStatus.Timeout||ex.Status==WebExceptionStatus.ConnectFailure||ex.Status==WebExceptionStatus.ConnectionClosed||ex.Status==WebExceptionStatus.ReceiveFailure||ex.Status==WebExceptionStatus.NameResolutionFailure;
                    int delay=(int)Math.Pow(2,attempt)*1000;
                    int retryAfter;
                    if(response!=null&&Int32.TryParse(response.Headers["Retry-After"],out retryAfter))delay=Math.Max(delay,Math.Min(60,Math.Max(0,retryAfter))*1000);
                    if(response!=null)response.Dispose();
                    if(!transient||attempt>=3)throw;
                    if(token.WaitHandle.WaitOne(delay))token.ThrowIfCancellationRequested();
                }
            }
        }
        void DownloadOnce(BundleRemoteFile file,string root,string originalHash,Action<long> progress,CancellationToken token)
        {
            string destination=YandexBundleSource.LocalPath(root,file.Path),part=PartPath(root,file);
            Directory.CreateDirectory(Path.GetDirectoryName(part));
            if(file.Size==0&&!File.Exists(part))using(var empty=File.Create(part)){}
            if(File.Exists(part)&&(File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new IOException("Временный файл является ссылкой");
            long offset=File.Exists(part)?new FileInfo(part).Length:0;
            if(offset>file.Size){throw new IOException("Недокачанный файл больше исходного; требуется очистка временного файла");}
            if(offset==file.Size&&File.Exists(part)&&!String.Equals(YandexBundleSource.Hash(part,token),file.Sha256,StringComparison.OrdinalIgnoreCase))
            {
                // Quarantine only this manager's failed part; preserve it for diagnosis.
                File.Move(part,part+".invalid-"+Guid.NewGuid().ToString("N"));offset=0;
            }
            progress(offset);
            if(offset<file.Size)
            {
                Uri link=linkProvider(file,token);
                if(link.Scheme!=Uri.UriSchemeHttps&&!(testLoopback&&link.IsLoopback))throw new IOException("Требуется HTTPS");
                var request=(HttpWebRequest)WebRequest.Create(link);request.Timeout=30000;request.ReadWriteTimeout=30000;request.UserAgent="BARS-Manager-3000";
                if(offset>0)request.AddRange(offset);
                using(token.Register(()=>request.Abort()))
                {
                    token.ThrowIfCancellationRequested();
                    using(var response=(HttpWebResponse)request.GetResponse())
                    {
                        if(response.ResponseUri.Scheme!=Uri.UriSchemeHttps&&!(testLoopback&&response.ResponseUri.IsLoopback))throw new IOException("Небезопасное перенаправление загрузки");
                        if(offset>0&&response.StatusCode==HttpStatusCode.PartialContent)
                        {
                            string range=response.Headers["Content-Range"];
                            if(range==null||!range.StartsWith("bytes "+offset+"-",StringComparison.Ordinal)||!range.EndsWith("/"+file.Size,StringComparison.Ordinal))throw new IOException("Некорректный ответ докачки");
                        }
                        else if(response.StatusCode==HttpStatusCode.OK){offset=0;progress(0);}
                        else throw new IOException("Неожиданный ответ сервера скачивания");
                        if(response.ContentLength>=0&&response.ContentLength!=file.Size-offset)throw new IOException("Размер ответа не совпадает с каталогом");
                        using(var input=response.GetResponseStream())using(var output=new FileStream(part,offset==0?FileMode.Create:FileMode.Append,FileAccess.Write,FileShare.None))
                        {
                            var buffer=new byte[128*1024];int count;
                            while((count=input.Read(buffer,0,buffer.Length))>0)
                            {
                                token.ThrowIfCancellationRequested();if(offset+count>file.Size)throw new IOException("Файл больше заявленного размера");
                                output.Write(buffer,0,count);offset+=count;progress(offset);
                            }
                            output.Flush(true);
                        }
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            if(new FileInfo(part).Length!=file.Size)throw new IOException("Передача оборвалась до получения полного файла: "+file.Path);
            if(!String.Equals(YandexBundleSource.Hash(part,token),file.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Размер или SHA-256 не совпадает: "+file.Path+". Частичный файл сохранён; требуется повторная проверка.");
            // Recheck the destination immediately before publishing; never replace
            // a locally edited file or an unexpected newly created file.
            destination=YandexBundleSource.LocalPath(root,file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if(originalHash!=null)
            {
                // Replacing an existing installer requires comparable EXE versions.
                // Unknown/MSI versions stay protected rather than risking downgrade.
                if(!String.Equals(Path.GetExtension(destination),".exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Версию существующего пакета нельзя надёжно сравнить; сохранены обе копии");
                Version before,after;
                string oldVersion=System.Diagnostics.FileVersionInfo.GetVersionInfo(destination).FileVersion;
                string newVersion=System.Diagnostics.FileVersionInfo.GetVersionInfo(part).FileVersion;
                if(!Version.TryParse(oldVersion,out before)||!Version.TryParse(newVersion,out after)||after<before)throw new IOException("Новая версия не подтверждена или старее локальной; локальный файл сохранён");
            }
            if(File.Exists(destination))
            {
                if(originalHash==null||!String.Equals(YandexBundleSource.Hash(destination,token),originalHash,StringComparison.OrdinalIgnoreCase))throw new IOException("Локальный файл изменился; новая копия не опубликована");
                File.Replace(part,destination,null);
            }
            else
            {
                if(originalHash!=null)throw new IOException("Локальный файл исчез во время загрузки; требуется сверка");
                File.Move(part,destination);
            }
            progress(file.Size);
        }
    }
}
