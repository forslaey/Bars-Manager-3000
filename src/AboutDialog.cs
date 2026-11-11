using System;
using System.Drawing;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class AboutDialog : Form
    {
        private readonly Font bodyFont = new Font("Segoe UI", 10F);
        private readonly Font titleFont = new Font("Segoe UI", 20F, FontStyle.Bold);
        private readonly Font subtitleFont = new Font("Segoe UI", 11F);
        private readonly Font detailFont = new Font("Segoe UI", 9.5F);
        private readonly Icon ownedIcon;

        public AboutDialog(ThemePalette palette, Icon applicationIcon, string moduleName = null, string moduleDescription = null)
        {
            SuspendLayout();
            ThemePalette colors = palette ?? ThemePalette.Create(false);
            NativeWindowTheme.Bind(this,()=>colors.Dark);
            Text = "О софтине";
            Font = bodyFont;
            BackColor = colors.Canvas;
            ForeColor = colors.Text;
            ClientSize = new Size(570, 276);
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

            TableLayoutPanel content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(24, 20, 24, 18),
                BackColor = colors.Canvas,
                Margin = Padding.Empty
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

            content.Controls.Add(MakeLabel(moduleName ?? "Bars Manager 3000", titleFont, colors.Text), 0, 0);
            content.Controls.Add(MakeLabel("Инструменты BARS", subtitleFont, colors.Muted), 0, 1);
            string version = typeof(AboutDialog).Assembly.GetName().Version.ToString(3);
            content.Controls.Add(MakeLabel("Версия " + version, detailFont, colors.Good), 0, 2);
            content.Controls.Add(MakeLabel(moduleDescription ?? (moduleName == null ? "Управление Барсом. Я главная тут" : "Проверка и установка компонентов BARS"), bodyFont, colors.Text), 0, 3);
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
            content.Controls.Add(actions, 0, 5);
            Controls.Add(content);
            AcceptButton = close;
            CancelButton = close;
            ResumeLayout(true);
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
            if (ownedIcon != null) ownedIcon.Dispose();
        }
    }
}
