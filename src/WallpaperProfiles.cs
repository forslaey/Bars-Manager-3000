using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ServiceTool
{
    internal static class WallpaperProfiles
    {
        internal static string SaveImage(string source)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web", "Wallpaper", "BARS");
            ToolsDeployment.SafePath(folder);
            Directory.CreateDirectory(folder);
            ToolsDeployment.SafePath(folder);
            string temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".bmp");
            string destination = Path.Combine(folder, "Desktop.bmp");
            try
            {
                using (Image image = Image.FromFile(source)) image.Save(temporary, ImageFormat.Bmp);
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return destination;
        }

        internal static string[] Apply(string wallpaper)
        {
            var failures = new List<string>();
            using (var profiles = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
            {
                if (profiles == null) throw new IOException("Windows не вернула список профилей.");
                foreach (string sid in profiles.GetSubKeyNames())
                {
                    if (!Regex.IsMatch(sid, @"\AS-1-(?:5-21-\d+-\d+-\d+-\d+|12-1-\d+-\d+-\d+-\d+)\z")) continue;
                    using (var profile = profiles.OpenSubKey(sid))
                    {
                        string directory = Convert.ToString(profile == null ? null : profile.GetValue("ProfileImagePath"));
                        UpdateProfile(sid, directory, wallpaper, failures);
                    }
                }
                UpdateProfile(null, Convert.ToString(profiles.GetValue("Default")), wallpaper, failures);
            }
            return failures.ToArray();
        }

        private static void UpdateProfile(string sid, string directory, string wallpaper, List<string> failures)
        {
            string mount = "BARSWallpaper_" + Guid.NewGuid().ToString("N");
            bool mounted = false;
            string label = sid == null ? "шаблон новых пользователей" : Path.GetFileName(directory);
            if (String.IsNullOrWhiteSpace(label)) label = sid;
            try
            {
                if (sid != null)
                {
                    using (var loaded = Registry.Users.OpenSubKey(sid, true))
                    {
                        if (loaded != null) { SetWallpaper(loaded, wallpaper); return; }
                    }
                }
                if (String.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory))
                    throw new IOException("Не найден путь к профилю.");
                string hive = Path.Combine(directory, "NTUSER.DAT");
                ToolsDeployment.SafePath(hive);
                if (!File.Exists(hive)) throw new FileNotFoundException("Не найден реестр профиля.", hive);
                RunReg("load HKU\\" + mount + " \"" + hive + "\"");
                mounted = true;
                using (var loaded = Registry.Users.OpenSubKey(mount, true))
                {
                    if (loaded == null) throw new IOException("Не удалось открыть реестр профиля.");
                    SetWallpaper(loaded, wallpaper);
                }
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is System.ComponentModel.Win32Exception)) throw;
                failures.Add(label);
            }
            finally
            {
                if (mounted)
                {
                    try { RunReg("unload HKU\\" + mount); }
                    catch (IOException) { failures.Add(label + " (не удалось закрыть реестр профиля)"); }
                    catch (System.ComponentModel.Win32Exception) { failures.Add(label + " (не удалось закрыть реестр профиля)"); }
                }
            }
        }

        private static void SetWallpaper(RegistryKey profile, string wallpaper)
        {
            using (var desktop = profile.CreateSubKey(@"Control Panel\Desktop"))
                desktop.SetValue("WallPaper", wallpaper, RegistryValueKind.String);
        }

        private static void RunReg(string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"), arguments)
                { UseShellExecute = false, CreateNoWindow = true }))
            {
                if (!process.WaitForExit(10000))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    throw new IOException("Истекло время ожидания реестра Windows.");
                }
                if (process.ExitCode != 0) throw new IOException("Windows не выполнила операцию с реестром профиля.");
            }
        }
    }
}
