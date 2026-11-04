using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace ServiceTool
{
    internal enum LibraryRelation { Unspecified, Missing, Older, Equal, Newer, Incompatible, Unconfirmed, NotApplicable }
    internal static class LibraryVersions
    {
        internal static string Clean(string value) { return (value ?? "").Trim().TrimStart('v', 'V').Split('+')[0]; }
        internal static Version Number(string value)
        {
            Version version; if (!Version.TryParse(Clean(value).Split('-')[0], out version)) return null;
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }
        internal static bool SameChannel(string a, string b)
        {
            Match x = Regex.Match(Clean(a), @"^\d+\.\d+\.\d+-(rc|preview|alpha|beta)\.(\d+)(?:\.|$)", RegexOptions.IgnoreCase);
            Match y = Regex.Match(Clean(b), @"^\d+\.\d+\.\d+-(rc|preview|alpha|beta)\.(\d+)(?:\.|$)", RegexOptions.IgnoreCase);
            return x.Success == y.Success && (!x.Success ? !Clean(a).Contains("-") && !Clean(b).Contains("-") : String.Equals(x.Groups[1].Value, y.Groups[1].Value, StringComparison.OrdinalIgnoreCase) && x.Groups[2].Value.TrimStart('0') == y.Groups[2].Value.TrimStart('0'));
        }
        internal static int Compare(string a, string b)
        {
            Version x = Number(a), y = Number(b); if (x == null || y == null) throw new ArgumentException("Не удалось прочитать номер версии");
            int basic = x.CompareTo(y); if (basic != 0) return basic;
            string[] sa = Clean(a).Split(new[] { '-' }, 2), sb = Clean(b).Split(new[] { '-' }, 2);
            if (sa.Length == 1 || sb.Length == 1) return sa.Length == sb.Length ? 0 : sa.Length == 1 ? 1 : -1;
            string[] xa = sa[1].ToLowerInvariant().Split('.'), xb = sb[1].ToLowerInvariant().Split('.');
            for (int i = 0; i < Math.Max(xa.Length, xb.Length); i++)
            {
                string aa = i < xa.Length ? xa[i] : "0", bb = i < xb.Length ? xb[i] : "0";
                bool an = Regex.IsMatch(aa, @"^\d+$"), bn = Regex.IsMatch(bb, @"^\d+$"); int comparison;
                if (an && bn) { aa = aa.TrimStart('0'); bb = bb.TrimStart('0'); comparison = aa.Length.CompareTo(bb.Length); if (comparison == 0) comparison = String.CompareOrdinal(aa, bb); }
                else comparison = String.CompareOrdinal(aa, bb);
                if (comparison != 0) return comparison;
            }
            return 0;
        }
        internal static bool Compatible(LibraryPackage package, string actual)
        {
            Version a = Number(actual), b = Number(Required(package)); if (a == null || b == null) return false;
            if (package.Kind == LibraryKind.DotNet) return a.Major == b.Major && a.Minor == b.Minor && SameChannel(actual, package.Version);
            if (package.Kind == LibraryKind.Framework) return a.Equals(b);
            if (package.Kind == LibraryKind.Msi && package.Name.IndexOf("MSXML", StringComparison.OrdinalIgnoreCase) >= 0) return a.Major == b.Major;
            if (package.Kind == LibraryKind.VcBundle) return a.Major == 14;
            return a.Major == b.Major && (package.Kind == LibraryKind.Access || a.Minor == b.Minor);
        }
        internal static string Required(LibraryPackage package) { return package.Kind == LibraryKind.Framework ? package.FrameworkVersion : package.Kind == LibraryKind.VcLegacy ? "11.0.61030 / 12.0.40664" : package.Version; }
        internal static LibraryCheck Known(LibraryPackage package, string actual, string evidence)
        {
            var check = new LibraryCheck { Package = package, ActualVersion = actual, Detail = evidence };
            if (!Compatible(package, actual)) { check.State = LibraryState.Missing; check.Relation = LibraryRelation.Incompatible; return check; }
            int comparison = Compare(actual, Required(package));
            check.Relation = comparison < 0 ? LibraryRelation.Older : comparison > 0 ? LibraryRelation.Newer : LibraryRelation.Equal;
            check.State = comparison < 0 ? LibraryState.Missing : LibraryState.Installed; check.HasProduct = true; return check;
        }
        internal static bool NeedsInstall(LibraryCheck check) { return check.State == LibraryState.Missing && check.Relation != LibraryRelation.NotApplicable; }
        internal static bool CanRemove(LibraryCheck check) { return !check.ProtectedComponent && (check.HasProduct || check.State == LibraryState.Installed || check.Relation == LibraryRelation.Older); }
        internal static bool CanReinstall(LibraryCheck check) { return CanRemove(check) && check.Relation != LibraryRelation.Newer && !check.HasNewerParts; }
        internal static string Advice(LibraryCheck check)
        {
            if (check.ProtectedComponent) return "Пропустить: предоставлен Windows; отдельное удаление не требуется";
            if (check.State == LibraryState.NotApplicable) return "Пропустить: не подходит для этой Windows";
            if (check.Relation == LibraryRelation.Older) return "Обновить: версия старее комплекта";
            if (check.Relation == LibraryRelation.Newer) return "Пропустить: установлена новее; понижение заблокировано";
            if (check.Relation == LibraryRelation.Incompatible) return "Установить требуемое поколение; найденное его не заменяет";
            if (check.State == LibraryState.Unknown) return "Проверить: наличие не подтверждено";
            if (check.State == LibraryState.Installed) return "Установлено; действий не требуется";
            return "Установить: компонент не найден";
        }
        internal static string BatchCaption(IEnumerable<LibraryCheck> selected)
        {
            var items = selected.ToList();
            if (items.Any(c => c.Relation == LibraryRelation.Older)) return "Обновить выбранные";
            if (items.Any(CanRemove)) return "Действия с выбранными";
            if (items.Count > 0 && items.All(c => c.State == LibraryState.Unknown || c.State == LibraryState.NotApplicable)) return "Проверить выбранные";
            return "Установить выбранные";
        }
    }
}
