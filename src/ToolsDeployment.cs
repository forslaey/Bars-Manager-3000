using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        internal string[] Executables;
    }
    internal sealed class ToolCopyRequest
    {
        public string Source {get;set;}
        public string[] Packages {get;set;}
        public string[] ExcludedDirectories {get;set;}
    }
    internal static class ToolsDeployment
    {
        internal static string ShortcutExecutable(string package)
        {
            if(package.StartsWith("STU-430",StringComparison.OrdinalIgnoreCase))return null;
            var entries=new[]{
                new[]{"TurboWriter","TurboWriter.exe"}, new[]{"WinSCP","WinSCP.exe"},
                new[]{"Termite","termite-3.4.exe"}, new[]{"R-Tool","R-Tool.exe"}, new[]{"R_Tool","R-Tool.exe"},
                new[]{"mfgtools","MfgTool2.exe"}, new[]{"Piglet","DK-QStart.exe"}, new[]{"fiLinn","cspPanel.exe"}
            };
            return entries.Where(entry=>package.StartsWith(entry[0],StringComparison.OrdinalIgnoreCase))
                .Select(entry=>entry[1]).FirstOrDefault();
        }

        internal static string[] ShortcutCandidates(string root,string executable)
        {
            SafePath(root);
            var pending=new Stack<string>();
            var paths=new List<string>();
            pending.Push(root);
            while(pending.Count!=0)
            {
                string directory=pending.Pop();
                foreach(string child in Directory.EnumerateDirectories(directory))
                    if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)pending.Push(child);
                foreach(string file in Directory.EnumerateFiles(directory))
                    if(Path.GetFileName(file).Equals(executable,StringComparison.OrdinalIgnoreCase)&&
                        (File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)
                        paths.Add(file.Substring(root.Length+1));
            }
            return paths.OrderBy(path=>path,StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        internal static List<BarsTool> Discover(string source,ISet<string> selected=null)
        {
            source = Path.GetFullPath(source); SafePath(source);
            if (!String.Equals(Path.GetFileName(source.TrimEnd('\\')), "_tools", StringComparison.OrdinalIgnoreCase)) throw new IOException("Выбери папку _tools внутри комплекта BarsManager3000");
            var result = new List<BarsTool>();
            string[] prefixes = { "fiLinn", "Piglet", "WinSCP" }, executables = { "cspPanel.exe", "DK-QStart.exe", "WinSCP.exe" };
            for (int i = 0; i < prefixes.Length; i++)
            {
                string prefix = prefixes[i], exe = executables[i];
                string[] matches = System.IO.Directory.GetDirectories(source).Where(d => (selected==null||selected.Contains(Path.GetFileName(d)))&& Path.GetFileName(d).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(d, exe))).ToArray();
                if (selected==null&&matches.Length!=1) throw new IOException("Для " + prefix + " нужна ровно одна папка с " + exe + ". Найдено: " + matches.Length);
                foreach(string match in matches)
                {
                    SafePath(match);SafePath(Path.Combine(match,exe));
                    if(i==0&&!File.Exists(Path.Combine(match,"tftpd32.exe")))throw new FileNotFoundException("В папке fiLinn отсутствует tftpd32.exe");
                    string name=Path.GetFileName(match);
                    result.Add(new BarsTool { Name=matches.Length>1?name:prefix,Directory=name,Executable=exe });
                }
            }
                        if(selected!=null)
            {
                foreach(string name in selected.Where(name=>!result.Any(tool=>String.Equals(tool.Directory,name,StringComparison.OrdinalIgnoreCase))))
                {
                    string folder=YandexBundleSource.LocalPath(source,"/"+name);
                    if(!String.Equals(Path.GetDirectoryName(folder),source.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)||!Directory.Exists(folder))
                        throw new DirectoryNotFoundException("Папка набора не найдена: "+name);
                    SafePath(folder);
                    result.Add(new BarsTool { Name=name,Directory=name });
                }
            }
            return result;
        }
        internal static List<BarsTool> Launchers(string source,ISet<string> selected=null,bool includeDeclared=true)
        {
            var tools=Discover(source,selected);
            var result = tools.Where(tool=>!String.IsNullOrEmpty(tool.Executable)).ToList();
            if(includeDeclared)
            {
                foreach(var tool in tools.Where(tool=>String.IsNullOrEmpty(tool.Executable)))
                {
                    string folder=Path.Combine(source,tool.Directory);
                    string descriptor=Path.Combine(folder,"_shortcut.txt");
                    if(!File.Exists(descriptor))continue;
                    SafePath(descriptor);
                    if(new FileInfo(descriptor).Length>4096)throw new IOException("Слишком длинное описание ярлыка: "+tool.Directory);
                    string relative=File.ReadAllText(descriptor).Trim().Replace('\\','/');
                    string exe=YandexBundleSource.LocalPath(folder,"/"+relative);
                    if(!Path.GetExtension(exe).Equals(".exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(exe))
                        throw new IOException("Файл ярлыка не найден в наборе: "+tool.Directory);
                    SafePath(exe);
                    result.Add(new BarsTool{Name=tool.Name,Directory=tool.Directory,Executable=relative});
                }
            }
            var filinn=result.Where(tool=>tool.Executable.Equals("cspPanel.exe",StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach(var tool in filinn)
                result.Add(new BarsTool {Name=filinn.Length==1?"tftpd32":tool.Directory+" - tftpd32",Directory=tool.Directory,Executable="tftpd32.exe"});
            return result;
        }
        internal static void SafePath(string path)
        {
            for (string current = Path.GetFullPath(path); !String.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Ссылки и точки подключения не поддерживаются: " + current);
        }
        // Default location shown to the operator; the full path stays editable.
        internal const string DefaultTarget = @"C:\DataKrat\_tools";
        internal static string Target(string drive)
        {
            string full = Path.GetFullPath(drive);
            if (!String.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase) || full.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException("Выбери локальный диск");
            return Path.Combine(full, @"DataKrat\_tools");
        }
        // Validates an operator-chosen absolute path. UNC shares, relative paths and reparse points
        // are refused; the folder name itself is forced to _tools.
        internal static string TargetFolder(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new IOException("Укажи полный путь для распаковки");
            string value = path.Trim().Trim('"');
            // GetFullPath would silently resolve a relative value against the process directory,
            // so the operator's input is rejected unless it already names a drive.
            if (!Path.IsPathRooted(value)) throw new IOException("Укажи полный путь с буквой диска, например C:\\DataKrat\\_tools");
            if(value.Length<3 || value[1]!=':' || (value[2]!='\\'&&value[2]!='/')) throw new IOException("Укажи полный локальный путь с буквой диска");
            string full; try { full = Path.GetFullPath(value); } catch (ArgumentException) { throw new IOException("Некорректный путь"); } catch (NotSupportedException) { throw new IOException("Некорректный путь"); }
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException("Сетевые пути не поддерживаются: выбери локальный диск");
            string root = Path.GetPathRoot(full);
            if (String.IsNullOrEmpty(root) || !String.Equals(Path.GetPathRoot(root), root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Укажи полный путь с буквой диска, например C:\\DataKrat\\_tools");
            if (!Directory.Exists(root)) throw new IOException("Диск " + root + " недоступен");
            full = Path.GetFullPath(full);
            if (!Path.GetFileName(full.TrimEnd('\\')).Equals("_tools", StringComparison.OrdinalIgnoreCase)) full = Path.Combine(full, "_tools");
            return full;
        }
        internal static void Copy(string source, string drive, Action<string> progress, Func<bool> cancelled)
        {
            CopyToTarget(source, Target(drive), progress, cancelled);
        }
        internal static void CopyToTarget(string source, string target, Action<string> progress, Func<bool> cancelled,ISet<string> selected=null,IEnumerable<string> excludedDirectories=null)
        {
            target = Path.GetFullPath(target);
            if (Path.GetFileName(target) != "_tools") throw new IOException("Некорректная целевая папка");
            var tools = Discover(source,selected);
            var excluded=new HashSet<string>((excludedDirectories??Enumerable.Empty<string>()).Select(relative=>YandexBundleSource.LocalPath(source,"/"+relative.Replace((char)92,(char)47))),StringComparer.OrdinalIgnoreCase);
            string sourceRoot=Path.GetFullPath(source);
            foreach(string skip in excluded)
                if(!tools.Any(tool=>skip.StartsWith(Path.Combine(sourceRoot,tool.Directory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("Исключённая версия находится вне выбранного набора.");
            if (Path.GetFullPath(source).TrimEnd('\\').Equals(target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new IOException("Исходная папка уже находится в выбранном месте");
            string sourceFull=Path.GetFullPath(source).TrimEnd('\\'),targetFull=target.TrimEnd('\\');
            if(sourceFull.StartsWith(targetFull+"\\",StringComparison.OrdinalIgnoreCase)||targetFull.StartsWith(sourceFull+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Исходная папка и место установки не должны находиться друг внутри друга");
            SafePath(target);
            string parent = Path.GetDirectoryName(target); Directory.CreateDirectory(parent);
            string stage = Path.Combine(parent, ".t" + Guid.NewGuid().ToString("N").Substring(0,8));
            string backup = stage + "-old";
            bool moved = false, published = false;
            try
            {
                Directory.CreateDirectory(stage);
                long[] work=new long[]{0,1+(Directory.Exists(target)?CountWork(target):0)+tools.Sum(tool=>CountWork(Path.Combine(source,tool.Directory),excluded))};
                // Preserve settings and other tools in the staged replacement.
                if (Directory.Exists(target)) CopyTree(target, stage, false, progress, cancelled,work);
                foreach (var tool in tools)
                {
                    CopyTree(Path.Combine(source, tool.Directory), Path.Combine(stage, tool.Directory), true, progress, cancelled,work,excluded);
                    string stagedTool=Path.Combine(stage,tool.Directory);
                    string marker=Path.Combine(stagedTool,".bars-utility");
                    if(File.Exists(marker))File.SetAttributes(marker,FileAttributes.Normal);
                    File.WriteAllText(marker,"BARS Manager 3000");
                    string executable=tool.Executable;
                    string expected=ShortcutExecutable(tool.Directory);
                    if(String.IsNullOrEmpty(executable)&&expected!=null)
                        executable=ShortcutCandidates(stagedTool,expected).LastOrDefault();
                    if(String.IsNullOrEmpty(executable))continue;
                    string ini = Path.Combine(stagedTool,"desktop.ini");
                    if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
                    File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=" + Path.Combine(target, tool.Directory, executable) + ",0\r\n", Encoding.Unicode);
                    File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
                    File.SetAttributes(stagedTool, File.GetAttributes(stagedTool) | FileAttributes.ReadOnly);
                }
                LibraryCancellation.Check(cancelled);
                if (Directory.Exists(target)) { Directory.Move(target, backup); moved = true; }
                try { Directory.Move(stage, target); published = true; }
                catch { if (moved && !Directory.Exists(target)) Directory.Move(backup, target); throw; }
                progress("BARS_FRACTION|"+work[1]+"|"+work[1]+"|Файлы скопированы и сохранены");
                if(moved)
                {
                    try { DeleteStage(backup); }
                    catch(IOException ex){progress("Не удалось удалить временную копию: "+backup+". "+ex.Message);}
                    catch(UnauthorizedAccessException ex){progress("Не удалось удалить временную копию: "+backup+". "+ex.Message);}
                }
                progress("Скопировано: " + target);
            }
            finally
            {
                // Only our own fresh staging tree may be removed. Never delete the user's backup.
                if (!published && Directory.Exists(stage)) DeleteStage(stage);
            }
        }
        private static bool IsSetting(string file)
        { return new[] { ".ini", ".config", ".json", ".xml", ".settings", ".cst" }.Contains(Path.GetExtension(file).ToLowerInvariant()) && !Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase); }
        private static long CountWork(string source,ISet<string> excluded=null){if(excluded!=null&&excluded.Contains(source))return 0;SafePath(source);long count=Directory.GetFiles(source).Count(file=>excluded==null||!excluded.Contains(file));foreach(string child in Directory.GetDirectories(source))count+=CountWork(child,excluded);return count;}
        private static void FileProcessed(long[] work,string file,Action<string> progress){if(work!=null)progress("BARS_FRACTION|"+(++work[0])+"|"+work[1]+"|Обработан файл: "+Path.GetFileName(file));}
        private static void CopyTree(string source, string target, bool preserveSettings, Action<string> progress, Func<bool> cancelled,long[] work=null,ISet<string> excluded=null)
        {
            LibraryCancellation.Check(cancelled); if(excluded!=null&&excluded.Contains(source))return;SafePath(source); Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
            {
                LibraryCancellation.Check(cancelled); if(excluded!=null&&excluded.Contains(file))continue; SafePath(file); string destination = Path.Combine(target, Path.GetFileName(file));
                if (preserveSettings && IsSetting(file) && File.Exists(destination)){FileProcessed(work,file,progress);continue;}
                progress("Копируем: " + Path.GetFileName(file));
                if (File.Exists(destination)) File.SetAttributes(destination, FileAttributes.Normal);
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[1024 * 1024]; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { LibraryCancellation.Check(cancelled); output.Write(buffer, 0, count); }
                    output.Flush(true);
                }
                FileProcessed(work,file,progress);
            }
            foreach (string directory in Directory.GetDirectories(source)) CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)), preserveSettings, progress, cancelled,work,excluded);
        }
        private static void DeleteStage(string directory)
        {
            SafePath(directory);
            foreach (string child in Directory.GetDirectories(directory)) DeleteStage(child);
            foreach (string file in Directory.GetFiles(directory)) { SafePath(file); File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
            File.SetAttributes(directory, FileAttributes.Directory); Directory.Delete(directory);
        }
        internal static void Shortcuts(string source, string target, string desktop)
        {
            EnableAdministratorLaunch(source, target);
            ShortcutsAt(source, target, desktop);
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
        internal static void EnableAdministratorLaunch(string source, string target,ISet<string> selected=null)
        {
            // This is the per-user EXE Compatibility property, not a modification of its
            // bytes/manifest/signature. Execute on the interactive user's UI thread.
            // Microsoft: The Program Compatibility Assistant - Part Two (AskPerf).
            using (var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
            using (var layers = user.CreateSubKey(CompatibilityKey))
                AdministratorSettings(layers, Launchers(source,selected,includeDeclared:false).Select(t => Path.Combine(target, t.Directory, t.Executable)));
        }
        internal static void ShortcutsAt(string source, string target, string desktop, bool create = true,ISet<string> selected=null,IEnumerable<BarsTool> explicitLaunchers=null)
        {
            if(!create)return;
            // Called in the interactive user's STA, never in the administrator helper.
            foreach (var tool in explicitLaunchers??Launchers(source,selected))
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
                        if (!Path.GetFileName(previous).Equals(Path.GetFileName(tool.Executable), StringComparison.OrdinalIgnoreCase) || !previous.StartsWith(target.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                        {
                            Marshal.FinalReleaseComObject(shortcut); shortcut = null;
                            link = Path.Combine(desktop, tool.Name + " " + Guid.NewGuid().ToString("N").Substring(0, 8) + ".lnk");
                            shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                        }
                    }
                    foreach (var property in new[] { new[] { "TargetPath", exe }, new[] { "WorkingDirectory", Path.GetDirectoryName(exe) }, new[] { "IconLocation", exe + ",0" }, new[] { "Description", "Утилита BARS: " + tool.Name } })
                        shortcut.GetType().InvokeMember(property[0], BindingFlags.SetProperty, null, shortcut, new object[] { property[1] });
                    shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                }
                finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); if (shell != null) Marshal.FinalReleaseComObject(shell); }
            }
        }
        // Only recognizable tool directories below the selected _tools folder qualify.
        internal static List<BarsTool> Installed(string target)
        {
            target=Path.GetFullPath(target); if(target.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("Для удаления выбери локальную папку"); SafePath(target);
            if(!Path.GetFileName(target.TrimEnd('\\')).Equals("_tools",StringComparison.OrdinalIgnoreCase)) throw new IOException("Для удаления выбери папку _tools");
            var result=new List<BarsTool>(); if(!Directory.Exists(target))return result;
            foreach(string folder in Directory.GetDirectories(target).OrderBy(path=>path,StringComparer.OrdinalIgnoreCase))
            {
                string name=Path.GetFileName(folder);
                if(name.StartsWith(".")||(File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)continue;
                string executable=ShortcutExecutable(name);
                string marker=Path.Combine(folder,".bars-utility");
                bool managed=File.Exists(marker)&&(File.GetAttributes(marker)&FileAttributes.ReparsePoint)==0&&
                    new FileInfo(marker).Length<=64&&File.ReadAllText(marker)=="BARS Manager 3000";
                var candidates=executable==null?new string[0]:ShortcutCandidates(folder,executable);
                if(!managed&&candidates.Length==0)continue;
                if(name.StartsWith("fiLinn",StringComparison.OrdinalIgnoreCase))
                    candidates=candidates.Concat(ShortcutCandidates(folder,"tftpd32.exe")).ToArray();
                result.Add(new BarsTool{Name=name,Directory=name,Executables=candidates});
            }
            return result;
        }
        internal static List<string> InstalledLaunchers(string target,IEnumerable<BarsTool> tools)
        {
            return tools.SelectMany(tool=>(tool.Executables??new[]{tool.Executable})
                .Where(executable=>!String.IsNullOrEmpty(executable))
                .Select(executable=>Path.Combine(target,tool.Directory,executable))).ToList();
        }
        internal static void Remove(string target,string confirmedDirectories,Action<string> progress)
        {
            target=Path.GetFullPath(target); var tools=Installed(target);
            if(tools.Count==0 || String.Join("\n",tools.Select(t=>t.Directory))!=confirmedDirectories)throw new IOException("Состав утилит изменился. Проверь путь и повтори удаление");
            foreach(string exe in InstalledLaunchers(target,tools))foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))using(process)
            {
                try{if(!process.HasExited && process.MainModule.FileName.StartsWith(target.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Закрой утилиты перед удалением");}
                catch(System.ComponentModel.Win32Exception){throw new IOException("Не удалось проверить работающую утилиту. Закрой её перед удалением");}
            }
            // All resolved removal targets must stay inside the explicitly selected folder.
            foreach(var tool in tools){string directory=Path.GetFullPath(Path.Combine(target,tool.Directory));if(!directory.StartsWith(target.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Путь удаления выходит за пределы _tools");SafePath(directory);}
            LibraryCancellation.Check();
            string stage=Path.Combine(target,".bars-remove-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            var moved=new List<BarsTool>();
            try{foreach(var tool in tools){Directory.Move(Path.Combine(target,tool.Directory),Path.Combine(stage,tool.Directory));moved.Add(tool);}}
            catch{foreach(var tool in moved.AsEnumerable().Reverse())Directory.Move(Path.Combine(stage,tool.Directory),Path.Combine(target,tool.Directory));Directory.Delete(stage);throw;}
            // Backups and unrelated directories/files are deliberately outside this private stage.
            try{DeleteStage(stage);}catch(Exception ex){throw new IOException("Не удалось завершить удаление. Остатки находятся в "+stage+". "+ex.Message);}
            progress("Удалены: "+String.Join(", ",tools.Select(t=>t.Name))+". Посторонние файлы и резервные копии сохранены");
        }
        internal static void RemoveAdministratorSettings(RegistryKey layers,IEnumerable<string> paths)
        {
            foreach(string path in paths)
            {
                string value=Convert.ToString(layers.GetValue(path,""));if(value.Length==0)continue;
                string rest=String.Join(" ",value.Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries).Where(f=>!f.Equals("RUNASADMIN",StringComparison.OrdinalIgnoreCase)));
                if(rest=="~"||rest.Length==0)layers.DeleteValue(path,false);else layers.SetValue(path,rest,RegistryValueKind.String);
            }
        }
        internal static void RemoveReferences(string target,IEnumerable<BarsTool> tools,string desktop)
        {
            var paths=InstalledLaunchers(target,tools);
            using(var user=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,Environment.Is64BitOperatingSystem?RegistryView.Registry64:RegistryView.Registry32))
            using(var layers=user.OpenSubKey(CompatibilityKey,true)){if(layers!=null)RemoveAdministratorSettings(layers,paths);}
            RemoveShortcuts(paths,desktop);
        }
        internal static void RemoveShortcuts(IEnumerable<string> targets,string desktop)
        {
            if(!Directory.Exists(desktop))return;SafePath(desktop);var expected=new HashSet<string>(targets.Select(Path.GetFullPath),StringComparer.OrdinalIgnoreCase);
            foreach(string link in Directory.GetFiles(desktop,"*.lnk"))
            {
                SafePath(link);object shell=null,shortcut=null;
                try
                {
                    shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true));shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{link});
                    string previous=Convert.ToString(shortcut.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null));
                    if(!String.IsNullOrWhiteSpace(previous)&&expected.Contains(Path.GetFullPath(previous)))File.Delete(link);
                }
                finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
            }
        }
        internal static string FindSource()
        {
            return BundleFolders.FindSibling("_tools");
        }
    }
}
