using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Xml;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ServiceTool
{
    internal sealed class SoftwareInstallRequest
    {
        public string Id, Sha256, Version, Password;
        public bool AllowAnyDesk32, ConfigureRemote;
        internal string Serialize()
        {
            var doc=new XmlDocument(); var root=doc.CreateElement("software"); doc.AppendChild(root);
            foreach (var pair in new[] {new[]{"id",Id},new[]{"hash",Sha256},new[]{"version",Version},new[]{"allow32",AllowAnyDesk32.ToString()},new[]{"remote",ConfigureRemote.ToString()},new[]{"password",Password??""}}) root.SetAttribute(pair[0],pair[1]);
            return doc.OuterXml;
        }
        internal static SoftwareInstallRequest Parse(string xml)
        {
            var doc=new XmlDocument { XmlResolver=null };
            using (var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=12000 })) doc.Load(reader);
            var root=doc.DocumentElement; if (root==null || root.Name!="software" || root.ChildNodes.Count!=0) throw new InvalidDataException("Некорректный запрос установки");
            bool allow,remote; if (!Boolean.TryParse(root.GetAttribute("allow32"),out allow) || !Boolean.TryParse(root.GetAttribute("remote"),out remote)) throw new InvalidDataException("Некорректные параметры установки");
            var request=new SoftwareInstallRequest {Id=root.GetAttribute("id"),Sha256=root.GetAttribute("hash"),Version=root.GetAttribute("version"),Password=root.GetAttribute("password"),AllowAnyDesk32=allow,ConfigureRemote=remote};
            var app=SoftwareCatalog.Find(request.Id);
            if (remote && !app.Remote) throw new InvalidDataException("Эта программа не поддерживает удалённый доступ");
            if (remote && (request.Password.Length<8 || request.Password.Length>128 || request.Password.Any(c=>Char.IsControl(c)))) throw new InvalidDataException("Пароль должен содержать от 8 до 128 символов без переводов строки");
            return request;
        }
    }
    internal static class SoftwareInstaller
    {
        internal static ProcessStartInfo Command(string id, string file, string log, string existingDirectory=null)
        {
            string directory=existingDirectory ?? Path.Combine(Environment.GetFolderPath(id=="anydesk" ? Environment.SpecialFolder.ProgramFilesX86 : Environment.SpecialFolder.ProgramFiles),SoftwareCatalog.Find(id).Folder);
            var start=new ProcessStartInfo { UseShellExecute=false, CreateNoWindow=true, WorkingDirectory=Path.GetDirectoryName(file) };
            if (id=="rudesktop") { start.FileName=Path.Combine(Environment.SystemDirectory,"msiexec.exe"); start.Arguments="/i "+LibraryPlan.Quote(file)+" /qn /norestart REBOOT=ReallySuppress /L*v "+LibraryPlan.Quote(log); }
            else { start.FileName=file; start.Arguments=id=="anydesk" ? "--install "+LibraryPlan.Quote(directory)+" --silent --start-with-win --create-desktop-icon --update-manually" : "/S"; }
            return start;
        }
        private static bool AnyDeskPrinter()
        {
            using (var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers"))
                return key!=null && key.GetSubKeyNames().Any(n=>n.IndexOf("AnyDesk",StringComparison.OrdinalIgnoreCase)>=0);
        }
        // Documented by AnyDesk: "echo <password> | anydesk.exe --set-password <profile>".
        // The password always travels through standard input; only the profile name is an argument.
        // _unattended_access is the built-in Unattended Access profile (AnyDesk 7+).
        internal const string AnyDeskProfile = "_unattended_access";
        // A second AnyDesk call must not reuse the same unattended password verbatim, so the
        // value itself is never placed on a command line for either vendor.
        internal static LibraryInstallResult Install(string xml, string source, Action<string> progress)
        {
            var request=SoftwareInstallRequest.Parse(xml); var app=SoftwareCatalog.Find(request.Id);
            if (!Path.IsPathRooted(source)) throw new InvalidDataException("Нужен полный путь установщика"); ToolsDeployment.SafePath(source);
            SoftwarePackage package=SoftwareCatalog.Describe(request.Id,source,"Выбранный пакет"); SoftwareCatalog.Validate(package,request.AllowAnyDesk32);
            if (!String.Equals(package.Sha256,request.Sha256,StringComparison.OrdinalIgnoreCase) || SoftwareCatalog.Compare(package.Version,request.Version)!=0) throw new InvalidDataException("Пакет изменился после подтверждения");
            SoftwareCheck before=SoftwareDetection.Check(package.Id,request.AllowAnyDesk32);
            bool install=SoftwareDetection.ShouldInstall(before,package);
            if (install && !app.Remote)
            {
                string[] names=package.Id=="7zip" ? new[]{"7zFM","7z"} : new[]{"notepad++"};
                foreach (string name in names) foreach (var process in Process.GetProcessesByName(name)) using(process) if (!process.HasExited) throw new InvalidOperationException("Закрой "+app.Name+" перед обновлением. Приложение принудительно не закрывалось");
            }
            if (!install && !request.ConfigureRemote) return new LibraryInstallResult { ExitCode=0,State=LibraryState.Installed,Message="Уже установлена такая же или более новая версия. Установщик пропущен" };
            bool printerBefore=package.Id=="anydesk" && AnyDeskPrinter();
            string stage=Path.Combine(Path.GetTempPath(),"BARS.Software."+Guid.NewGuid().ToString("N"));
            var security=new DirectorySecurity(); security.SetAccessRuleProtection(true,false); var user=WindowsIdentity.GetCurrent().User; security.SetOwner(user);
            foreach (var sid in new[]{user,new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)}) security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            Directory.CreateDirectory(stage,security); string verified=Path.Combine(stage,Path.GetFileName(source));
            string reports=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"BARS\Manager\SoftwareLogs"); Directory.CreateDirectory(reports);
            string log=Path.Combine(reports,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+package.Id+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".log");
            try
            {
                int code=0;
                if (install)
                {
                    progress("Проверяем пакет и создаём защищённую копию");
                    using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read)) using(var output=new FileStream(verified,FileMode.CreateNew,FileAccess.Write,FileShare.None)) LibraryCancellation.Copy(input,output);
                    package.File=verified;
                    using (var locked=new FileStream(verified,FileMode.Open,FileAccess.Read,FileShare.Read))
                    {
                        SoftwareTrust.Verify(package,true); LibraryCancellation.Check(); progress("Устанавливаем "+app.Name);
                        code=LibraryProcess.Run(Command(package.Id,verified,log,before.State==LibraryState.Installed ? Path.GetDirectoryName(before.Executable) : null),progress);
                    }
                }
                SoftwareCheck after=SoftwareDetection.Check(package.Id,request.AllowAnyDesk32);
                if (!LibraryPlan.Success(code) || after.State!=LibraryState.Installed || SoftwareCatalog.Compare(after.Version,package.Version)<0)
                    return new LibraryInstallResult {ExitCode=code,State=LibraryState.Unknown,Message=LibraryPlan.NeedsReboot(code) ? "Нужна перезагрузка; результат нужно проверить после неё" : "Установка не подтверждена. Код "+code+". "+after.Detail,LogPath=File.Exists(log)?log:""};
                string note = "";
                // The vendor client has no command-line switch for the printer: AnyDesk installs the
                // AnyDesk Printer only when that box is ticked in the installer UI, which silent
                // mode never does. It is still verified, because a custom client can preset it.
                if (package.Id=="anydesk" && !printerBefore && AnyDeskPrinter()) note += " Внимание: появился виртуальный принтер AnyDesk. Удаление принтера автоматически не выполнялось.";
                if (request.ConfigureRemote)
                {
                    SoftwareTrust.Signature(after.Executable,app.Publisher); LibraryCancellation.Check(); progress("Настраиваем неконтролируемый доступ "+app.Name);
                    if (package.Id=="anydesk") ConfigureAnyDesk(after.Executable,request.Password);
                    else ConfigureRuDesktop(after.Executable,request.Password);
                    note += package.Id=="anydesk"
                        ? " Пароль неконтролируемого доступа задан через стандартный ввод AnyDesk; подключение проверь с другого компьютера."
                        : " RuDesktop принимает пароль только аргументом процесса; во время настройки он был виден локальному администратору. Подключение проверь с другого компьютера.";
                }
                return new LibraryInstallResult {ExitCode=code,State=LibraryState.Installed,Message="Установка "+after.Version+" подтверждена."+note,LogPath=File.Exists(log)?log:""};
            }
            finally { try {if(File.Exists(verified)) File.Delete(verified);Directory.Delete(stage,false);} catch(IOException){} catch(UnauthorizedAccessException){} }
        }
        private static void ConfigureAnyDesk(string exe,string password)
        {
            // AnyDesk 7+ takes the profile as the argument and the password on stdin. On older
            // clients every argument after --set-password is ignored as a profile selector, so the
            // plain documented form is used as a fallback instead of guessing a version number.
            try { Configure(exe,"--set-password "+AnyDeskProfile,password); }
            catch(InvalidOperationException) { Configure(exe,"--set-password",password); }
        }
        private static void ConfigureRuDesktop(string exe,string password)
        {
            // RuDesktop documents only the command-line argument form (rudesktop --password <p>).
            // The secret is quoted and never written to a journal or a log file.
            Configure(exe,"--password "+LibraryPlan.Quote(password),null);
            Configure(exe,"--set-option verification-method use-permanent-password",null);
        }
        private static void Configure(string exe,string arguments,string input)
        {
            var start=new ProcessStartInfo(exe,arguments) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            // Output is deliberately never logged: third-party clients may echo a secret.
            try
            {
                using(var process=Process.Start(start))
                {
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    if(input!=null) process.StandardInput.WriteLine(input); process.StandardInput.Close();
                    var watch=Stopwatch.StartNew();
                    while(!process.WaitForExit(100))
                    {
                        if(LibraryCancellation.Requested || watch.ElapsedMilliseconds>30000) {try{process.Kill();}catch(InvalidOperationException){} throw new InvalidOperationException("Настройка доступа прервана или превысила время ожидания. Проверь её вручную");}
                    }
                    try{process.CancelOutputRead();process.CancelErrorRead();}catch(InvalidOperationException){}
                    if(process.ExitCode!=0) throw new InvalidOperationException("Клиент не подтвердил настройку доступа. Код "+process.ExitCode);
                }
            }
            catch(InvalidOperationException){throw;}
            catch(Exception){throw new InvalidOperationException("Не удалось выполнить настройку доступа. Пароль в журнал не записывался");}
        }
    }
    internal static class SoftwarePasswordStore
    {
        internal static void Save(string id,string password)
        {
            SoftwareCatalog.Find(id); Directory.CreateDirectory(ApplicationStorage.DirectoryPath); byte[] clear=Encoding.UTF8.GetBytes(password);
            try { File.WriteAllBytes(ApplicationStorage.FilePath("software-password-"+id+".bin"),ProtectedData.Protect(clear,null,DataProtectionScope.CurrentUser)); } finally{Array.Clear(clear,0,clear.Length);}
        }
        internal static void Forget(string id) {SoftwareCatalog.Find(id);string path=ApplicationStorage.FilePath("software-password-"+id+".bin");if(File.Exists(path))File.Delete(path);}
        internal static string Load(string id)
        {
            SoftwareCatalog.Find(id); string path=ApplicationStorage.FilePath("software-password-"+id+".bin"); if(!File.Exists(path)) return "";
            byte[] clear=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);try{return Encoding.UTF8.GetString(clear);}finally{Array.Clear(clear,0,clear.Length);}
        }
    }
}
