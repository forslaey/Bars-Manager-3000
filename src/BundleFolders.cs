using System;
using System.IO;

namespace ServiceTool
{
    internal static class BundleFolders
    {
        internal static string ExecutableDirectory
        {
            get
            {
                string original = Environment.GetEnvironmentVariable("BARS_MANAGER_LAUNCH_DIRECTORY");
                return !String.IsNullOrWhiteSpace(original) && Path.IsPathRooted(original) && Directory.Exists(original)
                    ? original : AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        internal const string DefaultRoot = @"C:\BarsManager3000";

        internal static string Find(string executableDirectory, string section)
        {
            foreach (string root in new[] { executableDirectory, DefaultRoot })
            {
                string path = Path.Combine(root, section);
                if (Directory.Exists(path)) return path;

            }
            return null;
        }

        internal static string BundleRoot(string libraries)
        {
            return Path.GetFileName(libraries).Equals("_libraries", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(libraries) : libraries;
        }

        internal static string DownloadPath(string root, string path)
        {
            if (path.StartsWith("/_", StringComparison.OrdinalIgnoreCase)) return path;
            return "/_libraries" + path;
        }

        internal static string FindSibling(string section)
        {
            string executable = BundleFolders.ExecutableDirectory;
            string saved = ApplicationStorage.FilePath("library-source.txt");
            if (File.Exists(saved))
            {
                string libraries = File.ReadAllText(saved).Trim().TrimEnd('\\', '/');
                if (Path.IsPathRooted(libraries) && Path.GetFileName(libraries).Equals("_libraries", StringComparison.OrdinalIgnoreCase))
                {
                    string sibling = Path.Combine(Path.GetDirectoryName(libraries), section);
                    if (Directory.Exists(sibling) || section == "_soft") return sibling;
                }
            }
            string local = Path.Combine(executable, section);
            if (Directory.Exists(local)) return local;
            string preferred = Path.Combine(BundlePreferences.Load(), section);
            return Directory.Exists(preferred) ? preferred : Find(executable, section);
        }
    }
}
