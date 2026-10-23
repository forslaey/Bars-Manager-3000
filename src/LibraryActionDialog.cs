using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
    internal sealed class LibraryActionDialog : Form
    {
        internal LibraryOperation Operation { get; private set; }
        internal LibraryActionDialog(ThemePalette colors, Icon icon, IEnumerable<LibraryCheck> selected)
        {
            var items = selected.ToList();
            NativeWindowTheme.Bind(this,()=>colors.Dark);
            Text = "Действие с компонентами"; Icon = icon; Font = new Font("Segoe UI", 10);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(760, 470); MinimumSize = new Size(700, 450); StartPosition = FormStartPosition.CenterParent;
            BackColor = colors.Canvas; ForeColor = colors.Text; DoubleBuffered = true; MinimizeBox = MaximizeBox = false;
            var layout = new BufferedLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.Controls.Add(new Label { Text = "Выбранные компоненты", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            var card = new Card { Dock = DockStyle.Fill, BackColor = colors.Surface, BorderColor = colors.Border, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 8) };
            var list = new JournalTextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None, BackColor = colors.Surface, ForeColor = colors.Text, Font = Font };
            list.Text = String.Join(Environment.NewLine + Environment.NewLine, items.Select(c => c.Package.Name + Environment.NewLine + c.Caption + ". " + LibraryVersions.Advice(c) + Environment.NewLine + "В комплекте: " + LibraryVersions.Required(c.Package) + "; в системе: " + (c.ActualVersion ?? "—") + Environment.NewLine + c.Detail));
            var scroll = new JournalScrollBar { Dock = DockStyle.Right, Width = 14, Palette = colors, BackColor = colors.Surface }; scroll.Bind(list); card.Controls.Add(list); card.Controls.Add(scroll); layout.Controls.Add(card, 0, 1);
            layout.Controls.Add(new Label { Text = "Рекомендуемое действие: установить отсутствующие, обновить старые; новые пропустить.\nПереустановка и удаление затрагивают зарегистрированные версии выбранного поколения и разрядности. Встроенные компоненты Windows не удаляются.", Dock = DockStyle.Fill, ForeColor = colors.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 0, 2);
            var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            for (int i = 0; i < 4; i++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var recommended = new BrandButton { Text = "По рекомендации", Primary = true, Palette = colors, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), Font = Font, Enabled = items.Any(c => LibraryVersions.NeedsInstall(c)) };
            recommended.Click += delegate { Operation = items.Any(c => c.Relation == LibraryRelation.Older) ? LibraryOperation.Update : LibraryOperation.Install; DialogResult = DialogResult.OK; };
            var reinstall = new BrandButton { Text = "Переустановить", Enabled = items.Any(LibraryVersions.CanRemove) && !items.Any(c => c.Relation == LibraryRelation.Newer || c.HasNewerParts), Palette = colors, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), Font = Font };
            var remove = new BrandButton { Text = "Удалить", Enabled = items.Any(LibraryVersions.CanRemove), Destructive = true, Palette = colors, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), Font = Font };
            var cancel = new BrandButton { Text = "Отмена", Palette = colors, Dock = DockStyle.Fill, Margin = Padding.Empty, DialogResult = DialogResult.Cancel, Font = Font };
            reinstall.Click += delegate { Operation = LibraryOperation.Reinstall; DialogResult = DialogResult.OK; };
            remove.Click += delegate { Operation = LibraryOperation.Remove; DialogResult = DialogResult.OK; };
            buttons.Controls.Add(recommended, 0, 0); buttons.Controls.Add(reinstall, 1, 0); buttons.Controls.Add(remove, 2, 0); buttons.Controls.Add(cancel, 3, 0); layout.Controls.Add(buttons, 0, 3); Controls.Add(layout); CancelButton = cancel;
        }
    }
}
