using System;
using System.IO;
using System.Net;
using System.Threading;

namespace ServiceTool
{
    internal sealed class BundleTransientException : IOException
    { internal BundleTransientException(string message,Exception inner):base(message,inner){} }
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
        { Download(file,root,originalHash,progress,token,null,null); }
        internal void Download(BundleRemoteFile file,string root,string originalHash,Action<long> progress,CancellationToken token,Action<long> received,Action<string> stage)
        {
            for(int attempt=0;;attempt++)
            {
                token.ThrowIfCancellationRequested();
                try { DownloadOnce(file,root,originalHash,progress,token,received,stage);return; }
                catch(BundleTransientException)
                {
                    if(attempt>=3)throw;
                    if(stage!=null)stage("Ожидание повтора");
                    WaitRetry((1<<attempt)*1000,token,stage,"Соединение прервано");
                }
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
                    if(stage!=null)stage("Ожидание повтора");
                    WaitRetry(delay,token,stage,code==0?"Источник не отвечает":"HTTP "+code);
                }
            }
        }
        static void WaitRetry(int delay,CancellationToken token,Action<string> stage,string reason)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<delay){
                int remaining=Math.Max(1,delay-(int)watch.ElapsedMilliseconds);
                if(stage!=null)stage("Повтор через "+((remaining+999)/1000)+" с · "+reason);
                if(token.WaitHandle.WaitOne(Math.Min(1000,remaining)))token.ThrowIfCancellationRequested();
            }
        }
        void DownloadOnce(BundleRemoteFile file,string root,string originalHash,Action<long> progress,CancellationToken token,Action<long> received,Action<string> stage)
        {
            string destination=YandexBundleSource.LocalPath(root,file.Path),part=PartPath(root,file);
            Directory.CreateDirectory(Path.GetDirectoryName(part));
            if(file.Size==0&&!File.Exists(part))using(var empty=File.Create(part)){}
            if(File.Exists(part)&&(File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new IOException("Временный файл является ссылкой");
            long offset=File.Exists(part)?new FileInfo(part).Length:0;
            if(offset>file.Size){File.Move(part,part+".invalid-"+Guid.NewGuid().ToString("N"));offset=0;}
            if(offset==file.Size&&File.Exists(part)&&!String.Equals(YandexBundleSource.Hash(part,token),file.Sha256,StringComparison.OrdinalIgnoreCase))
            {
                // Quarantine only this manager's failed part; preserve it for diagnosis.
                File.Move(part,part+".invalid-"+Guid.NewGuid().ToString("N"));offset=0;
            }
            progress(offset==file.Size&&file.Size>0?file.Size-1:offset);
            if(offset<file.Size)
            {
                if(stage!=null)stage("Скачивание");
                Uri link=linkProvider(file,token);
                if(link.Scheme!=Uri.UriSchemeHttps&&!(testLoopback&&link.IsLoopback))throw new IOException("Требуется HTTPS");
                var request=(HttpWebRequest)WebRequest.Create(link);request.Timeout=90000;request.ReadWriteTimeout=30000;request.ServicePoint.ConnectionLimit=6;request.UserAgent="BARS-Manager-3000";
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
                            int code=(int)response.StatusCode;System.Text.RegularExpressions.Match parsed=null;
                            if(range!=null)parsed=System.Text.RegularExpressions.Regex.Match(range,@"^bytes (\d+)-(\d+)/(\d+)$");
                            long first,last,length;
                            if(parsed==null||!parsed.Success||!Int64.TryParse(parsed.Groups[1].Value,out first)||!Int64.TryParse(parsed.Groups[2].Value,out last)||!Int64.TryParse(parsed.Groups[3].Value,out length)||first!=offset||last!=file.Size-1||length!=file.Size)throw new IOException("Некорректный ответ докачки");
                        }
                        else if(response.StatusCode==HttpStatusCode.OK){offset=0;progress(0);}
                        else throw new IOException("Неожиданный ответ сервера скачивания");
                        if(response.ContentLength>=0&&response.ContentLength!=file.Size-offset)throw new IOException("Размер ответа не совпадает с каталогом");
                        using(var input=response.GetResponseStream())using(var output=new FileStream(part,offset==0?FileMode.Create:FileMode.Append,FileAccess.Write,FileShare.None))
                        {
                            var buffer=new byte[128*1024];int count;
                            while(true)
                            {
                                try{count=input.Read(buffer,0,buffer.Length);}catch(IOException ex){throw new BundleTransientException("Сетевое чтение прервано",ex);}
                                if(count==0)break;
                                token.ThrowIfCancellationRequested();if(offset+count>file.Size)throw new IOException("Файл больше заявленного размера");
                                output.Write(buffer,0,count);offset+=count;if(received!=null)received(count);progress(offset==file.Size&&file.Size>0?file.Size-1:offset);
                            }
                            output.Flush(true);
                        }
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            if(new FileInfo(part).Length!=file.Size)throw new BundleTransientException("Передача оборвалась до получения полного файла: "+file.Path,null);
            if(stage!=null)stage("Проверка SHA-256");
            if(!String.Equals(YandexBundleSource.Hash(part,token),file.Sha256,StringComparison.OrdinalIgnoreCase))
            {
                File.Move(part,part+".invalid-"+Guid.NewGuid().ToString("N"));
                throw new BundleTransientException("SHA-256 не совпадает; повреждённая копия сохранена в карантине",null);
            }
            // Recheck the destination immediately before publishing; never replace
            // a locally edited file or an unexpected newly created file.
            destination=YandexBundleSource.LocalPath(root,file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            lock(BundlePublication.Gate)
            {
                BundleMaintenance.ValidateAgainstLocal(root,part,file,token);
                if(originalHash!=null)BundlePackageIdentity.ValidateUpgrade(destination,part,file.Path);
                if(stage!=null)stage("Публикация");
                BundlePublication.Publish(part,destination,originalHash,token,null);
            }
            progress(file.Size);
        }
    }
}
