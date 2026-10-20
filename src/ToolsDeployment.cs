using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ServiceTool
{
    internal sealed class BarsTool
    {
        public string Name, Directory, Executable;
    }
    internal static class ToolsDeployment
    {
        internal static List<BarsTool> Discover(string source)
        {
            source = Path.GetFullPath(source); SafePath(source);
            if (!String.Equals(Path.GetFileName(source.TrimEnd('\\')), "_tools", StringComparison.OrdinalIgnoreCase)) throw new IOException("Выбери папку _tools внутри комплекта библиотек");
            var result = new List<BarsTool>();
            string[] prefixes = { "fiLinn", "Piglet", "WinSCP" }, executables = { "cspPanel.exe", "DK-QStart.exe", "WinSCP.exe" };
            for (int i = 0; i < prefixes.Length; i++)
            {
                string prefix = prefixes[i], exe = executables[i];
                string[] matches = System.IO.Directory.GetDirectories(source).Where(d => Path.GetFileName(d).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(d, exe))).ToArray();
                if (matches.Length != 1) throw new IOException("Для " + prefix + " нужна ровно одна папка с " + exe + ". Найдено: " + matches.Length);
                SafePath(matches[0]); SafePath(Path.Combine(matches[0], exe));
                if (i == 0 && !File.Exists(Path.Combine(matches[0], "tftpd32.exe"))) throw new FileNotFoundException("В папке fiLinn отсутствует tftpd32.exe");
                result.Add(new BarsTool { Name = prefix, Directory = Path.GetFileName(matches[0]), Executable = exe });
            }
            return result;
        }
        internal static List<BarsTool> Launchers(string source)
        {
            var result = Discover(source);
            var filin = result.Single(t => t.Name == "fiLinn");
            result.Insert(1, new BarsTool { Name = "tftpd32", Directory = filin.Directory, Executable = "tftpd32.exe" });
            return result;
        }
        internal static void SafePath(string path)
        {
            for (string current = Path.GetFullPath(path); !String.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Ссылки и точки подключения не поддерживаются: " + current);
        }
        internal static string Target(string drive)
        {
            string full = Path.GetFullPath(drive);
            if (!String.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase) || full.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException("Выбери локальный диск");
            return Path.Combine(full, @"DataKrat\_tools");
        }
        internal static void Copy(string source, string drive, Action<string> progress, Func<bool> cancelled)
        {
            CopyToTarget(source, Target(drive), progress, cancelled);
        }
        internal static void CopyToTarget(string source, string target, Action<string> progress, Func<bool> cancelled)
        {
            target = Path.GetFullPath(target);
            if (Path.GetFileName(target) != "_tools") throw new IOException("Некорректная целевая папка");
            var tools = Discover(source);
            if (Path.GetFullPath(source).TrimEnd('\\').Equals(target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new IOException("Исходная папка уже находится в выбранном месте");
            SafePath(target);
            string parent = Path.GetDirectoryName(target); Directory.CreateDirectory(parent);
            string stage = Path.Combine(parent, ".bars-tools-" + Guid.NewGuid().ToString("N"));
            string backup = target + "-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            bool moved = false, published = false;
            try
            {
                Directory.CreateDirectory(stage);
                // Existing settings and other tools remain intact; save the whole old tree on replacement.
                if (Directory.Exists(target)) CopyTree(target, stage, false, progress, cancelled);
                foreach (var tool in tools)
                {
                    CopyTree(Path.Combine(source, tool.Directory), Path.Combine(stage, tool.Directory), true, progress, cancelled);
                    string ini = Path.Combine(stage, tool.Directory, "desktop.ini");
                    if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
                    File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=" + Path.Combine(target, tool.Directory, tool.Executable) + ",0\r\n", Encoding.Unicode);
                    File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
                    File.SetAttributes(Path.Combine(stage, tool.Directory), File.GetAttributes(Path.Combine(stage, tool.Directory)) | FileAttributes.ReadOnly);
                }
                LibraryCancellation.Check(cancelled);
                if (Directory.Exists(target)) { Directory.Move(target, backup); moved = true; }
                try { Directory.Move(stage, target); published = true; }
                catch { if (moved && !Directory.Exists(target)) Directory.Move(backup, target); throw; }
                progress("Скопировано: " + target + (moved ? ". Предыдущая версия: " + backup : ""));
            }
            finally
            {
                // Only our own fresh staging tree may be removed. Never delete the user's backup.
                if (!published && Directory.Exists(stage)) DeleteStage(stage);
            }
        }
        private static bool IsSetting(string file)
        { return new[] { ".ini", ".config", ".json", ".xml", ".settings", ".cst" }.Contains(Path.GetExtension(file).ToLowerInvariant()) && !Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase); }
        private static void CopyTree(string source, string target, bool preserveSettings, Action<string> progress, Func<bool> cancelled)
        {
            LibraryCancellation.Check(cancelled); SafePath(source); Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
            {
                LibraryCancellation.Check(cancelled); SafePath(file); string destination = Path.Combine(target, Path.GetFileName(file));
                if (preserveSettings && IsSetting(file) && File.Exists(destination)) continue;
                progress("Копируем: " + Path.GetFileName(file));
                if (File.Exists(destination)) File.SetAttributes(destination, FileAttributes.Normal);
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[1024 * 1024]; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { LibraryCancellation.Check(cancelled); output.Write(buffer, 0, count); }
                    output.Flush(true);
                }
            }
            foreach (string directory in Directory.GetDirectories(source)) CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)), preserveSettings, progress, cancelled);
        }
        private static void DeleteStage(string directory)
        {
            SafePath(directory);
            foreach (string child in Directory.GetDirectories(directory)) DeleteStage(child);
            foreach (string file in Directory.GetFiles(directory)) { SafePath(file); File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
            File.SetAttributes(directory, FileAttributes.Directory); Directory.Delete(directory);
        }
        internal static void Shortcuts(string source, string drive, string desktop)
        {
            EnableAdministratorLaunch(source, Target(drive));
            ShortcutsAt(source, Target(drive), desktop);
        }
        internal const string CompatibilityKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        internal static string AdministratorFlags(string current)
        {
            var flags = (current ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(f => !f.Equals("RUNASINVOKER", StringComparison.OrdinalIgnoreCase) && !f.Equals("RUNASHIGHEST", StringComparison.OrdinalIgnoreCase)).ToList();
            if (!flags.Contains("~")) flags.Insert(0, "~");
            if (!flags.Any(f => f.Equals("RUNASADMIN", StringComparison.OrdinalIgnoreCase))) flags.Add("RUNASADMIN");
            return String.Join(" ", flags);
        }
        internal static void AdministratorSettings(RegistryKey key, IEnumerable<string> executables)
        {
            foreach (string executable in executables)
            {
                string path = Path.GetFullPath(executable); SafePath(path);
                if (!File.Exists(path)) throw new FileNotFoundException("Не найден EXE для настройки запуска", path);
                key.SetValue(path, AdministratorFlags(Convert.ToString(key.GetValue(path, ""))), RegistryValueKind.String);
            }
        }
        internal static void EnableAdministratorLaunch(string source, string target)
        {
            // This is the per-user EXE Compatibility property, not a modification of its
            // bytes/manifest/signature. Execute on the interactive user's UI thread.
            // Microsoft: The Program Compatibility Assistant - Part Two (AskPerf).
            using (var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
            using (var layers = user.CreateSubKey(CompatibilityKey))
                AdministratorSettings(layers, Launchers(source).Select(t => Path.Combine(target, t.Directory, t.Executable)));
        }
        internal static void ShortcutsAt(string source, string target, string desktop)
        {
            // Called in the interactive user's STA, never in the administrator helper.
            foreach (var tool in Launchers(source))
            {
                string folder = Path.Combine(target, tool.Directory), exe = Path.Combine(folder, tool.Executable);
                if (!File.Exists(exe)) throw new FileNotFoundException("Утилита не скопирована", exe);
                Directory.CreateDirectory(desktop); SafePath(desktop);
                string link = Path.Combine(desktop, tool.Name + ".lnk");
                object shell = null, shortcut = null;
                try
                {
                    shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                    shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                    if (File.Exists(link))
                    {
                        string previous = Convert.ToString(shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null));
                        if (!Path.GetFileName(previous).Equals(tool.Executable, StringComparison.OrdinalIgnoreCase) || !previous.StartsWith(target.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                        {
                            Marshal.FinalReleaseComObject(shortcut); shortcut = null;
                            link = Path.Combine(desktop, tool.Name + " " + Guid.NewGuid().ToString("N").Substring(0, 8) + ".lnk");
                            shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                        }
                    }
                    foreach (var property in new[] { new[] { "TargetPath", exe }, new[] { "WorkingDirectory", folder }, new[] { "IconLocation", exe + ",0" }, new[] { "Description", "Утилита BARS: " + tool.Name } })
                        shortcut.GetType().InvokeMember(property[0], BindingFlags.SetProperty, null, shortcut, new object[] { property[1] });
                    shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                }
                finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); if (shell != null) Marshal.FinalReleaseComObject(shell); }
            }
        }
        internal static string FindSource()
        {
            string root = LibrarySource.AutoDetect(AppDomain.CurrentDomain.BaseDirectory);
            if (root == null) { string saved = ApplicationStorage.FilePath("library-source.txt"); if (File.Exists(saved)) root = File.ReadAllText(saved).Trim(); }
            return root == null ? null : Path.Combine(root, "_tools");
        }
    }
}
