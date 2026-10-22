using System;
using System.IO;
using System.Text;

namespace ServiceTool
{
    // Keep the full operation in memory too, so a failed write can be retried at completion.
    internal sealed class LibraryJournal
    {
        internal const string FolderName = "Журнал установки библиотек";
        private readonly StringBuilder content = new StringBuilder();
        internal string FilePath { get; private set; }
        internal static string Folder(string libraryRoot)
        {
            if (String.IsNullOrWhiteSpace(libraryRoot)) throw new InvalidOperationException("Сначала выбери папку «Библиотеки».");
            return Path.Combine(Path.GetFullPath(libraryRoot), FolderName);
        }
        internal static string ActionName(LibraryOperation operation)
        {
            return operation == LibraryOperation.Remove ? "Удаление" : operation == LibraryOperation.Reinstall ? "Переустановка" : operation == LibraryOperation.Update ? "Обновление" : "Установка";
        }
        internal LibraryJournal(string libraryRoot, LibraryOperation operation, string[] components)
        {
            string folder = Folder(libraryRoot); Directory.CreateDirectory(folder);
            string name = Environment.MachineName;
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            string stem = "Журнал от " + name + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff");
            for (int index = 0; ; index++)
            {
                FilePath = Path.Combine(folder, stem + (index == 0 ? "" : " (" + index + ")") + ".txt");
                try { using (var file = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { } break; }
                catch (IOException) { if (!File.Exists(FilePath)) throw; }
            }
            content.AppendLine("BARS Manager 3000 — журнал работы с библиотеками")
                .AppendLine("Компьютер: " + Environment.MachineName)
                .AppendLine("Начало: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"))
                .AppendLine("Действие: " + ActionName(operation))
                .AppendLine("Папка библиотек: " + libraryRoot)
                .AppendLine("Выбранные компоненты:");
            foreach (string component in components) content.AppendLine("  • " + component);
            content.AppendLine(); Save();
        }
        internal void Append(string message)
        {
            content.AppendLine(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            Save();
        }
        internal void Save()
        {
            // Open with read sharing only: an interrupted copy cannot overwrite this operation's journal.
            using (var file = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(file, new UTF8Encoding(true)))
            { writer.Write(content.ToString()); writer.Flush(); file.Flush(true); }
        }
    }
}
