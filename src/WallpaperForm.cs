using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class WallpaperForm : BrandWindow
    {
        private static readonly Dictionary<string, HashSet<string>> ShownImages =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> LastImage =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Random Random = new Random();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint value, string path, uint flags);

        internal WallpaperForm(ThemePalette colors)
        {
            Text = "Применить обои";
            Icon = BrandAssets.LoadIcon();
            Font = new Font("Segoe UI", 10F);
            NativeWindowTheme.Bind(this, () => colors.Dark);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(430, 100);
            BackColor = colors.Canvas;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            int column = 0;
            foreach (string category in new[] { "КСО", "КАССА" })
            {
                string selection = category;
                var button = new BrandButton { Text = category, Palette = colors, Dock = DockStyle.Fill, Margin = new Padding(4) };
                button.Click += async delegate {
                    layout.Enabled = false;
                    try { await Apply(selection); }
                    catch (Exception ex)
                    {
                        BrandMessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    finally { if (!IsDisposed) layout.Enabled = true; }
                };
                layout.Controls.Add(button, column++, 0);
            }
            Controls.Add(layout);
        }

        private static async System.Threading.Tasks.Task Apply(string category)
        {
            string root = BundleFolders.FindSibling("_wallpers");
            if (root == null) throw new DirectoryNotFoundException("Папка _wallpers не найдена в комплекте BARS.");
            string folder = Path.Combine(root, category);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Папка обоев не найдена: " + folder);
            ToolsDeployment.SafePath(folder);
            string[] files = Directory.GetFiles(folder).Where(file =>
                new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0).ToArray();
            if (files.Length == 0) throw new IOException("В папке нет подходящих изображений.");
            HashSet<string> shown;
            if (!ShownImages.TryGetValue(folder, out shown))
                ShownImages[folder] = shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] candidates = files.Where(file => !shown.Contains(file)).ToArray();
            if (candidates.Length == 0)
            {
                shown.Clear();
                candidates = files.Length > 1 ? files.Where(file => file != LastImage[folder]).ToArray() : files;
            }
            string chosen = candidates[Random.Next(candidates.Length)];
            string cache = await System.Threading.Tasks.Task.Run(() => WallpaperProfiles.SaveImage(chosen));
            string[] failed = await System.Threading.Tasks.Task.Run(() => WallpaperProfiles.Apply(cache));
            bool currentApplied = SystemParametersInfo(20, 0, cache, 3);
            shown.Add(chosen);
            LastImage[folder] = chosen;
            if (!currentApplied || failed.Length != 0)
                throw new IOException((currentApplied ? "" : "Windows не подтвердила изменение текущего рабочего стола. ")
                    + (failed.Length == 0 ? "" : "Не удалось обновить профили: " + String.Join(", ", failed) + "."));
        }
    }
}
