using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
    internal sealed class LibraryActionDialog : BrandWindow
    {
        internal LibraryOperation Operation { get; private set; }
        internal LibraryActionDialog(ThemePalette colors, Icon icon, IEnumerable<LibraryCheck> selected)
        {
            var items = selected.ToList();
            NativeWindowTheme.Bind(this,()=>colors.Dark);
            Text = "Действие с компонентами"; Icon = icon ?? BrandAssets.LoadIcon(); Font = new Font("Segoe UI", 10);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(980, 540); MinimumSize = new Size(980, 540); StartPosition = FormStartPosition.CenterParent;
            BackColor = colors.Canvas; ForeColor = colors.Text; DoubleBuffered = true; MinimizeBox = MaximizeBox = false;
            var layout = new BufferedLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.Controls.Add(new Label { Text = "Выбранные компоненты", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            var card = new Card { Dock = DockStyle.Fill, BackColor = colors.Surface, BorderColor = colors.Border, Padding = new Padding(12), Margin = Padding.Empty };
            var list = new JournalTextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None, BackColor = colors.Surface, ForeColor = colors.Text, Font = Font };
            list.Text = String.Join(Environment.NewLine + Environment.NewLine + Environment.NewLine, items.Select(c => c.Package.Name + Environment.NewLine + Environment.NewLine + "Состояние: " + c.Caption + Environment.NewLine + "Что делать: " + LibraryVersions.Advice(c) + Environment.NewLine + Environment.NewLine + "В библиотеке: " + LibraryVersions.Required(c.Package) + Environment.NewLine + "В системе: " + (c.ActualVersion ?? "не найдено") + (String.IsNullOrWhiteSpace(c.Detail) ? "" : Environment.NewLine + Environment.NewLine + c.Detail)));
            var scroll = new JournalScrollBar { AlwaysVisible=false, Dock = DockStyle.Right, Width = 14, Palette = colors, BackColor = colors.Surface }; scroll.Bind(list); card.Controls.Add(list); card.Controls.Add(scroll); layout.Controls.Add(card, 0, 1);
            layout.Controls.Add(new Label { Text = "Рекомендуемое действие: установить отсутствующие, обновить старые; новые пропустить.\n\nПереустановка и удаление затрагивают зарегистрированные версии выбранного поколения и разрядности. Встроенные компоненты Windows не удаляются.", AutoSize = true, Dock = DockStyle.Fill, ForeColor = colors.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 12, 0, 12) }, 0, 2);
            var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, Margin = Padding.Empty };
            for (int i = 0; i < 7; i++) buttons.ColumnStyles.Add(new ColumnStyle(i % 2 == 0 ? SizeType.Percent : SizeType.Absolute, i % 2 == 0 ? 25 : 12)); buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var recommended = new BrandButton { Text = "По рекомендации", Primary = true, Palette = colors, Dock = DockStyle.Fill, Margin = Padding.Empty, Font = Font, Enabled = items.Any(c => LibraryVersions.NeedsInstall(c)) };
            recommended.Click += delegate { Operation = items.Any(c => c.Relation == LibraryRelation.Older) ? LibraryOperation.Update : LibraryOperation.Install; DialogResult = DialogResult.OK; };
            var reinstall = new BrandButton { Text = "Переустановить", Enabled = items.Any(LibraryVersions.CanRemove) && !items.Any(c => c.Relation == LibraryRelation.Newer || c.HasNewerParts), Palette = colors, Dock = DockStyle.Fill, Margin = Padding.Empty, Font = Font };
            var remove = new BrandButton { Text = "Удалить", Enabled = items.Any(LibraryVersions.CanRemove), Palette = colors, Dock = DockStyle.Fill, Margin = Padding.Empty, Font = Font };
            var cancel = new BrandButton { Text = "Отмена", Palette = colors, Dock = DockStyle.Fill, Margin = Padding.Empty, DialogResult = DialogResult.Cancel, Font = Font };
            reinstall.Click += delegate { Operation = LibraryOperation.Reinstall; DialogResult = DialogResult.OK; };
            remove.Click += delegate { Operation = LibraryOperation.Remove; DialogResult = DialogResult.OK; };
            buttons.Controls.Add(recommended, 0, 0); buttons.Controls.Add(reinstall, 2, 0); buttons.Controls.Add(remove, 4, 0); buttons.Controls.Add(cancel, 6, 0); ModuleNavigation.StandardFooter(buttons, recommended, reinstall, remove, cancel); layout.Controls.Add(buttons, 0, 3); Controls.Add(layout); CancelButton = cancel;
        }
    }
}
