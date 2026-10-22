using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class LibraryJournalTests
    {
        static int count;
        static void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
        [STAThread] static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "BARS-journal-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (LibraryOperation action in new[] { LibraryOperation.Install, LibraryOperation.Remove, LibraryOperation.Reinstall, LibraryOperation.Update })
                {
                    var log = new LibraryJournal(root, action, new[] { ".NET Runtime x86", "Тестовый компонент x64" });
                    Check(Path.GetDirectoryName(log.FilePath) == Path.Combine(root, LibraryJournal.FolderName), "Selected library root");
                    Check(Path.GetFileName(log.FilePath).StartsWith("Журнал от " + Environment.MachineName + " ") && log.FilePath.EndsWith(".txt"), "Human readable filename");
                    log.Append("Компонент установлен. Код завершения: 0");
                    Check(File.ReadAllText(log.FilePath).Contains("Код завершения: 0"), "Saved immediately after step");
                    log.Append("Операция отменена. Проверь систему перед повтором.");
                    Check(File.ReadAllText(log.FilePath).Contains("Действие: " + LibraryJournal.ActionName(action)), "Operation in header");
                    var bytes = File.ReadAllBytes(log.FilePath); Check(bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191, "UTF8 BOM for Notepad");
                    using (var blocked = new FileStream(log.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    { bool failed = false; try { log.Append("Ошибка сохранения не теряет эту запись"); } catch (IOException) { failed = true; } Check(failed, "Write failure reported"); }
                    log.Save(); Check(File.ReadAllText(log.FilePath).Contains("не теряет эту запись"), "Retry keeps failed write");
                }
                Check(Directory.GetFiles(LibraryJournal.Folder(root), "*.txt").Length == 4, "Separate files without overwriting");
                bool missing = false; try { LibraryJournal.Folder(null); } catch (InvalidOperationException) { missing = true; } Check(missing, "No silent fallback to another folder");
                Application.EnableVisualStyles();
                using (var form = new LibraryManagerForm(true))
                {
                    var report = (Button)typeof(LibraryManagerForm).GetField("report", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    Check(report.Text == "Открыть журнал", "Open journal button");
                }
                Console.WriteLine("LibraryJournalTests: " + count + " passed; temporary files only, no installers."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
