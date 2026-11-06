using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security;
using System.Xml;
using Microsoft.Win32;

namespace ServiceTool
{
    internal static class SoftwareDetection
    {
        internal static bool Matches(string id, string display)
        {
            return id=="chrome" ? display.Equals("Google Chrome",StringComparison.OrdinalIgnoreCase) : id=="notepad" ? display.StartsWith("Notepad++",StringComparison.OrdinalIgnoreCase) : id=="7zip" ? Regex.IsMatch(display,@"^7-Zip(?:\s|$)",RegexOptions.IgnoreCase) :
                id=="anydesk" ? display.Equals("AnyDesk",StringComparison.OrdinalIgnoreCase) : display.StartsWith("RuDesktop",StringComparison.OrdinalIgnoreCase);
        }
        private static string IconPath(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string text=value.Trim(); if (text.StartsWith("\"")) { int end=text.IndexOf('"',1); return end>1 ? text.Substring(1,end-1) : null; }
            return Regex.Replace(text,@",\s*-?\d+\s*$","");
        }
        internal static string NormalizePath(string value)
        {
            if(String.IsNullOrWhiteSpace(value))return null;
            try { string path=Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));return Path.IsPathRooted(path) ? Path.GetFullPath(path) : null; }
            catch(ArgumentException){return null;}catch(NotSupportedException){return null;}catch(PathTooLongException){return null;}
        }
        internal static string NormalizeVersion(string id,string value)
        {
            if(id=="anydesk"){var match=Regex.Match(value??"",@"^(?:ad\s+)?(\d+(?:\.\d+){1,3})$",RegexOptions.IgnoreCase);return match.Success?match.Groups[1].Value:"";}return (value??"").Trim();
        }
        private static RegistryKey ReadKey(RegistryKey root,string name,ref bool partial)
        {try{return root.OpenSubKey(name);}catch(UnauthorizedAccessException){partial=true;return null;}catch(SecurityException){partial=true;return null;}}
        internal static SoftwareCheck Check(string id, bool allowAnyDesk32, bool machineOnly=false)
        {
            var app=SoftwareCatalog.Find(id); var valid=new List<SoftwareCheck>(); bool registered=false,partial=false;
            foreach (RegistryHive hive in machineOnly ? new[] { RegistryHive.LocalMachine } : new[] { RegistryHive.LocalMachine,RegistryHive.CurrentUser })
            foreach (RegistryView view in LibraryDetection.Views())
            using (var baseKey=RegistryKey.OpenBaseKey(hive,view)) using (var root=baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
            {
                if (root==null) continue;
                foreach (string name in root.GetSubKeyNames())
                {
                    using (var key=ReadKey(root,name,ref partial))
                    {
                        if (key==null || !Matches(id,Convert.ToString(key.GetValue("DisplayName")))) continue; registered=true;
                        string version=NormalizeVersion(id,Convert.ToString(key.GetValue("DisplayVersion"))), location=Convert.ToString(key.GetValue("InstallLocation"));
                        var candidates=new List<string>();
                        location=NormalizePath(location); if (location!=null) candidates.Add(Path.GetFileName(location).Equals(app.Exe,StringComparison.OrdinalIgnoreCase) ? location : Path.Combine(location,app.Exe));
                        string icon=NormalizePath(IconPath(Convert.ToString(key.GetValue("DisplayIcon")))); if (icon!=null && Path.GetFileName(icon).Equals(app.Exe,StringComparison.OrdinalIgnoreCase)) candidates.Add(icon);
                        string uninstall=Convert.ToString(key.GetValue("UninstallString"));
                        string uninstaller=uninstall.StartsWith("\"") ? uninstall.Split('"').ElementAtOrDefault(1) : Regex.Match(uninstall,@"^(.+?\.exe)(?:\s|$)",RegexOptions.IgnoreCase).Groups[1].Value;
                        uninstaller=NormalizePath(uninstaller);
                        if (!String.IsNullOrWhiteSpace(uninstaller)) candidates.Add(Path.Combine(Path.GetDirectoryName(uninstaller),app.Exe));
                        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),app.Folder,app.Exe));
                        if (id=="anydesk") candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),app.Folder,app.Exe));
                        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            if (!Path.IsPathRooted(candidate) || !File.Exists(candidate)) continue;
                            try
                            {
                                ToolsDeployment.SafePath(candidate); string arch=SoftwareCatalog.Architecture(candidate);
                                if (arch!="x64" && !(allowAnyDesk32 && id=="anydesk" && arch=="x86")) continue;
                                var meta=FileVersionInfo.GetVersionInfo(candidate); string expected=app.Name;
                                if ((meta.ProductName ?? "").IndexOf(expected,StringComparison.OrdinalIgnoreCase)<0) continue;
                                Version parsed; if (!Version.TryParse(version,out parsed)) continue;
                                string fileVersion=String.Format("{0}.{1}.{2}.{3}",meta.FileMajorPart,meta.FileMinorPart,meta.FileBuildPart,meta.FilePrivatePart);
                                // RuDesktop's FileVersion includes a literal @BUILD/private build
                                // number. ProductVersion is its MSI/ARP version domain.
                                string comparable=id=="rudesktop" ? meta.ProductVersion : fileVersion;
                                Version comparableVersion; if(!Version.TryParse(comparable,out comparableVersion) || SoftwareCatalog.Compare(comparable,version)!=0)continue;
                                valid.Add(new SoftwareCheck { Id=id,Version=version,Executable=candidate,State=LibraryState.Installed,Detail="Регистрация и EXE подтверждены, разрядность "+arch });
                            }
                            catch (IOException) { } catch (UnauthorizedAccessException) { }
                        }
                    }
                }
            }
            if (valid.Count>0) return valid.Aggregate((a,b)=>SoftwareCatalog.Compare(a.Version,b.Version)>=0 ? a : b);
            return new SoftwareCheck { Id=id,Version="",Executable="",State=registered||partial ? LibraryState.Unknown : LibraryState.Missing,Detail=!registered&&partial ? "Часть реестра недоступна; нужна проверка с правами администратора" : registered ? "Регистрация есть; версия, файл или нужная разрядность не подтверждены" : "Не установлено" };
        }
        internal static string Query(string mode)
        {
            if(mode!="x64-only" && mode!="allow-anydesk32")throw new InvalidDataException("Неверный режим проверки");
            var doc=new XmlDocument();var root=doc.CreateElement("programs");doc.AppendChild(root);
            foreach(var app in SoftwareCatalog.Apps){var check=Check(app.Id,mode=="allow-anydesk32");var node=doc.CreateElement("app");node.SetAttribute("id",check.Id);node.SetAttribute("version",check.Version);node.SetAttribute("exe",check.Executable);node.SetAttribute("state",check.State.ToString());node.SetAttribute("detail",check.Detail);root.AppendChild(node);}return doc.OuterXml;
        }
        internal static SoftwareCheck[] ParseQuery(string xml)
        {
            var doc=new XmlDocument{XmlResolver=null};using(var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=12000}))doc.Load(reader);
            if(doc.DocumentElement==null||doc.DocumentElement.Name!="programs")throw new InvalidDataException("Неверный ответ проверки");
            var result=new List<SoftwareCheck>();foreach(XmlNode node in doc.DocumentElement.ChildNodes){string id=node.Attributes["id"].Value;SoftwareCatalog.Find(id);LibraryState state;if(!Enum.TryParse<LibraryState>(node.Attributes["state"].Value,out state)||!Enum.IsDefined(typeof(LibraryState),state))throw new InvalidDataException("Неверное состояние");result.Add(new SoftwareCheck{Id=id,Version=node.Attributes["version"].Value,Executable=node.Attributes["exe"].Value,State=state,Detail=node.Attributes["detail"].Value});}
            if(result.Count!=SoftwareCatalog.Apps.Length||result.Select(v=>v.Id).Distinct().Count()!=SoftwareCatalog.Apps.Length)throw new InvalidDataException("Неполный ответ проверки");return result.ToArray();
        }
        internal static bool ShouldInstall(SoftwareCheck installed, SoftwarePackage package)
        {
            if (installed.State==LibraryState.Unknown) throw new InvalidOperationException("Перед установкой нужна проверка: "+installed.Detail);
            return installed.State!=LibraryState.Installed || SoftwareCatalog.Compare(installed.Version,package.Version)<0;
        }
    }
}
