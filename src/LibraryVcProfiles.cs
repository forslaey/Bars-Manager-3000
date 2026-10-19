using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
namespace ServiceTool
{
    internal static class LibraryVcProfiles
    {
        internal static int[] Generations(LibraryPackage package) { return package.Kind == LibraryKind.VcLegacy ? new[] { 11, 12 } : new[] { 14 }; }
        internal static string Minimum(int major, string architecture, LibraryPackage package)
        {
            switch (major) { case 8: return architecture == "x86" ? "8.0.61001.0" : "8.0.61000.0"; case 9: return "9.0.30729.6161"; case 10: return "10.0.40219.0"; case 11: return "11.0.61030.0"; case 12: return "12.0.40664.0"; default: return package.Version; }
        }
        internal static string Tasks(LibraryPackage package)
        {
            if (package.Kind == LibraryKind.VcLegacy) return "v0513,v0513\\silent,rtl";
            if (package.Kind == LibraryKind.VcBundle) return "v1522,v1522\\silent,rtl";
            throw new ArgumentException("Это не профиль VC++");
        }
        private static string FileVersion(int major, string architecture)
        {
            string system = Environment.Is64BitOperatingSystem ? architecture == "x86" ? "SysWOW64" : Environment.Is64BitProcess ? "System32" : "Sysnative" : "System32";
            string root = Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), system);
            string[] names = major == 14 ? new[] { "vcruntime140.dll", "msvcp140.dll" } : new[] { "msvcr" + major + "0.dll", "msvcp" + major + "0.dll" };
            var versions = new List<string>();
            foreach (string name in names)
            {
                string file = Path.Combine(root, name); if (!File.Exists(file)) return null;
                var info = FileVersionInfo.GetVersionInfo(file);
                string version = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart).ToString();
                if (info.FileMajorPart != major) return null; versions.Add(version);
            }
            return versions.OrderBy(v => LibraryVersions.Number(v)).First();
        }
        internal static LibraryCheck Check(LibraryPackage package, IEnumerable<LibraryRegisteredProduct> inventory, bool x64, Func<LibraryRegisteredProduct, bool> registered, Func<int, string, string> fileVersion = null)
        {
            if (fileVersion == null) fileVersion = FileVersion;
            var records = inventory.Where(r => LibraryMaintenance.Matches(package, r) && registered(r)).ToList();
            var actual = new List<string>(); var missing = new List<string>(); bool older = false, newer = false, unconfirmed = false;
            foreach (int generation in Generations(package))
            foreach (string architecture in x64 ? new[] { "x86", "x64" } : new[] { "x86" })
            {
                var candidates = records.Where(r => r.Architecture == architecture && LibraryVersions.Number(r.Version) != null && LibraryVersions.Number(r.Version).Major == generation).OrderByDescending(r => LibraryVersions.Number(r.Version)).ToList();
                string label = "v" + generation + " " + architecture;
                if (candidates.Count == 0) { missing.Add(label); continue; }
                string dll = fileVersion(generation, architecture);
                if (LibraryVersions.Number(dll) == null) { actual.Add(label + " " + candidates[0].Version + " (DLL не подтверждены)"); unconfirmed = true; continue; }
                // Redistributable package and individual DLL build numbers are different domains
                // (for example VC2012 Update 4 package 11.0.61030 includes CRT 11.0.51106.1).
                // Compare the registered package version; use DLLs to confirm family/presence.
                if (LibraryVersions.Number(dll).Major != generation) { unconfirmed = true; continue; }
                string version = candidates[0].Version;
                int compared = LibraryVersions.Compare(version, Minimum(generation, architecture, package)); older |= compared < 0; newer |= compared > 0;
                actual.Add(label + " " + version);
            }
            var result = new LibraryCheck { Package = package, ActualVersion = String.Join("; ", actual), HasProduct = records.Count > 0, HasNewerParts = newer,
                Detail = "Проверены поколения " + String.Join("/", Generations(package)) + ", разрядности, регистрация и системные DLL. При установке этого профиля RuntimePack Lite обязателен; отдельная полнота Lite не подтверждается по записям VC++." };
            if (unconfirmed) { result.State = LibraryState.Unknown; result.Relation = LibraryRelation.Unconfirmed; result.Detail = "Есть регистрация VC++, но файлы соответствующих DLL не подтверждены. " + result.Detail; }
            else if (missing.Count > 0) { result.State = LibraryState.Missing; result.Relation = LibraryRelation.Missing; result.Detail = "Отсутствуют: " + String.Join(", ", missing) + ". " + result.Detail; }
            else if (older) { result.State = LibraryState.Missing; result.Relation = LibraryRelation.Older; }
            else { result.State = LibraryState.Installed; result.Relation = newer ? LibraryRelation.Newer : LibraryRelation.Equal; }
            if (package.Kind == LibraryKind.VcLegacy && newer && result.State != LibraryState.Installed)
            { result.State = LibraryState.Unknown; result.Relation = LibraryRelation.Unconfirmed; result.Detail = "Смешанный набор: часть VC++ новее файлов сборника. Автоматическая установка старого профиля может понизить версии; нужен подходящий установщик. " + result.Detail; }
            return result;
        }
        internal static void ValidateNoDowngrade(LibraryPackage package, IEnumerable<LibraryRegisteredProduct> products)
        {
            foreach (var product in products)
            {
                var version = LibraryVersions.Number(product.Version); if (version == null) continue;
                bool relevant = package.Kind == LibraryKind.VcLegacy ? new[] { 8, 9, 10, 11, 12 }.Contains(version.Major) : version.Major == 14;
                if (!relevant || !product.Name.StartsWith("Microsoft Visual C++", StringComparison.OrdinalIgnoreCase) || product.Name.IndexOf("Redistributable", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (LibraryVersions.Compare(product.Version, Minimum(version.Major, product.Architecture, package)) > 0)
                    throw new InvalidOperationException(product.Name + ": установлена более новая версия " + product.Version + ". Понижение сборником не запускалось.");
            }
        }
    }
}
