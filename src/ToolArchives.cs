using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace ServiceTool
{
    internal static class ToolArchives
    {
        internal static bool IsZip(string path) { return Path.GetExtension(path).Equals(".zip",StringComparison.OrdinalIgnoreCase); }

        internal static bool Supports(string path)
        {
            string extension=Path.GetExtension(path);
            return IsZip(path)||String.Equals(extension,".rar",StringComparison.OrdinalIgnoreCase)||
                String.Equals(extension,".7z",StringComparison.OrdinalIgnoreCase);
        }

        internal enum ReadState { Ready, Damaged, Unavailable, MissingTool }

        internal static ReadState Inspect(string path,CancellationToken token,out string[] entries)
        {
            entries=new string[0];
            token.ThrowIfCancellationRequested();
            try
            {
                // Detect access/sharing failures before asking tar to parse the file.
                using(var input=File.OpenRead(path))
                {
                    if(!IsZip(path)&&!File.Exists(WindowsTar()))return ReadState.MissingTool;
                    entries=Entries(path,token);
                    return entries.Length==0?ReadState.Damaged:ReadState.Ready;
                }
            }
            catch(InvalidDataException){return ReadState.Damaged;}
            catch(IOException){return ReadState.Unavailable;}
            catch(UnauthorizedAccessException){return ReadState.Unavailable;}
        }

        internal static string ReadStatus(ReadState state)
        {
            switch(state)
            {
                case ReadState.Damaged:return "Архив повреждён";
                case ReadState.MissingTool:return "Для RAR и 7z нужен tar.exe из Windows";
                case ReadState.Unavailable:return "Локальный файл недоступен";
                default:return "";
            }
        }

        internal static string[] Entries(string path,CancellationToken token=default(CancellationToken))
        {
            if(IsZip(path))
            {
                token.ThrowIfCancellationRequested();
                using(var input=File.OpenRead(path))
                using(var zip=new ZipArchive(input,ZipArchiveMode.Read))
                    return zip.Entries.Select(entry=>entry.FullName.Replace('\\','/')).ToArray();
            }
            return RunTar("-tf",path,null,token).Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)
                .Select(entry=>entry.Replace('\\','/')).Where(entry=>entry!="."&&entry!="./").ToArray();
        }

        internal static string[] NormalizeEntries(string path,string[] entries)
        {
            string prefix=Path.GetFileNameWithoutExtension(path)+"/";
            return HasWrapper(entries,prefix)
                ?entries.Select(entry=>entry.Substring(prefix.Length)).Where(entry=>entry.Length>0).ToArray():entries;
        }

        private static bool HasWrapper(string[] entries,string prefix)
        {
            return entries.Length>0&&entries.All(entry=>entry.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));
        }

        internal static void ValidateDownload(string path,string formatPath,CancellationToken token)
        {
            if(!IsZip(formatPath))
            {
                if(Entries(path,token).Length==0)throw new InvalidDataException("Архив пуст.");
                RunTar("-xOf",path,null,token,true);
                return;
            }

            using(var input=File.OpenRead(path))
            using(var zip=new ZipArchive(input,ZipArchiveMode.Read))
            {
                if(zip.Entries.Count==0)throw new InvalidDataException("Архив пуст.");
                var buffer=new byte[65536];
                foreach(var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    long bytes=0;
                    using(var contents=entry.Open())
                    {
                        int count;
                        while((count=contents.Read(buffer,0,buffer.Length))>0)
                        {
                            token.ThrowIfCancellationRequested();
                            bytes+=count;
                            if(bytes>entry.Length)throw new InvalidDataException("Размер записи ZIP не совпадает.");
                        }
                    }
                    if(bytes!=entry.Length)throw new InvalidDataException("Размер записи ZIP не совпадает.");
                }
            }
            if(File.Exists(WindowsTar()))RunTar("-xOf",path,null,token,true);
        }

        internal static string Extract(string path,CancellationToken token,Action<TransferProgress> report,bool replace)
        {
            if(IsZip(path))return PackageZipExtraction.Extract(path,token,report,replace);
            string parent=Path.GetDirectoryName(path);
            string name=Path.GetFileNameWithoutExtension(path);
            string target=YandexBundleSource.LocalPath(parent,"/"+name);
            string staging=YandexBundleSource.LocalPath(parent,"/.s"+Guid.NewGuid().ToString("N").Substring(0,8));
            var entries=Entries(path,token);
            if(entries.Length==0)throw new InvalidDataException("Архив пуст.");
            foreach(string entry in entries)
                YandexBundleSource.LocalPath(staging,"/"+entry.TrimEnd('/'));
            if(!replace&&Directory.Exists(target))throw new IOException("Папка уже существует: "+target+".");
            Directory.CreateDirectory(staging);
            try
            {
                if(report!=null)report(new TransferProgress{Stage="Распаковываем комплект…"});
                RunTar("-xkf",path,staging,token);
                var pending=new System.Collections.Generic.Stack<string>();
                pending.Push(staging);
                while(pending.Count>0)
                {
                    token.ThrowIfCancellationRequested();
                    foreach(string item in Directory.EnumerateFileSystemEntries(pending.Pop()))
                    {
                        var attributes=File.GetAttributes(item);
                        if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Архив содержит ссылку.");
                        if((attributes&FileAttributes.Directory)!=0)pending.Push(item);
                    }
                }
                string wrapped=Path.Combine(staging,name);
                string prepared=Directory.Exists(wrapped)&&HasWrapper(entries,name+"/")?wrapped:staging;
                string previous=staging+"-old";
                if(Directory.Exists(target))Directory.Move(target,previous);
                try { Directory.Move(prepared,target); }
                catch { if(Directory.Exists(previous))Directory.Move(previous,target);throw; }
                PackageZipExtraction.CleanupPrevious(previous,report);
                return target;
            }
            finally { PackageZipExtraction.CleanupFolder(staging,"Не удалось удалить временную папку: ",report); }
        }

        private static string WindowsTar()
        {
            string system=Environment.Is64BitOperatingSystem&&!Environment.Is64BitProcess
                ?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Sysnative"):Environment.SystemDirectory;
            return Path.Combine(system,"tar.exe");
        }

        private static string RunTar(string options,string path,string folder,CancellationToken token,bool discardOutput=false)
        {
            string tar=WindowsTar();
            if(!File.Exists(tar))throw new IOException("Для архивов RAR и 7z нужен tar.exe из Windows.");
            var start=new ProcessStartInfo(tar,options+" \""+path+"\""+(folder==null?"":" -C \""+folder+"\""))
            {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                StandardOutputEncoding=Encoding.UTF8
            };
            token.ThrowIfCancellationRequested();
            using(var process=Process.Start(start))
            {
                process.StandardInput.Close();
                var output=discardOutput?null:process.StandardOutput.ReadToEndAsync();
                var drain=discardOutput?process.StandardOutput.BaseStream.CopyToAsync(Stream.Null):null;
                var clock=Stopwatch.StartNew();
                var limit=options=="-tf"?TimeSpan.FromSeconds(30):TimeSpan.FromMinutes(30);
                var errors=process.StandardError.ReadToEndAsync();
                while(!process.WaitForExit(100))
                {
                    if(!token.IsCancellationRequested&&clock.Elapsed<limit)continue;
                    try{process.Kill();}catch(InvalidOperationException){}
                    process.WaitForExit();
                    token.ThrowIfCancellationRequested();
                    throw new IOException("Превышено время обработки архива.");
                }
                token.ThrowIfCancellationRequested();
                if(process.ExitCode!=0)throw new InvalidDataException("Не удалось прочитать архив. "+errors.GetAwaiter().GetResult().Trim());
                if(drain!=null)drain.GetAwaiter().GetResult();
                return output==null?"":output.GetAwaiter().GetResult();
            }
        }
    }
}