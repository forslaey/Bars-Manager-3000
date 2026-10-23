using System.Drawing;
using System.IO;
using System.Reflection;
using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class BrandAssets
    {
        internal const string WebsiteDescription = "Официальный сайт";
        internal static ProcessStartInfo WebsiteStartInfo()
        { return new ProcessStartInfo("https://www.datakrat.ru/") { UseShellExecute = true }; }

        internal static void BindWebsite(PictureBox banner, ToolTip tips, IWin32Window owner, Action<ProcessStartInfo> launch = null)
        {
            banner.Cursor = Cursors.Hand;
            banner.AccessibleName = "Открыть сайт ДАТАКРАТ";
            banner.AccessibleDescription = WebsiteDescription + ": https://www.datakrat.ru/";
            tips.SetToolTip(banner, WebsiteDescription);
            banner.Click += delegate
            {
                try { if (launch != null) launch(WebsiteStartInfo()); else Process.Start(WebsiteStartInfo()); }
                catch (Exception ex) { BrandMessageBox.Show(owner, ex.Message, "Не удалось открыть сайт ДАТАКРАТ", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
        }

        public static Image LoadLogo()
        {
            return LoadLogo(false);
        }

        public static Image LoadLogo(bool dark)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(dark ? "BARS.Logo.Dark.png" : "BARS.Logo.png"))
            {
                if (stream == null) return null;
                using (var image = new Bitmap(stream))
                {
                    // Center the visible artwork, including when a source has uneven transparent margins.
                    int left = image.Width, top = image.Height, right = -1, bottom = -1;
                    using (Bitmap pixels = image.Clone(new Rectangle(0, 0, image.Width, image.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        var data = pixels.LockBits(new Rectangle(0, 0, pixels.Width, pixels.Height),
                            System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        try
                        {
                            byte[] row = new byte[pixels.Width * 4];
                            for (int y = 0; y < pixels.Height; y++)
                            {
                                System.Runtime.InteropServices.Marshal.Copy(System.IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                                for (int x = 0; x < pixels.Width; x++) if (row[x * 4 + 3] > 8)
                                { left = System.Math.Min(left, x); right = System.Math.Max(right, x); top = System.Math.Min(top, y); bottom = System.Math.Max(bottom, y); }
                            }
                        }
                        finally { pixels.UnlockBits(data); }
                    }
                    if (right < left) return new Bitmap(image);
                    var centered = new Bitmap(right - left + 5, bottom - top + 5);
                    using (Graphics graphics = Graphics.FromImage(centered))
                        graphics.DrawImage(image, new Rectangle(2, 2, right - left + 1, bottom - top + 1),
                            new Rectangle(left, top, right - left + 1, bottom - top + 1), GraphicsUnit.Pixel);
                    return centered;
                }
            }
        }

        internal static Image LoadSoftwareIcon(string id)
        {
            string name=id=="anydesk"?"AnyDesk":"RuDesktop";
            using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("BARS."+name+".png"))
            {if(stream==null)return null;using(var bitmap=new Bitmap(stream))return new Bitmap(bitmap);}
        }
        public static Icon LoadIcon()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BARS.Icon.ico"))
            {
                if (stream == null) return null;
                using (Icon icon = new Icon(stream, 32, 32)) return (Icon)icon.Clone();
            }
        }
    }
}
