using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class ApplicationLogsForm : ManagerModuleForm, IModuleOperation
    {
        private readonly ServiceGrid running = new ServiceGrid();
        private readonly LibraryGridScrollBar runningScroll = new LibraryGridScrollBar();
        private readonly BrandTextBox path = new BrandTextBox { Dock = DockStyle.Fill, ReadOnly = true, Margin = new Padding(0, 3, 0, 3) };
        private readonly Label status;
        private readonly BrandButton collect, cancel, refresh;
        private CancellationTokenSource archive;
        private string source;
        private bool scanning, sourceIsFolder;

        internal ApplicationLogsForm() : base("Логи программ BARS", new Size(840, 460))
        {
            MinimumSize = SizeFromClientSize(new Size(720, 460));
            ClientSize = new Size(840, 460);
            Root.RowStyles[0].Height = 78;
            Root.AutoScroll = true;
            var body = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 36, 44, 45, 48 })
                body.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            body.MinimumSize = new Size(0, 345 + body.Margin.Vertical);
            body.Controls.Add(ModuleHeading("Логи программ BARS"), 0, 0);
            running.Dock = DockStyle.Fill;
            running.BorderStyle = BorderStyle.None;
            running.ReadOnly = true;
            running.AllowUserToAddRows = running.AllowUserToDeleteRows = running.AllowUserToResizeRows = false;
            running.RowHeadersVisible = false;
            running.MultiSelect = false;
            running.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            running.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            running.EnableHeadersVisualStyles = false;
            running.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            running.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            running.ColumnHeadersHeight = 42;
            running.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            running.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            running.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            running.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            running.RowTemplate.Height = 30;
            running.ScrollBars = ScrollBars.None;
            running.TouchScrollingEnabled = true;
            running.Columns.Add("app", "Программа");
            running.Columns.Add("path", "Путь к программе");
            running.Columns[0].FillWeight = 30;
            running.Columns[1].FillWeight = 70;
            foreach (DataGridViewColumn column in running.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            running.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (running.Rows.Count != 0) return;
                var area = Rectangle.FromLTRB(0, running.ColumnHeadersHeight, running.ClientSize.Width, running.ClientSize.Height);
                TextRenderer.DrawText(e.Graphics, scanning ? "Ищем программы BARS…" : "Запущенные программы BARS не найдены.",
                    running.Font, area, Colors.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            running.CellClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) Choose(Convert.ToString(running.Rows[e.RowIndex].Cells[1].Value));
            };
            running.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && running.CurrentRow != null)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    Choose(Convert.ToString(running.CurrentRow.Cells[1].Value));
                }
            };
            var processSlot = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            var processCard = new Card { Dock = DockStyle.Top, Padding = new Padding(1), Margin = Padding.Empty };
            runningScroll.Dock = DockStyle.Right;
            runningScroll.Width = 14;
            runningScroll.UseCardBackground = true;
            runningScroll.MaximumThumbLength = 80;
            runningScroll.Bind(running);
            processCard.Controls.Add(running);
            processCard.Controls.Add(runningScroll);
            processSlot.Controls.Add(processCard);
            body.Controls.Add(processSlot, 0, 1);
            Action fitProcessTable = () =>
            {
                int available = processSlot.ClientSize.Height - processCard.Padding.Vertical;
                if (available <= 0) return;
                int gridHeight = running.RowCount == 0 ? available : GridViewportLayout.WholeRowsHeight(running, available);
                processCard.Height = gridHeight + processCard.Padding.Vertical;
            };
            processSlot.SizeChanged += delegate { fitProcessTable(); };
            running.RowsAdded += delegate { fitProcessTable(); };
            running.RowsRemoved += delegate { fitProcessTable(); };
            Shown += delegate { fitProcessTable(); };
            body.Controls.Add(path, 0, 2);
            var pickers = Buttons(3);
            refresh = Button("Обновить список", false);
            refresh.Click += async delegate { await Scan(); };
            var file = Button("Выбрать файл…", false);
            file.Click += delegate
            {
                using (var dialog = new OpenFileDialog { Filter = "Программы Windows (*.exe)|*.exe" })
                    if (ScopedDialogs.Show(this, dialog) == DialogResult.OK) Choose(dialog.FileName);
            };
            var folder = Button("Выбрать папку…", false);
            folder.Click += delegate
            {
                using (var dialog = new FolderBrowserDialog { ShowNewFolderButton = false })
                    if (ScopedDialogs.Show(this, dialog) == DialogResult.OK) Choose(dialog.SelectedPath);
            };
            pickers.Controls.Add(refresh, 0, 0); pickers.Controls.Add(file, 1, 0); pickers.Controls.Add(folder, 2, 0);
            body.Controls.Add(pickers, 0, 3);
            status = Label("Выбери запущенную программу или укажи путь вручную.", true);
            status.AutoEllipsis = true;
            body.Controls.Add(status, 0, 4);
            var actions = Buttons(4);
            collect = Button("Собрать логи", true);
            collect.Enabled = false;
            collect.Click += async delegate { await Collect(); };
            cancel = Button("Отменить сбор", false);
            cancel.Enabled = false;
            cancel.Click += delegate { if (archive != null) archive.Cancel(); };
            var back = Button("В меню", false);
            back.Click += delegate { if (!ModuleNavigation.ReturnToHub()) Close(); };
            actions.Controls.Add(collect, 0, 0); actions.Controls.Add(cancel, 1, 0); actions.Controls.Add(back, 2, 0);
            body.Controls.Add(actions, 0, 5);
            var close = Button("Закрыть", false);
            close.Click += delegate { Close(); };
            actions.Controls.Add(close, 3, 0);
            folder.Margin = Padding.Empty;
            ModuleNavigation.StandardFooter(actions, collect, cancel, back, close);
            Root.Controls.Add(body, 0, 1);
            AllowDrop = true;
            DragEnter += delegate(object sender, DragEventArgs e) { if (archive == null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += delegate(object sender, DragEventArgs e)
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null && paths.Length == 1) Choose(paths[0]);
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (archive == null) return;
                e.Cancel = true;
                if (closeAfterArchive) return;
                if (!ModuleNavigation.ExitConfirmed && BrandMessageBox.ConfirmStop(this, "Отменить сбор логов и закрыть окно?") != DialogResult.Yes) return;
                closeAfterArchive = true;
                archive.Cancel();
                status.Text = "Отменяем сбор логов…";
            };
            SetTheme(Colors.Dark, false);
            Shown += async delegate { await Scan(); };
        }

        private bool closeAfterArchive;
        string IModuleOperation.ActiveOperation { get { return archive != null ? "Логи программ BARS: сбор архива" : null; } }

        private static TableLayoutPanel Buttons(int count)
        {
            var row = new BufferedLayoutPanel { Dock = DockStyle.Fill, ColumnCount = count, RowCount = 1, Margin = Padding.Empty };
            for (int index = 0; index < count; index++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / count));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            return row;
        }

        protected override void OnThemeChanged()
        {
            running.BackgroundColor = Colors.Surface; running.AlternatingRowsDefaultCellStyle.BackColor = Colors.SurfaceAlt; running.GridColor = Colors.Border; runningScroll.Palette = Colors;
            running.DefaultCellStyle.BackColor = Colors.Surface; running.DefaultCellStyle.ForeColor = Colors.Text; running.DefaultCellStyle.SelectionBackColor = Colors.Selection; running.DefaultCellStyle.SelectionForeColor = Colors.SelectionText;
            running.ColumnHeadersDefaultCellStyle.BackColor = Colors.SurfaceAlt; running.ColumnHeadersDefaultCellStyle.ForeColor = Colors.Text; running.ColumnHeadersDefaultCellStyle.SelectionBackColor = Colors.SurfaceAlt; running.ColumnHeadersDefaultCellStyle.SelectionForeColor = Colors.Text;
            path.Palette = Colors;
        }

        private async Task Scan()
        {
            if (scanning || archive != null) return;
            scanning = true;
            refresh.Enabled = false;
            status.Text = "Ищем запущенные клиентские программы BARS…";
            try
            {
                var result = await Task.Run(() =>
                {
                    int skipped;
                    return Tuple.Create(RunningApplications(out skipped), skipped);
                });
                var paths = result.Item1;
                if (IsDisposed) return;
                running.Rows.Clear();
                foreach (string executable in paths) running.Rows.Add(Path.GetFileNameWithoutExtension(executable), executable);
                running.ClearSelection();
                if (source != null) Choose(source);
                else status.Text = paths.Length == 0 ? "Программы BARS не найдены. Укажи файл или папку вручную."
                    : "Выбери программу из списка или укажи путь вручную.";
                if (result.Item2 > 0)
                    status.Text += " Часть процессов недоступна для проверки.";
            }
            catch (Exception ex)
            {
                if (!IsDisposed) status.Text = "Не удалось проверить запущенные программы. Выбери файл или папку вручную. " + ex.Message;
            }
            finally { scanning = false; if (!IsDisposed) { refresh.Enabled = true; running.Invalidate(); } }
        }

        private static string[] RunningApplications(out int skipped)
        {
            skipped = 0;
            var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            int currentProcessId;
            using (Process current = Process.GetCurrentProcess()) currentProcessId = current.Id;
            foreach (Process process in Process.GetProcesses())
                using (process)
                {
                    try
                    {
                        if (process.MainWindowHandle == IntPtr.Zero) continue;
                        string executable = process.MainModule.FileName;
                        string name = Path.GetFileNameWithoutExtension(executable);
                        if (process.Id == currentProcessId) continue;
                        var version = FileVersionInfo.GetVersionInfo(executable);
                        if (IsManagerExecutable(name, version)) continue;
                        string company = version.CompanyName ?? "";
                        if (name.StartsWith("Bars.", StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith("DK.", StringComparison.OrdinalIgnoreCase) ||
                            company.IndexOf("DataKrat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            company.IndexOf("Датакрат", StringComparison.OrdinalIgnoreCase) >= 0)
                            paths.Add(executable);
                    }
                    catch (System.ComponentModel.Win32Exception) { skipped++; }
                    catch (InvalidOperationException) { }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { skipped++; }
                }
            var result = new string[paths.Count];
            paths.CopyTo(result);
            return result;
        }

        private static bool IsManagerExecutable(string name, FileVersionInfo version)
        {
            return IsManagerName(name) || IsManagerName(version.ProductName) ||
                IsManagerName(Path.GetFileNameWithoutExtension(version.OriginalFilename));
        }

        private static bool IsManagerName(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return false;
            return name.StartsWith("Bars Manager 3000", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Bars Service Manager 3000", StringComparison.OrdinalIgnoreCase);
        }

        private void Choose(string candidate)
        {
            if (archive != null) return;
            if (!Directory.Exists(candidate) && (!File.Exists(candidate) ||
                !String.Equals(Path.GetExtension(candidate), ".exe", StringComparison.OrdinalIgnoreCase)))
            {
                status.Text = "Выбери существующую папку или файл программы .exe.";
                return;
            }
            source = Path.GetFullPath(candidate);
            sourceIsFolder = Directory.Exists(source);
            path.Text = source;
            string logs = Path.Combine(Directory.Exists(source) ? source : Path.GetDirectoryName(source), "Logs");
            status.Text = Directory.Exists(logs) ? "Источник логов: " + logs : "Папка Logs пока не найдена: " + logs;
            collect.Enabled = Directory.Exists(logs);
        }

        private async Task Collect()
        {
            if (archive != null || source == null) return;
            string input = sourceIsFolder ? source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar : source;
            string name = sourceIsFolder ? new DirectoryInfo(source).Name : Path.GetFileNameWithoutExtension(input);
            archive = new CancellationTokenSource();
            CancellationToken token = archive.Token;
            collect.Enabled = false;
            cancel.Enabled = true;
            running.Enabled = false;
            try
            {
                status.Text = "Читаем папку Logs…";
                var folders = await Task.Run(() =>
                {
                    ServiceLogArchive.ValidateSource(input, new LogArchiveSelection(), token);
                    return ServiceLogArchive.ListDateFolders(input, token);
                });
                token.ThrowIfCancellationRequested();
                LogArchiveSelection selection;
                using (var scope = new LogArchiveDialog(Colors, name, folders, DateTime.Today, true))
                {
                    if (await ModuleNavigation.ShowOwnedAsync(this, scope) != DialogResult.OK) { status.Text = "Сбор логов отменён."; return; }
                    selection = scope.Selection;
                }
                using (var save = new SaveFileDialog
                {
                    Title = "Сохрани логи программы " + name, Filter = "ZIP-архив (*.zip)|*.zip",
                    DefaultExt = "zip", AddExtension = true, OverwritePrompt = true,
                    CheckPathExists = true, FileName = ServiceLogArchive.BuildSuggestedFileName(name, selection, DateTime.Now)
                })
                {
                    if (ScopedDialogs.Show(this, save) != DialogResult.OK) { status.Text = "Сбор логов отменён."; return; }
                    string destination = save.FileName;
                    status.Text = "Создаём ZIP…";
                    var result = await Task.Run(() => ServiceLogArchive.Create(input, name, destination, selection, token));
                    status.Text = "Архив сохранён. Файлов: " + result.FileCount + ".";
                }
            }
            catch (OperationCanceledException) { status.Text = "Сбор логов отменён."; }
            catch (Exception ex) { status.Text = "Не удалось собрать логи. " + OperationMessage.FinishSentence(ex.Message); }
            finally
            {
                archive.Dispose();
                archive = null;
                cancel.Enabled = false;
                collect.Enabled = source != null && Directory.Exists(Path.Combine(sourceIsFolder ? source : Path.GetDirectoryName(source), "Logs"));
                running.Enabled = true;
                if (closeAfterArchive) BeginInvoke((MethodInvoker)Close);
            }
        }
    }
}
