using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ServiceTool
{
    internal sealed class WindowsComponent
    {
        public string Id, Name, Group;
        public string[] Features;
    }
    internal static class WindowsComponents
    {
        // Exact IIS profile captured from the user's configured Windows on 20 October 2026.
        // Parent features are included; unrelated ASP.NET, FTP and authentication features are not.
        public static readonly WindowsComponent[] Profiles = {
            new WindowsComponent { Id = "iis", Name = "IIS", Group = "Компоненты для модуля «Отчёты для арендаторов»", Features = new[] {
                "IIS-WebServerRole", "IIS-WebServer", "IIS-CommonHttpFeatures", "IIS-HttpErrors", "IIS-HttpRedirect",
                "IIS-ApplicationDevelopment", "IIS-Security", "IIS-RequestFiltering", "IIS-HealthAndDiagnostics",
                "IIS-HttpLogging", "IIS-LoggingLibraries", "IIS-RequestMonitor", "IIS-HttpTracing", "IIS-Performance",
                "IIS-WebServerManagementTools", "IIS-ManagementScriptingTools", "IIS-IIS6ManagementCompatibility",
                "IIS-Metabase", "IIS-StaticContent", "IIS-DefaultDocument", "IIS-DirectoryBrowsing", "IIS-CustomLogging",
                "IIS-HttpCompressionStatic", "IIS-ManagementConsole", "IIS-ManagementService", "IIS-WMICompatibility", "IIS-LegacyScripts" } },
            new WindowsComponent { Id = "telnet", Name = "Telnet Client", Group = "Компоненты для fiLinn, piglet", Features = new[] { "TelnetClient" } },
            new WindowsComponent { Id = "tftp", Name = "TFTP Client", Group = "Компоненты для fiLinn, piglet", Features = new[] { "TFTP" } }
        };
        internal static string[] Resolve(string ids)
        {
            var selected = (ids ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (selected.Length == 0 || selected.Distinct().Count() != selected.Length || selected.Any(id => !Profiles.Any(p => p.Id == id)))
                throw new InvalidDataException("Неизвестный набор компонентов Windows");
            return Profiles.Where(p => selected.Contains(p.Id)).SelectMany(p => p.Features).Distinct().ToArray();
        }
        internal static string PowerShellPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? @"Sysnative\WindowsPowerShell\v1.0\powershell.exe" : @"System32\WindowsPowerShell\v1.0\powershell.exe"); }
        }
        internal static string QueryScript()
        {
            return "$ErrorActionPreference='Stop'; Get-WindowsOptionalFeature -Online | Where-Object { $_.FeatureName -like 'IIS-*' -or $_.FeatureName -eq 'TelnetClient' -or $_.FeatureName -eq 'TFTP' } | ForEach-Object { $_.FeatureName + '=' + $_.State.ToString() }";
        }
        internal static string EnableScript(string ids)
        {
            string[] names = Resolve(ids);
            return "$ErrorActionPreference='Stop'; $r=Enable-WindowsOptionalFeature -Online -FeatureName @(" +
                String.Join(",", names.Select(n => "'" + n + "'")) + ") -All -NoRestart; if(@($r | Where-Object { $_.RestartNeeded -eq $true }).Count -gt 0){'BARS_RESTART_REQUIRED'}";
        }
        // Read both pipes concurrently: DISM/PowerShell cannot deadlock on a full error pipe.
        // Windows servicing is allowed to finish; killing TrustedInstaller is never attempted.
        internal static LibraryInstallResult Run(string script, Action<string> progress)
        {
            var start = new ProcessStartInfo(PowerShellPath, "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script))) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            // Explicit UTF-8 on the child streams, independent of the machine's ANSI/OEM code page.
            start.Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false); " + script));
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
                while (!process.WaitForExit(1000)) progress("Windows обрабатывает запрос. Дождись завершения…");
                string text = output.GetAwaiter().GetResult(), error = errors.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException(String.IsNullOrWhiteSpace(error) ? "Ошибка Windows: " + process.ExitCode : error.Trim());
                return new LibraryInstallResult { ExitCode = text.Contains("BARS_RESTART_REQUIRED") ? 3010 : 0, Message = text.Trim(), State = LibraryState.Unknown };
            }
        }
        internal static Dictionary<string, string> Parse(string text)
        {
            var states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            { int i = line.IndexOf('='); if (i > 0) states[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim(); }
            return states;
        }
        internal static string Status(WindowsComponent profile, IDictionary<string, string> states)
        {
            if (profile.Features.Any(f => !states.ContainsKey(f))) return "Недоступно в этой Windows";
            if (profile.Features.Any(f => states[f].IndexOf("Pending", StringComparison.OrdinalIgnoreCase) >= 0)) return "Ожидает перезагрузки";
            int enabled = profile.Features.Count(f => states[f] == "Enabled");
            if (enabled == profile.Features.Length) return "Установлено";
            if (profile.Features.Any(f => states[f] != "Enabled" && states[f] != "Disabled" && states[f] != "DisabledWithPayloadRemoved" && states[f] != "Removed")) return "Нужна проверка";
            return enabled == 0 ? "Не установлено" : "Частично: " + enabled + " из " + profile.Features.Length;
        }
    }
    internal static class ManagerSystemActions
    {
        internal static bool Known(string action) { return action == "features-query" || action == "features-enable" || action == "tools-deploy" || action == "system-restart"; }
        internal static LibraryInstallResult Execute(string action, string data, string path, Action<string> progress)
        {
            if (!Known(action)) throw new InvalidDataException("Неизвестное действие менеджера");
            LibraryCancellation.Check();
            using (var gate = new Mutex(false, @"Global\BARS.Library.Installation"))
            {
                bool acquired; try { acquired = gate.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new InvalidOperationException("Другая операция менеджера ещё выполняется");
                try
                {
                    if (action == "features-query") return WindowsComponents.Run(WindowsComponents.QueryScript(), progress);
                    if (action == "features-enable") return WindowsComponents.Run(WindowsComponents.EnableScript(data), progress);
                    if (action == "tools-deploy") { ToolsDeployment.Copy(data, path, progress, () => LibraryCancellation.Requested); return new LibraryInstallResult { ExitCode = 0, Message = "Утилиты скопированы", State = LibraryState.Installed }; }
                    if (data != "restart-confirmed" || path != "") throw new InvalidDataException("Перезагрузка не подтверждена");
                    var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), "/r /t 0") { UseShellExecute = false, CreateNoWindow = true };
                    using (var process = Process.Start(start)) { process.WaitForExit(); if (process.ExitCode != 0) throw new InvalidOperationException("Windows не приняла запрос перезагрузки: " + process.ExitCode); }
                    return new LibraryInstallResult { ExitCode = 0, Message = "Запрошена перезагрузка Windows", State = LibraryState.Unknown };
                }
                finally { gate.ReleaseMutex(); }
            }
        }
    }
}
