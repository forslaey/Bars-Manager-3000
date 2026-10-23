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
        internal static readonly string[] ArchiveExtensions={".7z",".zip",".zipx",".z01",".rar",".r00",".001",".arj",".bz2",".bzip2",".cab",".cpio",".deb",".dmg",".gz",".gzip",".iso",".lzh",".lha",".lzma",".lzma86",".rpm",".tar",".taz",".tbz",".tbz2",".tgz",".tpz",".txz",".tzst",".udf",".wim",".swm",".esd",".xar",".xip",".xz",".z",".zst",".zstd",".ar",".udeb",".ova",".cramfs",".squashfs",".mslz",".pmd"};
        internal static Dictionary<string,string> ChromeMappings(RegistryKey machine)
        {
            var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            using(var apps=machine.OpenSubKey(@"Software\RegisteredApplications"))
            {
                string path=apps==null?"":Convert.ToString(apps.GetValue("Google Chrome"));
                if(path.Length>0)using(var capabilities=machine.OpenSubKey(path))
                {
                    if(capabilities!=null)foreach(string group in new[]{"FileAssociations","URLAssociations"})using(var associations=capabilities.OpenSubKey(group))
                    {
                        if(associations==null)continue;
                        foreach(string name in associations.GetValueNames())
                        {
                            string prog=Convert.ToString(associations.GetValue(name));
                            if(name.Length==0||prog.Length==0)continue;
                            using(var command=machine.OpenSubKey(@"Software\Classes\"+prog+@"\shell\open\command"))
                                if(command!=null&&!String.IsNullOrWhiteSpace(Convert.ToString(command.GetValue(""))))result[name]=prog;
                        }
                    }
                }
            }
            if(!result.ContainsKey("http")||!result.ContainsKey("https"))throw new InvalidOperationException("Chrome не зарегистрировал поддерживаемые ассоциации для всех пользователей. Восстанови установку Chrome");
            return result;
        }
        private static Dictionary<string,string> ChromeMappings()
        {using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))return ChromeMappings(machine);}
        internal static string[] Extensions(string id)
        {return id=="chrome"?ChromeMappings().Keys.OrderBy(v=>v,StringComparer.OrdinalIgnoreCase).ToArray():id=="notepad"?TextExtensions:ArchiveExtensions;}
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
        internal static string ProgId(string id) {return id=="chrome" ? "BARS.Chrome.Html" : id=="notepad" ? "BARS.NotepadPlusPlus.Text" : "BARS.SevenZip.Archive";}
        internal static XmlDocument Merge(string xml,IEnumerable<string> ids)
        {return Merge(xml,ids,null);}
        internal static XmlDocument Merge(string xml,IEnumerable<string> ids,IDictionary<string,string> chrome)
        {
            var document=Read(xml);
            foreach(string id in ids)
            {
                if(id!="chrome" && id!="notepad" && id!="7zip") throw new InvalidDataException("Неизвестный профиль ассоциаций");
                var mappings=id=="chrome"?(chrome??ChromeMappings()):null;
                foreach(string extension in id=="chrome"?mappings.Keys:Extensions(id))
                {
                    foreach(XmlNode node in document.DocumentElement.SelectNodes("Association")) if(String.Equals(node.Attributes["Identifier"]==null ? "" : node.Attributes["Identifier"].Value,extension,StringComparison.OrdinalIgnoreCase)) document.DocumentElement.RemoveChild(node);
                    var association=document.CreateElement("Association"); association.SetAttribute("Identifier",extension); association.SetAttribute("ProgId",id=="chrome"?mappings[extension]:ProgId(id)); association.SetAttribute("ApplicationName",SoftwareCatalog.Find(id).Name); document.DocumentElement.AppendChild(association);
                }
            }
            return document;
        }
        internal static void Register(RegistryKey machine,string id,string exe)
        {
            if(id!="chrome" && id!="notepad" && id!="7zip") throw new InvalidDataException("Неизвестный профиль ассоциаций");
            // Chrome already provides the correct per-type native ProgIDs.
            // A synthetic HTML/URL handler for every file type is not equivalent.
            if(id=="chrome"){ChromeMappings(machine);return;}
            string prog=ProgId(id),app=SoftwareCatalog.Find(id).Name;
            using(var key=machine.CreateSubKey(@"Software\Classes\"+prog))
            {
                if(id=="chrome")key.SetValue("URL Protocol","");
                key.SetValue("",id=="chrome" ? "Документ HTML" : id=="notepad" ? "Текстовый файл" : "Архив");
                using(var icon=key.CreateSubKey("DefaultIcon")) icon.SetValue("",LibraryPlan.Quote(exe)+",0");
                using(var command=key.CreateSubKey(@"shell\open\command")) command.SetValue("",LibraryPlan.Quote(exe)+(id=="chrome" ? " --single-argument" : "")+" \"%1\"");
            }
            string capabilities=@"Software\BARS\Manager\Defaults\"+id+@"\Capabilities";
            using(var key=machine.CreateSubKey(capabilities))
            {
                key.SetValue("ApplicationName",app); key.SetValue("ApplicationDescription",id=="chrome" ? "Поддерживаемые Chrome файлы и ссылки" : id=="notepad" ? "Текстовые файлы и настройки BARS" : "Архивы"); key.SetValue("ApplicationIcon",LibraryPlan.Quote(exe)+",0");
                using(var associations=key.CreateSubKey("FileAssociations")) foreach(string ext in Extensions(id).Where(e=>e.StartsWith("."))) associations.SetValue(ext,prog);
                if(id=="chrome")using(var urls=key.CreateSubKey("URLAssociations"))foreach(string protocol in Extensions(id).Where(e=>!e.StartsWith(".")))urls.SetValue(protocol,prog);
            }
            using(var key=machine.CreateSubKey(@"Software\RegisteredApplications")) key.SetValue("BARS."+id,capabilities);
        }
        private static readonly string[] ProfileIds={"chrome","notepad","7zip"};
        internal static string[] ManagedIds(string oldXml,string manifest)
        {
            var result=new HashSet<string>((manifest??"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Where(ProfileIds.Contains));
            if(!String.IsNullOrEmpty(oldXml))
            {
                var old=Read(oldXml);
                foreach(string id in ProfileIds)if(old.DocumentElement.SelectNodes("Association").Cast<XmlNode>().Any(n=>n.Attributes["ProgId"]!=null&&n.Attributes["ProgId"].Value==ProgId(id)))result.Add(id);
            }
            return ProfileIds.Where(result.Contains).ToArray();
        }
        internal static XmlDocument RemoveApp(string xml,string id)
        {
            var document=Read(xml);
            foreach(XmlNode node in document.DocumentElement.SelectNodes("Association"))
                if(node.Attributes["ProgId"]!=null&&(node.Attributes["ProgId"].Value==ProgId(id)||id=="chrome"&&node.Attributes["ApplicationName"]!=null&&node.Attributes["ApplicationName"].Value==SoftwareCatalog.Find(id).Name))document.DocumentElement.RemoveChild(node);
            return document;
        }
        private static void SaveProfile(XmlDocument document,string path)
        {
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{document.Save(temporary);if(File.Exists(path))File.Replace(temporary,path,path+"."+DateTime.Now.ToString("yyyyMMddHHmmssfff")+".bak");else File.Move(temporary,path);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        internal static void RemoveRegistration(string id,Action<string> progress)
        {
            if(!ProfileIds.Contains(id))return;
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"BARS\Manager\DefaultAssociations");
            string final=Path.Combine(directory,"BARS.xml"),image=Path.Combine(directory,"ImageDefaults.xml"),manifest=Path.Combine(directory,"managed-apps.txt");
            using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
            {
                if(File.Exists(final))
                {
                    ToolsDeployment.SafePath(final);
                    string old=File.ReadAllText(final);
                    var remaining=ManagedIds(old,File.Exists(manifest)?File.ReadAllText(manifest):"").Where(v=>v!=id&&SoftwareDetection.Check(v,false,true).State==LibraryState.Installed).ToArray();
                    var policyProfile=Merge("<DefaultAssociations/>",remaining);
                    string activePolicy;using(var policy=machine.OpenSubKey(PolicyKey))activePolicy=policy==null?"":Convert.ToString(policy.GetValue("DefaultAssociationsConfiguration"));
                    if(activePolicy.Length==0||activePolicy.Equals(final,StringComparison.OrdinalIgnoreCase))
                    {
                        // Never feed the scoped sign-in policy into DISM: it would
                        // replace the complete image defaults with an incomplete list.
                        if(!File.Exists(image))
                        {
                            int exported=LibraryProcess.Run(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"dism.exe"),"/Online "+LibraryPlan.Quote("/Export-DefaultAppAssociations:"+image)){UseShellExecute=false,CreateNoWindow=true},progress);
                            if(exported!=0||!File.Exists(image))throw new IOException("Не удалось сохранить полный профиль Windows перед очисткой ассоциаций. Код "+exported);
                        }
                        string full=File.ReadAllText(image);
                        var imageProfile=RemoveApp(full,id);
                        SaveProfile(imageProfile,image);
                        int code=LibraryProcess.Run(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"dism.exe"),"/Online "+LibraryPlan.Quote("/Import-DefaultAppAssociations:"+image)){UseShellExecute=false,CreateNoWindow=true},progress);
                        if(code!=0)throw new IOException("Программа удалена, но профиль новых пользователей требует проверки. Код "+code);
                    }
                    SaveProfile(policyProfile,final);File.WriteAllLines(manifest,remaining,Encoding.UTF8);
                    if(remaining.Length==0)using(var policy=machine.OpenSubKey(PolicyKey,true)){if(policy!=null&&String.Equals(Convert.ToString(policy.GetValue("DefaultAssociationsConfiguration")),final,StringComparison.OrdinalIgnoreCase))policy.DeleteValue("DefaultAssociationsConfiguration",false);}
                }
                using(var classes=machine.OpenSubKey(@"Software\Classes",true)){if(classes!=null)classes.DeleteSubKeyTree(ProgId(id),false);}
                using(var apps=machine.OpenSubKey(@"Software\RegisteredApplications",true)){if(apps!=null)apps.DeleteValue("BARS."+id,false);}
                using(var own=machine.OpenSubKey(@"Software\BARS\Manager\Defaults",true)){if(own!=null)own.DeleteSubKeyTree(id,false);}
            }
        }
        internal static LibraryInstallResult Apply(string data,Action<string> progress)
        {
            var ids=(data??"").Split(',');if(ids.Length==0||ids.Any(id=>!ProfileIds.Contains(id))||ids.Distinct().Count()!=ids.Length)throw new InvalidDataException("Неверный список ассоциаций");
            var apps=ids.Select(id=>SoftwareDetection.Check(id,false,true)).ToArray();
            if(apps.Any(a=>a.State!=LibraryState.Installed))throw new InvalidOperationException("Сначала установи выбранные программы x64 для всех пользователей");
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"BARS\Manager\DefaultAssociations");ToolsDeployment.SafePath(directory);
            string final=Path.Combine(directory,"BARS.xml"),image=Path.Combine(directory,"ImageDefaults.xml"),manifest=Path.Combine(directory,"managed-apps.txt");
            using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
            {
                using(var policy=machine.OpenSubKey(PolicyKey))
                {
                    string previous=policy==null?"":Convert.ToString(policy.GetValue("DefaultAssociationsConfiguration"));
                    if(previous.Length>0&&!previous.Equals(final,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Ассоциации управляются другим системным профилем. Существующая политика не изменена");
                }
                var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);
                foreach(var sid in new[]{new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
                Directory.CreateDirectory(directory,security);Directory.SetAccessControl(directory,security);
                string exported=Path.Combine(directory,"export-"+Guid.NewGuid().ToString("N")+".xml"),imported=Path.Combine(directory,"import-"+Guid.NewGuid().ToString("N")+".xml");
                try
                {
                    LibraryCancellation.Check();progress("Сохраняем исходные ассоциации Windows до внесения изменений");
                    string dism=Path.Combine(Environment.SystemDirectory,"dism.exe");
                    int code=LibraryProcess.Run(new ProcessStartInfo(dism,"/Online "+LibraryPlan.Quote("/Export-DefaultAppAssociations:"+exported)){UseShellExecute=false,CreateNoWindow=true},progress);
                    if(code!=0||!File.Exists(exported))throw new InvalidOperationException("Не удалось экспортировать исходные ассоциации. Код "+code);
                    string original=File.ReadAllText(exported);
                    string suffix=DateTime.Now.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
                    File.Copy(exported,Path.Combine(directory,"before-"+suffix+".xml"),false);
                    // A new request manages exactly its checked profiles. Removing
                    // an old policy entry does not delete that user's current choice.
                    var active=ProfileIds.Where(ids.Contains).ToArray();
                    foreach(string id in active){var app=SoftwareDetection.Check(id,false,true);Register(machine,id,app.Executable);}
                    var full=Merge(original,active);
                    var scoped=Merge("<DefaultAssociations/>",active);
                    full.Save(imported);LibraryCancellation.Check();progress("Применяем полный профиль только для новых пользователей; посторонние типы сохранены");
                    code=LibraryProcess.Run(new ProcessStartInfo(dism,"/Online "+LibraryPlan.Quote("/Import-DefaultAppAssociations:"+imported)){UseShellExecute=false,CreateNoWindow=true},progress);
                    if(code!=0)throw new InvalidOperationException("Импорт ассоциаций не подтверждён. Код "+code);
                    SaveProfile(full,image);SaveProfile(scoped,final);File.WriteAllLines(manifest,active,Encoding.UTF8);
                    bool supported=SupportsPolicy();
                    if(supported)using(var policy=machine.CreateSubKey(PolicyKey))policy.SetValue("DefaultAssociationsConfiguration",final,RegistryValueKind.String);
                    return new LibraryInstallResult{ExitCode=0,State=LibraryState.Installed,LogPath=final,Message=supported?"Профиль сохранён. При следующем входе будут назначены только выбранные типы файлов и ссылок. Прочие ассоциации пользователей не включены в политику. Текущий выбор Windows не удалялся; для немедленного применения подтверди приложения в параметрах Windows":"Профиль сохранён для новых пользователей. Windows Home не поддерживает эту политику; текущему пользователю нужно подтвердить приложения в параметрах Windows"};
                }
                finally{if(File.Exists(exported))File.Delete(exported);if(File.Exists(imported))File.Delete(imported);}
            }
        }
    }
}
