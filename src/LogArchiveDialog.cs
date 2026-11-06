using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class LogArchiveDialog : Form
    {
        private const int PageSize = 12;
        private readonly ThemePalette palette;
        private readonly List<LogDateFolder> folders;
        private readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly ComboBox scope = new BrandComboBox { UseManagedPopup = true };
        private readonly TableLayoutPanel dates = new BufferedLayoutPanel();
        private readonly FlushLabel summary = new FlushLabel {InkLeft=g=>0};
        private readonly Label pageLabel = new Label();
        private readonly Button previous, next;
        private readonly DateTime today;
        private int page;
        private readonly bool applicationLogs;
        internal LogArchiveSelection Selection { get; private set; }

        internal LogArchiveDialog(ThemePalette colors, string serviceName, List<LogDateFolder> available, DateTime localToday)
            : this(colors, serviceName, available, localToday, false) { }

        internal LogArchiveDialog(ThemePalette colors, string serviceName, List<LogDateFolder> available, DateTime localToday, bool applicationLogs)
        {
            SuspendLayout();
            palette = colors;
            this.applicationLogs = applicationLogs;
            NativeWindowTheme.Bind(this,()=>palette.Dark);
            folders = available ?? new List<LogDateFolder>();
            today = localToday.Date;
            Text = "Сбор логов";
            Font = new Font("Segoe UI", 10F);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 440);
            FormBorderStyle = FormBorderStyle.FixedSingle; Icon = BrandAssets.LoadIcon();
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = MinimizeBox = MaximizeBox = false;
            BackColor = palette.Canvas;
            ForeColor = palette.Text;
            TableLayoutPanel content = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(24, 16, 24, 16), Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            Label title = new FlushLabel { Text = serviceName, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold), Margin = Padding.Empty };
            content.Controls.Add(title, 0, 0);
            scope.Dock = DockStyle.Fill;
            scope.Margin = new Padding(0, 4, 0, 4);
            scope.DropDownStyle = ComboBoxStyle.DropDownList;
            scope.DrawMode = DrawMode.OwnerDrawFixed;
            scope.ItemHeight = 23;
            scope.FlatStyle=FlatStyle.Flat;((BrandComboBox)scope).HoverHighlight=false;
            scope.BackColor = palette.SurfaceAlt;
            scope.ForeColor = palette.Text;
            ((BrandComboBox)scope).Palette = palette;
            scope.Items.AddRange(new object[] { "Только за сегодня — " + today.ToString("dd.MM.yyyy"), "Выбранные даты", "Все логи" });
            scope.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                bool highlight = (e.State & DrawItemState.Selected) != 0;
                using (Brush brush = new SolidBrush(highlight ? palette.Selection : palette.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
                if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, Convert.ToString(scope.Items[e.Index]), scope.Font, e.Bounds,
                    highlight ? palette.SelectionText : palette.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsTranslateTransform);
            };
            scope.SelectedIndexChanged += delegate { UpdateScope(); };
            content.Controls.Add(scope, 0, 1);
            summary.Dock = DockStyle.Fill;
            summary.ForeColor = palette.Muted;
            summary.Font = new Font("Segoe UI", 9F);
            summary.Margin = new Padding(0, 10, 0, 4);
            content.Controls.Add(summary, 0, 2);
            dates.Dock = DockStyle.Fill;
            dates.BackColor = palette.Canvas;
            dates.ColumnCount = 2;
            dates.RowCount = 8;
            dates.Margin = Padding.Empty;
            dates.Padding = new Padding(76, 4, 76, 4);
            for (int i = 0; i < 2; i++) dates.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            dates.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            for (int i = 0; i < 6; i++) dates.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            dates.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            content.Controls.Add(dates, 0, 3);
            TableLayoutPanel paging = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            paging.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            paging.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            paging.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            previous = Button("‹");
            next = Button("›");
            previous.Dock = next.Dock = DockStyle.None;
            previous.Size = next.Size = new Size(32, 26);
            previous.Anchor = next.Anchor = AnchorStyles.None;
            previous.Margin = next.Margin = Padding.Empty;
            previous.Click += delegate { if (page > 0) { page--; RenderDates(); } };
            next.Click += delegate { if ((page + 1) * PageSize < folders.Count) { page++; RenderDates(); } };
            pageLabel.Dock = DockStyle.Fill;
            pageLabel.TextAlign = ContentAlignment.MiddleCenter;
            pageLabel.ForeColor = palette.Muted;
            pageLabel.Font = new Font("Segoe UI", 8.5F);
            paging.Controls.Add(previous, 0, 0);
            paging.Controls.Add(pageLabel, 1, 0);
            paging.Controls.Add(next, 2, 0);
            content.Controls.Add(paging, 0, 4);
            var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Padding=new Padding(0,10,0,6),Margin=Padding.Empty};
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,12));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            Button save=Button("Выбрать путь...");save.Margin=Padding.Empty;((BrandButton)save).Primary=true;
            save.Click+=delegate{ConfirmSelection();};
            Button cancel=Button("Отмена");cancel.Margin=Padding.Empty;cancel.DialogResult=DialogResult.Cancel;
            actions.Controls.Add(cancel,0,0);actions.Controls.Add(save,2,0);
            content.Controls.Add(actions, 0, 5);
            Controls.Add(content);
            AcceptButton = save;
            CancelButton = cancel;
            scope.SelectedIndex = folders.Any(folder => folder.Date.HasValue && folder.Date.Value.Date == today) ? 0 : 2;
            UpdateScope();
            ResumeLayout(true);
        }

        private Button Button(string text)
        {
            return new BrandButton { Text = text, Palette = palette, Dock = DockStyle.Fill, Margin = new Padding(3, 2, 0, 0), Font = Font };
        }

        private void UpdateScope()
        {
            summary.ForeColor = palette.Muted;
            if (scope.SelectedIndex == 2) summary.Text = applicationLogs ? "Все логи программы, включая вложенные папки." : "Все log-файлы из папки с логами службы, включая вложенные папки.";
            else if (scope.SelectedIndex == 0) summary.Text = "Логи из папок с сегодняшней датой. Дата определяется по имени папки.";
            else summary.Text = "Отметь одну или несколько дат. Выбор сохраняется при переходе между страницами.";
            if(dates.Controls.Count==0)RenderDates();else UpdateDateChecks();
        }

        private bool updatingDates;
        private void UpdateDateChecks(){updatingDates=true;try{foreach(CheckBox item in dates.Controls){var folder=folders.First(f=>f.Name==item.Text);item.Enabled=scope.SelectedIndex==1;item.Checked=scope.SelectedIndex==2||(scope.SelectedIndex==0?folder.Date.HasValue&&folder.Date.Value.Date==today:selected.Contains(folder.Name));}}finally{updatingDates=false;}UpdatePageLabel();}
        private void RenderDates()
        {
            dates.SuspendLayout();
            while (dates.Controls.Count > 0) { Control old = dates.Controls[0]; dates.Controls.Remove(old); old.Dispose(); }
            int count = Math.Min(PageSize, folders.Count - page * PageSize);
            for (int row = 1; row <= 6; row++) dates.RowStyles[row].Height = row <= (count + 1) / 2 ? (float)Math.Round(29 * dates.DeviceDpi / 96F) : 0;
            for (int i = page * PageSize; i < Math.Min(folders.Count, (page + 1) * PageSize); i++)
            {
                string name = folders[i].Name;
                bool isChecked = scope.SelectedIndex == 2 || (scope.SelectedIndex == 0
                    ? folders[i].Date.HasValue && folders[i].Date.Value.Date == today : selected.Contains(name));
                CheckBox item = new LogDateCheckBox { Palette = palette, Text = name, Checked = isChecked, Dock = DockStyle.Fill, AutoEllipsis = true,
                    FlatStyle = FlatStyle.Flat, ForeColor = palette.Text, BackColor = palette.Canvas, Margin = new Padding(5, 1, 5, 1),
                    Enabled = scope.SelectedIndex == 1, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
                item.CheckedChanged += delegate { if(updatingDates||scope.SelectedIndex!=1)return; if (item.Checked) selected.Add(name); else selected.Remove(name); UpdatePageLabel(); };
                int index = i - page * PageSize;
                item.Dock = DockStyle.None; item.Anchor = AnchorStyles.None;
                item.Size = new Size(TextRenderer.MeasureText(name, dates.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + (int)Math.Ceiling(34 * dates.DeviceDpi / 96F), (int)Math.Round(26 * dates.DeviceDpi / 96F));
                dates.Controls.Add(item, index % 2, 1 + index / 2);
                if (index == count - 1 && count % 2 == 1) dates.SetColumnSpan(item, 2);
            }
            dates.ResumeLayout(true);
            previous.Enabled = page > 0;
            next.Enabled = (page + 1) * PageSize < folders.Count;
            UpdatePageLabel();
        }

        private void UpdatePageLabel()
        {
            pageLabel.Text = folders.Count == 0 ? "Папок дат с логами нет" :
                "Страница " + (page + 1) + " из " + Math.Max(1, (folders.Count + PageSize - 1) / PageSize) + "; выбрано: " + selected.Count;
        }

        private void ConfirmSelection()
        {
            if (scope.SelectedIndex == 1 && selected.Count == 0)
            { summary.ForeColor = palette.Danger; summary.Text = "Отметь хотя бы одну папку даты."; return; }
            if (scope.SelectedIndex == 0 && !folders.Any(folder => folder.Date.HasValue && folder.Date.Value.Date == today))
            { summary.ForeColor = palette.Danger; summary.Text = "В Logs нет логов в папке с сегодняшней датой. Выбери другой режим."; return; }
            Selection = new LogArchiveSelection { Scope = scope.SelectedIndex == 2 ? LogArchiveScope.All : scope.SelectedIndex == 0 ? LogArchiveScope.Today : LogArchiveScope.SelectedFolders,
                Today = today, FolderNames = selected.ToArray() };
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // Native disabled CheckBox text can ignore ForeColor and become black in a dark theme.
    // Retain CheckBox interaction/accessibility and paint every state with the current palette.
    internal sealed class LogDateCheckBox : BrandCheckBox
    {
        protected override void OnClick(EventArgs e) { if (Enabled) base.OnClick(e); }
    }
}
