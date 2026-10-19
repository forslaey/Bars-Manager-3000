using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace ServiceTool
{
    internal enum LogArchiveScope { All, Today, SelectedFolders }

    internal sealed class LogArchiveSelection
    {
        internal LogArchiveScope Scope;
        internal string[] FolderNames;
        internal DateTime Today = DateTime.Today;
    }

    internal sealed class LogDateFolder
    {
        internal string Name;
        internal DateTime? Date;
    }

    internal sealed class LogArchiveResult
    {
        internal int FileCount { get; private set; }
        internal long TotalBytes { get; private set; }
        internal string LogsDirectory { get; private set; }

        internal LogArchiveResult(int count, long bytes, string directory)
        {
            FileCount = count;
            TotalBytes = bytes;
            LogsDirectory = directory;
        }
    }

    internal static class ServiceLogArchive
    {
        internal static string BuildSuggestedFileName(string serviceName, DateTime collectedAt)
        {
            return SafeName(serviceName, 100) + "_Логи_" + collectedAt.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".zip";
        }

        internal static string BuildSuggestedFileName(string serviceName, LogArchiveSelection selection, DateTime collectedAt)
        {
            selection = selection ?? new LogArchiveSelection();
            string date = collectedAt.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            string suffix;
            if (selection.Scope == LogArchiveScope.All) suffix = "Логи_все_собрано_" + date;
            else if (selection.Scope == LogArchiveScope.Today)
                suffix = "Логи_за_" + selection.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "_собрано_" + date;
            else if (selection.Scope == LogArchiveScope.SelectedFolders)
            {
                var names = new HashSet<string>(selection.FolderNames ?? new string[0], StringComparer.OrdinalIgnoreCase);
                if (names.Count == 1)
                {
                    string only = null;
                    foreach (string name in names) only = name;
                    suffix = "Логи_за_" + SafeName(only, 40) + "_собрано_" + date;
                }
                else suffix = "Логи_выбранные_даты-" + names.Count.ToString(CultureInfo.InvariantCulture) + "_собрано_" + date;
            }
            else throw new ArgumentException("Неизвестный режим сбора логов.", "selection");
            return SafeName(serviceName, 100) + "_" + suffix + ".zip";
        }

        private static string SafeName(string value, int maximumLength)
        {
            string name = (value ?? String.Empty).Trim();
            var safe = new StringBuilder();
            const string invalid = "<>:\"/\\|?*";
            foreach (char character in name)
                safe.Append(character < 32 || invalid.IndexOf(character) >= 0 ? '_' : character);
            name = safe.ToString().Trim().TrimEnd('.', ' ');
            if (name.Length == 0) name = "Служба";
            if (name.Length > maximumLength)
            {
                name = name.Substring(0, maximumLength);
                if (Char.IsHighSurrogate(name[name.Length - 1])) name = name.Substring(0, name.Length - 1);
                name = name.TrimEnd('.', ' ');
            }
            string firstPart = name.Split('.')[0];
            if (IsDeviceName(firstPart)) name = "_" + name;
            return name;
        }

        // An inexpensive validation before the Save As dialog; Create checks again afterwards.
        internal static string ValidateSource(string executablePath)
        {
            return ValidateSource(executablePath, null, CancellationToken.None);
        }

        internal static string ValidateSource(string executablePath, LogArchiveSelection selection, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = GetLogsDirectory(executablePath);
            List<string> files = FindSelectedFiles(directory, selection, cancellationToken, true);
            EnsureFilesFound(files, selection);
            return directory;
        }

        internal static List<LogDateFolder> ListDateFolders(string executablePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = GetLogsDirectory(executablePath);
            var result = new List<LogDateFolder>();
            foreach (string child in OrdinaryChildDirectories(directory, cancellationToken))
            {
                if (FindFiles(child, cancellationToken, true).Count == 0) continue;
                string name = Path.GetFileName(child);
                result.Add(new LogDateFolder { Name = name, Date = ParseDate(name) });
            }
            result.Sort(delegate(LogDateFolder left, LogDateFolder right)
            {
                int comparison = Nullable.Compare<DateTime>(right.Date, left.Date);
                return comparison == 0 ? StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name) : comparison;
            });
            return result;
        }

        internal static LogArchiveResult Create(string executablePath, string serviceName, string destinationPath)
        {
            return Create(executablePath, serviceName, destinationPath, CancellationToken.None);
        }

        internal static LogArchiveResult Create(string executablePath, string serviceName, string destinationPath,
            CancellationToken cancellationToken)
        {
            return Create(executablePath, serviceName, destinationPath, null, cancellationToken);
        }

        internal static LogArchiveResult Create(string executablePath, string serviceName, string destinationPath,
            LogArchiveSelection selection, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = GetLogsDirectory(executablePath);
            List<string> files = FindSelectedFiles(directory, selection, cancellationToken, false);
            EnsureFilesFound(files, selection);
            if (String.IsNullOrWhiteSpace(destinationPath))
                throw new ArgumentException("Не выбран файл для сохранения архива.", "destinationPath");
            string destination = Path.GetFullPath(destinationPath);
            if (!String.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Для архива выбери файл с расширением .zip.", "destinationPath");
            string parent = Path.GetDirectoryName(destination);
            if (String.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                throw new DirectoryNotFoundException("Папка для сохранения архива не найдена.");

            string staging = Path.Combine(parent, ".bars-logs-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                long totalBytes = 0;
                using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true, Encoding.UTF8))
                    {
                        foreach (string file in files)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string relative = file.Substring(directory.Length + 1);
                            try
                            {
                                EnsureOrdinaryPath(directory, file);
                                // The initial length bounds copying even while the service keeps appending.
                                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read,
                                    FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan))
                                {
                                    long length = input.Length;
                                    ZipArchiveEntry entry = archive.CreateEntry(relative.Replace('\\', '/'), CompressionLevel.Optimal);
                                    DateTime modified = File.GetLastWriteTime(file);
                                    if (modified.Year >= 1980 && modified.Year <= 2107)
                                        entry.LastWriteTime = new DateTimeOffset(modified);
                                    using (Stream compressed = entry.Open())
                                        CopySnapshot(input, compressed, length, cancellationToken);
                                    totalBytes = checked(totalBytes + length);
                                }
                            }
                            catch (Exception error)
                            {
                                if (!(error is IOException) && !(error is UnauthorizedAccessException) &&
                                    !(error is System.Security.SecurityException)) throw;
                                throw new IOException("Не удалось собрать лог «" + relative + "». Архив не сохранён. " + error.Message, error);
                            }
                        }
                    }
                    output.Flush(true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                // The existing archive stays untouched until every log and the ZIP directory are written.
                if (File.Exists(destination)) File.Replace(staging, destination, null);
                else File.Move(staging, destination);
                staging = null;
                return new LogArchiveResult(files.Count, totalBytes, directory);
            }
            finally
            {
                if (staging != null)
                {
                    try { File.Delete(staging); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (System.Security.SecurityException) { }
                }
            }
        }

        private static string GetLogsDirectory(string executablePath)
        {
            if (String.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("Не удалось определить .exe выбранной службы.", "executablePath");
            string executable = Path.GetFullPath(executablePath);
            string parent = Path.GetDirectoryName(executable);
            if (String.IsNullOrEmpty(parent))
                throw new ArgumentException("Не удалось определить папку .exe службы.", "executablePath");
            string directory = Path.Combine(parent, "Logs");
            FileAttributes attributes;
            try { attributes = File.GetAttributes(directory); }
            catch (DirectoryNotFoundException) { throw new InvalidOperationException("Рядом с .exe выбранной службы нет папки Logs."); }
            catch (FileNotFoundException) { throw new InvalidOperationException("Рядом с .exe выбранной службы нет папки Logs."); }
            if ((attributes & FileAttributes.Directory) == 0)
                throw new InvalidOperationException("Рядом с .exe службы Logs не является папкой.");
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Папка Logs является ссылкой. Для сбора нужна обычная папка с логами службы.");
            return directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static List<string> FindSelectedFiles(string root, LogArchiveSelection selection,
            CancellationToken cancellationToken, bool stopAfterFirst)
        {
            if (selection == null || selection.Scope == LogArchiveScope.All)
                return FindFiles(root, cancellationToken, stopAfterFirst);
            var selected = new List<string>();
            List<string> children = OrdinaryChildDirectories(root, cancellationToken);
            if (selection.Scope == LogArchiveScope.Today)
            {
                foreach (string child in children)
                {
                    DateTime? date = ParseDate(Path.GetFileName(child));
                    if (date.HasValue && date.Value.Date == selection.Today.Date) selected.Add(child);
                }
            }
            else if (selection.Scope == LogArchiveScope.SelectedFolders)
            {
                var available = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string child in children) available[Path.GetFileName(child)] = child;
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in selection.FolderNames ?? new string[0])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (String.IsNullOrWhiteSpace(name) || name == "." || name == ".." ||
                        name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                        !String.Equals(name, name.TrimEnd('.', ' '), StringComparison.Ordinal))
                        throw new ArgumentException("Выбранная папка должна находиться непосредственно внутри Logs.", "selection");
                    if (!names.Add(name)) continue;
                    string child;
                    if (!available.TryGetValue(name, out child))
                        throw new InvalidOperationException("Выбранная папка логов «" + name + "» не найдена или является ссылкой.");
                    selected.Add(child);
                }
                if (selected.Count == 0) throw new InvalidOperationException("Выбери хотя бы одну папку с логами.");
            }
            else throw new ArgumentException("Неизвестный режим сбора логов.", "selection");
            var files = new List<string>();
            foreach (string child in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                files.AddRange(FindFiles(child, cancellationToken, stopAfterFirst));
                if (stopAfterFirst && files.Count != 0) return files;
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private static void EnsureFilesFound(List<string> files, LogArchiveSelection selection)
        {
            if (files.Count != 0) return;
            if (selection != null && selection.Scope == LogArchiveScope.Today)
                throw new InvalidOperationException("В Logs нет TXT/LOG-логов в папках за " +
                    selection.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".");
            if (selection != null && selection.Scope == LogArchiveScope.SelectedFolders)
                throw new InvalidOperationException("В выбранных папках Logs нет файлов TXT или LOG для архива.");
            throw new InvalidOperationException("В папке Logs нет файлов TXT или LOG для архива.");
        }

        private static List<string> OrdinaryChildDirectories(string root, CancellationToken cancellationToken)
        {
            var result = new List<string>();
            try
            {
                EnsureOrdinaryPath(root, root);
                foreach (string child in Directory.EnumerateDirectories(root))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) result.Add(child);
                }
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) &&
                    !(error is System.Security.SecurityException)) throw;
                throw new IOException("Не удалось прочитать папку логов «" + root + "». " + error.Message, error);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static DateTime? ParseDate(string name)
        {
            DateTime date;
            return DateTime.TryParseExact(name, new string[] {
                "yyyy.MM.dd", "yyyy-MM-dd", "yyyy_MM_dd", "dd.MM.yyyy", "dd-MM-yyyy", "dd_MM_yyyy", "yyyyMMdd"
            }, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? (DateTime?)date.Date : null;
        }

        private static List<string> FindFiles(string root, CancellationToken cancellationToken, bool stopAfterFirst)
        {
            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pending.Pop();
                try
                {
                    // Recheck every parent too: a directory may have changed since enumeration.
                    EnsureOrdinaryPath(root, directory);
                    foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        FileAttributes attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                        else if (String.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase) ||
                            String.Equals(Path.GetExtension(path), ".log", StringComparison.OrdinalIgnoreCase))
                        {
                            files.Add(path);
                            if (stopAfterFirst) return files;
                        }
                    }
                }
                catch (Exception error)
                {
                    if (!(error is IOException) && !(error is UnauthorizedAccessException) &&
                        !(error is System.Security.SecurityException)) throw;
                    throw new IOException("Не удалось прочитать папку логов «" + directory + "». " + error.Message, error);
                }
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private static void EnsureOrdinaryPath(string root, string path)
        {
            string prefix = root + Path.DirectorySeparatorChar;
            if (!String.Equals(path, root, StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Путь лога выходит за пределы папки Logs.");
            string current = path;
            while (true)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Путь лога стал ссылкой. Повтори сбор после проверки папки Logs.");
                if (String.Equals(current, root, StringComparison.OrdinalIgnoreCase)) return;
                current = Path.GetDirectoryName(current);
            }
        }

        private static void CopySnapshot(Stream input, Stream output, long length, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[81920];
            long remaining = length;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) throw new IOException("Файл был усечён во время чтения. Повтори сбор логов.");
                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private static bool IsDeviceName(string value)
        {
            string name = value.ToUpperInvariant();
            if (name == "CON" || name == "PRN" || name == "AUX" || name == "NUL" || name == "CLOCK$") return true;
            return name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) &&
                (name[3] >= '1' && name[3] <= '9' || name[3] == '¹' || name[3] == '²' || name[3] == '³');
        }
    }
}
