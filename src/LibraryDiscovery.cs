using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ServiceTool
{
    // Read-only discovery. Never runs /? or loads any library from the selected directory.
    internal static class LibraryDiscovery
    {
        private static object Call(object target, string name, params object[] args)
        { return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args); }
        private static string Cell(object record, int index)
        { return Convert.ToString(record.GetType().InvokeMember("StringData", BindingFlags.GetProperty, null, record, new object[] { index })); }
        private static Dictionary<string, string> MsiProperties(string file, out string architecture)
        {
            object installer = null, db = null, view = null; architecture = "x86";
            try
            {
                installer = Activator.CreateInstance(Type.GetTypeFromProgID("WindowsInstaller.Installer", true));
                db = Call(installer, "OpenDatabase", file, 0); view = Call(db, "OpenView", "SELECT `Property`, `Value` FROM `Property`");
                Call(view, "Execute"); var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                object record;
                while ((record = Call(view, "Fetch")) != null) { try { properties[Cell(record, 1)] = Cell(record, 2); } finally { Marshal.FinalReleaseComObject(record); } }
                Call(view, "Close"); Marshal.FinalReleaseComObject(view); view = null;
                object summary = db.GetType().InvokeMember("SummaryInformation", BindingFlags.GetProperty, null, db, new object[] { 0 });
                try
                {
                    string platform = Convert.ToString(summary.GetType().InvokeMember("Property", BindingFlags.GetProperty, null, summary, new object[] { 7 }));
                    architecture = platform.IndexOf("64", StringComparison.OrdinalIgnoreCase) >= 0 ? "x64" : "x86";
                }
                finally { Marshal.FinalReleaseComObject(summary); }
                return properties;
            }
            finally { if (view != null) Marshal.FinalReleaseComObject(view); if (db != null) Marshal.FinalReleaseComObject(db); if (installer != null) Marshal.FinalReleaseComObject(installer); }
        }
        private static string Hash(string text)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 24);
        }
        public static List<LibraryPackage> Scan(string selected, Action<string> progress, Func<bool> cancelled = null)
        {
            string root = LibrarySource.NormalizeRoot(selected);
            var result = new List<LibraryPackage>();
            foreach (string file in Files(root, progress))
            {
                LibraryCancellation.Check(cancelled);
                string extension = Path.GetExtension(file);
                if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".msi", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileName(file);
                if (name == "Microsoft .NET Runtime 1.0.31.exe" || name == "Исправлятор .NET Famework.exe" || name == "Удалятор .NET Framework.exe" || name.IndexOf("RuntimePack", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                progress("Читаем пакет: " + name);
                string relative = file.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
                LibraryPackage package = Describe(file, relative);
                if (package == null) { progress("Неизвестный или ручной пакет пропущен: " + name); continue; }
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    package.Sha256 = LibraryCancellation.Hash(input, cancelled);
                LibraryCatalog.Validate(package); result.Add(package);
                if (package.Kind == LibraryKind.VcBundle) result.Add(Legacy(package));
            }
            // Keep multiple major/minor families; prefer newest patch for the same runtime family/architecture.
            return result.GroupBy(p => p.Id).Select(g => g.OrderByDescending(p => Numeric(p.Version)).First()).OrderBy(p => p.Order).ToList();
        }
        private static IEnumerable<string> Files(string root, Action<string> progress)
        {
            var pending = new Stack<string>(); pending.Push(root); int count = 0;
            while (pending.Count > 0)
            {
                string directory = pending.Pop(); string[] files, children;
                try { files = Directory.GetFiles(directory); children = Directory.GetDirectories(directory); }
                catch (UnauthorizedAccessException) { progress("Каталог недоступен: " + directory); continue; }
                catch (IOException) { progress("Каталог недоступен: " + directory); continue; }
                foreach (string child in children) if (!Path.GetFileName(child).Equals("_tools", StringComparison.OrdinalIgnoreCase) && (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                foreach (string file in files)
                {
                    if (++count > 4096) throw new InvalidDataException("В папке больше 4096 файлов. Выбери непосредственно папку библиотек.");
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) yield return file;
                }
            }
        }
        private static Version Numeric(string value) { Version version; return Version.TryParse(value.Split('-')[0], out version) ? version : new Version(0, 0); }
        public static string Id(LibraryPackage p)
        {
            string key;
            if (p.Kind == LibraryKind.Msi) key = "MSI|" + (String.IsNullOrEmpty(p.UpgradeCode) ? p.ProductCode : p.UpgradeCode) + "|" + p.Architecture;
            else if (p.Kind == LibraryKind.DotNet) { Version v = Numeric(p.Version); key = "NET|" + p.RuntimeFamily + "|" + v.Major + "." + v.Minor + "|" + p.Architecture + "|" + p.Preview; }
            else if (p.Kind == LibraryKind.Framework) key = "Framework|" + p.FrameworkVersion;
            else if (p.Kind == LibraryKind.Inno) { Version v = Numeric(p.Version); key = "DevExpress|" + v.Major + "." + v.Minor; }
            else if (p.Kind == LibraryKind.Access) key = "Access|" + p.Architecture + "|" + Numeric(p.Version).Major;
            else key = p.Kind == LibraryKind.Composite ? "RuntimePack" : p.Kind == LibraryKind.VcLegacy ? "VisualCLegacy" : "VisualCBundle";
            return "pkg_" + Hash(key.ToUpperInvariant());
        }
        public static LibraryPackage Legacy(LibraryPackage modern)
        {
            var legacy = new LibraryPackage { Kind = LibraryKind.VcLegacy, Name = "Visual C++ 2012/2013 · профиль 2005–2013 + Lite", Group = "Visual C++", RelativePath = modern.RelativePath, Version = modern.Version, Architecture = modern.Architecture, Sha256 = modern.Sha256, Order = modern.Order - 1, ProductCode = "", UpgradeCode = "", RuntimeFamily = "", UninstallKey = "" }; legacy.Id = Id(legacy); return legacy;
        }
        private static bool Inno(string file)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] bytes = new byte[(int)Math.Min(stream.Length, 1048576)]; int offset = 0, read;
                while (offset < bytes.Length && (read = stream.Read(bytes, offset, bytes.Length - offset)) > 0) offset += read;
                return Encoding.ASCII.GetString(bytes, 0, offset).Contains("Inno Setup Setup Data");
            }
        }
        public static LibraryPackage Describe(string file, string relative)
        {
            var p = new LibraryPackage { RelativePath = relative, ProductCode = "", UpgradeCode = "", RuntimeFamily = "", UninstallKey = "", Sha256 = new string('0', 64), Architecture = "both", FrameworkVersion = "4.8.1" };

            if (Path.GetExtension(file).Equals(".msi", StringComparison.OrdinalIgnoreCase))
            {
                string arch; Dictionary<string, string> values = MsiProperties(file, out arch);
                p.Kind = LibraryKind.Msi; p.Architecture = arch;
                if (!values.TryGetValue("ProductName", out p.Name) || !values.TryGetValue("ProductVersion", out p.Version) || !values.TryGetValue("ProductCode", out p.ProductCode)) throw new InvalidDataException("MSI без обязательных свойств: " + file);
                values.TryGetValue("UpgradeCode", out p.UpgradeCode);
                p.Group = p.Name.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0 || p.Name.IndexOf("Management Objects", StringComparison.OrdinalIgnoreCase) >= 0 ? "SQL Server" : "Системные компоненты";
                p.Name = p.Name.Trim() + " · " + arch;
                p.Order = p.Name.Contains("CLR") ? 20 : p.Name.Contains("Management Objects") ? 30 : 50;
            }
            else
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(file);
                string product = (info.ProductName ?? "").Trim(), version = (info.ProductVersion ?? "").Trim();
                Match runtime = Regex.Match(product, @"(?:Microsoft )?(ASP\.NET Core(?: Runtime)?|\.NET Runtime)(?:\s+-)?\s+(\d+)\.(\d+)\.(\d+)(?:\s+(RC\s*\d+|Preview\s*\d+))?\s*.*?\((x64|x86)\)", RegexOptions.IgnoreCase);
                if (runtime.Success)
                {
                    p.Kind = LibraryKind.DotNet; p.Group = "Microsoft .NET"; p.Architecture = runtime.Groups[6].Value.ToLowerInvariant();
                    p.RuntimeFamily = runtime.Groups[1].Value.StartsWith("ASP", StringComparison.OrdinalIgnoreCase) ? "Microsoft.AspNetCore.App" : "Microsoft.NETCore.App";
                    string basic = runtime.Groups[2].Value + "." + runtime.Groups[3].Value + "." + runtime.Groups[4].Value;
                    p.Preview = runtime.Groups[5].Success;
                    Match fileVersion = Regex.Match((info.OriginalFilename ?? "") + " " + Path.GetFileName(file), @"runtime-(\d+\.\d+\.\d+(?:-[a-zA-Z]+\.\d+\.[\d.]+)?)-win-(x64|x86)", RegexOptions.IgnoreCase);
                    p.Version = fileVersion.Success ? fileVersion.Groups[1].Value : basic;
                    if (p.Preview && !p.Version.Contains("-")) p.Version = basic + "-" + Regex.Replace(runtime.Groups[5].Value.ToLowerInvariant(), @"\s+", ".");
                    p.Name = (p.RuntimeFamily == "Microsoft.NETCore.App" ? ".NET Runtime " : "ASP.NET Core ") + p.Version + " · " + p.Architecture;
                    p.Order = 100 + Int32.Parse(runtime.Groups[2].Value) * 4 + (p.RuntimeFamily == "Microsoft.NETCore.App" ? 0 : 2) + (p.Architecture == "x64" ? 1 : 0);
                }
                else if (product.Contains(".NET Framework") && product.Contains("Developer Pack"))
                {
                    Match framework = Regex.Match(product, @"(\d+\.\d+(?:\.\d+)?)");
                    if (!framework.Success) return null;
                    p.Kind = LibraryKind.Framework; p.Group = ".NET Framework"; p.FrameworkVersion = framework.Value; p.Version = version;
                    p.Name = ".NET Framework " + p.FrameworkVersion + " Developer Pack"; p.Order = 0;
                }
                else if (product.Contains("DevExpress") && Inno(file))
                {
                    p.Kind = LibraryKind.Inno; p.Group = "DevExpress"; p.Version = version;
                    p.Name = "DevExpress " + version;
                    string uninstallName = Regex.Replace(product, @"\s+", " ").Trim();
                    p.UninstallKey = uninstallName + "_is1"; p.Order = 60;
                    Version number = Numeric(version);
                }
                else if (product.Contains("Visual C++") && product.Contains("Redistributable") && Inno(file))
                {
                    p.Kind = LibraryKind.VcBundle; p.Group = "Visual C++"; p.Version = version;
                    p.Name = "Visual C++ v14 (2015–2026) + Lite"; p.Order = 400;
                }
                else if (product.Contains("RuntimePack"))
                {
                    return null;
                }
                else if (Regex.IsMatch(Path.GetFileName(file) + " " + (info.OriginalFilename ?? ""), "accessdatabaseengine", RegexOptions.IgnoreCase) || product.IndexOf("Access database engine", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    p.Kind = LibraryKind.Access; p.Group = "Системные компоненты"; p.Version = version;
                    p.Architecture = Path.GetFileName(file).IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0 ? "x64" : "x86";
                    p.Name = "Access Database Engine · " + p.Architecture; p.Order = 55;
                }
                else return null;
            }
            p.Id = Id(p);
            return p;
        }
    }
}
