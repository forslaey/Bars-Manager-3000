using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Xml;

namespace ServiceTool
{
    // This is an append-only catalog of explicitly observed application EXEs, never a guess
    // based on Windows service names. It does not query or modify the Service Control Manager.
    internal sealed class ManagedServiceStore
    {
        private const int MaximumBytes = 1024 * 1024;
        private const int MaximumEntries = 1000;
        private readonly object gate = new object();
        private readonly bool memory;
        private readonly string storagePath;
        private List<string> paths = new List<string>();
        private readonly List<string> pendingPaths = new List<string>();
        private string warning = String.Empty;
        private bool writesDisabled;

        private sealed class DiskDocument
        {
            public byte[] Content; // Null means the file did not exist.
            public List<string> Paths;
        }

        public ManagedServiceStore(string storagePath)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(storagePath)) throw new ArgumentException("Не указан путь каталога .exe служб BARS.");
                foreach (char ch in storagePath)
                    if (ch < 32) throw new ArgumentException("Путь каталога содержит недопустимый символ.");
                this.storagePath = Path.GetFullPath(storagePath);
                paths = ReadDocument(this.storagePath).Paths;
            }
            catch (Exception ex)
            {
                if (!IsStorageError(ex)) throw;
                writesDisabled = true;
                warning = "Не удалось прочитать каталог .exe служб BARS. Исходный файл не изменён; сохранение отключено. Исправь файл и перезапусти утилиту. " + ex.Message;
            }
        }

        private ManagedServiceStore(bool memory, string warning)
        {
            this.memory = memory;
            this.warning = warning;
            writesDisabled = !memory;
        }

        public static ManagedServiceStore CreateDefault()
        {
            try
            {
                string directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (String.IsNullOrWhiteSpace(directory))
                    return new ManagedServiceStore(false, "Папка локальных данных пользователя недоступна; каталог .exe служб BARS не сохраняется.");
                return new ManagedServiceStore(ApplicationStorage.FilePath("services.xml"));
            }
            catch (Exception ex)
            {
                if (!IsStorageError(ex)) throw;
                return new ManagedServiceStore(false, "Каталог .exe служб BARS недоступен: " + ex.Message);
            }
        }

        public static ManagedServiceStore CreateMemory()
        {
            return new ManagedServiceStore(true, String.Empty);
        }

        public string Warning
        {
            get { lock (gate) return warning; }
        }

        public string[] GetExecutablePaths()
        {
            lock (gate) return paths.ToArray();
        }

        // True means the catalog was extended (and, for a disk store, committed successfully).
        // False with an empty Warning means it was already present.
        // A failed save retains valid additions in memory for the rest of this session.
        public bool Remember(string exe)
        {
            lock (gate)
            {
                string normalized;
                try { normalized = NormalizeExecutable(exe, false); }
                catch (Exception ex)
                {
                    if (!IsStorageError(ex)) throw;
                    if (writesDisabled) warning = WithSessionNotice(warning);
                    else warning = "Не удалось добавить .exe службы BARS в каталог. Проверь путь к файлу .exe.";
                    return false;
                }
                if (memory)
                {
                    if (Contains(paths, normalized)) { warning = String.Empty; return false; }
                    if (paths.Count >= MaximumEntries) { SetLimitWarning(); return false; }
                    paths.Add(normalized);
                    warning = String.Empty;
                    return true;
                }
                if (!Contains(paths, normalized))
                {
                    if (paths.Count >= MaximumEntries) { SetLimitWarning(); return false; }
                    paths.Add(normalized);
                    pendingPaths.Add(normalized);
                }
                if (writesDisabled)
                {
                    warning = WithSessionNotice(warning);
                    return false;
                }
                try
                {
                    string directory = Path.GetDirectoryName(storagePath);
                    Directory.CreateDirectory(directory);
                    // All instances of this utility cooperate on a stable sibling lock file.
                    using (var fileLock = new FileStream(storagePath + ".lock", FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None))
                    {
                        for (int attempt = 0; attempt < 3; ++attempt)
                        {
                            DiskDocument current;
                            try { current = ReadDocument(storagePath); }
                            catch (Exception ex)
                            {
                                if (!IsStorageError(ex)) throw;
                                writesDisabled = true;
                                warning = WithSessionNotice("Каталог .exe служб BARS изменён или недоступен. Исходный файл не изменён; сохранение отключено. Исправь файл и перезапусти утилиту. " + ex.Message);
                                return false;
                            }
                            // Re-read under the lock instead of overwriting another instance's additions
                            // or restoring entries an external editor deliberately removed.
                            if (!Contains(current.Paths, normalized) && !Contains(pendingPaths, normalized))
                                pendingPaths.Add(normalized);
                            var merged = new List<string>(current.Paths);
                            foreach (string pending in pendingPaths)
                            {
                                if (Contains(merged, pending)) continue;
                                if (merged.Count >= MaximumEntries) { SetLimitWarning(); return false; }
                                merged.Add(pending);
                            }
                            paths = merged;
                            if (merged.Count == current.Paths.Count)
                            {
                                pendingPaths.Clear();
                                warning = String.Empty;
                                return false;
                            }
                            string temporary = null;
                            try
                            {
                                temporary = WriteTemporary(merged);
                                // An editor that ignores our lock may have changed the XML. Merge again
                                // rather than knowingly replacing its newer contents.
                                if (!SameContent(current.Content, ReadContent(storagePath))) continue;
                                if (current.Content == null) File.Move(temporary, storagePath);
                                else File.Replace(temporary, storagePath, null);
                                temporary = null;
                                paths = merged;
                                pendingPaths.Clear();
                                warning = String.Empty;
                                return true;
                            }
                            finally
                            {
                                if (temporary != null)
                                {
                                    try { File.Delete(temporary); }
                                    catch (IOException) { }
                                    catch (UnauthorizedAccessException) { }
                                    catch (SecurityException) { }
                                }
                            }
                        }
                        warning = WithSessionNotice("Каталог .exe служб BARS одновременно изменяется другим приложением. Повтори сохранение.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    if (!IsStorageError(ex)) throw;
                    warning = WithSessionNotice("Не удалось сохранить каталог .exe служб BARS. Существующий файл не заменён. " + ex.Message);
                    return false;
                }
            }
        }

        private void SetLimitWarning()
        {
            warning = "Каталог служб BARS содержит максимальное число .exe: " + MaximumEntries + ".";
        }

        private static string WithSessionNotice(string message)
        {
            const string notice = "Новые .exe служб BARS остаются только в текущем сеансе.";
            return message.IndexOf(notice, StringComparison.Ordinal) >= 0 ? message : message + " " + notice;
        }

        private static string NormalizeExecutable(string exe, bool requireAbsolute)
        {
            if (requireAbsolute)
            {
                if (String.IsNullOrWhiteSpace(exe)) throw new InvalidDataException("В каталоге отсутствует путь .exe службы BARS.");
                bool absolute = exe.StartsWith(@"\\", StringComparison.Ordinal) ||
                    (exe.Length >= 3 && Char.IsLetter(exe[0]) && exe[1] == ':' && (exe[2] == '\\' || exe[2] == '/'));
                if (!absolute) throw new InvalidDataException("Каталог содержит относительный путь .exe службы BARS.");
            }
            string normalized = ExecutablePaths.Normalize(exe);
            if (!String.Equals(Path.GetExtension(normalized), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("В каталоге разрешены только пути к .exe служб BARS.");
            return normalized;
        }

        private static bool Contains(List<string> items, string value)
        {
            foreach (string item in items)
                if (String.Equals(item, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static DiskDocument ReadDocument(string file)
        {
            byte[] content = ReadContent(file);
            var result = new DiskDocument { Content = content, Paths = new List<string>() };
            if (content == null) return result;
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumBytes,
                MaxCharactersFromEntities = 1024,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };
            var document = new XmlDocument { XmlResolver = null };
            using (var stream = new MemoryStream(content, false))
            using (var reader = XmlReader.Create(stream, settings)) document.Load(reader);
            XmlElement root = document.DocumentElement;
            if (root == null || root.Name != "managedServices" || root.NamespaceURI.Length != 0 ||
                root.Attributes.Count != 1 || root.GetAttribute("version") != "1")
                throw new InvalidDataException("Неизвестный формат каталога .exe служб BARS.");
            int count = 0;
            foreach (XmlNode node in root.ChildNodes)
            {
                var element = node as XmlElement;
                if (element == null || element.Name != "executable" || element.NamespaceURI.Length != 0 ||
                    element.Attributes.Count != 1 || !element.HasAttribute("path") || element.HasChildNodes)
                    throw new InvalidDataException("Некорректная запись каталога .exe служб BARS.");
                if (++count > MaximumEntries) throw new InvalidDataException("Каталог содержит более " + MaximumEntries + " записей.");
                string normalized = NormalizeExecutable(element.GetAttribute("path"), true);
                if (!Contains(result.Paths, normalized)) result.Paths.Add(normalized);
            }
            return result;
        }

        private static byte[] ReadContent(string file)
        {
            try
            {
                using (var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (source.Length > MaximumBytes) throw new InvalidDataException("Каталог .exe служб BARS превышает 1 МБ.");
                    using (var target = new MemoryStream())
                    {
                        var buffer = new byte[8192];
                        int count;
                        while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (target.Length + count > MaximumBytes) throw new InvalidDataException("Каталог .exe служб BARS превышает 1 МБ.");
                            target.Write(buffer, 0, count);
                        }
                        return target.ToArray();
                    }
                }
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        private string WriteTemporary(List<string> values)
        {
            string temporary = storagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var settings = new XmlWriterSettings
                    {
                        Encoding = new UTF8Encoding(false),
                        Indent = true,
                        NewLineChars = "\r\n",
                        CloseOutput = false
                    };
                    using (var writer = XmlWriter.Create(stream, settings))
                    {
                        writer.WriteStartDocument();
                        writer.WriteStartElement("managedServices");
                        writer.WriteAttributeString("version", "1");
                        foreach (string value in values)
                        {
                            writer.WriteStartElement("executable");
                            writer.WriteAttributeString("path", value);
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                        writer.WriteEndDocument();
                    }
                    if (stream.Length > MaximumBytes) throw new InvalidDataException("Каталог .exe служб BARS превышает 1 МБ.");
                    stream.Flush(true);
                }
                return temporary;
            }
            catch
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (SecurityException) { }
                throw;
            }
        }

        private static bool SameContent(byte[] first, byte[] second)
        {
            if (first == null || second == null) return first == second;
            if (first.Length != second.Length) return false;
            for (int i = 0; i < first.Length; ++i)
                if (first[i] != second[i]) return false;
            return true;
        }

        private static bool IsStorageError(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is SecurityException ||
                ex is ArgumentException || ex is NotSupportedException || ex is XmlException;
        }
    }
}
