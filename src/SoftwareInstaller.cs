using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
            if (!install) return new LibraryInstallResult { ExitCode=0,State=LibraryState.Installed,Message="Уже установлена такая же или более новая версия. Установщик пропущен" };
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
                    ConfigureRemote(package.Id,request.Password,(arguments,input)=>Configure(after.Executable,arguments,input));
                    note += package.Id=="anydesk"
                        ? " Пароль неконтролируемого доступа задан через стандартный ввод AnyDesk; подключение проверь с другого компьютера."
                        : " RuDesktop принимает пароль только аргументом процесса; во время настройки он был виден локальному администратору. Подключение проверь с другого компьютера.";
                }
                return new LibraryInstallResult {ExitCode=code,State=LibraryState.Installed,Message="Установка "+after.Version+" подтверждена."+note,LogPath=File.Exists(log)?log:""};
            }
            finally { try {if(File.Exists(verified)) File.Delete(verified);Directory.Delete(stage,false);} catch(IOException){} catch(UnauthorizedAccessException){} }
        }
        internal static void ConfigureRemote(string id,string password,Action<string,string> execute)
        {
            if(id!="anydesk" && id!="rudesktop") throw new InvalidDataException("Неизвестный клиент удалённого доступа");
            LibraryCancellation.Check();
            if(id=="anydesk") execute("--set-password "+AnyDeskProfile,password);
            else
            {
                execute("--password "+LibraryPlan.Quote(password),null);
                LibraryCancellation.Check();
                execute("--set-option verification-method use-permanent-password",null);
            }
            LibraryCancellation.Check();
        }
        internal static void Configure(string exe,string arguments,string input)
        {
            LibraryCancellation.Check();
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
                        if(LibraryCancellation.Requested || watch.ElapsedMilliseconds>30000) {try{process.Kill();}catch(InvalidOperationException){} LibraryCancellation.Check(); throw new TimeoutException("Настройка доступа превысила время ожидания. Проверь её вручную");}
                    }
                    try{process.CancelOutputRead();process.CancelErrorRead();}catch(InvalidOperationException){}
                    if(process.ExitCode!=0) throw new InvalidOperationException("Клиент не подтвердил настройку доступа. Код "+process.ExitCode);
                }
            }
            catch(OperationCanceledException){throw;}
            catch(TimeoutException){throw;}
            catch(InvalidOperationException){throw;}
            catch(Exception){throw new InvalidOperationException("Не удалось выполнить настройку доступа. Пароль в журнал не записывался");}
        }
    }
    internal static class SoftwareMaintenance
    {
        internal static bool CanInstall(SoftwareCheck check,SoftwarePackage package)
        {return check!=null&&package!=null&&(check.State==LibraryState.Missing||check.State==LibraryState.Installed&&package.Version!="0.0"&&SoftwareCatalog.Compare(package.Version,check.Version)>0);}
        internal static ProcessStartInfo RemovalCommand(string id,string exe,string code,string log)
        {
            SoftwareCatalog.Find(id);
            if(id=="rudesktop")
            {
                Guid product;if(!Guid.TryParse(code,out product))throw new InvalidDataException("Не найден код установленного RuDesktop");
                return new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe"),"/x "+LibraryPlan.Quote(product.ToString("B"))+" /qn /norestart REBOOT=ReallySuppress /L*v "+LibraryPlan.Quote(log)){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Environment.SystemDirectory};
            }
            return new ProcessStartInfo(id=="anydesk"?exe:Path.Combine(Path.GetDirectoryName(exe),"uninstall.exe"),id=="anydesk"?"--remove":"/S"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(exe)};
        }
        private static List<LibraryRegisteredProduct> Registrations(string id)
        {
            var result=new List<LibraryRegisteredProduct>();
            foreach(var view in LibraryDetection.Views())using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view))using(var root=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
            {
                if(root==null)continue;
                foreach(string name in root.GetSubKeyNames())
                {
                    RegistryKey key;try{key=root.OpenSubKey(name);}catch(UnauthorizedAccessException){continue;}catch(System.Security.SecurityException){continue;}
                    using(key)
                    {
                        if(key==null)continue;string display=Convert.ToString(key.GetValue("DisplayName"));if(!SoftwareDetection.Matches(id,display))continue;
                        result.Add(new LibraryRegisteredProduct{Key=name,Name=display,Version=SoftwareDetection.NormalizeVersion(id,Convert.ToString(key.GetValue("DisplayVersion"))),Msi=Convert.ToInt32(key.GetValue("WindowsInstaller",0))==1,Uninstall=Convert.ToString(key.GetValue("UninstallString")),QuietUninstall=Convert.ToString(key.GetValue("QuietUninstallString"))});
                    }
                }
            }
            return result.GroupBy(v=>v.Key,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
        }
        internal static LibraryInstallResult ConfigureAccess(string xml,Action<string> progress)
        {
            var request=SoftwareInstallRequest.Parse(xml);if(!request.ConfigureRemote)throw new InvalidDataException("Настройка доступа не подтверждена");
            var check=SoftwareDetection.Check(request.Id,true,true);if(check.State!=LibraryState.Installed)throw new IOException("Установленная программа не подтверждена");
            SoftwareTrust.Signature(check.Executable,SoftwareCatalog.Find(request.Id).Publisher);progress("Настраиваем доступ "+SoftwareCatalog.Find(request.Id).Name);
            SoftwareInstaller.ConfigureRemote(request.Id,request.Password,(args,input)=>SoftwareInstaller.Configure(check.Executable,args,input));
            return new LibraryInstallResult{ExitCode=0,State=LibraryState.Installed,Message="Команды настройки постоянного пароля выполнены. Проверь подключение с другого компьютера"};
        }
        internal static LibraryInstallResult Remove(string id,Action<string> progress)
        {
            var app=SoftwareCatalog.Find(id);var check=SoftwareDetection.Check(id,true,true);
            if(check.State!=LibraryState.Installed)throw new IOException("Установленная программа не подтверждена; удаление не запускалось");
            var records=Registrations(id).Where(v=>{Version parsed;return Version.TryParse(v.Version,out parsed)&&SoftwareCatalog.Compare(v.Version,check.Version)==0;}).ToArray();
            if(records.Length!=1)throw new IOException("Неоднозначная регистрация удаления; нужна ручная проверка");var record=records[0];
            string directory=Path.GetDirectoryName(check.Executable);ToolsDeployment.SafePath(directory);
            if(id!="anydesk"&&id!="rudesktop")foreach(string name in id=="notepad"?new[]{"notepad++"}:new[]{"7zFM","7z"})foreach(var process in Process.GetProcessesByName(name))using(process)if(!process.HasExited)throw new IOException("Закрой "+app.Name+" перед удалением");
            string logs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"BARS\Manager\SoftwareLogs");Directory.CreateDirectory(logs);string log=Path.Combine(logs,id+"-remove-"+Guid.NewGuid().ToString("N")+".log");
            var start=RemovalCommand(id,check.Executable,record.Msi?record.Key:null,log);
            if(id=="rudesktop"&&!LibraryMaintenance.ExactMsiInstalled(record.Key))throw new IOException("Регистрация Windows Installer не подтверждена; удаление не запускалось");
            if(id=="notepad"||id=="7zip")
            {
                var registered=LibraryMaintenance.Command(record,log);
                if(!Path.GetFullPath(registered.FileName).Equals(Path.GetFullPath(start.FileName),StringComparison.OrdinalIgnoreCase))throw new IOException("Деинсталлятор не соответствует установленной программе");
            }
            if(id!="7zip")SoftwareTrust.Signature(check.Executable,app.Publisher);
            if(id!="rudesktop"){ToolsDeployment.SafePath(start.FileName);if(!File.Exists(start.FileName))throw new FileNotFoundException("Деинсталлятор не найден");if(id=="notepad")SoftwareTrust.Signature(start.FileName,app.Publisher);}
            progress("Удаляем "+app.Name);int code=RunRemoval(start,progress,id=="anydesk"?(Func<bool>)(()=>
            {
                if(File.Exists(check.Executable)||Registrations(id).Count>0)return false;
                using(var service=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\AnyDesk"))return service==null;
            }):null);
            if(!LibraryPlan.Success(code)||LibraryPlan.NeedsReboot(code))return new LibraryInstallResult{ExitCode=code,State=LibraryState.Unknown,Message="Удаление требует проверки"+(LibraryPlan.NeedsReboot(code)?" после перезагрузки":". Код "+code),LogPath=File.Exists(log)?log:""};
            for(int i=0;i<20&&Registrations(id).Count>0;i++){LibraryCancellation.Check();Thread.Sleep(150);}
            if(Registrations(id).Count>0)throw new IOException("Программа ещё зарегистрирована. Настройки не очищались");
            foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(check.Executable)))using(process)
            {
                try{if(!process.HasExited&&String.Equals(process.MainModule.FileName,check.Executable,StringComparison.OrdinalIgnoreCase))throw new IOException("Процесс программы ещё работает. Настройки не очищались; проверь завершение удаления");}
                catch(System.ComponentModel.Win32Exception){throw new IOException("Не удалось проверить завершение процесса. Настройки не очищались");}
                catch(InvalidOperationException){}
            }
            // Vendor uninstall must complete before any settings are touched.
            foreach(string path in MachineSettings(id))DeleteSettings(path,Path.GetDirectoryName(path));
            if(id=="notepad"||id=="7zip")SoftwareDefaults.RemoveRegistration(id,progress);
            return new LibraryInstallResult{ExitCode=0,State=LibraryState.Missing,Message="Программа удалена, общие настройки очищены",LogPath=File.Exists(log)?log:""};
        }
        internal static string[] MachineSettings(string id)
        {
            SoftwareCatalog.Find(id);
            if(id=="anydesk")return new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"AnyDesk")};
            if(id=="rudesktop")return new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"ServiceProfiles\LocalService\AppData\Roaming\RuDesktop\config")};
            return new string[0];
        }
        // Some AnyDesk builds remove all installed state but leave their own --remove
        // process resident. End only our job, after absence stays confirmed for two seconds.
        internal static int RunRemoval(ProcessStartInfo start,Action<string> progress,Func<bool> removed)
        {
            if(removed==null)return LibraryProcess.Run(start,progress);
            bool confirmed=false;long absentSince=-1,lastProbe=-1000;var clock=Stopwatch.StartNew();
            Func<bool> stop=()=>
            {
                if(LibraryCancellation.Requested)return true;
                if(clock.ElapsedMilliseconds-lastProbe<500)return false;lastProbe=clock.ElapsedMilliseconds;
                bool absent=false;try{absent=removed();}catch(IOException){}catch(UnauthorizedAccessException){}catch(System.Security.SecurityException){}
                if(!absent){absentSince=-1;return false;}
                if(absentSince<0)absentSince=clock.ElapsedMilliseconds;
                if(clock.ElapsedMilliseconds-absentSince<2000)return false;
                confirmed=true;return true;
            };
            try{return LibraryProcess.Run(start,text=>{if(!confirmed)progress(text);},stop);}
            catch(OperationCanceledException)
            {
                if(!confirmed||LibraryCancellation.Requested)throw;
                progress("Удаление подтверждено. Оставшийся процесс деинсталлятора завершён");return 0;
            }
        }
        internal static void DeleteSettings(string path,string allowedParent)
        {
            string full=Path.GetFullPath(path),parent=Path.GetFullPath(allowedParent).TrimEnd('\\')+"\\";
            if(!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||full.TrimEnd('\\').Equals(parent.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new IOException("Путь очистки выходит за разрешённую папку");
            if(!Directory.Exists(full))return;ValidateTree(full);DeleteTree(full);
        }
        private static void ValidateTree(string path){ToolsDeployment.SafePath(path);foreach(string f in Directory.GetFiles(path))ToolsDeployment.SafePath(f);foreach(string d in Directory.GetDirectories(path))ValidateTree(d);}
        private static void DeleteTree(string path){ToolsDeployment.SafePath(path);foreach(string d in Directory.GetDirectories(path))DeleteTree(d);foreach(string f in Directory.GetFiles(path)){ToolsDeployment.SafePath(f);File.SetAttributes(f,FileAttributes.Normal);File.Delete(f);}File.SetAttributes(path,FileAttributes.Directory);Directory.Delete(path);}
        internal static void PreserveBackupDirectory(string profile,string store,Action<string> progress)
        {
            string backup=Path.Combine(profile,"backup");if(!Directory.Exists(backup))return;
            ToolsDeployment.SafePath(backup);ToolsDeployment.SafePath(store);Directory.CreateDirectory(store);
            string saved=Path.Combine(store,Guid.NewGuid().ToString("N"));Directory.Move(backup,saved);progress("Несохранённые резервные файлы Notepad++ сохранены: "+saved);
        }
        internal static void PreserveUserDocuments(string id,Action<string> progress)
        {
            SoftwareCatalog.Find(id);if(id!="notepad")return;
            foreach(var process in Process.GetProcessesByName("notepad++"))using(process)if(!process.HasExited)throw new IOException("Закрой Notepad++ перед удалением");
            string store=Path.Combine(ApplicationStorage.DirectoryPath,"UninstallBackups");
            foreach(string root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}.Distinct(StringComparer.OrdinalIgnoreCase))PreserveBackupDirectory(Path.Combine(root,"Notepad++"),store,progress);
        }
        internal static void CleanupUser(string id,Action<string> progress)
        {
            PreserveUserDocuments(id,progress);string roaming=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder=id=="notepad"?"Notepad++":id=="7zip"?"7-Zip":SoftwareCatalog.Find(id).Folder;
            foreach(string root in new[]{roaming,local}.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string path=Path.Combine(root,folder);
                if(id=="rudesktop")DeleteSettings(Path.Combine(path,"config"),path);else DeleteSettings(path,root);
            }
            if(id=="7zip")using(var user=Registry.CurrentUser.OpenSubKey("Software",true)){if(user!=null)user.DeleteSubKeyTree("7-Zip",false);}
            if(SoftwareCatalog.Find(id).Remote)SoftwarePasswordStore.Forget(id);
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
