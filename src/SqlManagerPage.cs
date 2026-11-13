using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class SqlManagerPage : TableLayoutPanel
    {
        sealed class Item
        {
            internal string Folder;
            internal SqlInstallStep Step;
            internal bool Available;
        }

        readonly Form owner;
        readonly ServiceGrid grid = new ServiceGrid();
        readonly LibraryGridScrollBar scroll = new LibraryGridScrollBar();
        readonly JournalTextBox journal = new JournalTextBox { Multiline = true, ReadOnly = true };
        readonly BrandButton run, scan, download, prepare, back, close;
        readonly Label installed;
        readonly Card journalCard;
        readonly DownloadProgressView extractionProgress=new DownloadProgressView{AccessibleName="Ход распаковки",Visible=false};
        readonly TableLayoutPanel journalArea;
        internal bool Busy { get; private set; }
        internal bool Installing { get; private set; }
        bool closeWhenFinished, extracting;
        System.Threading.CancellationTokenSource operation;
        internal bool CanClose(bool exitConfirmed)
        {
            if(!Busy)return true;
            if(!Installing){if(operation!=null)operation.Cancel();if(extracting){closeWhenFinished=true;return false;}return true;}
            if(!closeWhenFinished&&(exitConfirmed||BrandMessageBox.Show(owner,"Мастер ещё работает. Закрыть окно после завершения?","SQL Server и SSMS",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes))
                closeWhenFinished=true;
            return false;
        }

        internal SqlManagerPage(Form owner, Func<string> softwareFolder, Action returnToPrograms, ThemePalette palette)
        {
            this.owner = owner;
            Font=owner.Font;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            ColumnCount = 1;
            RowCount = 6;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 44, 0, 52, 46, 104, 54 })
                RowStyles.Add(new RowStyle(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? 100 : height));
            Controls.Add(ModuleHeadingStyle.Create("SQL Server и SSMS", true), 0, 0);

            var slot = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            grid.Dock = DockStyle.Fill;
            grid.Margin = Padding.Empty;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.RowHeadersVisible = false;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = grid.AllowUserToResizeRows = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.ColumnHeadersHeight = 38;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.Font=new Font(Font,FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.DefaultCellStyle.Padding = new Padding(6, 4, 6, 4);
            grid.ScrollBars = ScrollBars.None;
            grid.TouchScrollingEnabled = true;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Набор", FillWeight = 39, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Шаг установки", FillWeight = 41, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Состояние", FillWeight = 20, SortMode = DataGridViewColumnSortMode.NotSortable });
            scroll.Dock = DockStyle.Right;
            scroll.Width = 14;
            scroll.AlignToRows = true;
            scroll.AutoHideWhenUnused = true;
            scroll.Bind(grid);
            slot.Controls.Add(grid);
            slot.Controls.Add(scroll);
            Controls.Add(slot, 0, 1);
            RowStyles[1].SizeType=SizeType.Absolute;
            RowStyles[4].SizeType=SizeType.Percent;
            RowStyles[4].Height=100;
            bool pending=false;
            Action fit=delegate
            {
                if(pending||!IsHandleCreated||IsDisposed)return;
                pending=true;
                BeginInvoke((MethodInvoker)delegate
                {
                    pending=false;
                    if(IsDisposed)return;
                    int fixedHeight=(int)Math.Ceiling(RowStyles[0].Height+RowStyles[2].Height+RowStyles[3].Height+RowStyles[5].Height);
                    int available=Math.Max(grid.ColumnHeadersHeight+grid.RowTemplate.Height,ClientSize.Height-fixedHeight-3*Font.Height-56-(journalArea==null?0:(int)journalArea.RowStyles[0].Height));
                    int wanted=grid.RowCount==0?grid.ColumnHeadersHeight+grid.RowTemplate.Height:GridViewportLayout.WholeRowsHeight(grid,available);
                    if(Math.Abs(RowStyles[1].Height-wanted)>1)RowStyles[1].Height=wanted;
                });
            };
            SizeChanged+=delegate{fit();};
            HandleCreated+=delegate{fit();};
            grid.RowsAdded+=delegate{fit();};grid.RowsRemoved+=delegate{fit();};grid.RowHeightChanged+=delegate{fit();};

            scan = NewButton("Проверить локально");
            download = NewButton("Скачать наборы…");
            prepare = NewButton("Подготовить набор…");
            run = NewButton("Запустить мастер установки", true);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            ModuleNavigation.StandardFooter(actions, scan, download, prepare, run);
            Controls.Add(actions, 0, 2);
            installed = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            Controls.Add(installed, 0, 3);
            journalCard = JournalPanel.Create(journal, "Журнал SQL Server и SSMS", palette);
            journalArea=(TableLayoutPanel)extractionProgress.WithJournal(journalCard);
            journalArea.RowStyles[0].Height=0;
            Controls.Add(journalArea,0,4);
            extractionProgress.VisibleChanged+=delegate{fit();};
            back = NewButton("Программы");
            close = NewButton("Закрыть");
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            var spacer = new Label();
            footer.Controls.Add(spacer);
            ModuleNavigation.StandardFooter(footer, back, close);
            Controls.Add(footer, 0, 5);

            grid.SelectionChanged += delegate { UpdateActions(); };
            scan.Click += async delegate { await RefreshLocal(softwareFolder()); };
            download.Click += async delegate
            {
                string folder = softwareFolder();
                string root = String.IsNullOrEmpty(folder) ? BundleFolders.DefaultRoot : Path.GetDirectoryName(folder);
                using (var dialog = new BundleDownloadForm(root, sqlOnly: true))
                {
                    await ModuleNavigation.ShowOwnedAsync(owner, dialog);
                    if (!IsDisposed && dialog.RootAccepted) selectedSoftwareFolder = Path.Combine(dialog.LibraryRoot, "_soft");
                }
                if (!IsDisposed) await RefreshLocal(softwareFolder());
            };
            prepare.Click += async delegate { Prepare(softwareFolder()); await RefreshLocal(softwareFolder()); };
            run.Click += async delegate { if(extracting){operation.Cancel();return;}await RunSelected(); };
            back.Click += delegate { returnToPrograms(); };
            close.Click += delegate { owner.Close(); };
            ApplyTheme(palette);
            UpdateActions();
        }

        string selectedSoftwareFolder;
        static BrandButton NewButton(string text, bool primary = false) { return new BrandButton { Text = text, Primary = primary }; }
        void Log(string text) { if(IsDisposed)return;journal.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); }
        Item SelectedItem { get { return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as Item; } }
        void UpdateActions()
        {
            if (scan == null) return;
            scan.Enabled = download.Enabled = prepare.Enabled = back.Enabled = !Busy;
            var item = SelectedItem;
            run.Text=item!=null&&item.Step!=null&&SqlInstallPackages.IsArchive(item.Step.file)?"Распаковать комплект":"Запустить мастер установки";
            if(extracting){run.Text="Отменить распаковку";run.Enabled=true;return;}
            run.Enabled = !Busy && item != null && item.Step != null && item.Available;
        }

        internal void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.Canvas; ForeColor = palette.Text;
            foreach (var button in new[] { run, scan, download, prepare, back, close }) button.Palette = palette;
            installed.ForeColor = palette.Text;
            grid.BackgroundColor = palette.Canvas; grid.GridColor = palette.Border;
            grid.DefaultCellStyle.BackColor = palette.Surface; grid.DefaultCellStyle.ForeColor = palette.Text;
            grid.DefaultCellStyle.SelectionBackColor = palette.Selection; grid.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
            scroll.Palette = palette;extractionProgress.Palette=palette;extractionProgress.Invalidate();
            journalCard.BackColor=palette.Surface; journalCard.BorderColor=palette.Border;
            foreach(Control child in journalCard.Controls)
            {
                child.BackColor=palette.Surface;
                child.ForeColor=palette.Muted;
                foreach(Control nested in child.Controls)
                {
                    nested.BackColor=palette.Surface;
                    var thumb=nested as JournalScrollBar;
                    if(thumb!=null)thumb.Palette=palette;
                }
            }
            journal.ForeColor=palette.Text;
            Invalidate(true);
        }

        string SqlRoot(string software)
        {
            return Path.Combine(selectedSoftwareFolder ?? software ?? Path.Combine(BundleFolders.DefaultRoot, "_soft"), "_SQL");
        }

        internal async Task RefreshLocal(string software)
        {
            if (Busy) return;
            Busy = true;
            UpdateActions();
            string root = SqlRoot(software);
            operation=new System.Threading.CancellationTokenSource();
            var token=operation.Token;
            try
            {
                var rows = await Task.Run(() =>
                {
                    var found = new List<Tuple<Item, string>>();
                    var catalog=SqlInstallPackages.ExpectedFiles();
                    if (Directory.Exists(root)) foreach (string folder in Directory.GetDirectories(root).OrderBy(path => path))
                    {
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            var steps = SqlInstallPackages.Read(folder,catalog);
                            if (steps.Length == 0) found.Add(Tuple.Create(new Item { Folder = folder }, "Нет установщика или комплекта"));
                            foreach (var step in steps)
                                {
                                bool available=File.Exists(SqlInstallPackages.Resolve(folder,step.file));
                                found.Add(Tuple.Create(new Item {Folder=folder,Step=step,Available=available},available?"Файл на месте":"Нет файла"));
                            }
                        }
                        catch(OperationCanceledException){throw;}
                        catch (Exception ex) { found.Add(Tuple.Create(new Item { Folder = folder }, ex.Message)); }
                    }
                    return found;
                });
                if (IsDisposed||token.IsCancellationRequested) return;
                grid.Rows.Clear();
                foreach (var row in rows)
                {
                    var item = row.Item1;
                    int index = grid.Rows.Add(Path.GetFileName(item.Folder), item.Step == null ? "" : item.Step.label, row.Item2);
                    grid.Rows[index].Tag = item;
                }
                installed.Text = await ReadInstalled();
                if (!IsDisposed) Log("Папка SQL: " + root + ". Найдено строк: " + rows.Count + ".");
            }
            catch(OperationCanceledException){}
            catch (Exception ex) { if (!IsDisposed) Log("Не удалось проверить наборы. " + ex.Message); }
            finally { operation.Dispose();operation=null;Busy=false;if(!IsDisposed)UpdateActions(); }
        }

        async Task<string> ReadInstalled()
        {
            var current = await Task.Run(() => SqlInstallPackages.Installed());
            string text = current.Length == 0 ? "SQL Server и SSMS в системе не обнаружены." : "В системе: " + String.Join("; ", current);
            if (!IsDisposed) Log(text);
            return text;
        }

        void Prepare(string software)
        {
            try
            {
                string folder = SelectedItem == null ? SqlRoot(software) : SelectedItem.Folder;
                if (SelectedItem == null)
                {
                    using (var picker = new FolderBrowserDialog { Description = "Выбери папку с установщиками SQL", SelectedPath = BundleFolders.PickerStart(folder), ShowNewFolderButton = false })
                    {
                        if (ScopedDialogs.Show(owner, picker) != DialogResult.OK) return;
                        folder = picker.SelectedPath;
                    }
                }
                var steps = new List<SqlInstallStep>();
                while (true)
                {
                    using (var picker = new OpenFileDialog { Title = "Выбери установщик для шага " + (steps.Count + 1), InitialDirectory = folder, Filter = "Установщики и комплекты (*.exe;*.iso;*.zip)|*.exe;*.iso;*.zip" })
                    {
                        if (ScopedDialogs.Show(owner, picker) != DialogResult.OK) return;
                        string prefix = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        if (!picker.FileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Установщик должен находиться внутри папки набора.");
                        string relative = picker.FileName.Substring(prefix.Length);
                        SqlInstallPackages.Resolve(folder, relative);
                        steps.Add(new SqlInstallStep { file = relative, label = Path.GetFileNameWithoutExtension(relative) });
                    }
                    if (BrandMessageBox.Show(owner, "Добавить ещё один шаг? Выбери «Нет», чтобы сохранить манифест.", "Подготовить набор", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) break;
                }
                SqlInstallPackages.Save(folder, steps.ToArray());
                Log("Манифест сохранён: " + Path.Combine(folder, SqlInstallPackages.ManifestName) + ".");
            }
            catch (Exception ex) { Log("Не удалось подготовить набор. " + ex.Message); }
        }

        void FinishExtraction(string text,bool success=false,bool failed=false,bool cancelled=false)
        {
            var last=extractionProgress.Snapshot;
            extractionProgress.Report(new TransferProgress{Key=last.Key,Stage=text,Bytes=last.Bytes,Total=last.Total,
                Complete=success,Failed=failed,Cancelled=cancelled,Active=false});
        }

        async Task RunSelected()
        {
            var item = SelectedItem;
            if (Busy || item == null || item.Step == null) return;

            bool archive=SqlInstallPackages.IsArchive(item.Step.file);
            using(var graphics=CreateGraphics())journalArea.RowStyles[0].Height=archive?(int)Math.Round(112*graphics.DpiY/96F):0;
            extractionProgress.Visible=archive;
            string progressKey=Guid.NewGuid().ToString("N");
            if(archive)extractionProgress.Report(new TransferProgress{Key=progressKey,Stage="Подготавливаем распаковку…"});
            Busy = true;
            operation=new System.Threading.CancellationTokenSource();
            var token=operation.Token;
            UpdateActions();
            try
            {
                using (await WindowsOperationQueue.EnterAsync(() => Log(WindowsOperationQueue.WaitingMessage),()=>token.IsCancellationRequested))
                {
                    token.ThrowIfCancellationRequested();
                    string path=SqlInstallPackages.Resolve(item.Folder,item.Step.file);
                    if(archive)
                    {
                        extracting=true;UpdateActions();
                        Log("Распаковываем комплект: "+item.Step.label+"…");
                        string target=await Task.Run(()=>PackageZipExtraction.Extract(path,token,progress=>{progress.Key=progressKey;extractionProgress.Report(progress);if(progress.CleanupWarning)BeginInvoke((MethodInvoker)delegate{Log(progress.Stage);});},replace:true));
                        FinishExtraction("Комплект распакован.",success:true);
                        Log("Распаковано в "+target+".");
                    }
                    else
                    {
                    Installing=true;
                    Log("Запуск мастера: " + item.Step.label + ".");
                    int code = await Task.Run(() => SqlInstallPackages.Run(path));
                    Log(code == 0 ? "Процесс мастера завершился. Проверяем установленные продукты." :
                        code == 3010 || code == 1641 ? "Мастер сообщил о необходимости перезагрузки Windows." :
                        "Мастер завершился с кодом " + code + ".");
                    installed.Text = await ReadInstalled();
                    }
                }
            }
            catch(OperationCanceledException){if(!IsDisposed){if(archive)FinishExtraction("Распаковка отменена.",cancelled:true);Log("Действие отменено.");}}
            catch (Exception ex) { if(!IsDisposed){if(archive)FinishExtraction("Не удалось распаковать комплект.",failed:true);Log("Не удалось завершить шаг. " + ex.Message);} }
            finally { operation.Dispose();operation=null;Installing=false;extracting=false;Busy=false;if(!IsDisposed)UpdateActions(); }
            if(closeWhenFinished&&!owner.IsDisposed){owner.Close();return;}
            if(archive&&!IsDisposed)await RefreshLocal(Path.GetDirectoryName(Path.GetDirectoryName(item.Folder)));
        }
    }
}
