using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ServiceTool
{
    internal sealed class SoftwareDefinition
    {
        public string Id, Name, Exe, Folder, Publisher;
        public bool Remote;
    }
    internal sealed class SoftwarePackage
    {
        public string Id, Version, Architecture, File, Sha256, Source, Url;
    }
    internal sealed class SoftwareCheck
    {
        public string Id, Version, Executable, Detail;
        public LibraryState State;
    }
    internal static class SoftwareCatalog
    {
        internal static readonly SoftwareDefinition[] Apps = {
            new SoftwareDefinition { Id="chrome", Name="Google Chrome", Exe="chrome.exe", Folder=@"Google\Chrome\Application", Publisher="Google LLC" },
            new SoftwareDefinition { Id="notepad", Name="Notepad++", Exe="notepad++.exe", Folder="Notepad++", Publisher="NOTEPAD++" },
            new SoftwareDefinition { Id="7zip", Name="7-Zip", Exe="7zFM.exe", Folder="7-Zip", Publisher="" },
            new SoftwareDefinition { Id="anydesk", Name="AnyDesk", Exe="AnyDesk.exe", Folder="AnyDesk", Publisher="AnyDesk Software GmbH", Remote=true },
            new SoftwareDefinition { Id="rudesktop", Name="RuDesktop", Exe="rudesktop.exe", Folder="RuDesktop", Publisher="Advanced Technologies LLC", Remote=true },
        };
        internal static SoftwareDefinition Find(string id)
        { var value = Apps.SingleOrDefault(a => a.Id == id); if (value == null) throw new InvalidDataException("Неизвестная программа"); return value; }
        internal static string Hash(string file)
        { using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) return LibraryCancellation.Hash(input, null); }
        internal static string Architecture(string file)
        {
            using (var input = new BinaryReader(File.OpenRead(file)))
            {
                if (input.BaseStream.Length < 64 || input.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Файл не является EXE Windows");
                input.BaseStream.Position = 60; uint offset = input.ReadUInt32();
                if (offset > input.BaseStream.Length - 6) throw new InvalidDataException("Повреждённый заголовок EXE");
                input.BaseStream.Position = offset; if (input.ReadUInt32() != 0x4550) throw new InvalidDataException("Повреждённый PE");
                ushort machine = input.ReadUInt16(); return machine == 0x8664 ? "x64" : machine == 0x14c ? "x86" : "другая";
            }
        }
        internal static int Compare(string left, string right)
        {
            Version a, b;
            if (!Version.TryParse(left, out a) || !Version.TryParse(right, out b)) throw new InvalidDataException("Не удалось сравнить версии");
            return new Version(a.Major,a.Minor,Math.Max(0,a.Build),Math.Max(0,a.Revision)).CompareTo(new Version(b.Major,b.Minor,Math.Max(0,b.Build),Math.Max(0,b.Revision)));
        }
        internal static SoftwarePackage Describe(string id, string file, string source)
        {
            var app = Find(id); ToolsDeployment.SafePath(file); var info = new FileInfo(file);
            if (!info.Exists || info.Length < 65536 || info.Length > 300L*1024*1024) throw new InvalidDataException("Некорректный размер установщика");
            string name = info.Name, version, arch;
            if (id == "rudesktop" || id == "chrome")
            {
                if (!name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Нужен MSI " + app.Name + " x64");
                var properties = LibraryDiscovery.MsiProperties(file, out arch);
                if (!properties.ContainsKey("ProductName") || !String.Equals(properties["ProductName"],app.Name,StringComparison.OrdinalIgnoreCase) || arch != "x64") throw new InvalidDataException("Не подтверждён MSI для " + app.Name);
                version = properties["ProductVersion"];
                if(id=="chrome" && version.Split('.').Length!=4)throw new InvalidDataException("Версия старого MSI Chrome закодирована; используй актуальный автономный установщик Google");
            }
            else
            {
                var metadata = FileVersionInfo.GetVersionInfo(file);
                if ((metadata.ProductName ?? "").IndexOf(app.Name, StringComparison.OrdinalIgnoreCase) < 0) throw new InvalidDataException("Метаданные установщика не соответствуют программе");
                arch = Architecture(file);
                if (id == "notepad")
                {
                    // NSIS bootstrapper is x86 even when the enclosed Notepad++ is x64.
                    Match match = Regex.Match(name, @"^npp\.(\d+(?:\.\d+){1,3})\.Installer\.x64\.exe$", RegexOptions.IgnoreCase);
                    if (!match.Success) throw new InvalidDataException("Нужен установщик Notepad++ Installer.x64.exe"); version = match.Groups[1].Value; arch = "x64";
                }
                else if (id == "7zip")
                {
                    Match match = Regex.Match(name, @"^7z(\d{2})(\d{2})-x64\.exe$", RegexOptions.IgnoreCase);
                    if (!match.Success || arch != "x64") throw new InvalidDataException("Нужен установщик 7-Zip x64"); version = Int32.Parse(match.Groups[1].Value) + "." + match.Groups[2].Value;
                }
                else { version = Regex.Match(metadata.FileVersion ?? "", @"^\d+(?:\.\d+){1,3}").Value; }
            }
            Version parsed; if (!Version.TryParse(version, out parsed)) throw new InvalidDataException("Не найдена версия установщика");
            return new SoftwarePackage { Id=id, File=Path.GetFullPath(file), Version=version, Architecture=arch, Sha256=Hash(file), Source=source, Url="" };
        }
        internal static void Validate(SoftwarePackage package, bool allowAnyDesk32)
        {
            Find(package.Id);
            if (!Environment.Is64BitOperatingSystem) throw new InvalidOperationException("Этот модуль устанавливает программы на Windows x64");
            if (package.Architecture != "x64" && !(package.Id == "anydesk" && package.Architecture == "x86"))
                throw new InvalidOperationException("Требуется x64; AnyDesk поддерживается также в разрядности x86");
            if (!Regex.IsMatch(package.Sha256 ?? "", @"\A[0-9a-fA-F]{64}\z")) throw new InvalidDataException("Нет контрольной суммы пакета");
        }
        internal static string FindFolder()
        {
            string adjacent = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_soft"); if (Directory.Exists(adjacent)) return adjacent;
            string root = LibrarySource.AutoDetect(AppDomain.CurrentDomain.BaseDirectory);
            if (root == null) { string saved = ApplicationStorage.FilePath("library-source.txt"); if (File.Exists(saved)) root = File.ReadAllText(saved).Trim(); }
            return root == null ? null : Path.Combine(root, "_soft");
        }
        internal static List<SoftwarePackage> Local(string folder, Action<string> progress, string sourceLabel="Локальный _soft")
        { return Local(folder,progress,sourceLabel,true); }
        internal static List<SoftwarePackage> Local(string folder, Action<string> progress, string sourceLabel, bool onlineDigest)
        {
            var packages = new List<SoftwarePackage>(); if (String.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return packages;
            ToolsDeployment.SafePath(folder);
            foreach (string file in Directory.GetFiles(folder))
            {
                LibraryCancellation.Check();string id=PackageId(file);
                if (id == null) continue;
                try { var package = Describe(id,file,sourceLabel); SoftwareTrust.Verify(package, onlineDigest); packages.Add(package); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { progress(Path.GetFileName(file) + ": " + ex.Message); }
            }
            return packages;
        }
        internal static string PackageId(string file)
        {
            string name=Path.GetFileName(file).ToLowerInvariant();
            return name=="googlechromestandaloneenterprise64.msi"?"chrome":name.StartsWith("npp.")?"notepad":Regex.IsMatch(name,@"^7z\d+-x64\.exe$")?"7zip":name=="anydesk.exe"?"anydesk":name.StartsWith("rudesktop-")&&name.EndsWith("-x64.msi")?"rudesktop":null;
        }
    }
    internal static class SoftwareTrust
    {
        // The vendor's EXE is unsigned. This hash is independently matched to the official
        // ip7z/7zip release asset; future unsigned versions need a fresh official digest.
        internal const string Local7ZipHash = "d54bf805f9f3704d1e8db2fa3498ae7ef2df0312b40b558e7c71c734430a665d";
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct FileInfo { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle, Subject; }
        [StructLayout(LayoutKind.Sequential)] private struct TrustData { public uint Size; public IntPtr Callback, Sip; public uint UI, Revocation, Union; public IntPtr File; public uint State; public IntPtr StateData, Url; public uint Flags, Context; }
        [DllImport("wintrust.dll",ExactSpelling=true,PreserveSig=true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
        internal static void Signature(string file, string publisher)
        {
            var entry = new FileInfo { Size=(uint)Marshal.SizeOf(typeof(FileInfo)), Path=file };
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FileInfo))); Marshal.StructureToPtr(entry,pointer,false);
            var data = new TrustData { Size=(uint)Marshal.SizeOf(typeof(TrustData)), UI=2, Union=1, File=pointer, State=1, Flags=0x1000|0x10 };
            Guid action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            try
            {
                if (WinVerifyTrust(new IntPtr(-1),ref action,ref data) != 0) throw new InvalidDataException("Цифровая подпись установщика не подтверждена Windows");
                using (var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file)))
                    if (!String.Equals(cert.GetNameInfo(X509NameType.SimpleName,false),publisher,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Неожиданный издатель установщика");
            }
            finally { data.State=2; WinVerifyTrust(new IntPtr(-1),ref action,ref data); Marshal.DestroyStructure(pointer,typeof(FileInfo)); Marshal.FreeHGlobal(pointer); }
        }
        private static string ApprovalFolder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"BARS\Manager\SoftwareApprovals"); } }
        private static bool Approved(SoftwarePackage package)
        {
            try
            {
                string folder=ApprovalFolder; if(!Directory.Exists(folder)) return false; ToolsDeployment.SafePath(folder);
                var acl=Directory.GetAccessControl(folder);
                string owner=acl.GetOwner(typeof(SecurityIdentifier)).Value;
                if(owner!="S-1-5-32-544" && owner!="S-1-5-18") return false;
                foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier)))
                {
                    string sid=rule.IdentityReference.Value;
                    if(rule.AccessControlType==AccessControlType.Allow && sid!="S-1-5-32-544" && sid!="S-1-5-18" &&
                        (rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | FileSystemRights.DeleteSubdirectoriesAndFiles))!=0) return false;
                }
                string path=Path.Combine(folder,package.Sha256.ToLowerInvariant()+".bin"); ToolsDeployment.SafePath(path);
                var fileAcl=File.GetAccessControl(path);
                if(fileAcl.GetOwner(typeof(SecurityIdentifier)).Value!="S-1-5-32-544") return false;
                foreach(FileSystemAccessRule rule in fileAcl.GetAccessRules(true,true,typeof(SecurityIdentifier)))
                    if(rule.AccessControlType==AccessControlType.Allow && rule.IdentityReference.Value!="S-1-5-32-544" && rule.IdentityReference.Value!="S-1-5-18" && (rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership))!=0) return false;
                byte[] bytes=File.ReadAllBytes(path); if(bytes.Length>4096) return false;
                string value=Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes,null,DataProtectionScope.LocalMachine));
                return value==package.Id+"|"+package.Version+"|"+package.Sha256.ToLowerInvariant();
            }
            catch(IOException){return false;} catch(UnauthorizedAccessException){return false;} catch(CryptographicException){return false;}
        }
        private static void Remember(SoftwarePackage package)
        {
            if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return;
            string folder=ApprovalFolder; ToolsDeployment.SafePath(folder);
            var acl=new DirectorySecurity(); acl.SetAccessRuleProtection(true,false);
            acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null));
            foreach(var sid in new[]{WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid}) acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            Directory.CreateDirectory(folder,acl); Directory.SetAccessControl(folder,acl);
            string path=Path.Combine(folder,package.Sha256.ToLowerInvariant()+".bin"); ToolsDeployment.SafePath(path);
            byte[] value=ProtectedData.Protect(Encoding.UTF8.GetBytes(package.Id+"|"+package.Version+"|"+package.Sha256.ToLowerInvariant()),null,DataProtectionScope.LocalMachine);
            // Replace a pre-existing file so it inherits the protected directory's permissions.
            string temp=Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp");
            try {File.WriteAllBytes(temp,value); var fileAcl=File.GetAccessControl(temp); fileAcl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null)); File.SetAccessControl(temp,fileAcl); if(File.Exists(path))File.Delete(path); File.Move(temp,path);}
            finally {if(File.Exists(temp))File.Delete(temp);}
        }
        internal static void Verify(SoftwarePackage package, bool onlineDigest)
        {
            if (!String.Equals(SoftwareCatalog.Hash(package.File),package.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Файл изменился после проверки SHA-256");
            if (package.Id == "7zip")
            {
                if (String.Equals(package.Sha256,Local7ZipHash,StringComparison.OrdinalIgnoreCase)) return;
                if (Approved(package)) return;
                if (!onlineDigest) throw new InvalidDataException("Для этой неподписанной версии 7-Zip нужен SHA-256 из официального релиза");
                var release = SoftwareSources.GitHubVersion(package.Version);
                if (!String.Equals(release.Sha256,package.Sha256,StringComparison.OrdinalIgnoreCase) || SoftwareCatalog.Compare(release.Version,package.Version)!=0) throw new InvalidDataException("7-Zip не совпадает с официальным релизом");
                Remember(package);
                return;
            }
            Signature(package.File,SoftwareCatalog.Find(package.Id).Publisher);
        }
    }
}
