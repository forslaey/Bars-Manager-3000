using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ServiceTool
{
    internal static class LogArchiveTests
    {
        private static int checks;
        private static int skipped;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CreateSymbolicLink(string link, string target, int flags);

        private static void Assert(bool value, string message)
        {
            ++checks;
            if (!value) throw new Exception("FAIL: " + message);
        }

        private static T Expect<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T error)
            {
                Assert(!String.IsNullOrWhiteSpace(error.Message), message + " explains the failure");
                return error;
            }
            throw new Exception("FAIL: " + message + " must fail with " + typeof(T).Name);
        }

        private static byte[] Bytes(string value)
        {
            return new UTF8Encoding(false).GetBytes(value);
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; ++i)
                if (left[i] != right[i]) return false;
            return true;
        }

        private static byte[] ReadEntry(ZipArchiveEntry entry)
        {
            using (Stream input = entry.Open())
            using (var output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
            }
        }

        private static Dictionary<string, byte[]> ReadZip(string path)
        {
            var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    Assert(!contents.ContainsKey(entry.FullName), "ZIP has no duplicate relative names");
                    Assert(!entry.FullName.StartsWith("/", StringComparison.Ordinal)
                        && entry.FullName.IndexOf(':') < 0
                        && entry.FullName.IndexOf('\\') < 0
                        && !entry.FullName.StartsWith("../", StringComparison.Ordinal)
                        && entry.FullName.IndexOf("/../", StringComparison.Ordinal) < 0,
                        "ZIP entry remains a portable relative path");
                    contents.Add(entry.FullName, ReadEntry(entry));
                }
            }
            return contents;
        }

        private static string NewService(string fixture, string name)
        {
            string directory = Path.Combine(fixture, name);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, name + ".exe");
        }

        private static void CheckSuggestedNames()
        {
            DateTime date = new DateTime(2026, 10, 15, 16, 7, 9);
            string localized = ServiceLogArchive.BuildSuggestedFileName("Служба оплаты BARS", date);
            Assert(localized.IndexOf("Служба оплаты BARS", StringComparison.Ordinal) >= 0,
                "suggested name retains the recognizable Cyrillic service name");
            Assert(localized.IndexOf("2026-10-15", StringComparison.Ordinal) >= 0,
                "suggested name identifies the collection date");
            Assert(localized.IndexOf("Логи", StringComparison.OrdinalIgnoreCase) >= 0,
                "suggested name identifies its log contents");

            string[] names = { null, "", "   ", "CON", "CON.txt", "PRN", "AUX", "NUL", "COM1", "LPT9",
                "../BARS\\HTTP:Служба*?\"<>|", "точка. ", "BARS\u0001\u0002\r\nHTTP", new string('Я', 1000) };
            foreach (string name in names)
            {
                string file = ServiceLogArchive.BuildSuggestedFileName(name, date);
                Assert(!String.IsNullOrWhiteSpace(file) && file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
                    "suggested filename is a nonempty ZIP name");
                Assert(file.Length < 256 && file == Path.GetFileName(file)
                    && file.IndexOfAny(Path.GetInvalidFileNameChars()) < 0,
                    "suggested filename fits a Windows filename without path/control characters");
                Assert(!file.EndsWith(".", StringComparison.Ordinal) && !file.EndsWith(" ", StringComparison.Ordinal),
                    "suggested filename has no forbidden Windows suffix");
                string deviceStem = file.Split('.')[0].ToUpperInvariant();
                bool device = deviceStem == "CON" || deviceStem == "PRN" || deviceStem == "AUX" || deviceStem == "NUL"
                    || (deviceStem.Length == 4 && (deviceStem.StartsWith("COM", StringComparison.Ordinal)
                        || deviceStem.StartsWith("LPT", StringComparison.Ordinal))
                        && deviceStem[3] >= '1' && deviceStem[3] <= '9');
                Assert(!device, "suggested filename avoids reserved Windows devices");
            }
        }

        private static void CheckSourceFailures(string fixture)
        {
            string missingExe = NewService(fixture, "БезЛогов");
            string destination = Path.Combine(fixture, "preserved.zip");
            byte[] original = Bytes("Existing user file must survive failed log collection.");
            File.WriteAllBytes(destination, original);
            Exception missing = Expect<InvalidOperationException>(delegate {
                ServiceLogArchive.Create(missingExe, "Без логов", destination);
            }, "missing Logs directory");
            Assert(missing.Message.IndexOf("Logs", StringComparison.OrdinalIgnoreCase) >= 0,
                "missing-source message identifies the required Logs folder");
            Assert(Equal(original, File.ReadAllBytes(destination)), "missing Logs does not overwrite a saved destination");

            string emptyExe = NewService(fixture, "ПустыеЛоги");
            string logs = Path.Combine(Path.GetDirectoryName(emptyExe), "Logs");
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, "settings.config"), "not a supported log");
            Exception empty = Expect<InvalidOperationException>(delegate {
                ServiceLogArchive.Create(emptyExe, "Пустые логи", destination);
            }, "Logs without TXT or LOG files");
            Assert(empty.Message.IndexOf("txt", StringComparison.OrdinalIgnoreCase) >= 0
                && empty.Message.IndexOf("log", StringComparison.OrdinalIgnoreCase) >= 0,
                "empty-source message identifies both supported log extensions");
            Assert(Equal(original, File.ReadAllBytes(destination)), "empty log selection preserves an existing destination");
        }

        private static void CheckArchiveAndActiveWriter(string fixture)
        {
            // A missing binary is acceptable: service registration can outlive a removed .exe.
            string exe = NewService(fixture, "Архивация");
            string logs = Path.Combine(Path.GetDirectoryName(exe), "Logs");
            string nested = Path.Combine(logs, "За октябрь", "Подкаталог");
            Directory.CreateDirectory(nested);
            byte[] first = Bytes("2026-10-15 Начало сеанса\r\nОплата принята: 123,45 ₽\r\n");
            byte[] second = new byte[180000];
            for (int i = 0; i < second.Length; ++i) second[i] = (byte)((i * 17 + 23) % 251);
            byte[] active = Bytes("Открытый действующей службой журнал\r\nПолная строка\r\n");
            byte[] diagnostic = Bytes("HttpService trace: проверка текстового LOG-файла\r\n");
            File.WriteAllBytes(Path.Combine(logs, "Оплата.txt"), first);
            File.WriteAllBytes(Path.Combine(nested, "Подробности.TXT"), second);
            File.WriteAllText(Path.Combine(logs, "secret.ini"), "exclude INI");
            File.WriteAllBytes(Path.Combine(nested, "Trace.LoG"), diagnostic);
            File.WriteAllText(Path.Combine(logs, "settings.config"), "exclude CONFIG");
            File.WriteAllBytes(Path.Combine(nested, "module.dll"), new byte[] { 77, 90, 0, 1 });
            File.WriteAllText(Path.Combine(logs, "not-text.txt.bak"), "exclude suffix BAK");
            File.WriteAllText(Path.Combine(logs, "not-text.log.bak"), "exclude log backup suffix BAK");
            string activePath = Path.Combine(logs, "Текущий.tXt");
            string destination = Path.Combine(fixture, "Архив.zip");

            Assert(!File.Exists(exe), "fixture does not create or launch a real service executable");
            Assert(String.Equals(ServiceLogArchive.ValidateSource(exe), logs, StringComparison.OrdinalIgnoreCase),
                "source is Logs beside the selected service executable");
            LogArchiveResult result;
            using (var writer = new FileStream(activePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                writer.Write(active, 0, active.Length);
                writer.Flush();
                result = ServiceLogArchive.Create(exe, "Служба оплаты", destination);
                writer.WriteByte(33); // Collection did not close or replace the service's writer.
                writer.Flush();
            }

            Assert(result.FileCount == 4, "result counts recursive TXT and LOG files case-insensitively");
            Assert(result.TotalBytes == first.Length + second.Length + active.Length + diagnostic.Length,
                "result reports the complete uncompressed byte count at collection");
            Assert(String.Equals(result.LogsDirectory, logs, StringComparison.OrdinalIgnoreCase),
                "result identifies the selected service log directory");
            Dictionary<string, byte[]> entries = ReadZip(destination);
            Assert(entries.Count == 4 && !entries.ContainsKey("settings.config")
                && !entries.ContainsKey("За октябрь/Подкаталог/module.dll"),
                "ZIP excludes configuration, DLL, backup and other non-log extensions");
            Assert(entries.ContainsKey("Оплата.txt") && Equal(entries["Оплата.txt"], first),
                "ZIP retains Cyrillic root name and exact log bytes");
            Assert(entries.ContainsKey("За октябрь/Подкаталог/Подробности.TXT")
                && Equal(entries["За октябрь/Подкаталог/Подробности.TXT"], second),
                "ZIP retains nested Cyrillic paths and a larger complete log");
            Assert(entries.ContainsKey("Текущий.tXt") && Equal(entries["Текущий.tXt"], active),
                "open shared-writer log is captured without later bytes");
            Assert(entries.ContainsKey("За октябрь/Подкаталог/Trace.LoG")
                && Equal(entries["За октябрь/Подкаталог/Trace.LoG"], diagnostic),
                "mixed-case LOG extension retains the complete text log and relative folder");
            Assert(new FileInfo(activePath).Length == active.Length + 1,
                "the application's writer remains usable after log collection");
            Expect<ArgumentException>(delegate {
                ServiceLogArchive.Create(exe, "Служба оплаты", Path.Combine(fixture, "wrong.txt"));
            }, "non-ZIP destination");
            Assert(!File.Exists(Path.Combine(fixture, "wrong.txt")), "invalid output choice creates no misleading file");

            byte[] beforeCancel = File.ReadAllBytes(destination);
            int outputsBeforeCancel = Directory.GetFiles(fixture).Length;
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Expect<OperationCanceledException>(delegate {
                    ServiceLogArchive.Create(exe, "Служба оплаты", destination, cancellation.Token);
                }, "canceled collection");
            }
            Assert(Equal(beforeCancel, File.ReadAllBytes(destination)),
                "canceling collection preserves a previously saved ZIP");
            Assert(Directory.GetFiles(fixture).Length == outputsBeforeCancel,
                "canceling collection leaves no partial archive beside the destination");

            // Replacing an existing destination succeeds only once all source files are readable.
            byte[] before = File.ReadAllBytes(destination);
            using (var locked = new FileStream(Path.Combine(logs, "Недоступен.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                locked.WriteByte(42);
                locked.Flush();
                Expect<IOException>(delegate {
                    ServiceLogArchive.Create(exe, "Служба оплаты", destination);
                }, "locked source file");
                Assert(Equal(before, File.ReadAllBytes(destination)),
                    "a mid-collection error preserves the previous complete ZIP byte-for-byte");
            }
            LogArchiveResult replacement = ServiceLogArchive.Create(exe, "Служба оплаты", destination);
            Dictionary<string, byte[]> replaced = ReadZip(destination);
            Assert(replacement.FileCount == 5 && replaced.Count == 5,
                "successful subsequent collection replaces the old ZIP with the full new set");
            Assert(replaced.ContainsKey("Недоступен.txt") && Equal(replaced["Недоступен.txt"], new byte[] { 42 }),
                "successful replacement includes the previously unavailable log");
        }

        private static void CheckReparsePoints(string fixture)
        {
            string exe = NewService(fixture, "Ссылки");
            string logs = Path.Combine(Path.GetDirectoryName(exe), "Logs");
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, "Свой.txt"), "own log", new UTF8Encoding(false));
            string outside = Path.Combine(fixture, "ЧужиеЛоги");
            Directory.CreateDirectory(outside);
            string externalLog = Path.Combine(outside, "ВнеПапки.txt");
            File.WriteAllText(externalLog, "outside data", new UTF8Encoding(false));
            bool dirLink = CreateSymbolicLink(Path.Combine(logs, "ВнешняяПапка"), outside, 1 | 2);
            int dirError = Marshal.GetLastWin32Error();
            bool fileLink = CreateSymbolicLink(Path.Combine(logs, "ВнешнийФайл.txt"), externalLog, 2);
            int fileError = Marshal.GetLastWin32Error();
            if (!dirLink && !fileLink)
            {
                ++skipped;
                Console.WriteLine("SKIP reparse fixture: symbolic links unavailable (Windows " + dirError + "/" + fileError + ").");
                return;
            }
            string destination = Path.Combine(fixture, "БезВнешнихЛогов.zip");
            LogArchiveResult result = ServiceLogArchive.Create(exe, "Ссылки", destination);
            Dictionary<string, byte[]> entries = ReadZip(destination);
            Assert(result.FileCount == 1 && entries.Count == 1 && entries.ContainsKey("Свой.txt"),
                "reparse links cannot include files outside the selected Logs directory");
            if (!dirLink || !fileLink)
            {
                ++skipped;
                Console.WriteLine("SKIP one reparse fixture: directory=" + dirLink + ", file=" + fileLink + ".");
            }
        }

        private static void ExpectInvalidSelection(string exe, string destination, LogArchiveSelection selection, string message)
        {
            byte[] before = File.ReadAllBytes(destination);
            try
            {
                ServiceLogArchive.Create(exe, "Выбор дат", destination, selection, CancellationToken.None);
            }
            catch (Exception error)
            {
                if (!(error is ArgumentException) && !(error is InvalidOperationException)) throw;
                Assert(!String.IsNullOrWhiteSpace(error.Message), message + " explains the invalid selection");
                Assert(Equal(before, File.ReadAllBytes(destination)), message + " preserves an existing destination");
                return;
            }
            throw new Exception("FAIL: " + message + " must reject the selection");
        }

        private static void CheckDateScopes(string fixture)
        {
            string exe = NewService(fixture, "ВыборДат");
            string logs = Path.Combine(Path.GetDirectoryName(exe), "Logs");
            string yesterday = Path.Combine(logs, "2026.10.14");
            string today = Path.Combine(logs, "2026.10.15");
            string tomorrow = Path.Combine(logs, "2026.10.16");
            string diagnostic = Path.Combine(logs, "Debug");
            string empty = Path.Combine(logs, "2026.10.13");
            foreach (string directory in new[] { yesterday, today, tomorrow, diagnostic, empty })
                Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(tomorrow, "Вложенные"));
            File.WriteAllText(Path.Combine(logs, "Корень.txt"), "root");
            File.WriteAllText(Path.Combine(yesterday, "Вчера.txt"), "yesterday");
            string todayFile = Path.Combine(today, "Сегодня.LoG");
            File.WriteAllText(todayFile, "today");
            File.SetLastWriteTime(todayFile, new DateTime(2020, 1, 1));
            File.WriteAllText(Path.Combine(tomorrow, "Вложенные", "Завтра.LOG"), "tomorrow");
            File.WriteAllText(Path.Combine(diagnostic, "Trace.log"), "debug");
            File.WriteAllText(Path.Combine(empty, "ignore.dat"), "no TXT");
            DateTime collectionDate = new DateTime(2026, 10, 15, 17, 30, 0);

            List<LogDateFolder> available = ServiceLogArchive.ListDateFolders(exe, CancellationToken.None);
            Assert(available.Count == 4, "folder choices contain top-level folders with recursive TXT or LOG files, including LOG-only folders");
            bool foundToday = false;
            bool foundUnknown = false;
            foreach (LogDateFolder folder in available)
            {
                Assert(folder.Name != "2026.10.13" && folder.Name != "Корень.txt", "empty folders and root files are not date choices");
                if (folder.Name == "2026.10.15")
                    foundToday = folder.Date.HasValue && folder.Date.Value.Date == collectionDate.Date;
                if (folder.Name == "Debug") foundUnknown = !folder.Date.HasValue;
            }
            Assert(foundToday, "yyyy.MM.dd folder names expose the correct parsed date");
            Assert(foundUnknown, "a non-date log folder remains selectable without an invented date");

            var todaySelection = new LogArchiveSelection { Scope = LogArchiveScope.Today, Today = collectionDate };
            Assert(String.Equals(ServiceLogArchive.ValidateSource(exe, todaySelection, CancellationToken.None), logs,
                StringComparison.OrdinalIgnoreCase), "today's matching date folder is a valid source");
            string destination = Path.Combine(fixture, "ПоДатам.zip");
            LogArchiveResult daily = ServiceLogArchive.Create(exe, "Выбор дат", destination, todaySelection, CancellationToken.None);
            Dictionary<string, byte[]> entries = ReadZip(destination);
            Assert(daily.FileCount == 1 && entries.Count == 1 && entries.ContainsKey("2026.10.15/Сегодня.LoG"),
                "Today selects by the folder date, ignores file modification date and excludes root/other-day logs");
            string suggested = ServiceLogArchive.BuildSuggestedFileName("Выбор дат", todaySelection, collectionDate);
            Assert(suggested.IndexOf("2026-10-15", StringComparison.Ordinal) >= 0
                && suggested.IndexOf("Выбор дат", StringComparison.Ordinal) >= 0,
                "today's suggested ZIP name identifies its service and date");

            var selected = new LogArchiveSelection { Scope = LogArchiveScope.SelectedFolders,
                FolderNames = new[] { "2026.10.14", "2026.10.16" }, Today = collectionDate };
            LogArchiveResult multiple = ServiceLogArchive.Create(exe, "Выбор дат", destination, selected, CancellationToken.None);
            entries = ReadZip(destination);
            Assert(multiple.FileCount == 2 && entries.Count == 2
                && entries.ContainsKey("2026.10.14/Вчера.txt")
                && entries.ContainsKey("2026.10.16/Вложенные/Завтра.LOG"),
                "multiple selected dates preserve their relative folders and exclude all unselected/root logs");

            foreach (string folder in new[] { "..", "../Debug", "..\\Debug", logs, "2026.10.14\\Вложенные", "missing" })
                ExpectInvalidSelection(exe, destination,
                    new LogArchiveSelection { Scope = LogArchiveScope.SelectedFolders, FolderNames = new[] { folder } },
                    "invalid or missing selected folder " + folder);
            ExpectInvalidSelection(exe, destination,
                new LogArchiveSelection { Scope = LogArchiveScope.SelectedFolders, FolderNames = new string[0] },
                "no selected dates");
            ExpectInvalidSelection(exe, destination,
                new LogArchiveSelection { Scope = LogArchiveScope.Today, Today = new DateTime(2026, 10, 17) },
                "no log folder for today");

            LogArchiveResult all = ServiceLogArchive.Create(exe, "Выбор дат", destination,
                new LogArchiveSelection { Scope = LogArchiveScope.All }, CancellationToken.None);
            entries = ReadZip(destination);
            Assert(all.FileCount == 5 && entries.Count == 5 && entries.ContainsKey("Корень.txt")
                && entries.ContainsKey("Debug/Trace.log"),
                "All includes root logs and every dated or undated log folder");
        }

        private static int Main(string[] args)
        {
            string parent = args.Length > 0 ? Path.GetFullPath(args[0])
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log-archive-fixtures");
            string fixture = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixture);
            try
            {
                CheckSuggestedNames();
                CheckSourceFailures(fixture);
                CheckArchiveAndActiveWriter(fixture);
                CheckDateScopes(fixture);
                CheckReparsePoints(fixture);
                Console.WriteLine("PASS LogArchiveTests: " + checks + " checks; " + skipped + " optional reparse cases skipped.");
                Console.WriteLine("Fixtures: " + fixture);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                Console.Error.WriteLine("Fixtures retained: " + fixture);
                return 1;
            }
        }
    }
}
