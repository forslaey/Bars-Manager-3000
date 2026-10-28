using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;

namespace ServiceTool
{
    // Shared package identity: use the same metadata rules as the installers.
    internal sealed class BundlePackageIdentity
    {
        internal string Key, Version;
        internal static BundlePackageIdentity Read(string path,string relative)
        {
            if(relative.StartsWith("/_tools/",StringComparison.OrdinalIgnoreCase))return null;
            string ext=Path.GetExtension(relative);
            if(!ext.Equals(".exe",StringComparison.OrdinalIgnoreCase)&&!ext.Equals(".msi",StringComparison.OrdinalIgnoreCase))return null;
            // Staging names end in .part: MSI metadata still needs its real extension.
            string metadata=path,alias=null;
            try
            {
                if(!Path.GetFileName(path).Equals(Path.GetFileName(relative),StringComparison.OrdinalIgnoreCase))
                {alias=Path.Combine(Path.GetDirectoryName(path),"identity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(alias);metadata=Path.Combine(alias,Path.GetFileName(relative));File.Copy(path,metadata,false);}
                if(relative.StartsWith("/_soft/",StringComparison.OrdinalIgnoreCase))
                {
                    string id=SoftwareCatalog.PackageId(relative);
                    if(!String.IsNullOrEmpty(id))
                    {
                        var p=SoftwareCatalog.Describe(id,metadata,"Комплект");
                        return new BundlePackageIdentity {Key="software|"+id+"|"+p.Architecture,Version=p.Version};
                    }
                }
                var library=LibraryDiscovery.Describe(metadata,relative);
                if(library!=null&&(library.Kind==LibraryKind.Access||library.Kind==LibraryKind.Framework||library.Kind==LibraryKind.Inno||library.Kind==LibraryKind.VcBundle))
                {
                    string arch=ExecutableArchitecture.Read(metadata);library.Id+="|"+arch;
                }
                if(library==null||LibraryVersions.Number(library.Version)==null)return null;
                return new BundlePackageIdentity {Key="library|"+library.Id+"|"+library.Architecture,Version=library.Version};
            }
            catch(Exception ex){if(ex is IOException||ex is ArgumentException||ex is System.Runtime.InteropServices.COMException||ex is UnauthorizedAccessException)return null;throw;}
            finally{if(alias!=null){if(File.Exists(metadata))File.Delete(metadata);if(Directory.Exists(alias))Directory.Delete(alias,false);}}
        }
        internal static void ValidateUpgrade(string oldFile,string newFile,string relative)
        {
            var before=Read(oldFile,relative);var after=Read(newFile,relative);
            if(before==null||after==null||before.Key!=after.Key||!LibraryVersions.SameChannel(before.Version,after.Version)||LibraryVersions.Compare(after.Version,before.Version)<0)
                throw new IOException("Продукт, архитектура, ветка или версия обновления не подтверждены; локальный файл сохранён");
        }
    }
    internal static class BundlePreferences
    {
        static string FileName {get{return ApplicationStorage.FilePath("bundle-root.txt");}}
        internal static string Load()
        {try{if(File.Exists(FileName)){string value=File.ReadAllText(FileName).Trim();if(Path.IsPathRooted(value))return value;}}catch(IOException){}catch(UnauthorizedAccessException){}return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Библиотеки");}
        internal static void Save(string root)
        {Directory.CreateDirectory(ApplicationStorage.DirectoryPath);File.WriteAllText(Path.Combine(ApplicationStorage.DirectoryPath,"bundle-root.txt"),Path.GetFullPath(root));}
    }
    internal sealed class BundleOldFile
    {internal string Path,Hash;}
    internal static class BundleMaintenance
    {
        internal static void ValidateAgainstLocal(string root,string staged,BundleRemoteFile file,CancellationToken token)
        {
            if(file.Group=="Утилиты")return;
            var incoming=BundlePackageIdentity.Read(staged,file.Path);if(incoming==null)return;
            string directory=Path.GetDirectoryName(YandexBundleSource.LocalPath(root,file.Path));if(!Directory.Exists(directory))return;
            // Compare only installable siblings; no execution and no traversal of utility trees.
            foreach(string local in Directory.GetFiles(directory))
            {
                token.ThrowIfCancellationRequested();if((File.GetAttributes(local)&FileAttributes.ReparsePoint)!=0)continue;
                string relative="/"+local.Substring(Path.GetFullPath(root).TrimEnd('\\','/').Length).TrimStart('\\','/').Replace('\\','/');
                var existing=BundlePackageIdentity.Read(local,relative);
                if(existing!=null&&existing.Key==incoming.Key&&LibraryVersions.SameChannel(existing.Version,incoming.Version)&&LibraryVersions.Compare(existing.Version,incoming.Version)>0)
                    throw new IOException("В комплекте уже есть более новая версия продукта; понижение заблокировано: "+relative);
            }
        }
        internal static System.Collections.Generic.List<BundleOldFile> OldFiles(string root,System.Collections.Generic.IEnumerable<BundleRemoteFile> source,CancellationToken token)
        {
            var current=source.ToList();var known=new System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<BundlePackageIdentity>>();
            foreach(var file in current.Where(f=>f.Group!="Утилиты"))
            {
                token.ThrowIfCancellationRequested();string path=YandexBundleSource.LocalPath(root,file.Path);
                if(!File.Exists(path)||YandexBundleSource.Hash(path,token)!=file.Sha256)continue;
                var identity=BundlePackageIdentity.Read(path,file.Path);if(identity==null)continue;
                System.Collections.Generic.List<BundlePackageIdentity> list;if(!known.TryGetValue(identity.Key,out list))known[identity.Key]=list=new System.Collections.Generic.List<BundlePackageIdentity>();list.Add(identity);
            }
            var result=new System.Collections.Generic.List<BundleOldFile>();
            var pending=new System.Collections.Generic.Stack<string>();pending.Push(Path.GetFullPath(root));
            var paths=new System.Collections.Generic.HashSet<string>(current.Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
            while(pending.Count>0)
            {
                token.ThrowIfCancellationRequested();string dir=pending.Pop();
                foreach(string child in Directory.GetDirectories(dir))if(!Path.GetFileName(child).Equals(".bars-download",StringComparison.OrdinalIgnoreCase)&&!Path.GetFileName(child).Equals("_tools",StringComparison.OrdinalIgnoreCase)&&(File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)pending.Push(child);
                foreach(string path in Directory.GetFiles(dir))
                {
                    string relative="/"+path.Substring(Path.GetFullPath(root).TrimEnd('\\','/').Length).TrimStart('\\','/').Replace('\\','/');
                    if(paths.Contains(relative)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;
                    string ext=Path.GetExtension(path);if(!ext.Equals(".exe",StringComparison.OrdinalIgnoreCase)&&!ext.Equals(".msi",StringComparison.OrdinalIgnoreCase))continue;
                    string hash=YandexBundleSource.Hash(path,token);var remote=new BundleRemoteFile {Path=relative,Sha256=hash};
                    if(!new BundleReceipts(root).CanReplace(remote,hash))continue;
                    var identity=BundlePackageIdentity.Read(path,relative);System.Collections.Generic.List<BundlePackageIdentity> replacements;
                    if(identity!=null&&known.TryGetValue(identity.Key,out replacements)&&replacements.Any(p=>LibraryVersions.SameChannel(identity.Version,p.Version)&&LibraryVersions.Compare(p.Version,identity.Version)>0))result.Add(new BundleOldFile {Path=relative,Hash=hash});
                }
            }
            return result;
        }
        internal static void RemoveOld(string root,BundleOldFile old)
        {
            string path=YandexBundleSource.LocalPath(root,old.Path);
            using(var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
            {
                using(var hash=System.Security.Cryptography.SHA256.Create())if(!String.Equals(BitConverter.ToString(hash.ComputeHash(held)).Replace("-",""),old.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Старая копия изменилась; сохранена: "+old.Path);
                File.Delete(path);
            }
        }
        // Enumerate without following junctions, symlinks or unknown directories.
        internal static string[] Parts(string root)
        {
            string stage=YandexBundleSource.LocalPath(root,"/.bars-download/placeholder");stage=Path.GetDirectoryName(stage);
            if(!Directory.Exists(stage))return new string[0];
            var result=new System.Collections.Generic.List<string>();
            foreach(string dir in Directory.GetDirectories(stage))
            {
                string name=Path.GetFileName(dir);
                if(name.Length!=64||name.Any(c=>!Uri.IsHexDigit(c))||(File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)continue;
                var pending=new System.Collections.Generic.Stack<string>();pending.Push(dir);
                while(pending.Count>0)
                {
                    string current=pending.Pop();
                    foreach(string child in Directory.GetDirectories(current))if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)pending.Push(child);
                    foreach(string file in Directory.GetFiles(current))if((File.GetAttributes(file)&FileAttributes.ReparsePoint)==0&&(file.EndsWith(".part",StringComparison.OrdinalIgnoreCase)||System.Text.RegularExpressions.Regex.IsMatch(file,@"\.part\.invalid-[0-9a-f]{32}$")))result.Add(file);
                }
            }
            return result.ToArray();
        }
        internal static long CleanParts(string root)
        {
            long bytes=0;foreach(string path in Parts(root)){string relative="/"+path.Substring(Path.GetFullPath(root).TrimEnd('\\','/').Length).TrimStart('\\','/').Replace('\\','/');string safe=YandexBundleSource.LocalPath(root,relative);bytes+=new FileInfo(safe).Length;File.Delete(safe);}return bytes;
        }
    }
}
