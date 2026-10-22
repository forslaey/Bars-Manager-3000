using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ServiceTool
{
    internal static class SoftwareSources
    {
        internal static string CacheFolder { get { return Path.Combine(ApplicationStorage.DirectoryPath,"SoftwareCache"); } }
        internal static bool Allowed(string id, Uri uri)
        {
            if (uri == null || uri.Scheme != "https" || !uri.IsDefaultPort || !String.IsNullOrEmpty(uri.UserInfo)) return false;
            string host = uri.DnsSafeHost.ToLowerInvariant();
            if(id=="chrome")return host=="versionhistory.googleapis.com" && uri.AbsolutePath.StartsWith("/v1/chrome/platforms/win64/channels/stable/",StringComparison.Ordinal) || host=="dl.google.com" && uri.AbsolutePath.Equals("/dl/chrome/install/googlechromestandaloneenterprise64.msi",StringComparison.OrdinalIgnoreCase);
            if (id == "notepad" || id == "7zip")
            {
                string repo = id == "notepad" ? "notepad-plus-plus/notepad-plus-plus" : "ip7z/7zip";
                return host == "api.github.com" && uri.AbsolutePath.StartsWith("/repos/"+repo+"/releases/",StringComparison.Ordinal) ||
                    host == "github.com" && uri.AbsolutePath.StartsWith("/"+repo+"/releases/download/",StringComparison.Ordinal) ||
                    host == "release-assets.githubusercontent.com" || host == "objects.githubusercontent.com";
            }
            return id == "rudesktop" ? host == "rudesktop.ru" || host == "storage.rudesktop.ru" : id == "anydesk" && (host == "download.anydesk.com" || host == "anydesk.com");
        }
        private static HttpWebResponse Open(string id, string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            for (int i=0;i<6;i++)
            {
                LibraryCancellation.Check(); Uri uri;
                if (!Uri.TryCreate(url,UriKind.Absolute,out uri) || !Allowed(id,uri)) throw new InvalidDataException("Источник или перенаправление не входит в официальный список");
                var request = (HttpWebRequest)WebRequest.Create(uri); request.UserAgent="BARS-Manager-3000"; request.AllowAutoRedirect=false;
                request.Timeout=10000; request.ReadWriteTimeout=10000; request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
                var response = (HttpWebResponse)request.GetResponse();
                int code = (int)response.StatusCode;
                if (code >= 300 && code <= 399) { string location=response.Headers["Location"]; response.Dispose(); url=new Uri(uri,location).AbsoluteUri; continue; }
                if (code != 200) { response.Dispose(); throw new IOException("Официальный сервер вернул HTTP " + code); }
                return response;
            }
            throw new IOException("Слишком много перенаправлений");
        }
        internal static string Text(string id, string url)
        {
            using (var response=Open(id,url))
            {
                string content=(response.ContentType??"").Split(';')[0].Trim();
                if(!content.StartsWith("text/",StringComparison.OrdinalIgnoreCase)&&!content.Equals("application/json",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Вместо сведений о версии сервер вернул другой тип файла");
                if(response.ContentLength>4*1024*1024)throw new InvalidDataException("Ответ метаданных слишком большой");
                using(var input=response.GetResponseStream()) using(var output=new MemoryStream())
                {
                var buffer=new byte[16384]; int n;
                while ((n=input.Read(buffer,0,buffer.Length))>0) { LibraryCancellation.Check(); if (output.Length+n>4*1024*1024) throw new InvalidDataException("Ответ метаданных слишком большой"); output.Write(buffer,0,n); }
                return Encoding.UTF8.GetString(output.ToArray());
                }
            }
        }
        internal static SoftwarePackage ParseAnyDesk(string text)
        {
            var matches=Regex.Matches(text??"",@"(?m)^\s*\d{2}\.\d{2}\.\d{4}\s+-\s+(\d+(?:\.\d+){1,3})\s+\(Windows\)[ \t]*\r?$",RegexOptions.IgnoreCase);
            if(matches.Count==0)throw new InvalidDataException("В официальном списке выпусков не найдена стабильная версия AnyDesk для Windows");
            string version=matches[0].Groups[1].Value;
            foreach(Match match in matches)if(SoftwareCatalog.Compare(match.Groups[1].Value,version)>0)version=match.Groups[1].Value;
            return new SoftwarePackage{Id="anydesk",Architecture="x86",Version=version,File="AnyDesk.exe",Url="https://download.anydesk.com/AnyDesk.exe",Source="Официальный AnyDesk для Windows",Sha256=""};
        }
        internal static SoftwarePackage ParseGitHub(string id, string json)
        {
            if (id != "7zip" && id != "notepad") throw new InvalidDataException("Неизвестный репозиторий");
            var parser=new JavaScriptSerializer { MaxJsonLength=4*1024*1024, RecursionLimit=32 };
            var document=parser.DeserializeObject(json) as Dictionary<string,object>;
            if (document==null || !document.ContainsKey("prerelease") || !document.ContainsKey("draft") || Convert.ToBoolean(document["prerelease"]) || Convert.ToBoolean(document["draft"])) throw new InvalidDataException("Не найден стабильный релиз");
            var assets=document["assets"] as object[]; if (assets==null) throw new InvalidDataException("В релизе нет файлов");
            var found=new List<SoftwarePackage>();
            foreach (var raw in assets)
            {
                var asset=raw as Dictionary<string,object>; if (asset==null) continue;
                string name=Convert.ToString(asset["name"]); Match match=Regex.Match(name,id=="notepad" ? @"^npp\.(\d+(?:\.\d+){1,3})\.Installer\.x64\.exe$" : @"^7z(\d{2})(\d{2})-x64\.exe$",RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                string url=Convert.ToString(asset["browser_download_url"]), prefix="https://github.com/"+(id=="notepad" ? "notepad-plus-plus/notepad-plus-plus" : "ip7z/7zip")+"/releases/download/";
                if (!url.StartsWith(prefix,StringComparison.Ordinal) || new Uri(url).IsDefaultPort==false || !String.IsNullOrEmpty(new Uri(url).UserInfo)) throw new InvalidDataException("Неверный адрес файла релиза");
                string digest=asset.ContainsKey("digest") ? Convert.ToString(asset["digest"]) : "";
                if (!Regex.IsMatch(digest,@"^sha256:[0-9a-fA-F]{64}$")) throw new InvalidDataException("GitHub не предоставил SHA-256 релиза");
                found.Add(new SoftwarePackage { Id=id, Version=id=="notepad" ? match.Groups[1].Value : Int32.Parse(match.Groups[1].Value)+"."+match.Groups[2].Value,
                    Architecture="x64", File=name, Url=url, Sha256=digest.Substring(7), Source="Официальный релиз на GitHub" });
            }
            if (found.Count!=1) throw new InvalidDataException("Не найден единственный установщик x64"); return found[0];
        }
        internal static SoftwarePackage GitHub(string id)
        { return ParseGitHub(id,Text(id,"https://api.github.com/repos/"+(id=="notepad" ? "notepad-plus-plus/notepad-plus-plus" : "ip7z/7zip")+"/releases/latest")); }
        internal static SoftwarePackage GitHubVersion(string version)
        {
            Version parsed; if(!Version.TryParse(version,out parsed)) throw new InvalidDataException("Неверная версия 7-Zip");
            return ParseGitHub("7zip",Text("7zip","https://api.github.com/repos/ip7z/7zip/releases/tags/"+Uri.EscapeDataString(version)));
        }
        internal static SoftwarePackage ParseRuDesktop(string html)
        {
            var matches=Regex.Matches(html,@"https://storage\.rudesktop\.ru/download/rudesktop-(\d+(?:\.\d+){1,3})-x64\.msi",RegexOptions.IgnoreCase);
            if (matches.Count==0) throw new InvalidDataException("На официальной странице не найден MSI x64");
            Match selected=matches[0]; foreach (Match match in matches) if (SoftwareCatalog.Compare(match.Groups[1].Value,selected.Groups[1].Value)>0) selected=match;
            return new SoftwarePackage { Id="rudesktop", Version=selected.Groups[1].Value, Architecture="x64", File=Path.GetFileName(new Uri(selected.Value).AbsolutePath), Url=selected.Value, Source="Официальный сайт RuDesktop", Sha256="" };
        }
        internal static SoftwarePackage ParseChrome(string json)
        {
            var doc=new JavaScriptSerializer{MaxJsonLength=4*1024*1024,RecursionLimit=24}.DeserializeObject(json) as Dictionary<string,object>;
            object value; if(doc==null||!doc.TryGetValue("versions",out value))throw new InvalidDataException("Google не вернул версии Chrome");
            var versions=value as object[]; if(versions==null||versions.Length==0)throw new InvalidDataException("Не найдена стабильная версия Chrome");
            string selected=null;
            foreach(var item in versions){var entry=item as Dictionary<string,object>;object raw;Version v;if(entry==null||!entry.TryGetValue("version",out raw)||!Version.TryParse(Convert.ToString(raw),out v)||v.Revision<0)throw new InvalidDataException("Некорректная версия Chrome");string version=Convert.ToString(raw);if(selected==null||SoftwareCatalog.Compare(version,selected)>0)selected=version;}
            return new SoftwarePackage{Id="chrome",Version=selected,Architecture="x64",File="googlechromestandaloneenterprise64.msi",Url="https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi",Source="Google · официальный автономный установщик",Sha256=""};
        }
        internal static SoftwarePackage Latest(string id)
        {
            if(id=="chrome")return ParseChrome(Text(id,"https://versionhistory.googleapis.com/v1/chrome/platforms/win64/channels/stable/versions?pageSize=1"));
            if (id=="notepad" || id=="7zip") return GitHub(id);
            if (id=="rudesktop") return ParseRuDesktop(Text(id,"https://rudesktop.ru/downloads/"));
            if (id=="anydesk") return ParseAnyDesk(Text(id,"https://download.anydesk.com/changelog.txt"));
            throw new InvalidDataException("Неизвестный источник");
        }
        internal static SoftwarePackage Download(SoftwarePackage remote, string cache, Action<string> progress, Func<bool> cancelled)
        {
            SoftwareCatalog.Find(remote.Id); string folder=Path.Combine(cache,remote.Id); Directory.CreateDirectory(folder); ToolsDeployment.SafePath(folder);
            string name=Path.GetFileName(remote.File); if (name!=remote.File || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0) throw new InvalidDataException("Неверное имя установщика");
            string final=Path.Combine(folder,name), temporary=Path.Combine(folder,Guid.NewGuid().ToString("N")+".part");
            try
            {
                using (var response=Open(remote.Id,remote.Url)) using (var input=response.GetResponseStream()) using (var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    if (response.ContentLength>300L*1024*1024) throw new InvalidDataException("Установщик слишком большой");
                    var buffer=new byte[65536]; int n; long total=0,last=-1;
                    while ((n=input.Read(buffer,0,buffer.Length))>0)
                    {
                        LibraryCancellation.Check(cancelled); total+=n; if (total>300L*1024*1024) throw new InvalidDataException("Установщик слишком большой"); output.Write(buffer,0,n);
                        if (total/1048576!=last) { last=total/1048576; progress("Скачано "+last+" МБ"+(response.ContentLength>0 ? " из "+response.ContentLength/1048576 : "")); }
                    }
                    if (response.ContentLength>=0 && total!=response.ContentLength) throw new IOException("Загрузка не завершена"); output.Flush(true);
                }
                LibraryCancellation.Check(cancelled);
                // Metadata readers use the vendor filename (NSIS version/architecture convention).
                string validation=Path.Combine(folder,"verify-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(validation);
                SoftwarePackage package;
                string staged=Path.Combine(validation,name);
                try
                {
                    File.Move(temporary,staged); package=SoftwareCatalog.Describe(remote.Id,staged,remote.Source); package.Url=remote.Url;
                    if (!String.IsNullOrEmpty(remote.Sha256) && !String.Equals(package.Sha256,remote.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 скачанного файла не совпал с релизом");
                    if (remote.Version!="0.0" && SoftwareCatalog.Compare(remote.Version,package.Version)!=0) throw new InvalidDataException("Версия файла не совпала с объявленной");
                    SoftwareTrust.Verify(package,true);
                    ToolsDeployment.SafePath(final); if (File.Exists(final)) File.Replace(staged,final,null); else File.Move(staged,final); package.File=final;
                }
                finally { if (File.Exists(staged)) File.Delete(staged); Directory.Delete(validation,false); }
                return package;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static SoftwarePackage Best(IEnumerable<SoftwarePackage> packages, string id)
        { SoftwarePackage best=null; foreach (var package in packages.Where(p=>p.Id==id)) if (best==null || SoftwareCatalog.Compare(package.Version,best.Version)>0) best=package; return best; }
    }
}
