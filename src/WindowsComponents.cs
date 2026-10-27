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
        internal const string RestartMarker = "BARS_RESTART_REQUIRED";
        private static string FeatureList(string ids)
        {
            return "@(" + String.Join(",", Resolve(ids).Select(n => "'" + n + "'")) + ")";
        }
        internal static string EnableScript(string ids)
        {
            return "$ErrorActionPreference='Stop'; $r=Enable-WindowsOptionalFeature -Online -FeatureName " + FeatureList(ids) +
                " -All -NoRestart; if(@($r | Where-Object { $_.RestartNeeded -eq $true }).Count -gt 0){'" + RestartMarker + "'}";
        }
        // Removal is the exact inverse of the same profile; -All removes parent/child pairs together
        // and -NoRestart keeps Windows servicing under the operator's control.
        internal static string DisableScript(string ids)
        {
            return "$ErrorActionPreference='Stop'; $r=Disable-WindowsOptionalFeature -Online -FeatureName " + FeatureList(ids) +
                " -All -NoRestart; if(@($r | Where-Object { $_.RestartNeeded -eq $true }).Count -gt 0){'" + RestartMarker + "'}";
        }
        // Groups are described for the UI; the underlying profile array keeps stable indices.
        internal static readonly string[] GroupOrder = { "Компоненты для fiLinn, piglet", "Компоненты для модуля «Отчёты для арендаторов»" };
        internal static WindowsComponent[] InGroup(string group)
        {
            return Profiles.Where(p => p.Group == group).ToArray();
        }
        // Read both pipes concurrently: DISM/PowerShell cannot deadlock on a full error pipe.
        // Windows servicing is allowed to finish; killing TrustedInstaller is never attempted.
        internal static LibraryInstallResult Run(string script, Action<string> progress, bool cancellableRead=false)
        {
            // Only a small bootstrap goes on the command line. The real script
            // travels as ASCII base64 on stdin, without a temporary script file.
            const string bootstrap = "$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false);$ErrorActionPreference='Stop';try{$s=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String([Console]::In.ReadToEnd())); & ([scriptblock]::Create($s))}catch{[Console]::Error.WriteLine($_.Exception.Message);exit 1}";
            var start = new ProcessStartInfo(PowerShellPath, "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(bootstrap))) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            // Explicit UTF-8 on the child streams, independent of the machine's ANSI/OEM code page.
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
                process.StandardInput.Write(Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false); $ProgressPreference='SilentlyContinue'; " + script)));
                process.StandardInput.Close();
                while (!process.WaitForExit(cancellableRead?100:1000))
                {
                    if(cancellableRead&&LibraryCancellation.Requested)
                    {
                        try{process.Kill();}catch(InvalidOperationException){}
                        process.WaitForExit();throw new OperationCanceledException("Проверка настроек отменена");
                    }
                    if(!cancellableRead)progress("Windows обрабатывает запрос. Дождись завершения…");
                }
                if(cancellableRead)LibraryCancellation.Check();
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
        internal enum ComponentState { Unknown, Missing, Partial, Installed, Pending, Unavailable }
        internal static ComponentState Classify(WindowsComponent profile, IDictionary<string, string> states)
        {
            if (states == null || states.Count == 0) return ComponentState.Unknown;
            if (profile.Features.Any(f => !states.ContainsKey(f))) return ComponentState.Unavailable;
            if (profile.Features.Any(f => states[f].IndexOf("Pending", StringComparison.OrdinalIgnoreCase) >= 0)) return ComponentState.Pending;
            int enabled = profile.Features.Count(f => states[f] == "Enabled");
            if (enabled == profile.Features.Length) return ComponentState.Installed;
            if (profile.Features.Any(f => states[f] != "Enabled" && states[f] != "Disabled" && states[f] != "DisabledWithPayloadRemoved" && states[f] != "Removed")) return ComponentState.Unknown;
            return enabled == 0 ? ComponentState.Missing : ComponentState.Partial;
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
        internal static bool Known(string action) { return action == "initial-settings" || action == "features-query" || action == "features-enable" || action == "features-disable" || action == "tools-deploy" || action == "tools-remove" || action == "system-restart" || action == "software-install" || action == "software-query" || action == "software-remove" || action == "software-access"; }
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
                    if (action == "initial-settings") return InitialSystemSettings.Run(data,progress);
                    if (action == "software-remove") return SoftwareMaintenance.Remove(data,progress);
                    if (action == "software-access") return SoftwareMaintenance.ConfigureAccess(data,progress);
                    if (action == "software-query") return new LibraryInstallResult {ExitCode=0,State=LibraryState.Unknown,Message=SoftwareDetection.Query(data)};
                    if (action == "software-install") return SoftwareInstaller.Install(data,path,progress);
                    if (action == "features-query") return WindowsComponents.Run(WindowsComponents.QueryScript(), progress);
                    if (action == "features-enable") return WindowsComponents.Run(WindowsComponents.EnableScript(data), progress);
                    if (action == "features-disable") return WindowsComponents.Run(WindowsComponents.DisableScript(data), progress);
                    if (action == "tools-remove") {ToolsDeployment.Remove(path,data,progress);return new LibraryInstallResult {ExitCode=0,State=LibraryState.Missing,Message="Утилиты удалены"};}
                    if (action == "tools-deploy") { ToolsDeployment.CopyToTarget(data, path, progress, () => LibraryCancellation.Requested); return new LibraryInstallResult { ExitCode = 0, Message = "Утилиты скопированы", State = LibraryState.Installed }; }
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
