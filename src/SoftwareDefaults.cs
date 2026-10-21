using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Xml;
using Microsoft.Win32;

namespace ServiceTool
{
    internal static class SoftwareDefaults
    {
        internal static readonly string[] TextExtensions={".txt",".cfg",".json",".config"};
        // Archive/container extensions only. Executables, DLLs, installers and help files
        // retain their Windows handlers even though 7-Zip can inspect their contents.
        internal static readonly string[] ArchiveExtensions={".7z",".zip",".rar",".001",".arj",".bz2",".bzip2",".cab",".cpio",".deb",".dmg",".gz",".gzip",".iso",".lzh",".lzma",".lzma86",".lz",".lzo",".rpm",".tar",".taz",".tbz",".tbz2",".tgz",".tlz",".txz",".tzst",".udf",".wim",".esd",".xar",".xz",".z",".zst",".zstd"};
        internal const string PolicyKey=@"Software\Policies\Microsoft\Windows\System";
        internal static bool SupportsPolicy()
        {
            using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64)) using(var key=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            { string edition=key==null ? "" : Convert.ToString(key.GetValue("EditionID")); return edition.Length>0 && !edition.StartsWith("Core",StringComparison.OrdinalIgnoreCase); }
        }
        internal static XmlDocument Read(string xml)
        {
            var document=new XmlDocument { XmlResolver=null };
            using(var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4*1024*1024})) document.Load(reader);
            if(document.DocumentElement==null || document.DocumentElement.Name!="DefaultAssociations") throw new InvalidDataException("Неверный XML ассоциаций Windows");
            return document;
        }
        internal static string ProgId(string id) {return id=="notepad" ? "BARS.NotepadPlusPlus.Text" : "BARS.SevenZip.Archive";}
        internal static XmlDocument Merge(string xml,IEnumerable<string> ids)
        {
            var document=Read(xml);
            foreach(string id in ids)
            {
                if(id!="notepad" && id!="7zip") throw new InvalidDataException("Неизвестный профиль ассоциаций");
                foreach(string extension in id=="notepad" ? TextExtensions : ArchiveExtensions)
                {
                    foreach(XmlNode node in document.DocumentElement.SelectNodes("Association")) if(String.Equals(node.Attributes["Identifier"]==null ? "" : node.Attributes["Identifier"].Value,extension,StringComparison.OrdinalIgnoreCase)) document.DocumentElement.RemoveChild(node);
                    var association=document.CreateElement("Association"); association.SetAttribute("Identifier",extension); association.SetAttribute("ProgId",ProgId(id)); association.SetAttribute("ApplicationName",SoftwareCatalog.Find(id).Name); document.DocumentElement.AppendChild(association);
                }
            }
            return document;
        }
        internal static void Register(RegistryKey machine,string id,string exe)
        {
            if(id!="notepad" && id!="7zip") throw new InvalidDataException("Неизвестный профиль ассоциаций");
            string prog=ProgId(id),app=SoftwareCatalog.Find(id).Name;
            using(var key=machine.CreateSubKey(@"Software\Classes\"+prog))
            {
                key.SetValue("",id=="notepad" ? "Текстовый файл" : "Архив");
                using(var icon=key.CreateSubKey("DefaultIcon")) icon.SetValue("",LibraryPlan.Quote(exe)+",0");
                using(var command=key.CreateSubKey(@"shell\open\command")) command.SetValue("",LibraryPlan.Quote(exe)+" \"%1\"");
            }
            string capabilities=@"Software\BARS\Manager\Defaults\"+id+@"\Capabilities";
            using(var key=machine.CreateSubKey(capabilities))
            {
                key.SetValue("ApplicationName",app); key.SetValue("ApplicationDescription",id=="notepad" ? "Текстовые файлы и настройки BARS" : "Архивы"); key.SetValue("ApplicationIcon",LibraryPlan.Quote(exe)+",0");
                using(var associations=key.CreateSubKey("FileAssociations")) foreach(string ext in id=="notepad" ? TextExtensions : ArchiveExtensions) associations.SetValue(ext,prog);
            }
            using(var key=machine.CreateSubKey(@"Software\RegisteredApplications")) key.SetValue("BARS."+id,capabilities);
        }
        internal static LibraryInstallResult Apply(string data,Action<string> progress)
        {
            var ids=(data??"").Split(','); if(ids.Length==0 || ids.Any(id=>id!="notepad" && id!="7zip") || ids.Distinct().Count()!=ids.Length) throw new InvalidDataException("Неверный список ассоциаций");
            var apps=ids.Select(id=>SoftwareDetection.Check(id,false,true)).ToArray();
            if(apps.Any(a=>a.State!=LibraryState.Installed)) throw new InvalidOperationException("Сначала установи выбранные программы x64 для всех пользователей");
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"BARS\Manager\DefaultAssociations"); ToolsDeployment.SafePath(directory);
            string final=Path.Combine(directory,"BARS.xml");
            using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
            {
                using(var policy=machine.OpenSubKey(PolicyKey))
                {
                    string previous=policy==null ? "" : Convert.ToString(policy.GetValue("DefaultAssociationsConfiguration"));
                    if(previous.Length>0 && !previous.Equals(final,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Ассоциации уже управляются другим системным профилем. Его нужно согласованно объединить; существующая политика не изменена");
                }
                var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);
                foreach(var sid in new[]{new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)}) security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
                Directory.CreateDirectory(directory,security); Directory.SetAccessControl(directory,security);
                string exported=Path.Combine(directory,"export-"+Guid.NewGuid().ToString("N")+".xml"), imported=Path.Combine(directory,"import-"+Guid.NewGuid().ToString("N")+".xml");
                try
                {
                    LibraryCancellation.Check(); progress("Сохраняем полный исходный профиль Windows");
                    string dism=Path.Combine(Environment.SystemDirectory,"dism.exe");
                    int code=LibraryProcess.Run(new ProcessStartInfo(dism,"/Online "+LibraryPlan.Quote("/Export-DefaultAppAssociations:"+exported)) {UseShellExecute=false,CreateNoWindow=true},progress);
                    if(code!=0 || !File.Exists(exported)) throw new InvalidOperationException("Не удалось экспортировать исходные ассоциации. Код "+code);
                    string original=File.ReadAllText(exported); var profile=Merge(original,ids);
                    // Keep registrations from our earlier profile when applying one app later.
                    if(File.Exists(final))
                    {
                        var old=Read(File.ReadAllText(final));
                        foreach(string previousId in new[]{"notepad","7zip"})
                            if(!ids.Contains(previousId) && old.DocumentElement.SelectNodes("Association").Cast<XmlNode>().Any(n=>n.Attributes["ProgId"]!=null && n.Attributes["ProgId"].Value==ProgId(previousId)) && SoftwareDetection.Check(previousId,false,true).State==LibraryState.Installed)
                                profile=Merge(profile.OuterXml,new[]{previousId});
                    }
                    foreach(var app in apps) Register(machine,app.Id,app.Executable);
                    profile.Save(imported); LibraryCancellation.Check(); progress("Применяем профиль для новых пользователей");
                    code=LibraryProcess.Run(new ProcessStartInfo(dism,"/Online "+LibraryPlan.Quote("/Import-DefaultAppAssociations:"+imported)) {UseShellExecute=false,CreateNoWindow=true},progress);
                    if(code!=0) throw new InvalidOperationException("Импорт ассоциаций не подтверждён. Код "+code);
                    string suffix=DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
                    File.Copy(exported,Path.Combine(directory,"before-"+suffix+".xml"),false);
                    if(File.Exists(final)) File.Replace(imported,final,Path.Combine(directory,"previous-"+suffix+".xml")); else File.Move(imported,final);
                    bool policySupported=SupportsPolicy();
                    if(policySupported) using(var policy=machine.CreateSubKey(PolicyKey)) policy.SetValue("DefaultAssociationsConfiguration",final,RegistryValueKind.String);
                    return new LibraryInstallResult {ExitCode=0,State=LibraryState.Installed,LogPath=final,Message=policySupported ? "Системный профиль ассоциаций применён. Для существующих пользователей потребуется выйти из Windows и войти заново; доменная политика может иметь приоритет" : "Профиль подготовлен для первого входа новых пользователей. В Windows Home принудительная политика не поддерживается; текущему пользователю нужно подтвердить приложения в параметрах Windows"};
                }
                finally {if(File.Exists(exported))File.Delete(exported);if(File.Exists(imported))File.Delete(imported);}
            }
        }
    }
}
