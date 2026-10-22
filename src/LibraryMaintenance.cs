using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ServiceTool
{
    internal enum LibraryOperation { Install, Reinstall, Remove, Update }
    internal sealed class LibraryRegisteredProduct
    {
        public string Key, Name, Version, Architecture, Uninstall, QuietUninstall;
        public bool Hidden, Msi;
    }
    internal static class LibraryMaintenance
    {
        internal static bool ExactMsiInstalled(string code) { return MsiQueryProductState(code) == 5; }
        private const string UninstallRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern int MsiQueryProductState(string product);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiEnumRelatedProducts(string upgrade, uint reserved, uint index, StringBuilder product);
        public static List<LibraryRegisteredProduct> Inventory()
        {
            var result = new Dictionary<string, LibraryRegisteredProduct>(StringComparer.OrdinalIgnoreCase);
            foreach (RegistryView view in LibraryDetection.Views())
            using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            using (RegistryKey uninstall = machine.OpenSubKey(UninstallRoot))
            {
                if (uninstall == null) continue;
                foreach (string name in uninstall.GetSubKeyNames())
                using (RegistryKey key = uninstall.OpenSubKey(name))
                {
                    if (key == null) continue;
                    string display = Convert.ToString(key.GetValue("DisplayName"));
                    if (String.IsNullOrWhiteSpace(display)) continue;
                    string architecture = Regex.IsMatch(display, @"\b(x64|64-bit)\b", RegexOptions.IgnoreCase) ? "x64" : Regex.IsMatch(display, @"\b(x86|32-bit)\b", RegexOptions.IgnoreCase) ? "x86" : view == RegistryView.Registry64 ? "x64" : "x86";
                    var record = new LibraryRegisteredProduct { Key = name, Name = display, Version = Convert.ToString(key.GetValue("DisplayVersion")), Architecture = architecture,
                        Uninstall = Convert.ToString(key.GetValue("UninstallString")), QuietUninstall = Convert.ToString(key.GetValue("QuietUninstallString")), Msi = Convert.ToInt32(key.GetValue("WindowsInstaller", 0)) == 1, Hidden = Convert.ToInt32(key.GetValue("SystemComponent", 0)) == 1 };
                    result[name + "|" + architecture] = record;
                }
            }
            return result.Values.ToList();
        }
        internal static string RuntimeVersion(LibraryRegisteredProduct record)
        {
            Match visible = Regex.Match(record.Name ?? "", @"\d+\.\d+\.\d+(?:-(?:rc|preview|alpha|beta)\.\d+(?:\.[0-9.]+)?|\s+(?:RC|Preview|Alpha|Beta)\s*\d+)?", RegexOptions.IgnoreCase);
            return visible.Success ? Regex.Replace(visible.Value, @"\s+(RC|Preview|Alpha|Beta)\s*(\d+)", m => "-" + m.Groups[1].Value.ToLowerInvariant() + "." + m.Groups[2].Value, RegexOptions.IgnoreCase) : record.Version;
        }
        internal static bool RuntimeFamily(LibraryPackage package, LibraryRegisteredProduct record)
        {
            return record.Architecture == package.Architecture && (!record.Hidden || record.Msi) && (package.RuntimeFamily == "Microsoft.AspNetCore.App" ? Regex.IsMatch(record.Name ?? "", @"^Microsoft ASP\.NET Core\b", RegexOptions.IgnoreCase) : Regex.IsMatch(record.Name ?? "", @"^Microsoft \.NET (Core )?Runtime\b", RegexOptions.IgnoreCase));
        }
        internal static bool Matches(LibraryPackage package, LibraryRegisteredProduct record)
        {
            string name = record.Name ?? "";
            bool arch = package.Architecture == "both" || package.Architecture == record.Architecture;
            if (!arch || record.Hidden && !(package.Kind == LibraryKind.DotNet && record.Msi)) return false;
            if (package.Kind == LibraryKind.Msi) return String.Equals(package.ProductCode, record.Key, StringComparison.OrdinalIgnoreCase);
            if (package.Kind == LibraryKind.Inno) return String.Equals(package.UninstallKey, record.Key, StringComparison.OrdinalIgnoreCase) && LibraryVersions.Compatible(package, record.Version);
            if (package.Kind == LibraryKind.DotNet)
            {
                bool family = package.RuntimeFamily == "Microsoft.AspNetCore.App" ? Regex.IsMatch(name, @"^Microsoft ASP\.NET Core\b", RegexOptions.IgnoreCase) : Regex.IsMatch(name, @"^Microsoft \.NET (Core )?Runtime\b", RegexOptions.IgnoreCase);
                string version = RuntimeVersion(record);
                bool preview = package.Preview && (String.Equals(version, package.Version, StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(version, @"^\d+\.\d+\.\d+-(?:rc|preview|alpha|beta)\.\d+$", RegexOptions.IgnoreCase) && package.Version.StartsWith(version + ".", StringComparison.OrdinalIgnoreCase));
                return family && (package.Preview ? preview : !version.Contains("-") && LibraryVersions.Compatible(package, version));
            }
            if (package.Kind == LibraryKind.Framework)
                return name.IndexOf(".NET Framework", StringComparison.OrdinalIgnoreCase) >= 0 && name.Contains(package.FrameworkVersion) &&
                    (name.IndexOf("SDK", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Targeting Pack", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Developer Pack", StringComparison.OrdinalIgnoreCase) >= 0);
            if (package.Kind == LibraryKind.Access) return name.StartsWith("Microsoft Access database engine 2016", StringComparison.OrdinalIgnoreCase);
            if (package.Kind == LibraryKind.VcBundle || package.Kind == LibraryKind.VcLegacy)
            {
                Version version;
                return name.StartsWith("Microsoft Visual C++", StringComparison.OrdinalIgnoreCase) && name.IndexOf("Redistributable", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    Version.TryParse(record.Version, out version) && LibraryVcProfiles.Generations(package).Contains(version.Major);
            }
            return false;
        }
        internal static void ValidateReinstall(LibraryPackage package, IEnumerable<LibraryRegisteredProduct> products)
        {
            if (package.Kind != LibraryKind.DotNet && package.Kind != LibraryKind.Inno && package.Kind != LibraryKind.Msi && package.Kind != LibraryKind.Access) return;
            Version required;
            if (!Version.TryParse(package.Version.Split('-')[0], out required)) return;
            foreach (var product in products)
            {
                string value = product.Version;
                if (package.Kind == LibraryKind.DotNet)
                { value = RuntimeVersion(product); }
                Version actual;
                if (!Version.TryParse((value ?? "").Split('-')[0], out actual)) continue;
                var a = new Version(actual.Major, actual.Minor, Math.Max(0, actual.Build), Math.Max(0, actual.Revision));
                var b = new Version(required.Major, required.Minor, Math.Max(0, required.Build), Math.Max(0, required.Revision));
                if (a.CompareTo(b) > 0 || a.Equals(b) && LibraryVersions.Compare(value, package.Version) > 0) throw new InvalidOperationException(product.Name + ": установлена более новая версия " + value + ". Для переустановки обнови установщик в папке библиотек; удаление не начиналось.");
            }
        }
        public static List<LibraryRegisteredProduct> Resolve(LibraryPackage package)
        {
            List<LibraryRegisteredProduct> inventory = Inventory();
            List<LibraryRegisteredProduct> result = inventory.Where(r => Matches(package, r)).ToList();
            if (package.Kind == LibraryKind.DotNet && result.Any(r => !r.Hidden)) result = result.Where(r => !r.Hidden).ToList();
            if (package.Kind == LibraryKind.DotNet && package.Preview && result.Count > 1)
                throw new InvalidOperationException(package.Name + ": найдено несколько записей этой предварительной версии. Неоднозначное удаление не запускалось.");
            if (package.Kind == LibraryKind.Msi && !String.IsNullOrEmpty(package.UpgradeCode))
            {
                for (uint index = 0; index < 128; index++)
                {
                    var code = new StringBuilder(39); uint status = MsiEnumRelatedProducts(package.UpgradeCode, 0, index, code);
                    if (status == 259) break;
                    if (status != 0) throw new System.ComponentModel.Win32Exception((int)status);
                    var record = inventory.FirstOrDefault(r => String.Equals(r.Key, code.ToString(), StringComparison.OrdinalIgnoreCase) && (package.Architecture == r.Architecture || package.Architecture == "both") && LibraryVersions.Compatible(package, r.Version));
                    if (record != null && !result.Contains(record)) result.Add(record);
                }
                Guid guid;
                if (result.Count == 0 && Guid.TryParse(package.ProductCode, out guid) && MsiQueryProductState(package.ProductCode) == 5)
                    result.Add(new LibraryRegisteredProduct { Key = package.ProductCode, Name = package.Name, Version = package.Version, Architecture = package.Architecture, Msi = true });
            }
            if (result.Count == 0) throw new InvalidOperationException(package.Name + ": отдельный зарегистрированный деинсталлятор не найден. Компонент может входить в Windows или другой продукт; его файлы не удалялись.");
            return result.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        internal static ProcessStartInfo Command(LibraryRegisteredProduct product, string log)
        {
            Guid guid;
            if (product.Msi && Guid.TryParse(product.Key, out guid))
                return new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), "/x " + LibraryPlan.Quote(guid.ToString("B")) + " /qn /norestart REBOOT=ReallySuppress /L*v " + LibraryPlan.Quote(log)) { UseShellExecute = false, WorkingDirectory = Environment.SystemDirectory };
            bool quiet = !String.IsNullOrWhiteSpace(product.QuietUninstall);
            string command = Environment.ExpandEnvironmentVariables(quiet ? product.QuietUninstall : product.Uninstall ?? "").Trim();
            string file, arguments;
            if (command.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = command.IndexOf('"', 1); if (end < 0) throw new InvalidDataException("Некорректная строка деинсталлятора");
                file = command.Substring(1, end - 1); arguments = command.Substring(end + 1).Trim();
            }
            else
            {
                Match executable = Regex.Match(command, @"^(.+?\.exe)(?:\s+|$)", RegexOptions.IgnoreCase);
                if (!executable.Success) throw new InvalidDataException("Не найден EXE зарегистрированного деинсталлятора");
                file = executable.Groups[1].Value; arguments = command.Substring(executable.Length).Trim();
            }
            if (!Path.IsPathRooted(file) || !String.Equals(Path.GetExtension(file), ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                throw new InvalidDataException("Файл зарегистрированного деинсталлятора недоступен: " + file);
            string basename = Path.GetFileName(file);
            if (new[] { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "rundll32.exe", "msiexec.exe" }.Contains(basename, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Эта строка деинсталляции требует отдельного правила: " + product.Name);
            if (!quiet && product.Key.EndsWith("_is1", StringComparison.OrdinalIgnoreCase)) arguments += " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=" + LibraryPlan.Quote(log);
            return new ProcessStartInfo(file, arguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(file) };
        }
        internal static bool StillRegistered(LibraryRegisteredProduct target)
        {
            Guid guid;
            if (target.Msi && Guid.TryParse(target.Key, out guid)) return MsiQueryProductState(target.Key) == 5;
            return Inventory().Any(r => r.Key == target.Key && r.Architecture == target.Architecture);
        }
        public static LibraryInstallResult Remove(LibraryPackage package, string directory, Action<string> progress, Func<bool> connected)
        {
            List<LibraryRegisteredProduct> targets = Resolve(package);
            Directory.CreateDirectory(directory);
            // Prepare every command before launching any removal in this component.
            var commands = targets.Select((r, i) => Command(r, Path.Combine(directory, package.Id + "-remove-" + i + ".log"))).ToList();
            for (int index = 0; index < targets.Count; index++)
            {
                LibraryCancellation.Check();
                if (!connected()) throw new IOException("Менеджер закрыл соединение; следующий деинсталлятор не запускался");
                progress("Удаляем " + targets[index].Name + ", версия " + targets[index].Version);
                int code = LibraryProcess.Run(commands[index], progress);
                bool remains = StillRegistered(targets[index]);
                if (!LibraryPlan.Success(code) || remains || LibraryPlan.NeedsReboot(code))
                    return new LibraryInstallResult { ExitCode = code, State = remains ? LibraryState.Installed : LibraryState.Missing,
                        Message = !LibraryPlan.Success(code) ? "Деинсталлятор завершился с кодом " + code : LibraryPlan.NeedsReboot(code) ? "Удаление требует перезагрузки; очередь приостановлена" : "Деинсталлятор завершился, но регистрация компонента сохранилась", LogPath = directory };
            }
            string summary = Path.Combine(directory, package.Id + "-remove-result.txt");
            File.WriteAllText(summary, DateTime.Now.ToString("O") + Environment.NewLine + String.Join(Environment.NewLine, targets.Select(r => r.Name + " " + r.Version)) + Environment.NewLine + "Удаление подтверждено проверкой регистрации", Encoding.UTF8);
            return new LibraryInstallResult { ExitCode = 0, State = LibraryState.Missing, Message = "Удаление подтверждено проверкой регистрации", LogPath = summary };
        }
    }
}
