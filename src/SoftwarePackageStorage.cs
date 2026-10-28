using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ServiceTool
{
    internal static class SoftwarePackageStorage
    {
        internal static string Folder(string selected)
        {
            if(String.IsNullOrWhiteSpace(selected))throw new IOException("Выбери папку Библиотеки или _soft");
            string value=selected.Trim().Trim('"');
            if(!Regex.IsMatch(value,@"\A(?:[a-zA-Z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+(?:[\\/]|$))"))throw new IOException("Укажи полный путь к папке Библиотеки или _soft");
            string full=Path.GetFullPath(value);if(full.Length>Path.GetPathRoot(full).Length)full=full.TrimEnd('\\','/');
            if(Path.GetFileName(full).Equals("_soft",StringComparison.OrdinalIgnoreCase))return full;
            string child=Path.Combine(full,"Библиотеки");if(Directory.Exists(child))full=child;
            return Path.Combine(full,"_soft");
        }
        internal static SoftwarePackage Download(SoftwarePackage remote,string selected,Action<string> progress,Func<bool> cancelled)
        {
            SoftwareCatalog.Find(remote.Id);
            string folder=Folder(selected);ToolsDeployment.SafePath(folder);
            if(!Directory.Exists(Path.GetDirectoryName(folder)))throw new DirectoryNotFoundException("Папка библиотек недоступна: "+Path.GetDirectoryName(folder));
            Directory.CreateDirectory(folder);ToolsDeployment.SafePath(folder);
            string stage=Path.Combine(folder,".bars-download-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            try
            {
                var package=SoftwareSources.Download(remote,stage,progress,cancelled);
                return Publish(package,folder,progress,cancelled);
            }
            finally
            {
                string staged=Path.Combine(stage,remote.Id,Path.GetFileName(remote.File));
                try{ToolsDeployment.SafePath(staged);if(File.Exists(staged))File.Delete(staged);}
                catch(IOException ex){progress("Не удалось убрать временный файл: "+staged+". "+ex.Message);}
                catch(UnauthorizedAccessException ex){progress("Не удалось убрать временный файл: "+staged+". "+ex.Message);}
                // Remove only empty directories created by this download. A failed rollback
                // must retain the previous file and report its location, never erase it here.
                RemoveEmpty(Path.Combine(stage,remote.Id),progress);RemoveEmpty(stage,progress);
            }
        }
        private static void RemoveEmpty(string path,Action<string> progress)
        {
            try{ToolsDeployment.SafePath(path);if(Directory.Exists(path)&&!Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path,false);}
            catch(IOException ex){progress("Не удалось убрать временную папку: "+path+". "+ex.Message);}
            catch(UnauthorizedAccessException ex){progress("Не удалось убрать временную папку: "+path+". "+ex.Message);}
        }
        internal static SoftwarePackage Publish(SoftwarePackage package,string selected,Action<string> progress,Func<bool> cancelled)
        {
            return PublishVerified(package,selected,progress,cancelled,p=>{SoftwareCatalog.Validate(p,true);SoftwareTrust.Verify(p,true);},SoftwareCatalog.Describe);
        }
        internal static SoftwarePackage PublishVerified(SoftwarePackage package,string selected,Action<string> progress,Func<bool> cancelled,Action<SoftwarePackage> verify,Func<string,string,string,SoftwarePackage> describe)
        {
            LibraryCancellation.Check(cancelled);string folder=Folder(selected);ToolsDeployment.SafePath(folder);
            verify(package);
            var actual=describe(package.Id,package.File,package.Source);
            if(actual.Architecture!=package.Architecture||SoftwareCatalog.Compare(actual.Version,package.Version)!=0||!String.Equals(actual.Sha256,package.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Установщик изменился до сохранения");
            if(SoftwareCatalog.PackageId(package.File)!=package.Id)throw new InvalidDataException("Неверное имя установщика");
            Directory.CreateDirectory(folder);ToolsDeployment.SafePath(folder);
            string final=Path.Combine(folder,Path.GetFileName(package.File));ToolsDeployment.SafePath(final);
            var previous=new List<SoftwarePackage>();
            foreach(string file in Directory.GetFiles(folder))
            {
                if(SoftwareCatalog.PackageId(file)!=package.Id)continue;
                SoftwarePackage old;
                try{old=describe(package.Id,file,"Локальный _soft");}
                catch(InvalidDataException ex){if(String.Equals(file,final,StringComparison.OrdinalIgnoreCase))throw;progress("Прежний файл не распознан и сохранён: "+file+". "+ex.Message);continue;}
                catch(IOException ex){if(String.Equals(file,final,StringComparison.OrdinalIgnoreCase))throw;progress("Прежний файл не распознан и сохранён: "+file+". "+ex.Message);continue;}
                catch(UnauthorizedAccessException ex){if(String.Equals(file,final,StringComparison.OrdinalIgnoreCase))throw;progress("Прежний файл недоступен и сохранён: "+file+". "+ex.Message);continue;}
                if(old.Architecture!=package.Architecture){if(String.Equals(file,final,StringComparison.OrdinalIgnoreCase))throw new IOException("Файл назначения содержит установщик другой разрядности: "+file);continue;}
                int comparison=SoftwareCatalog.Compare(old.Version,package.Version);
                if(comparison>0)throw new IOException("В _soft уже есть более новый установщик: "+file);
                if(comparison<0&&!String.Equals(file,final,StringComparison.OrdinalIgnoreCase))previous.Add(old);
            }
            if(String.Equals(Path.GetFullPath(package.File),Path.GetFullPath(final),StringComparison.OrdinalIgnoreCase))throw new IOException("Новый установщик должен сначала пройти загрузку во временную папку");
            string backup=Path.Combine(Path.GetDirectoryName(package.File),Guid.NewGuid().ToString("N")+".previous");
            LibraryCancellation.Check(cancelled);bool movedOld=false;
            try
            {
                if(File.Exists(final)){File.Move(final,backup);movedOld=true;}
                // Same-volume renames also work on FAT/exFAT removable drives; no File.Replace.
                File.Move(package.File,final);
            }
            catch
            {
                if(movedOld&&!File.Exists(final))
                {
                    try{File.Move(backup,final);}
                    catch(Exception ex){progress("Не удалось восстановить прежний установщик. Он сохранён здесь: "+backup+". "+ex.Message);}
                }
                throw;
            }
            package.File=final;progress("Установщик сохранён: "+final);
            if(movedOld)DeletePrevious(backup,null,progress);
            foreach(var old in previous)DeletePrevious(old.File,old.Sha256,progress);
            return package;
        }
        private static void DeletePrevious(string file,string expectedHash,Action<string> progress)
        {
            try
            {
                ToolsDeployment.SafePath(file);
                if(!File.Exists(file))return;
                if(expectedHash!=null&&!String.Equals(SoftwareCatalog.Hash(file),expectedHash,StringComparison.OrdinalIgnoreCase))throw new IOException("Файл изменился после проверки");
                File.Delete(file);progress("Прежний установщик удалён: "+file);
            }
            catch(IOException ex){progress("Новый установщик сохранён, но прежний удалить не удалось: "+file+". "+ex.Message);}
            catch(UnauthorizedAccessException ex){progress("Новый установщик сохранён, но прежний удалить не удалось: "+file+". "+ex.Message);}
        }
    }
}
