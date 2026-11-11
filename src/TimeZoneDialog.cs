using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class TimeZoneDialog : Form
    {
        internal string ZoneId { get; private set; }

        internal TimeZoneDialog(ThemePalette colors)
        {
            Text = "Часовой пояс Windows";
            Icon = BrandAssets.LoadIcon();
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(560, 140);
            BackColor = colors.Canvas;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            NativeWindowTheme.Bind(this, () => colors.Dark);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(18) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            var zones = new BrandComboBox { Palette = colors, UseManagedPopup = true, Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = colors.Surface, ForeColor = colors.Text, AccessibleName = Text };
            foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
            {
                int index = zones.Items.Add(zone);
                if (zone.Id == TimeZoneInfo.Local.Id) zones.SelectedIndex = index;
            }
            var cancel = new BrandButton { Text = "Отмена", Palette = colors, DialogResult = DialogResult.Cancel };
            var apply = new BrandButton { Text = "Применить", Palette = colors, Primary = true, Enabled = zones.SelectedIndex >= 0 };
            zones.SelectedIndexChanged += delegate { apply.Enabled = zones.SelectedIndex >= 0; };
            apply.Click += delegate
            {
                var zone = zones.SelectedItem as TimeZoneInfo;
                if (zone == null) return;
                ZoneId = zone.Id;
                DialogResult = DialogResult.OK;
            };
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            ModuleNavigation.StandardFooter(footer, cancel, apply);
            layout.Controls.Add(zones, 0, 0);
            layout.Controls.Add(footer, 0, 1);
            Controls.Add(layout);
            AcceptButton = apply;
            CancelButton = cancel;
        }

        internal static void Apply(string id)
        {
            string verifiedId = TimeZoneInfo.FindSystemTimeZoneById(id).Id;
            using (var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "tzutil.exe"),
                "/s " + LibraryPlan.Quote(verifiedId)) { UseShellExecute = false, CreateNoWindow = true }))
            {
                if (!process.WaitForExit(10000)) throw new TimeoutException("Windows не ответила вовремя.");
                if (process.ExitCode != 0) throw new IOException("Windows отклонила изменение. Код " + process.ExitCode + ".");
            }
            TimeZoneInfo.ClearCachedData();
            if (!String.Equals(TimeZoneInfo.Local.Id, verifiedId, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Windows не подтвердила выбранный часовой пояс.");
        }
    }
}