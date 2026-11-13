using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace ServiceTool
{
    internal enum LibraryState { Installed, Missing, Unknown, NotApplicable }
    internal sealed class LibraryCheck
    {
        public LibraryPackage Package;
        public LibraryState State;
        public string Detail;
        public string ActualVersion = "";
        public LibraryRelation Relation;
        public bool HasProduct, HasNewerParts, ProtectedComponent;
        public string Caption { get { if (Relation == LibraryRelation.Older) return "Нужно обновить"; if (Relation == LibraryRelation.Newer) return "Установлена новее"; if (Relation == LibraryRelation.Incompatible) return "Другое поколение"; return State == LibraryState.Installed ? "Установлено" : State == LibraryState.Missing ? "Не установлено" : State == LibraryState.NotApplicable ? "Не подходит для этой ОС" : "Нужна проверка"; } }
    }
    internal static class LibraryPresentation
    {
        internal static string SystemVersion(LibraryCheck check)
        {
            if(check.Package.Kind!=LibraryKind.DotNet)return String.IsNullOrEmpty(check.ActualVersion)?"—":check.ActualVersion;
            var versions=(check.ActualVersion??"").Split(',').Select(v=>v.Trim()).Where(v=>LibraryVersions.Compatible(check.Package,v)).Distinct().ToArray();
            if(versions.Length>0)return String.Join(", ",versions);
            return check.State==LibraryState.Unknown?"Нужна проверка":check.State==LibraryState.NotApplicable?"Не подходит для этой ОС":"Не установлено";
        }
        private static int Group(LibraryPackage p)
        {
            if (p.Group == "Microsoft .NET" || p.Group == ".NET Framework") return 0;
            if (p.Group == "SQL Server") return 1;
            if (p.Group == "DevExpress") return 2;
            if (p.Group == "Visual C++" || p.Group == "Visual C++ и RuntimePack") return 3;
            return 4;
        }
        private static int State(LibraryState state) { return state == LibraryState.Missing ? 0 : state == LibraryState.Unknown ? 1 : state == LibraryState.Installed ? 2 : 3; }
        public static List<LibraryCheck> Sort(IEnumerable<LibraryCheck> values)
        {
            return values.OrderBy(c => State(c.State)).ThenBy(c => Group(c.Package)).ThenBy(c => c.Package.Order)
                .ThenBy(c => c.Package.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Package.Architecture).ToList();
        }
    }
    internal static class LibrarySource
    {
        public static string AutoDetect(string executableDirectory)
        {
            return FindAdjacent(executableDirectory) ?? FindAdjacent(BundlePreferences.Load()) ?? FindAdjacent(BundleFolders.DefaultRoot);
        }
        internal static string FindAdjacent(string root)
        {
            string path = Path.Combine(root, "_libraries");
            return Directory.Exists(path) ? path : null;
        }
        public static string NormalizeRoot(string selected)
        {
            if (String.IsNullOrWhiteSpace(selected)) throw new ArgumentException("Выбери папку с библиотеками.");
            string full = Path.GetFullPath(selected);
            foreach (string name in new[] { "_libraries", "Библиотеки" })
            {
                string child = Path.Combine(full, name);
                if (Directory.Exists(child)) { full = child; break; }
            }
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Папка библиотек недоступна.");
            return full.Length == Path.GetPathRoot(full).Length ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        public static string PackagePath(string root, LibraryPackage package)
        {
            LibraryCatalog.Validate(package);
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(fullRoot, package.RelativePath));
            if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new SecurityException("Путь пакета выходит за выбранную папку.");
            return path;
        }
        public static void VerifyFile(string path, LibraryPackage package, Func<bool> cancelled = null)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("В выбранной папке нет файла: " + package.RelativePath, path);
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) VerifyStream(input, package, cancelled);
        }
        public static void VerifyStream(Stream input, LibraryPackage package, Func<bool> cancelled = null)
        {
            string hash = LibraryCancellation.Hash(input, cancelled);
            if (!String.Equals(hash, package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Файл отличается от проверенного набора: " + package.RelativePath + ". Обнови каталог пакетов; изменённый файл не запускался.");
        }
    }
    internal static class LibraryDetection
    {
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern int MsiQueryProductState(string code);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiEnumRelatedProducts(string upgrade, uint reserved, uint index, System.Text.StringBuilder product);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiGetProductInfo(string code, string property, System.Text.StringBuilder value, ref uint length);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct OsVersion
        {
            public int Size, Major, Minor, Build, Platform;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ServicePack;
        }
        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)] private static extern int RtlGetVersion(ref OsVersion version);
        internal static int WindowsBuild()
        {
            OsVersion version = new OsVersion { Size = Marshal.SizeOf(typeof(OsVersion)) };
            return RtlGetVersion(ref version) == 0 ? version.Build : 0;
        }
        public static bool RebootPending() { return RebootPendingReason().Length>0; }
        internal static string RebootPendingReason()
        {
            foreach (RegistryView view in Views())
            using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            {
                using (RegistryKey key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")) if (key != null) return "Windows ожидает перезагрузки после обслуживания компонентов (CBS). Это не обязательно результат работы менеджера.";
                using (RegistryKey key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")) if (key != null) return "Центр обновления Windows ожидает перезагрузки. Это не обязательно результат работы менеджера.";
            }
            return "";
        }
        internal static RegistryView[] Views() { return Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Registry32 }; }
        public static List<LibraryCheck> Scan(Action<int,int,string> progress=null)
        {
            List<LibraryRegisteredProduct> inventory = null;
            string inventoryError=null;
            try { inventory = LibraryMaintenance.Inventory(); } catch(Exception ex) { inventoryError=ex.Message; }
            var packages=LibraryCatalog.All().ToList();var result=new List<LibraryCheck>();foreach(var package in packages){result.Add(Check(package,inventory,inventoryError));if(progress!=null)progress(result.Count,packages.Count,package.Name);}return result;
        }
        private static List<LibraryRegisteredProduct> InventoryOrThrow(List<LibraryRegisteredProduct> inventory,string error)
        {
            if(error!=null)throw new InvalidOperationException("Не удалось прочитать список установленных программ: "+error);
            return inventory??LibraryMaintenance.Inventory();
        }
        public static LibraryCheck Check(LibraryPackage package) { return Check(package, null); }
        private static LibraryCheck Check(LibraryPackage package, List<LibraryRegisteredProduct> inventory,string inventoryError=null)
        {
            LibraryCheck result = new LibraryCheck { Package = package, State = LibraryState.Missing, Detail = "Компонент не найден" };
            try
            {
                if (package.Kind == LibraryKind.Composite) { result.State = LibraryState.NotApplicable; result.Relation = LibraryRelation.NotApplicable; result.Detail = "RuntimePack Full исключён из каталога и установки"; return result; }
                if (package.Architecture == "x64" && !Environment.Is64BitOperatingSystem)
                { result.State = LibraryState.NotApplicable; result.Detail = "Для этого пакета нужна 64-разрядная Windows"; return result; }
                // Do not blacklist MSXML x86 on x64. The installer reports its actual result.
                if (package.Kind == LibraryKind.Framework && package.FrameworkVersion == "4.8.1" && WindowsBuild() > 0 && WindowsBuild() < 19042)
                { result.State = LibraryState.NotApplicable; result.Detail = "Этот Developer Pack требует Windows 10 20H2+ или Server 2022+; сам менеджер работает на Framework 4.6+"; return result; }
                if (package.Kind == LibraryKind.Msi)
                {
                    if (package.Name.IndexOf("MSXML", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string windows = Environment.GetEnvironmentVariable("WINDIR");
                        string system = package.Architecture == "x64" && Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" :
                            package.Architecture == "x86" && Environment.Is64BitOperatingSystem ? "SysWOW64" : "System32";
                        string dll = Path.Combine(windows, system, "msxml6.dll");
                        if (File.Exists(dll))
                        {
                            FileVersionInfo info = FileVersionInfo.GetVersionInfo(dll);
                            var version = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart); Version required;
                            using (RegistryKey classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, package.Architecture == "x64" ? RegistryView.Registry64 : RegistryView.Registry32))
                            using (RegistryKey registration = classes.OpenSubKey(@"Msxml2.DOMDocument.6.0\CLSID"))
                                if (registration != null && Version.TryParse(package.Version, out required) && version.Major == required.Major && version.CompareTo(required) >= 0)
                                {
                                    var integrated = LibraryVersions.Known(package, version.ToString(), "MSXML 6 предоставлен Windows: " + version + ", разрядность " + package.Architecture + "; COM зарегистрирован; отдельное удаление не требуется");
                                    integrated.ProtectedComponent = true; return integrated;
                                }
                        }
                    }
                    var codes = new List<string>();
                    if (MsiQueryProductState(package.ProductCode) == 5) codes.Add(package.ProductCode);
                    if (!String.IsNullOrEmpty(package.UpgradeCode))
                    {
                        for (uint index = 0; index < 128; index++)
                        {
                            var code = new System.Text.StringBuilder(39); uint status = MsiEnumRelatedProducts(package.UpgradeCode, 0, index, code);
                            if (status == 259) break; if (status != 0) throw new Win32Exception((int)status);
                            if (MsiQueryProductState(code.ToString()) == 5 && !codes.Contains(code.ToString())) codes.Add(code.ToString());
                        }
                    }
                    var choices = new List<LibraryCheck>();
                    foreach (string code in codes)
                    {
                        uint length = 255; var version = new System.Text.StringBuilder(256);
                        if (MsiGetProductInfo(code, "VersionString", version, ref length) != 0 || LibraryVersions.Number(version.ToString()) == null) continue;
                        LibraryCheck found = LibraryVersions.Known(package, version.ToString(), "Windows Installer: " + code + "; версия " + version);
                        found.HasProduct = found.Relation != LibraryRelation.Incompatible; choices.Add(found);
                    }
                    if (choices.Count > 0) return choices.OrderBy(c => c.Relation == LibraryRelation.Incompatible ? 1 : 0).ThenByDescending(c => LibraryVersions.Number(c.ActualVersion)).First();
                    if (codes.Count > 0) { result.State = LibraryState.Unknown; result.Detail = "Продукт зарегистрирован, но его версию определить не удалось"; }
                }
                else if (package.Kind == LibraryKind.DotNet)
                {
                    return ProbeRuntime(package, RuntimeDirectories(package.Architecture, package.RuntimeFamily), InventoryOrThrow(inventory,inventoryError), LibraryMaintenance.StillRegistered);
                }
                else if (package.Kind == LibraryKind.Inno)
                {
                    var confirmed = new List<LibraryCheck>();
                    foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                    foreach (RegistryView view in Views())
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + package.UninstallKey))
                    {
                        if (key == null) continue;
                        string version = Convert.ToString(key.GetValue("DisplayVersion")), directory = Convert.ToString(key.GetValue("InstallLocation"));
                        if (!LibraryVersions.Compatible(package, version)) continue;
                        if (!String.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) && Directory.EnumerateFiles(directory, "DevExpress*.dll", SearchOption.AllDirectories).Any())
                        { confirmed.Add(LibraryVersions.Known(package, version, "Запись установки " + version + " и файлы DevExpress найдены")); continue; }
                        result.State = LibraryState.Unknown; result.Detail = "Запись установки найдена, но DLL в каталоге не подтверждены";
                    }
                    if (confirmed.Count > 0) return confirmed.OrderByDescending(c => LibraryVersions.Number(c.ActualVersion)).First();
                }
                else if (package.Kind == LibraryKind.Framework)
                {
                    bool runtime = false, sdk = false;
                    foreach (RegistryView view in Views())
                    using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    {
                        using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                            if (key != null && Convert.ToInt32(key.GetValue("Release", 0), CultureInfo.InvariantCulture) >= FrameworkRelease(package.FrameworkVersion)) runtime = true;
                        using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SDKs\NETFXSDK\" + package.FrameworkVersion))
                            if (key != null && Directory.Exists(Convert.ToString(key.GetValue("InstallationFolder")))) sdk = true;
                    }
                    string references = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Reference Assemblies\Microsoft\Framework\.NETFramework\v" + package.FrameworkVersion + @"\RedistList\FrameworkList.xml");
                    if (runtime && sdk && File.Exists(references))
                    {
                        var registered = (InventoryOrThrow(inventory,inventoryError)).Where(p => LibraryMaintenance.Matches(package, p) && LibraryMaintenance.StillRegistered(p)).ToList();
                        if (registered.Any(p => p.Name.IndexOf("SDK", StringComparison.OrdinalIgnoreCase) >= 0) && registered.Any(p => p.Name.IndexOf("Targeting Pack", StringComparison.OrdinalIgnoreCase) >= 0))
                            return Found(result, "Runtime " + package.FrameworkVersion + ", файлы SDK/targeting pack и действующая регистрация обоих компонентов найдены");
                        result.State = LibraryState.Unknown; result.Detail = "Runtime и файлы SDK/targeting pack есть, но действующая регистрация Developer Pack не подтверждена"; return result;
                    }
                    result.Detail = runtime ? "Runtime уже есть; комплект SDK/targeting pack не подтверждён" : "Комплект .NET Framework " + package.FrameworkVersion + " Developer Pack не найден";
                }
                else if (package.Kind == LibraryKind.Access)
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, package.Architecture == "x64" ? RegistryView.Registry64 : RegistryView.Registry32))
                    using (RegistryKey progId = root.OpenSubKey(@"Microsoft.ACE.OLEDB.16.0\CLSID"))
                    {
                        if (progId != null)
                        using (RegistryKey server = root.OpenSubKey(@"CLSID\" + Convert.ToString(progId.GetValue(null)) + @"\InprocServer32"))
                        {
                            string path = server == null ? null : Convert.ToString(server.GetValue(null));
                            if (File.Exists(path))
                            {
                                var standalone = (InventoryOrThrow(inventory,inventoryError)).Where(p => LibraryMaintenance.Matches(package, p) && LibraryMaintenance.StillRegistered(p) && LibraryVersions.Number(p.Version) != null).OrderByDescending(p => LibraryVersions.Number(p.Version)).FirstOrDefault();
                                if (standalone != null) return LibraryVersions.Known(package, standalone.Version, "Microsoft.ACE.OLEDB.16.0, разрядность " + package.Architecture + "; DLL и регистрация Access Database Engine найдены");
                                result.State = LibraryState.Unknown; result.Detail = "Провайдер ACE доступен, но отдельная установка Access Database Engine не подтверждена; он может входить в Office или остаться после удаления"; return result;
                            }
                        }
                    }
                    result.Detail = "Провайдер ACE x64 не найден. Перед установкой проверь разрядность Office";
                }
                else if (package.Kind == LibraryKind.VcBundle || package.Kind == LibraryKind.VcLegacy)
                    return LibraryVcProfiles.Check(package, InventoryOrThrow(inventory,inventoryError), Environment.Is64BitOperatingSystem, LibraryMaintenance.StillRegistered);
                else
                {
                    result.State = LibraryState.Unknown;
                    result.Detail = "Для этого типа компонента нет правила проверки";
                }
            }
            catch (Exception ex) { result.State = LibraryState.Unknown; result.Detail = "Не удалось завершить проверку: " + ex.Message; }
            return result;
        }
        private static int FrameworkRelease(string version)
        {
            switch (version) { case "4.6": return 393295; case "4.6.1": return 394254; case "4.6.2": return 394802; case "4.7": return 460798; case "4.7.1": return 461308; case "4.7.2": return 461808; case "4.8": return 528040; case "4.8.1": return 533320; default: throw new NotSupportedException("Неизвестное поколение .NET Framework: " + version); }
        }
        internal static LibraryCheck ProbeRuntime(LibraryPackage package, IEnumerable<string> directories, IEnumerable<LibraryRegisteredProduct> inventory, Func<LibraryRegisteredProduct, bool> registered, Func<string, string> informationalVersion = null)
        {
            var result = new LibraryCheck { Package = package, State = LibraryState.Missing, Relation = LibraryRelation.Missing, Detail = "Подходящий runtime не найден: " + package.RuntimeFamily + ", разрядность " + package.Architecture };
            var products = inventory.Where(p => LibraryMaintenance.RuntimeFamily(package, p) && registered(p)).ToList();
            var compatible = products.Where(p => LibraryVersions.Compatible(package, LibraryMaintenance.RuntimeVersion(p))).ToList();
            result.HasProduct = compatible.Count > 0; var confirmed = new List<LibraryCheck>(); bool orphan = false; var others = new List<string>();
            foreach (string directory in directories)
            {
                if (!Directory.Exists(directory)) continue;
                foreach (string candidate in Directory.EnumerateDirectories(directory))
                {
                    string version = Path.GetFileName(candidate); if (LibraryVersions.Number(version) == null) continue;
                    if (!LibraryVersions.Compatible(package, version)) { others.Add(version); continue; }
                    string file = Path.Combine(candidate, package.RuntimeFamily == "Microsoft.NETCore.App" ? "coreclr.dll" : "Microsoft.AspNetCore.dll");
                    if (!File.Exists(file)) continue;
                    if (package.Preview)
                    {
                        string metadataFile = package.RuntimeFamily == "Microsoft.NETCore.App" ? Path.Combine(candidate, "System.Private.CoreLib.dll") : file;
                        string actual = File.Exists(metadataFile) ? (informationalVersion == null ? FileVersionInfo.GetVersionInfo(metadataFile).ProductVersion : informationalVersion(metadataFile)) : "";
                        if (String.IsNullOrWhiteSpace(actual) || !String.Equals(LibraryVersions.Clean(actual), version, StringComparison.OrdinalIgnoreCase))
                        { orphan = true; result.Detail = "Полная версия DLL не совпала с каталогом: " + candidate; continue; }
                    }
                    var exact = new LibraryPackage { Kind = LibraryKind.DotNet, Architecture = package.Architecture, RuntimeFamily = package.RuntimeFamily, Version = version, Preview = package.Preview };
                    var product = compatible.FirstOrDefault(p => LibraryMaintenance.Matches(exact, p) && (package.Preview || LibraryVersions.Compare(LibraryMaintenance.RuntimeVersion(p), version) == 0));
                    if (product != null)
                    {
                        LibraryCheck found = LibraryVersions.Known(package, version, "Файлы: " + candidate + "; действующая регистрация: " + product.Name); found.HasProduct = true; confirmed.Add(found);
                    }
                    else { orphan = true; result.Detail = "Файлы " + version + " есть в " + candidate + ", но действующая регистрация этой версии не подтверждена; возможны остатки удаления"; }
                }
            }
            if (confirmed.Count > 0)
            {
                var found = confirmed.OrderByDescending(c => c.ActualVersion, Comparer<string>.Create(LibraryVersions.Compare)).First();
                found.Detail = "В комплекте: " + package.Version + "; в системе: " + String.Join(", ", confirmed.Select(c => c.ActualVersion).Distinct()) + ". " + found.Detail; return found;
            }
            if (orphan || compatible.Count > 0) { result.State = LibraryState.Unknown; result.Relation = LibraryRelation.Unconfirmed; if (!orphan) result.Detail = "Запись установки есть, но соответствующие файлы runtime отсутствуют"; }
            else
            {
                foreach (var product in products) others.Add(LibraryMaintenance.RuntimeVersion(product));
                if (others.Count > 0) { result.Relation = LibraryRelation.Incompatible; result.ActualVersion = String.Join(", ", others.Distinct().OrderBy(v => v)); result.Detail = "Найдены другие поколения или каналы " + package.RuntimeFamily + ": " + result.ActualVersion + ". Они не заменяют " + package.Version; }
            }
            return result;
        }
        private static LibraryCheck Found(LibraryCheck result, string detail) { result.State = LibraryState.Installed; result.Relation = LibraryRelation.Equal; result.ActualVersion = LibraryVersions.Required(result.Package); result.HasProduct = true; result.Detail = detail; return result; }
        internal static bool VersionFits(string actual, string required)
        {
            Version a, b;
            if (!Version.TryParse(actual, out a) || !Version.TryParse(required, out b)) return false;
            return a.Major == b.Major && a.Minor == b.Minor && LibraryVersions.Compare(actual, required) >= 0;
        }
        private static IEnumerable<string> RuntimeDirectories(string architecture, string family)
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string folder = architecture == "x64" ? Environment.GetEnvironmentVariable("ProgramW6432") : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (String.IsNullOrWhiteSpace(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            roots.Add(Path.Combine(folder, "dotnet"));
            foreach (RegistryView view in Views())
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\" + architecture))
            {
                string location = key == null ? null : Convert.ToString(key.GetValue("InstallLocation"));
                if (!String.IsNullOrWhiteSpace(location)) roots.Add(location);
            }
            foreach (string root in roots) yield return Path.Combine(root, "shared", family);
        }
    }
    internal static class LibraryPlan
    {
        public static List<LibraryPackage> Expand(IEnumerable<LibraryPackage> selected)
        {
            var items = new Dictionary<string, LibraryPackage>();
            foreach (LibraryPackage item in selected)
            {
                items[item.Id] = item;
                IEnumerable<LibraryPackage> dependencies = LibraryCatalog.All().Where(p =>
                    (item.Kind == LibraryKind.Msi && item.Name.Contains("Management Objects") && p.Kind == LibraryKind.Msi && p.Name.Contains("CLR") && p.Architecture == item.Architecture && Major(p.Version) == Major(item.Version)) ||
                    (item.RuntimeFamily == "Microsoft.AspNetCore.App" && p.RuntimeFamily == "Microsoft.NETCore.App" && p.Version == item.Version && p.Architecture == item.Architecture));
                foreach (LibraryPackage dependency in dependencies) items[dependency.Id] = dependency;
            }
            return items.Values.OrderBy(p => p.Order).ToList();
        }
        private static int Major(string version) { Version v; return Version.TryParse(version, out v) ? v.Major : -1; }
        public static bool Success(int code) { return code == 0 || code == 3010 || code == 1641; }
        public static bool NeedsReboot(int code) { return code == 3010 || code == 1641; }
        internal static string Quote(string text)
        {
            if (text == null || text.IndexOf('\0') >= 0) throw new ArgumentException("Некорректный аргумент.");
            var result = new System.Text.StringBuilder("\""); int slashes = 0;
            foreach (char c in text) { if (c == '\\') { slashes++; continue; } if (c == '"') result.Append('\\', slashes * 2 + 1); else result.Append('\\', slashes); slashes = 0; result.Append(c); }
            result.Append('\\', slashes * 2); return result.Append('"').ToString();
        }
        public static ProcessStartInfo Command(LibraryPackage package, string verifiedFile, string log, bool smallUpdate = false)
        {
            if (package.Kind == LibraryKind.Composite) throw new InvalidOperationException("RuntimePack Full исключён из установки");
            string quiet = "/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010 /LOG=" + Quote(log);
            if (package.Kind == LibraryKind.Msi)
                return new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe"), "/i " + Quote(verifiedFile) + " /qn /norestart ALLUSERS=1 REBOOT=ReallySuppress /L*v " + Quote(log) + (package.Name.Contains("Native Client") ? " IACCEPTSQLNCLILICENSETERMS=YES" : "") + (smallUpdate ? " REINSTALL=ALL REINSTALLMODE=vomus" : ""));
            string args = package.Kind == LibraryKind.DotNet || package.Kind == LibraryKind.Framework ? "/install /quiet /norestart /log " + Quote(log) :
                package.Kind == LibraryKind.Access ? "/quiet" : quiet;
            if (package.Kind == LibraryKind.VcBundle || package.Kind == LibraryKind.VcLegacy) args += " /TASKS=" + Quote(LibraryVcProfiles.Tasks(package));
            return new ProcessStartInfo(verifiedFile, args);
        }
    }
}
