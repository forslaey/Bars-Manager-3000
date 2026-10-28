using System;
using System.IO;
using System.Threading;
namespace ServiceTool
{
    internal static class BundlePublication
    {
        internal static readonly object Gate=new object();
        // Same-volume rename transaction also works on FAT/exFAT. Never delete the
        // backup until the new destination is present. Failed rollback retains it.
        internal static void Publish(string source,string destination,string expected,CancellationToken token,Action<string> checkpoint)
        {
            string backup=Path.Combine(Path.GetDirectoryName(source),"backup-"+Guid.NewGuid().ToString("N")+".previous");
            bool moved=false;
            Action<string> step=name=>{token.ThrowIfCancellationRequested();if(checkpoint!=null)checkpoint(name);token.ThrowIfCancellationRequested();};
            step("before-publish");
            if(File.Exists(destination))
            {
                if(expected==null||!String.Equals(YandexBundleSource.Hash(destination,token),expected,StringComparison.OrdinalIgnoreCase))throw new IOException("Локальный файл изменился; новая копия не опубликована");
                File.Move(destination,backup);moved=true;
            }
            else if(expected!=null)throw new IOException("Локальный файл исчез во время загрузки; требуется сверка");
            try{
                step("before-new-rename");
                if(moved&&!String.Equals(YandexBundleSource.Hash(backup,token),expected,StringComparison.OrdinalIgnoreCase))throw new IOException("Прежний файл изменился во время публикации");
                File.Move(source,destination);
            }
            catch
            {
                if(moved&&!File.Exists(destination))
                {
                    try{File.Move(backup,destination);}
                    catch(Exception ex){throw new IOException("Не удалось восстановить прежний файл. Резервная копия сохранена: "+backup,ex);}
                }
                throw;
            }
            if(moved)
            {
                try{File.Delete(backup);}catch(IOException){System.Diagnostics.Trace.WriteLine("Резервная копия сохранена: "+backup);}catch(UnauthorizedAccessException){}
            }
        }
        internal static bool Matches(string root,BundleRemoteFile file,CancellationToken token)
        {
            string path=YandexBundleSource.LocalPath(root,file.Path);
            return File.Exists(path)&&new FileInfo(path).Length==file.Size&&String.Equals(YandexBundleSource.Hash(path,token),file.Sha256,StringComparison.OrdinalIgnoreCase);
        }
        internal static string Record(string root,BundleRemoteFile file,Action<string> checkpoint)
        {
            try{new BundleReceipts(root,checkpoint).Record(file);return null;}
            catch(IOException ex){return "Файл сохранён; квитанция не записана: "+ex.Message;}
            catch(UnauthorizedAccessException ex){return "Файл сохранён; квитанция не записана: "+ex.Message;}
        }
    }
}
