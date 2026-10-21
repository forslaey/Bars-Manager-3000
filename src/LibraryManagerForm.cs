using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class LibraryManagerForm : ManagerModuleForm
    {
        private readonly LibraryGridScrollBar gridScroll = new LibraryGridScrollBar();
        private readonly ServiceGrid grid = new ServiceGrid();
        private readonly Label summary, sourceLabel, activity;
        private readonly BrandButton refresh, install, stop, report, selectAll;
        private readonly RefreshToggle monitor = new RefreshToggle();
        private readonly JournalTextBox journal = new JournalTextBox();
        private readonly JournalScrollBar journalScroll = new JournalScrollBar();
        private readonly Timer timer = new Timer { Interval = 30000 };
        private readonly Dictionary<string, LibraryCheck> checks = new Dictionary<string, LibraryCheck>();
        private readonly HashSet<string> selection = new HashSet<string>();
        private readonly Font headerFont = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        private readonly bool preview;
        private bool checking, installing, stopRequested, binding, firstScan = true, useFocusedRow;
        private string source, lastLog, sourceError;
        private LibrarySession activeSession;
        private volatile bool cancelRequested;
        private bool closeAfterCancel;
        private bool rebootRequired, pendingRestart;
        private readonly BrandButton restart;
        private readonly ToolTip restartHelp = new ToolTip();
        private readonly BrandButton windowsComponents;
        private Func<bool, DialogResult> confirmCancellation = null;
        internal const string CancelWarning = "Отменить очередь и завершить процессы текущего установщика?\r\n\r\nСначала отправим запрос закрытия; через 2 секунды завершим оставшиеся процессы этой очереди. Принудительное прерывание может оставить компонент установленным частично. Полный откат не гарантирован; после отмены нужна проверка.\r\n\r\nСистемную службу Windows Installer и посторонние установки не завершаем. Windows может продолжить завершение своей операции или откат.";
        internal bool MenuRequested { get; private set; }
        internal bool IsInstalling { get { return installing; } }
        internal string SourceDirectory { get { return source; } }
        public LibraryManagerForm(bool previewMode) : base("Bars Component Manager 3000", new Size(1120, 800))
        {
            preview = previewMode; MinimumSize = new Size(1020, 760);
            if (!preview)
            {
                try { if (File.Exists(LibraryCatalog.CachePath)) LibraryCatalog.Load(LibraryCatalog.CachePath); } catch (Exception ex) { sourceError = "Не удалось прочитать сохранённый каталог: " + ex.Message; }
                source = LibrarySource.AutoDetect(AppDomain.CurrentDomain.BaseDirectory);
                if (source == null)
                {
                    try { string path = ApplicationStorage.FilePath("library-source.txt"); if (File.Exists(path) && Directory.Exists(File.ReadAllText(path).Trim())) source = File.ReadAllText(path).Trim(); } catch (Exception) { }
                }
            }
            var body = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 154)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 128)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            var status = new Card { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 12) };
            var statusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Margin = Padding.Empty };
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label title = Label("Менеджер системных библиотек Windows", false); title.Font = new Font("Segoe UI", 13, FontStyle.Bold); statusLayout.Controls.Add(title, 0, 0); statusLayout.SetColumnSpan(title, 2);
            monitor.Text = "Проверять каждый 30с."; monitor.AccessibleName = "Проверять каждые 30 секунд"; monitor.Dock = DockStyle.Fill; monitor.Checked = true; monitor.Margin = Padding.Empty;
            monitor.CheckedChanged += delegate { timer.Enabled = monitor.Checked && !preview; }; statusLayout.Controls.Add(monitor, 1, 3);
            summary = Label("Проверяем установленные компоненты…", true); statusLayout.Controls.Add(summary, 0, 1); statusLayout.SetColumnSpan(summary, 2);
            sourceLabel = Label("", true); sourceLabel.Font = new Font("Segoe UI", 9); statusLayout.Controls.Add(sourceLabel, 0, 2); statusLayout.SetColumnSpan(sourceLabel, 2); status.Controls.Add(statusLayout);
            body.Controls.Add(status, 0, 0);
            var tableCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 12), Margin = new Padding(0, 0, 0, 12) };
            var tableLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = new Padding(0, 0, 0, 10) };
            foreach (int weight in new[] { 28, 18, 15, 22, 17 }) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, weight)); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            refresh = Button("Проверить систему", false); refresh.Click += async delegate { await RefreshAsync(true); };
            BrandButton select = Button("Выбрать недостающие", false); select.Click += delegate { if (installing || checking) return; SelectMissing(); RenderRows(); };
            selectAll = Button("Выбрать всё", false); selectAll.Click += delegate { ToggleAll(); };
            install = Button("Установить выбранные", true); install.Click += async delegate { await InstallAsync(); };
            stop = Button("Отменить операцию", false); stop.Margin = Padding.Empty; stop.Enabled = false;
            stop.Click += delegate { if (installing && !cancelRequested && ConfirmCancel(false)) RequestCancellation(false); };
            actions.Controls.Add(install, 0, 0); actions.Controls.Add(select, 1, 0); actions.Controls.Add(selectAll, 2, 0); actions.Controls.Add(refresh, 3, 0); actions.Controls.Add(stop, 4, 0); tableLayout.Controls.Add(actions, 0, 0);
            ConfigureGrid(); var gridSlot = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            gridScroll.Dock = DockStyle.Right; gridScroll.Width = 14; gridScroll.Bind(grid); gridSlot.Controls.Add(grid); gridSlot.Controls.Add(gridScroll); tableLayout.Controls.Add(gridSlot, 0, 1); tableCard.Controls.Add(tableLayout); body.Controls.Add(tableCard, 0, 1);
            var logCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8), Margin = Padding.Empty };
            var logTitle = Label("Журнал проверки и установки", true); logTitle.Dock = DockStyle.Top; logTitle.Height = 24; logTitle.Font = new Font("Segoe UI", 9);
            journal.Dock = DockStyle.Fill; journal.Font = new Font("Consolas", 9); journalScroll.Dock = DockStyle.Right; journalScroll.Width = 14; journalScroll.Bind(journal);
            logCard.Controls.Add(journal); logCard.Controls.Add(journalScroll); logCard.Controls.Add(logTitle); body.Controls.Add(logCard, 0, 2);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, Padding = new Padding(0, 10, 0, 0), Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            restart = Button("Перезагрузка", false); restart.Margin = new Padding(0, 0, 10, 0); restart.Enabled = false; restart.Click += async delegate { if (!installing && !checking) await RestartOffer.Show(this); }; footer.Controls.Add(restart, 3, 1);
            activity = Label("Папка установщиков не нужна для проверки системы", true); activity.Font = new Font("Segoe UI", 9); footer.Controls.Add(activity, 0, 0); footer.SetColumnSpan(activity, 5);
            report = Button("Сохранить отчёт", false); report.Click += delegate { ExportReport(); }; footer.Controls.Add(report, 1, 1);
            BrandButton back = Button("В меню", false); back.Margin = Padding.Empty; back.Click += delegate { MenuRequested = true; Close(); }; footer.Controls.Add(back, 4, 1);
            windowsComponents = Button("Компоненты", false); windowsComponents.Click += delegate { OpenWindowsComponents(); }; footer.Controls.Add(windowsComponents, 2, 1);
            body.Controls.Add(footer, 0, 3);
            Root.Controls.Add(body, 0, 1); SetTheme(Colors.Dark, false); UpdateSource();
            timer.Tick += async delegate { if (!installing && !checking && Visible) await RefreshAsync(false); };
            Shown += async delegate
            {
                if (preview) { FillPreview(); return; }
                if (!String.IsNullOrEmpty(sourceError)) Log(sourceError);
                await RefreshAsync(true); if (!IsDisposed && !Disposing) timer.Enabled = monitor.Checked;
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (installing)
                {
                    e.Cancel = true;
                    if (closeAfterCancel && e.CloseReason != CloseReason.WindowsShutDown && e.CloseReason != CloseReason.TaskManagerClosing) return;
                    if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
                    { RequestCancellation(true); e.Cancel = false; return; }
                    if (cancelRequested || ConfirmCancel(true)) RequestCancellation(true); else MenuRequested = false;
                }
                else timer.Stop();
            };
        }
        private bool ConfirmCancel(bool closing)
        {
            return (confirmCancellation == null ? MessageBox.Show(this, (closing ? (MenuRequested ? "Отменить операцию и вернуться в меню?\r\n\r\n" : "Отменить операцию и закрыть программу?\r\n\r\n") : "") + CancelWarning,
                "Отмена операции", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) : confirmCancellation(closing)) == DialogResult.Yes;
        }
        internal void RequestCancellation(bool closing)
        {
            closeAfterCancel |= closing; cancelRequested = stopRequested = true; stop.Enabled = false;
            if (activeSession != null) activeSession.Cancel();
            activity.Text = closing ? (MenuRequested ? "Отменяем операцию и возвращаемся в меню…" : "Отменяем операцию и закрываем программу…") : "Отменяем текущую операцию…";
            Log("Запрошена отмена: новые пакеты не запускаются; процессы активной установки будут завершены. Полный откат не гарантирован.");
        }
        private void UpdateRestartAvailability()
        {
            string reason=pendingRestart ? "Установщик сообщил о необходимости перезагрузки." : LibraryDetection.RebootPendingReason();
            restart.Enabled=!installing&&!checking&&reason.Length>0;
            restart.AccessibleDescription=reason.Length>0?reason:"Перезагрузка не требуется.";
            restartHelp.SetToolTip(restart,restart.AccessibleDescription);
        }
        private void OpenWindowsComponents()
        {
            if(installing || checking) return;
            bool wasRunning=timer.Enabled; timer.Stop();
            try
            {
                using(var form=new WindowsFeaturesForm(preview, true))
                {
                    form.SetTheme(Colors.Dark,false); form.StartPosition=FormStartPosition.CenterParent;
                    form.ShowDialog(this);
                }
                SetTheme(ThemePreference.LoadDark(),false);
            }
            finally { if(!IsDisposed && !Disposing) timer.Enabled=wasRunning && monitor.Checked && !preview; }
        }
        private void ConfigureGrid()
        {
            grid.Dock = DockStyle.Fill; grid.Margin = Padding.Empty; grid.BackgroundColor = Colors.Surface; grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = grid.AllowUserToResizeRows = grid.AllowUserToResizeColumns = grid.AllowUserToOrderColumns = false;
            grid.RowHeadersVisible = false; grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersHeight = 56; grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.Font = headerFont; grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Padding = new Padding(6); grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True; grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.Single; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single; grid.ScrollBars = ScrollBars.None;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "", ReadOnly = true, Width = 46, MinimumWidth = 46, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            string[] names = { "Компонент", "Комплект", "В системе", "Разрядность", "Состояние", "Рекомендация" }; int[] weights = { 24, 12, 14, 9, 14, 27 };
            for (int i = 0; i < names.Length; i++) { int index = grid.Columns.Add("c" + i, names[i]); grid.Columns[index].ReadOnly = true; grid.Columns[index].FillWeight = weights[i]; grid.Columns[index].MinimumWidth = i == 0 ? 140 : i == 3 ? 100 : 70; }
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.EditMode = DataGridViewEditMode.EditProgrammatically;
            grid.CellClick += delegate(object sender, DataGridViewCellEventArgs e) { if (!binding && e.RowIndex >= 0 && e.ColumnIndex > 0) { useFocusedRow = true; UpdateAction(); } };
            grid.CurrentCellChanged += delegate { if (!binding && grid.Focused && grid.CurrentCell != null && grid.CurrentCell.ColumnIndex > 0) { useFocusedRow = true; UpdateAction(); } };
            grid.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e)
            { if (e.Button == MouseButtons.Left && e.ColumnIndex == 0) ToggleSelection(e.RowIndex); };
            grid.KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.KeyCode == Keys.Space && !e.Alt && !e.Control && grid.CurrentRow != null) { ToggleSelection(grid.CurrentRow.Index); e.Handled = e.SuppressKeyPress = true; } };
            grid.CellMouseEnter += delegate(object sender, DataGridViewCellEventArgs e) { grid.Cursor = e.RowIndex >= 0 && e.ColumnIndex == 0 ? Cursors.Hand : Cursors.Default; };
            grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (binding || e.RowIndex < 0 || e.ColumnIndex != 0 || e.RowIndex >= grid.Rows.Count) return;
                var package = grid.Rows[e.RowIndex].Tag as LibraryPackage; if (package == null) return;
                if (Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[0].Value)) selection.Add(package.Id); else selection.Remove(package.Id); UpdateSummary();
            };
            grid.CellPainting += delegate(object sender, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
                e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground);
                var graphicsState = e.Graphics.Save();
                try
                {
                e.Graphics.SetClip(e.CellBounds, CombineMode.Intersect);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int diameter = (int)Math.Round(18 * e.Graphics.DpiX / 96F);
                var circle = new Rectangle(e.CellBounds.Left + (e.CellBounds.Width - diameter) / 2, e.CellBounds.Top + (e.CellBounds.Height - diameter) / 2, diameter, diameter);
                bool chosen = Convert.ToBoolean(e.Value);
                using (var fill = new SolidBrush(chosen ? Colors.Accent : Colors.Surface)) e.Graphics.FillEllipse(fill, circle);
                using (var edge = new Pen(chosen ? Colors.Accent : Colors.Border)) e.Graphics.DrawEllipse(edge, circle);
                if (chosen) using (var point = new SolidBrush(Colors.AccentText)) e.Graphics.FillEllipse(point, Rectangle.Inflate(circle, -diameter / 3, -diameter / 3));
                }
                finally { e.Graphics.Restore(graphicsState); }
                e.Paint(e.CellBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus);
                e.Handled = true;
            };
        }
        private void ToggleSelection(int rowIndex)
        {
            if (binding || checking || installing || rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            var row = grid.Rows[rowIndex]; var package = row.Tag as LibraryPackage; LibraryCheck state;
            if (package == null || !checks.TryGetValue(package.Id, out state) || state.State == LibraryState.NotApplicable) return;
            useFocusedRow = false; row.Cells[0].Value = !Convert.ToBoolean(row.Cells[0].Value);
            grid.InvalidateCell(0, rowIndex);
        }
        protected override void OnThemeChanged()
        {
            if (grid == null) return;
            grid.BackgroundColor = Colors.Surface; grid.GridColor = Colors.Border;
            grid.DefaultCellStyle.BackColor = Colors.Surface; grid.DefaultCellStyle.ForeColor = Colors.Text;
            grid.DefaultCellStyle.SelectionBackColor = Colors.Selection; grid.DefaultCellStyle.SelectionForeColor = Colors.SelectionText;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Colors.SurfaceAlt; grid.ColumnHeadersDefaultCellStyle.ForeColor = Colors.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Colors.SurfaceAlt; grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Colors.Text;
            journal.BackColor = Colors.Surface; journal.ForeColor = Colors.Text; journalScroll.Palette = Colors; gridScroll.Palette = Colors; gridScroll.BackColor = Colors.Surface;
        }
        private void UpdateSource()
        {
            sourceLabel.Text = String.IsNullOrEmpty(source) ? "Папка не выбрана. Запросим её только при начале установки." : "Папка библиотек: " + source;
        }
        private void Log(string text)
        {
            if (IsDisposed) return; journal.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); journal.SelectionStart = journal.TextLength; journal.ScrollToCaret();
            if (journal.TextLength > 150000) journal.Text = journal.Text.Substring(journal.TextLength - 100000);
            if (!String.IsNullOrEmpty(lastLog)) try { File.AppendAllText(lastLog, DateTime.Now.ToString("O") + " " + text + Environment.NewLine, Encoding.UTF8); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        private void SelectMissing()
        {
            useFocusedRow = false; selection.Clear(); foreach (LibraryCheck check in checks.Values) if (check.Package.Recommended && LibraryVersions.NeedsInstall(check)) selection.Add(check.Package.Id);
        }
        private void ToggleAll()
        {
            if (binding || checking || installing) return;
            var available = checks.Values.Where(c => c.State != LibraryState.NotApplicable).ToList();
            bool clear = available.Count > 0 && available.All(c => selection.Contains(c.Package.Id));
            useFocusedRow = false; selection.Clear();
            if (!clear) foreach (var check in available) selection.Add(check.Package.Id);
            RenderRows();
        }
        private List<LibraryCheck> ActionTargets()
        {
            if (useFocusedRow && grid.CurrentRow != null)
            {
                var package = grid.CurrentRow.Tag as LibraryPackage; LibraryCheck check;
                if (package != null && checks.TryGetValue(package.Id, out check)) return new List<LibraryCheck> { check };
            }
            return checks.Values.Where(c => selection.Contains(c.Package.Id)).ToList();
        }
        private void UpdateAction()
        {
            if (install == null) return;
            install.Text = LibraryVersions.BatchCaption(ActionTargets());
            var available = checks.Values.Where(c => c.State != LibraryState.NotApplicable).ToList();
            selectAll.Text = available.Count > 0 && available.All(c => selection.Contains(c.Package.Id)) ? "Снять выбор" : "Выбрать всё";
            selectAll.Enabled = !checking && !installing && available.Count > 0;
            install.AccessibleDescription = useFocusedRow ? "Действие для выделенной строки" : "Действие для отмеченных компонентов";
        }
        private void UpdateSummary()
        {
            summary.Text = "Компонентов: " + checks.Count + " · установлено: " + checks.Values.Count(c => c.State == LibraryState.Installed) + " · не найдено: " + checks.Values.Count(c => c.State == LibraryState.Missing && c.Relation != LibraryRelation.Older) +
                " · нужно обновить: " + checks.Values.Count(c => c.Relation == LibraryRelation.Older) + " · нужна проверка: " + checks.Values.Count(c => c.State == LibraryState.Unknown) + " · выбрано: " + selection.Count; UpdateAction();
        }
        private void RenderRows()
        {
            string active = grid.CurrentRow == null || grid.CurrentRow.Tag == null ? null : ((LibraryPackage)grid.CurrentRow.Tag).Id;
            int scroll = grid.FirstDisplayedScrollingRowIndex; binding = true;
            try
            {
                grid.Rows.Clear();
                foreach (LibraryCheck check in LibraryPresentation.Sort(checks.Values))
                {
                    LibraryPackage package = check.Package;
                    string detail = (package.Preview ? "Предварительная версия. " : "") + (package.Interactive ? "С собственным окном установки. " : "") + check.Detail;
                    int index = grid.Rows.Add(selection.Contains(package.Id), package.Name, LibraryVersions.Required(package), String.IsNullOrEmpty(check.ActualVersion) ? "—" : check.ActualVersion, package.Architecture == "both" ? "x86 / x64" : package.Architecture, check.Caption, LibraryVersions.Advice(check));
                    foreach (DataGridViewCell cell in grid.Rows[index].Cells) cell.ToolTipText = detail;
                    grid.Rows[index].Tag = package; grid.Rows[index].Cells[0].ReadOnly = true;
                    if (check.State == LibraryState.NotApplicable) grid.Rows[index].DefaultCellStyle.ForeColor = Colors.Muted;
                    if (package.Id == active) grid.CurrentCell = grid.Rows[index].Cells[1];
                }
                if (scroll >= 0 && scroll < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = scroll;
            }
            finally { binding = false; } UpdateSummary();
        }
        internal async Task RefreshAsync(bool refreshSource)
        {
            if (checking || installing || IsDisposed) return;
            checking = true; windowsComponents.Enabled = false; refresh.Enabled = install.Enabled = restart.Enabled = false; activity.Text = "Проверяем систему…";
            try
            {
                if (refreshSource && !String.IsNullOrEmpty(source) && Directory.Exists(source))
                {
                    try { await ReadSourceAsync(); }
                    catch (Exception ex) { Log("Источник не обновлён; проверяем систему по сохранённому каталогу: " + ex.Message); }
                }
                List<LibraryCheck> found = await Task.Run(() => LibraryDetection.Scan());
                if (IsDisposed || Disposing) return;
                checks.Clear(); foreach (LibraryCheck check in found) checks[check.Package.Id] = check;
                if (firstScan) {
                    SelectMissing(); firstScan = false;
                    if (!preview) try { var saved = PausedLibraryQueue.Load(); if (saved.Count > 0) { selection.Clear(); foreach (var id in saved) if (checks.ContainsKey(id) && checks[id].State != LibraryState.NotApplicable) selection.Add(id); Log("Восстановлен выбор приостановленной очереди. Проверь состояния и выбери действие заново; автоматически ничего не запускается."); } } catch (Exception ex) { Log("Не удалось восстановить выбор: " + ex.Message); }
                }
                UpdateRestartAvailability();
                RenderRows(); Log("Проверка завершена. Подходящих компонентов: " + checks.Values.Count(c => c.State == LibraryState.Installed) + " из " + checks.Count + "; нужно обновить: " + checks.Values.Count(c => c.Relation == LibraryRelation.Older) + "; нужна проверка: " + checks.Values.Count(c => c.State == LibraryState.Unknown) + ".");
                activity.Text = "Проверка работает без папки установщиков";
            }
            catch (Exception ex) { Log("Ошибка проверки: " + ex.Message); activity.Text = "Не удалось завершить проверку"; }
            finally { checking = false; if (!IsDisposed) { windowsComponents.Enabled = !installing; refresh.Enabled = install.Enabled = true; UpdateRestartAvailability(); UpdateAction(); } }
        }
        private async Task ReadSourceAsync()
        {
            string external = Path.Combine(source, "libraries.catalog.xml");
            if (File.Exists(external)) LibraryCatalog.Merge(LibraryCatalog.ReadFile(external));
            List<LibraryPackage> packages = await Task.Run(() => LibraryDiscovery.Scan(source, text =>
            {
                SafeActivity(text);
                if (text.StartsWith("Неизвестный", StringComparison.Ordinal) && IsHandleCreated && !IsDisposed)
                    try { BeginInvoke((MethodInvoker)delegate { Log(text); }); } catch (InvalidOperationException) { }
            }, () => installing && cancelRequested));
            LibraryCatalog.Merge(packages);
            try { LibraryCatalog.Save(LibraryCatalog.CachePath); } catch (Exception ex) { Log("Каталог доступен в сеансе, но не сохранён: " + ex.Message); }
            Log("Каталог обновлён из метаданных: " + packages.Count + " пакетов. Для изменения версий пересборка не нужна.");
        }
        private void SafeActivity(string text)
        {
            if (!IsDisposed && IsHandleCreated) try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) activity.Text = text; }); } catch (InvalidOperationException) { }
        }
        private async Task InstallAsync()
        {
            if (checking || installing || preview) return;
            grid.EndEdit();
            List<LibraryCheck> targets = ActionTargets();
            if (targets.Count == 0) { activity.Text = "Выбери строку или отметь компоненты в первом столбце"; return; }
            if (targets.All(c => c.State == LibraryState.Unknown || c.State == LibraryState.NotApplicable) && !targets.Any(LibraryVersions.CanRemove))
            { await RefreshAsync(false); return; }
            LibraryOperation operation = targets.Any(c => c.Relation == LibraryRelation.Older) ? LibraryOperation.Update : LibraryOperation.Install;
            if (targets.Any(LibraryVersions.CanRemove))
            {
                using (var dialog = new LibraryActionDialog(Colors, Icon, targets))
                { if (dialog.ShowDialog(this) != DialogResult.OK) return; operation = dialog.Operation; }
                if (operation == LibraryOperation.Reinstall && targets.Any(c => c.Relation == LibraryRelation.Newer || c.HasNewerParts))
                { activity.Text = "Обнови файлы установщиков: переустановка более старой версией заблокирована"; return; }
            }
            var targetIds = new HashSet<string>(targets.Select(c => c.Package.Id));
            if (operation != LibraryOperation.Remove)
            {
            if (String.IsNullOrEmpty(source) || !Directory.Exists(source)) source = LibrarySource.AutoDetect(AppDomain.CurrentDomain.BaseDirectory);
            if (String.IsNullOrEmpty(source))
            {
                using (var dialog = new FolderBrowserDialog { Description = "Выбери папку «Библиотеки» с установщиками BARS", ShowNewFolderButton = false })
                { if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try { source = LibrarySource.NormalizeRoot(dialog.SelectedPath); }
                    catch (Exception ex) { Log("Не удалось выбрать папку: " + ex.Message); return; } }
            }
            }
            UpdateSource(); installing = true; windowsComponents.Enabled = false; stopRequested = cancelRequested = closeAfterCancel = false; timer.Stop(); install.Enabled = refresh.Enabled = selectAll.Enabled = restart.Enabled = false; stop.Enabled = true;
            foreach (DataGridViewRow row in grid.Rows) row.Cells[0].ReadOnly = true;
            try
            {
                string reports = Path.Combine(ApplicationStorage.DirectoryPath, "LibraryReports"); Directory.CreateDirectory(reports);
                lastLog = Path.Combine(reports, "queue-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".log");
                Log("Готовим очередь. Кнопка отмены останавливает текущую операцию после подтверждения; полный откат не гарантирован.");
                if (operation != LibraryOperation.Remove) await ReadSourceAsync();
                Directory.CreateDirectory(ApplicationStorage.DirectoryPath); if (operation != LibraryOperation.Remove) File.WriteAllText(ApplicationStorage.FilePath("library-source.txt"), source, Encoding.UTF8);
                List<LibraryPackage> chosen = targetIds.Select(LibraryCatalog.Find).Where(p => p != null).ToList();
                List<LibraryPackage> plan = operation == LibraryOperation.Remove ? chosen.Where(p => p.Kind != LibraryKind.Composite).OrderByDescending(p => p.Order).ToList() : LibraryPlan.Expand(chosen);
                foreach (LibraryPackage dependency in plan.Where(p => !targetIds.Contains(p.Id))) Log("Добавлена зависимость: " + dependency.Name);
                var needed = new List<LibraryPackage>();
                foreach (LibraryPackage package in plan)
                {
                    if (stopRequested) break;
                    LibraryCheck state = await Task.Run(() => LibraryDetection.Check(package)); checks[package.Id] = state;
                    if (operation == LibraryOperation.Remove && !LibraryVersions.CanRemove(state)) { Log(package.Name + ": не установлено; удаление пропущено"); continue; }
                    if (state.State == LibraryState.Installed && (operation == LibraryOperation.Install || operation == LibraryOperation.Update || state.Relation == LibraryRelation.Newer || !targetIds.Contains(package.Id))) { Log(package.Name + ": уже установлено; " + state.Detail + "; пропускаем"); continue; }
                    if (state.State == LibraryState.NotApplicable) throw new InvalidOperationException(package.Name + ": " + state.Detail);
                    if (state.State == LibraryState.Unknown && (operation == LibraryOperation.Install || operation == LibraryOperation.Update))
                        throw new InvalidOperationException(package.Name + ": нужна проверка. " + state.Detail);
                    if (operation == LibraryOperation.Reinstall && (state.Relation == LibraryRelation.Newer || state.HasNewerParts))
                        throw new InvalidOperationException(package.Name + ": переустановка старым комплектом заблокирована");
                    if (operation != LibraryOperation.Remove && (package.Kind == LibraryKind.VcBundle || package.Kind == LibraryKind.VcLegacy))
                        await Task.Run(() => LibraryVcProfiles.ValidateNoDowngrade(package, LibraryMaintenance.Inventory()));
                    needed.Add(package);
                }
                foreach (LibraryPackage package in needed)
                {
                    if (stopRequested) break;
                    if (operation == LibraryOperation.Remove || operation == LibraryOperation.Reinstall && LibraryVersions.CanRemove(checks[package.Id]))
                    {
                        SafeActivity("Проверяем деинсталлятор: " + package.Name);
                        await Task.Run(() => { var products = LibraryMaintenance.Resolve(package); if (operation == LibraryOperation.Reinstall) LibraryMaintenance.ValidateReinstall(package, products); foreach (var product in products) LibraryMaintenance.Command(product, Path.Combine(reports, "preflight.log")); });
                    }
                    if (operation != LibraryOperation.Remove)
                    {
                        SafeActivity("Проверяем файл: " + package.Name);
                        await Task.Run(() => LibrarySource.VerifyFile(LibrarySource.PackagePath(source, package), package, () => cancelRequested));
                    }
                }
                if (stopRequested || needed.Count == 0) { Log(needed.Count == 0 ? "Для выбранных компонентов нет действий в этой очереди." : "Очередь остановлена до запуска установщиков"); return; }
                PausedLibraryQueue.Save(new string[0], operation);
                using (LibrarySession session = await Task.Run(() => LibrarySession.Start(() => cancelRequested)))
                {
                    activeSession = session; if (cancelRequested) session.Cancel();
                    for (int i = 0; i < needed.Count && !stopRequested; i++)
                    {
                        LibraryPackage package = needed[i]; string prefix = "[" + (i + 1) + "/" + needed.Count + "] " + package.Name;
                        LibraryOperation step = operation == LibraryOperation.Reinstall && !targetIds.Contains(package.Id) ? LibraryOperation.Install : operation;
                        if ((step == LibraryOperation.Install || step == LibraryOperation.Update) && checks[package.Id].Relation == LibraryRelation.Older) step = LibraryOperation.Update;
                        Log(prefix + (step == LibraryOperation.Remove ? ": удаление" : step == LibraryOperation.Reinstall ? ": переустановка" : step == LibraryOperation.Update ? ": обновление" : ": установка"));
                        LibraryInstallResult result = await Task.Run(() => session.Execute(package, source, step, text => SafeActivity(prefix + " · " + text)));
                        Log(prefix + ": " + result.Message + (String.IsNullOrEmpty(result.LogPath) ? "" : ". Отчёт: " + result.LogPath));
                        if (closeAfterCancel && cancelRequested) break;
                        List<LibraryCheck> updated = await Task.Run(() => LibraryDetection.Scan()); checks.Clear(); foreach (var check in updated) checks[check.Package.Id] = check; RenderRows();
                        if (operation == LibraryOperation.Remove && checks[package.Id].State != LibraryState.Missing) Log(package.Name + ": после удаления — " + checks[package.Id].Caption + ". " + checks[package.Id].Detail);
                        if (result.Reboot) {
                            rebootRequired = pendingRestart = true;
                            PausedLibraryQueue.Save(needed.Skip(i).Select(p => p.Id), operation);
                            Log("Нужна перезагрузка. Выбор оставшихся компонентов сохранён; после запуска проверь рекомендацию и подтверди действие заново."); break;
                        }
                        if (!LibraryPlan.Success(result.ExitCode) || result.State != (operation == LibraryOperation.Remove ? LibraryState.Missing : LibraryState.Installed))
                        { Log("Очередь приостановлена: результат текущего компонента требует проверки. Уже выполненные шаги сохранены в журнале."); break; }
                    }
                }
            }
            catch (OperationCanceledException) { Log("Операция отменена. Запусти проверку системы перед повторной установкой."); }
            catch (Win32Exception ex) { Log(ex.NativeErrorCode == 1223 ? "Запрос прав администратора отменён; новые пакеты не запускались" : "Ошибка установки: " + ex.Message); }
            catch (Exception ex) { Log("Очередь остановлена: " + ex.Message + ". Если внешний установщик ещё работает, дождись его завершения перед повтором."); }
            finally
            {
                activeSession = null; installing = false;
                if (!IsDisposed)
                {
                    stop.Enabled = false; refresh.Enabled = install.Enabled = true; timer.Enabled = monitor.Checked && !closeAfterCancel;
                    activity.Text = cancelRequested ? "Отменено. Проверь состояние компонентов перед повторной установкой." : "Очередь завершена или приостановлена. Подробности в журнале.";
                    RenderRows(); windowsComponents.Enabled = true; UpdateRestartAvailability();
                    if (closeAfterCancel) BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) Close(); });
                }
            }
            if (rebootRequired && !closeAfterCancel && !cancelRequested && !IsDisposed) { rebootRequired = false; await RestartOffer.Show(this); }
        }
        private void ExportReport()
        {
            using (var dialog = new SaveFileDialog { Filter = "Текстовый отчёт (*.txt)|*.txt", FileName = "BARS-компоненты-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".txt" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var text = new StringBuilder("Bars Component Manager 3000 · " + DateTime.Now.ToString("O") + Environment.NewLine);
                    foreach (LibraryCheck check in LibraryPresentation.Sort(checks.Values)) text.AppendLine(check.Package.Name + " | в комплекте: " + LibraryVersions.Required(check.Package) + " | в системе: " + check.ActualVersion + " | " + check.Caption + " | " + LibraryVersions.Advice(check) + " | " + check.Detail);
                    text.AppendLine().AppendLine(journal.Text); File.WriteAllText(dialog.FileName, text.ToString(), new UTF8Encoding(true)); activity.Text = "Отчёт сохранён";
                }
                catch (Exception ex) { Log("Не удалось сохранить отчёт: " + ex.Message); }
            }
        }
        private void FillPreview()
        {
            checks.Clear(); int index = 0;
            foreach (LibraryPackage package in LibraryCatalog.All())
            {
                LibraryState state = index++ % 3 == 0 ? LibraryState.Installed : package.Interactive ? LibraryState.Unknown : LibraryState.Missing;
                checks[package.Id] = new LibraryCheck { Package = package, State = state, Relation = state == LibraryState.Installed ? LibraryRelation.Equal : LibraryRelation.Missing, HasProduct = state == LibraryState.Installed, ActualVersion = state == LibraryState.Installed ? LibraryVersions.Required(package) : "", Detail = state == LibraryState.Installed ? "Версия и разрядность подтверждены" : "Папка не нужна для проверки; выбери компонент для установки" };
            }
            SelectMissing(); RenderRows(); activity.Text = "Проверка системы доступна без установщиков"; Log("Проверка завершена. Путь к библиотекам запросим при начале установки.");
        }
        protected override void Dispose(bool disposing) { if (disposing) { if (installing && activeSession != null) activeSession.Cancel(); timer.Dispose(); restartHelp.Dispose(); } base.Dispose(disposing); if (disposing) headerFont.Dispose(); }
    }
}
