using System;
using System.IO;
using System.Security;

namespace ServiceTool
{
    internal static class ApplicationStorage
    {
        private static readonly string Parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BARS");
        internal static readonly string DirectoryPath = Path.Combine(Parent, "Bars Service Manager 3000");
        private static readonly string Legacy = Path.Combine(Parent, "ServiceManager3000");
        internal static string MigrationWarning;

        internal static string FilePath(string name)
        {
            string current = Path.Combine(DirectoryPath, name);
            string previous = Path.Combine(Legacy, name);
            return !File.Exists(current) && File.Exists(previous) ? previous : current;
        }

        internal static void MigrateLegacy()
        {
            try { MigrateLegacy(Legacy, DirectoryPath); }
            catch (IOException ex) { MigrationWarning = "Не удалось перенести прежние настройки. Доступные прежние файлы будут использованы без потери данных. " + ex.Message; }
            catch (UnauthorizedAccessException ex) { MigrationWarning = "Не удалось перенести прежние настройки: " + ex.Message; }
            catch (SecurityException ex) { MigrationWarning = "Не удалось перенести прежние настройки: " + ex.Message; }
        }

        internal static void MigrateLegacy(string previous, string current)
        {
            foreach (string name in new string[] { "services.xml", "install-profiles.xml", "theme.txt" })
            {
                string source = Path.Combine(previous, name), destination = Path.Combine(current, name);
                if (!File.Exists(source) || File.Exists(destination)) continue;
                Directory.CreateDirectory(current);
                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(source, temporary, false);
                    try { File.Move(temporary, destination); }
                    catch (IOException) { if (!File.Exists(destination)) throw; }
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
    }
}
