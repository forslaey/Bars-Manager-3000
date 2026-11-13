using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using System.Linq;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ServiceTool
{
    internal sealed class SqlInstallStep
    {
        public string label { get; set; }
        public string file { get; set; }
    }

    internal static class SqlInstallPackages
    {
        internal const string ManifestName = "package.json";

        internal static string Resolve(string folder, string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
                throw new InvalidDataException("Укажи относительный путь к установщику.");
            string path = YandexBundleSource.LocalPath(folder, "/" + relative.Replace('\\', '/'));
            string extension = Path.GetExtension(path);
            if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".iso", StringComparison.OrdinalIgnoreCase) && !IsArchive(path))
                throw new InvalidDataException("Выбери установщик EXE, образ ISO или комплект ZIP.");
            return path;
        }

        internal static ILookup<string,string> ExpectedFiles()
        {
            return BundleCloudSource.Baseline().Where(item=>BundleSqlFolders.Name(item)!=null&&IsInstallerFile(item.Path))
                .ToLookup(item=>BundleSqlFolders.Name(item),item=>String.Join("/",item.Path.Split('/').Skip(4)),StringComparer.OrdinalIgnoreCase);
        }

        internal static SqlInstallStep[] Read(string folder, ILookup<string,string> catalog=null)
        {
            string manifest = YandexBundleSource.LocalPath(folder, "/" + ManifestName);
            if (File.Exists(manifest))
            {
                if (new FileInfo(manifest).Length > 65536) throw new InvalidDataException("Манифест набора слишком большой.");
                var fields = new JavaScriptSerializer().Deserialize<Dictionary<string, SqlInstallStep[]>>(File.ReadAllText(manifest));
                SqlInstallStep[] steps;
                if (fields == null || !fields.TryGetValue("steps", out steps) || steps == null || steps.Length == 0 || steps.Length > 100)
                    throw new InvalidDataException("В манифесте нет корректного списка шагов.");
                foreach (var step in steps)
                {
                    if (step == null || String.IsNullOrWhiteSpace(step.label)) throw new InvalidDataException("У шага отсутствует название.");
                    Resolve(folder, step.file);
                }
                return steps;
            }
            var expected = (catalog??ExpectedFiles())[Path.GetFileName(folder)];
            return Discover(folder).Select(path => path.Substring(folder.Length + 1).Replace(Path.DirectorySeparatorChar, '/'))
                .Concat(expected).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(relative => new SqlInstallStep { file = relative, label = Path.GetFileName(relative) })
                .OrderBy(step => Path.GetFileName(step.file).IndexOf("-KB", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenBy(step => step.file, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        static bool IsInstallerFile(string path)
        {
            string name = Path.GetFileName(path);
            return (name.StartsWith("SQLServer", StringComparison.OrdinalIgnoreCase) &&
                (name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) || name.IndexOf("-KB", StringComparison.OrdinalIgnoreCase) >= 0 && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) ||
                name.Equals("SQLEXPRESS_x64_RUS.exe", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("SSMS_", StringComparison.OrdinalIgnoreCase) && IsArchive(name) ||
                (name.StartsWith("SSMS-Setup", StringComparison.OrdinalIgnoreCase) || name.StartsWith("vs_SSMS", StringComparison.OrdinalIgnoreCase)) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        static IEnumerable<string> Discover(string folder)
        {
            foreach (var file in Directory.GetFiles(folder))
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0 && IsInstallerFile(file))
                    yield return file;
            foreach (var child in Directory.GetDirectories(folder))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    foreach (var file in Discover(child)) yield return file;
        }

        internal static void Save(string folder, SqlInstallStep[] steps)
        {
            foreach (var step in steps) Resolve(folder, step.file);
            string target = YandexBundleSource.LocalPath(folder, "/" + ManifestName);
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(new { steps }));
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static string[] Installed()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            {
                const string sql = @"SOFTWARE\Microsoft\Microsoft SQL Server\";
                using (var instances = root.OpenSubKey(sql + @"Instance Names\SQL"))
                    if (instances != null) foreach (string name in instances.GetValueNames())
                    using (var setup = root.OpenSubKey(sql + Convert.ToString(instances.GetValue(name)) + @"\Setup"))
                        result.Add("SQL Server " + name + (setup == null ? "" : " · " + Convert.ToString(setup.GetValue("Version"))));
                using (var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    if (uninstall != null) foreach (string key in uninstall.GetSubKeyNames())
                    using (var product = uninstall.OpenSubKey(key))
                    {
                        string name = product == null ? "" : Convert.ToString(product.GetValue("DisplayName"));
                        if (name.IndexOf("SQL Server Management Studio", StringComparison.OrdinalIgnoreCase) >= 0)
                            result.Add(name + " · " + Convert.ToString(product.GetValue("DisplayVersion")));
                    }
            }
            return result.OrderBy(name => name).ToArray();
        }

        static string DiskImage(string path, string script)
        {
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
            var info = new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -EncodedCommand " +
                Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop';" + script)))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.EnvironmentVariables["BARS_SQL_IMAGE"] = path;
            using (var process = Process.Start(info))
            {
                var error = process.StandardError.ReadToEndAsync();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new IOException("Windows не смогла обработать образ SQL. " + error.GetAwaiter().GetResult().Trim());
                return output.Trim();
            }
        }

        internal static bool IsArchive(string path)
        {
            return String.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);
        }

        internal static int Run(string path)
        {
            bool mounted = false;
            string image = Path.GetExtension(path).Equals(".iso", StringComparison.OrdinalIgnoreCase) ? path : null;
            try
            {
                if (image != null)
                {
                    string mountedByManager = DiskImage(image, "$image=Get-DiskImage -ImagePath $env:BARS_SQL_IMAGE;if(!$image.Attached){Mount-DiskImage -ImagePath $env:BARS_SQL_IMAGE | Out-Null;'mounted'}");
                    mounted = mountedByManager == "mounted";
                    string drive = DiskImage(image, "(Get-DiskImage -ImagePath $env:BARS_SQL_IMAGE | Get-Volume | Where-Object DriveLetter | Select-Object -First 1).DriveLetter");
                    if (drive.Length != 1 || drive[0] < 'A' || drive[0] > 'Z') throw new IOException("У образа нет буквы диска.");
                    path = drive + @":\setup.exe";
                }
                using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    SoftwareTrust.Signature(path, "Microsoft Corporation");
                    string arguments=Path.GetFileName(path).StartsWith("vs_SSMS",StringComparison.OrdinalIgnoreCase)
                        ? (File.Exists(Path.Combine(Path.GetDirectoryName(path),"Layout.json"))?"--wait --noWeb":"--wait") : "";
                    using (var process = Process.Start(new ProcessStartInfo(path,arguments)
                    { WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = true, Verb = "runas" }))
                    {
                        if (process == null) throw new IOException("Мастер установки не запущен.");
                        process.WaitForExit();
                        return process.ExitCode;
                    }
                }
            }
            finally
            {
                if (mounted) DiskImage(image, "Dismount-DiskImage -ImagePath $env:BARS_SQL_IMAGE | Out-Null");
            }
        }
    }
}
