using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class AboutDialog : BrandWindow
    {
        private readonly Font bodyFont = new Font("Segoe UI", 10F);
        private readonly Font titleFont = new Font("Segoe UI", 20F, FontStyle.Bold);
        private readonly Font subtitleFont = new Font("Segoe UI", 11F);
        private readonly Font detailFont = new Font("Segoe UI", 9.5F);
        private readonly Icon ownedIcon;
        private readonly Bitmap portrait;

        public AboutDialog(ThemePalette palette, Icon applicationIcon, string moduleName = null, string moduleDescription = null)
        {
            SuspendLayout();
            ThemePalette colors = palette ?? ThemePalette.Create(false);
            if (moduleName == null && moduleDescription == null)
                portrait = LoadPortrait();
            NativeWindowTheme.Bind(this,()=>colors.Dark);
            if(applicationIcon==null)Icon=BrandAssets.LoadIcon();
            Text = "О софтине";
            Font = bodyFont;
            BackColor = colors.Canvas;
            ForeColor = colors.Text;
            ClientSize = portrait == null ? new Size(570, 276) : new Size(670, 388);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            if (applicationIcon != null)
            {
                ownedIcon = (Icon)applicationIcon.Clone();
                Icon = ownedIcon;
            }

            int textRow = portrait == null ? 0 : 1;
            TableLayoutPanel content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = portrait == null ? 1 : 2,
                RowCount = 6 + textRow,
                Padding = new Padding(24, 20, 24, 18),
                BackColor = colors.Canvas,
                Margin = Padding.Empty
            };
            int textColumn = 0;
            if (portrait != null)
            {
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 246F));
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 81F));
                var photo = new PictureBox
                {
                    Image = portrait,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 20, 0),
                    AccessibleName = "Портрет Леи"
                };
                content.Controls.Add(photo, 0, 0);
                content.SetRowSpan(photo, 6);
                textColumn = 1;
            }
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, moduleName == null && moduleDescription == null ? 46F : 32F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

            content.Controls.Add(MakeLabel(moduleName ?? "Bars Manager 3000", titleFont, colors.Text), textColumn, textRow);
            content.Controls.Add(MakeLabel("Инструменты BARS", subtitleFont, colors.Muted), textColumn, textRow + 1);
            string version = typeof(AboutDialog).Assembly.GetName().Version.ToString(3);
            content.Controls.Add(MakeLabel("Версия " + version, detailFont, colors.Good), textColumn, textRow + 2);
            content.Controls.Add(MakeLabel(moduleDescription ?? (moduleName == null ? "Привет! Меня зовут Лея, я написала программу с Павлушей для всех коллег из компании ДатаКрат." : "Проверка и установка компонентов BARS"), bodyFont, colors.Text), textColumn, textRow + 3);
            TableLayoutPanel actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = colors.Canvas,
                Margin = Padding.Empty
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156F));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label joke = MakeLabel("Создано мной с использованием вражеской нейросети,\r\nC# и Windows Powershell. 2024-2026.", detailFont, colors.Muted);
            joke.TextAlign = ContentAlignment.MiddleLeft;
            actions.Controls.Add(joke, 0, 0);
            BrandButton close = new BrandButton
            {
                Text = "Ясненько всё",
                Palette = colors,
                Primary = true,
                Size = new Size(156, 40),
                Anchor = AnchorStyles.Right,
                Margin = Padding.Empty,
                Font = bodyFont,
                DialogResult = DialogResult.OK,
                AccessibleName = "Закрыть окно о программе"
            };
            actions.Controls.Add(close, 1, 0);
            ModuleNavigation.StandardFooter(actions, close);
            content.Controls.Add(actions, 0, textRow + 5);
            content.SetColumnSpan(actions, content.ColumnCount);
            Controls.Add(content);
            AcceptButton = close;
            CancelButton = close;
            ResumeLayout(true);
        }

        private static Bitmap LoadPortrait()
        {
            using (var stream = typeof(AboutDialog).Assembly.GetManifestResourceStream("BARS.Leya.png"))
            {
                if (stream == null) return null;
                using (var image = Image.FromStream(stream))
                {
                    const int width = 624, height = 768;
                    const double radius = 104, feather = 58;
                    var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    float cropWidth = image.Width * 0.73F;
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(image, new Rectangle(0, 0, width, height),
                            new RectangleF(image.Width * 0.20F, image.Height * 0.095F, cropWidth,
                                cropWidth * height / width), GraphicsUnit.Pixel);
                    }
                    // Fade inward from a rounded rectangle so the same alpha works in both themes.
                    for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        double dx = Math.Min(x, width - 1 - x), dy = Math.Min(y, height - 1 - y);
                        if (Math.Min(dx, dy) >= radius + feather) continue;
                        double cornerX = Math.Max(0, radius - dx), cornerY = Math.Max(0, radius - dy);
                        double distance = Math.Min(dx, dy) >= radius ? Math.Min(dx, dy) :
                            radius - Math.Sqrt(cornerX * cornerX + cornerY * cornerY);
                        if (distance >= feather) continue;
                        double opacity = Math.Max(0, distance / feather);
                        opacity = opacity * opacity * (3 - 2 * opacity);
                        Color pixel = bitmap.GetPixel(x, y);
                        bitmap.SetPixel(x, y, Color.FromArgb((int)Math.Round(pixel.A * opacity), pixel));
                    }
                    return bitmap;
                }
            }
        }

        private static Label MakeLabel(string text, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter,
                UseMnemonic = false
            };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            bodyFont.Dispose();
            titleFont.Dispose();
            subtitleFont.Dispose();
            detailFont.Dispose();
            if (portrait != null) portrait.Dispose();
            if (ownedIcon != null) ownedIcon.Dispose();
        }
    }
}
