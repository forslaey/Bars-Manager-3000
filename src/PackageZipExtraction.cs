using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ServiceTool
{
    internal static class PackageZipExtraction
    {
        internal static bool HasReadableDirectory(string path)
        {
            try
            {
                using(var stream=File.OpenRead(path))
                using(var archive=new ZipArchive(stream,ZipArchiveMode.Read))
                    return archive.Entries.Count>0;
            }
            catch(InvalidDataException){return false;}
            catch(IOException){return false;}
            catch(UnauthorizedAccessException){return false;}
        }
        internal static string Extract(string path,System.Threading.CancellationToken token=default(System.Threading.CancellationToken),Action<TransferProgress> report=null,bool replace=false)
        {
            string parent = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string target = YandexBundleSource.LocalPath(parent, "/" + name);
            if (!replace&&Directory.Exists(target)) throw new IOException("Папка уже существует: " + target + ".");
            string staging = YandexBundleSource.LocalPath(parent, "/.s" + Guid.NewGuid().ToString("N").Substring(0,8));
            Directory.CreateDirectory(staging);
            try
            {
                using (var source = File.OpenRead(path))
                using (var archive = new ZipArchive(source, ZipArchiveMode.Read))
                {
                    if (archive.Entries.Count == 0) throw new InvalidDataException("Архив пуст.");
                    var drive = new DriveInfo(Path.GetPathRoot(staging));
                    long total=archive.Entries.Sum(entry=>entry.Length),written=0;
                    if (drive.IsReady && total > drive.AvailableFreeSpace)
                        throw new IOException("Недостаточно места для распаковки комплекта.");
                    string prefix=name+"/";
                    bool wrapped=archive.Entries.All(entry=>entry.FullName.Replace('\\', '/').StartsWith(prefix,StringComparison.OrdinalIgnoreCase));
                    var buffer=new byte[64*1024];
                    var clock=System.Diagnostics.Stopwatch.StartNew();
                    foreach (var entry in archive.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        string relative = entry.FullName.Replace('\\', '/');
                        if(wrapped){relative=relative.Substring(prefix.Length);if(relative.Length==0)continue;}
                        bool directory = relative.EndsWith("/", StringComparison.Ordinal);
                        string destination = YandexBundleSource.LocalPath(staging, "/" + relative.TrimEnd('/'));
                        if (directory) { Directory.CreateDirectory(destination); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        using (var input = entry.Open())
                        using (var output = new FileStream(destination, FileMode.CreateNew))
                        {
                            int count;
                            while((count=input.Read(buffer,0,buffer.Length))>0)
                            {
                                token.ThrowIfCancellationRequested();output.Write(buffer,0,count);written+=count;
                                if(report!=null&&clock.ElapsedMilliseconds>=80){report(new TransferProgress{Stage="Распаковываем комплект…",File=relative,Bytes=written,Total=total});clock.Restart();}
                            }
                        }
                    }
                    if(report!=null)report(new TransferProgress{Stage="Сохраняем распакованный комплект…",Bytes=written,Total=total});
                }
                token.ThrowIfCancellationRequested();
                if(!replace||!Directory.Exists(target)) Directory.Move(staging,target);
                else
                {
                    string previous=staging+"-old";
                    Directory.Move(target,previous);
                    try{Directory.Move(staging,target);}
                    catch{Directory.Move(previous,target);throw;}
                    Directory.Delete(previous,true);
                }
                return target;
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }

    }
}
