using System;
using System.Diagnostics;
using System.IO;
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

        internal static bool HasReadableDirectory(string path,string formatPath=null,CancellationToken token=default(CancellationToken))
        {
            if(IsZip(formatPath??path))return PackageZipExtraction.HasReadableDirectory(path);
            try { return Entries(path,token).Length>0; }
            catch(IOException){return false;}
            catch(UnauthorizedAccessException){return false;}
        }

        internal static string[] Entries(string path,CancellationToken token=default(CancellationToken))
        {
            return RunTar("-tf",path,null,token).Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)
                .Select(entry=>entry.Replace('\\','/')).Where(entry=>entry!="."&&entry!="./").ToArray();
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
                string[] top=Directory.GetFileSystemEntries(staging);
                string prepared=top.Length==1&&Directory.Exists(top[0])&&Path.GetFileName(top[0]).Equals(name,StringComparison.OrdinalIgnoreCase)?top[0]:staging;
                string previous=staging+"-old";
                if(Directory.Exists(target))Directory.Move(target,previous);
                try { Directory.Move(prepared,target); }
                catch { if(Directory.Exists(previous))Directory.Move(previous,target);throw; }
                if(Directory.Exists(previous))Directory.Delete(previous,true);
                return target;
            }
            finally { if(Directory.Exists(staging))Directory.Delete(staging,true); }
        }

        private static string RunTar(string options,string path,string folder,CancellationToken token)
        {
            string system=Environment.Is64BitOperatingSystem&&!Environment.Is64BitProcess
                ?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Sysnative"):Environment.SystemDirectory;
            string tar=Path.Combine(system,"tar.exe");
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
                var output=process.StandardOutput.ReadToEndAsync();
                var errors=process.StandardError.ReadToEndAsync();
                while(!process.WaitForExit(100))
                {
                    if(!token.IsCancellationRequested)continue;
                    try{process.Kill();}catch(InvalidOperationException){}
                    process.WaitForExit();
                    token.ThrowIfCancellationRequested();
                }
                token.ThrowIfCancellationRequested();
                if(process.ExitCode!=0)throw new InvalidDataException("Не удалось прочитать архив. "+errors.GetAwaiter().GetResult().Trim());
                return output.GetAwaiter().GetResult();
            }
        }
    }
}