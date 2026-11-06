using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ServiceTool
{
    internal enum ServiceFileKind { Unknown, Installer, OrdinaryProgram, WindowsWidget }

    internal static class ServiceFileInspection
    {
        internal static ServiceFileKind Identify(string path, bool managedInstaller)
        {
            try
            {
                var version = FileVersionInfo.GetVersionInfo(path);
                foreach (string name in new[] { "Google Chrome", "Google Update", "Google Installer", "Notepad++", "7-Zip" })
                    if (Contains(version.ProductName, name) || Contains(version.FileDescription, name))
                        return ServiceFileKind.OrdinaryProgram;

                if (managedInstaller) return ServiceFileKind.Installer;
                string markers = ReadMarkers(path);
                if (HasInstaller(markers)) return ServiceFileKind.Installer;

                string assembly = Path.ChangeExtension(path, ".dll");
                if (File.Exists(Path.ChangeExtension(path, ".runtimeconfig.json")) && File.Exists(assembly))
                {
                    markers = ReadMarkers(assembly);
                    if (HasInstaller(markers)) return ServiceFileKind.Installer;
                }

                if (String.Equals(Path.GetFileName(path), "Bars.WebStore.BackendWidget.exe", StringComparison.OrdinalIgnoreCase)
                    && (Contains(markers, "UseWindowsService") || Contains(markers, "AddWindowsService")))
                    return ServiceFileKind.WindowsWidget;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return ServiceFileKind.Unknown;
        }

        private static bool HasInstaller(string markers)
        {
            return Contains(markers, "AddServiceInstallerCommands") &&
                (Contains(markers, "UseWindowsService") || Contains(markers, "AddWindowsService"));
        }

        private static string ReadMarkers(string path)
        {
            const int limit = 8 * 1024 * 1024;
            using (var reader = new BinaryReader(File.OpenRead(path)))
                return Encoding.ASCII.GetString(reader.ReadBytes((int)Math.Min(reader.BaseStream.Length, limit)));
        }

        private static bool Contains(string text, string marker)
        {
            return text != null && text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static ProcessStartInfo CreateWidget(string executable)
        {
            return new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
                Arguments = "create Bars.BackendWidget binPath= \"\\\"" + executable +
                    "\\\"\" DisplayName= Bars.WebStore.BackendWidget type= own start= auto",
                WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }
    }
}
