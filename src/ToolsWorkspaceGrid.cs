using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class ToolsWorkspaceGrid : Panel
    {
        private readonly ServiceGrid grid=new ServiceGrid();
        private readonly LibraryGridScrollBar scroll=new LibraryGridScrollBar {Width=14,AlignToRows=true,UseCardBackground=true,LimitTrackToWholeRows=true};
        private readonly Dictionary<string,Row> packages=new Dictionary<string,Row>(StringComparer.OrdinalIgnoreCase);
        internal event EventHandler SelectionChanged;
        private sealed class Row
        {
            internal string Package, Path, SingleExecutable;
            internal bool Ready, Damaged, CanDownload, Selected, Expanded, HasVariants, Shortcut;
        }
        internal ToolsWorkspaceGrid()
        {
            Margin=new Padding(0,8,0,4);
            grid.Dock=DockStyle.None;
            grid.Anchor=AnchorStyles.Top|AnchorStyles.Left;
            grid.BorderStyle=BorderStyle.None;
            grid.RowHeadersVisible=false;
            grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=grid.AllowUserToResizeRows=false;
            grid.ReadOnly=true;
            grid.MultiSelect=false;
            grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
            grid.ScrollBars=ScrollBars.None;
            grid.TouchScrollingEnabled=true;
            grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
            grid.ColumnHeadersHeight=38;
            grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.RowTemplate.Height=34;
            grid.ColumnHeadersDefaultCellStyle.Font=new Font("Segoe UI",10,FontStyle.Bold);
            grid.DefaultCellStyle.Padding=new Padding(6,0,6,0);
            grid.CellBorderStyle=DataGridViewCellBorderStyle.Single;
            grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.Single;
            grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="download",HeaderText="",ToolTipText="Выбор набора для скачивания или распаковки",Width=34,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});
            grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Утилита",FillWeight=60});
            grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Источник",FillWeight=28});
            grid.Columns.Add(new DataGridViewCheckBoxColumn{HeaderText="Ярлык",FillWeight=12,MinimumWidth=65});
            foreach(DataGridViewColumn column in grid.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;
            grid.CellMouseDown+=delegate(object sender,DataGridViewCellMouseEventArgs e){if(e.Button==MouseButtons.Left)Toggle(e.RowIndex,e.ColumnIndex);};
            grid.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Space&&grid.CurrentCell!=null){Toggle(grid.CurrentCell.RowIndex,grid.CurrentCell.ColumnIndex);e.Handled=e.SuppressKeyPress=true;}};
            grid.CellPainting+=PaintCheck;
            Controls.Add(grid);
            Controls.Add(scroll);
            scroll.Bind(grid);
            scroll.VisibleChanged+=delegate{Fit();};
        }
        private ThemePalette colors=ThemePalette.Create(false);
        internal void SetPalette(ThemePalette palette)
        {
            colors=palette;
            BackColor=grid.BackgroundColor=colors.Surface;
            grid.GridColor=colors.Border;
            grid.DefaultCellStyle.BackColor=colors.Surface;
            grid.DefaultCellStyle.ForeColor=colors.Text;
            grid.DefaultCellStyle.SelectionBackColor=colors.Selection;
            grid.DefaultCellStyle.SelectionForeColor=colors.SelectionText;
            grid.EnableHeadersVisualStyles=false;
            grid.ColumnHeadersDefaultCellStyle.BackColor=colors.SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor=colors.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=colors.SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=colors.Text;
            grid.ColumnHeadersDefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;
            scroll.Palette=colors;scroll.BackColor=colors.Surface;
            Invalidate(true);
        }
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Fit();}
        private bool fitting;
        private void Fit()
        {
            if(fitting||grid==null||scroll==null||IsDisposed)return;
            fitting=true;
            try
            {
                grid.Width=Math.Max(1,ClientSize.Width-(scroll.Visible?scroll.Width:0));
                grid.Height=GridViewportLayout.WholeRowsHeight(grid,Math.Max(1,ClientSize.Height));
                scroll.Bounds=new Rectangle(grid.Right,0,scroll.Width,grid.Height);
                scroll.UpdateViewport();
                grid.Width=Math.Max(1,ClientSize.Width-(scroll.Visible?scroll.Width:0));
                scroll.Left=grid.Right;
            }
            finally{fitting=false;}
        }
        internal void LoadRows(string local,Dictionary<string,Tuple<string,bool,bool>> catalog)
        {
            var old=grid.Rows.Cast<DataGridViewRow>().Select(row=>(Row)row.Tag)
                .Where(row=>row!=null).ToDictionary(row=>row.Package+"|"+row.Path,StringComparer.OrdinalIgnoreCase);
            grid.Rows.Clear();
            packages.Clear();
            var names=(Directory.Exists(local)?ToolsDeploymentForm.LocalPackages(local):new string[0])
                .Concat(catalog.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name=>name,StringComparer.CurrentCultureIgnoreCase);
            foreach(string name in names)
            {
                Tuple<string,bool,bool> item;
                bool online=catalog.TryGetValue(name,out item);
                string path=Path.Combine(local,name);
                bool exists=Directory.Exists(path)||File.Exists(path);
                bool damaged=File.Exists(path)&&ToolArchives.Supports(path)&&!ToolArchives.HasReadableDirectory(path); bool ready=!damaged&&(exists||online&&item.Item2);
                var row=new Row{Package=name,Ready=ready,Damaged=damaged,CanDownload=online&&item.Item3,Selected=online&&item.Item3};
                Row saved;
                if(old.TryGetValue(name+"|",out saved)){row.Selected=(row.CanDownload||row.Ready)&&saved.Selected;row.Expanded=saved.Expanded;}
                var executables=ready?Executables(path,name).ToArray():new string[0];
                if(executables.Length==1)
                {
                    row.SingleExecutable=executables[0];
                    if(old.TryGetValue(name+"|",out saved)&&saved.SingleExecutable==row.SingleExecutable)row.Shortcut=saved.Shortcut;
                }
                packages.Add(name,row);
                row.HasVariants=executables.Length>1;
                Add(row,row.HasVariants?(row.Expanded?"▾  ":"▸  ")+name:name,damaged?(online?"ZIP повреждён — "+item.Item1:"ZIP повреждён"):online?item.Item1:"Локальная папка");
                if(executables.Length<2)continue;
                foreach(string executable in executables)
                {
                    var child=new Row{Package=name,Path=executable,Ready=true};
                    if(old.TryGetValue(name+"|"+executable,out saved)){child.Shortcut=saved.Shortcut;child.Selected=saved.Selected;}else child.Selected=row.Selected;
                    Add(child,"    "+executable,"").Visible=row.Expanded;
                }
            }
            Fit();
        }
        private DataGridViewRow Add(Row row,string name,string state)
        {
            var rendered=grid.Rows[grid.Rows.Add(row.Selected,name,state,row.Shortcut)];
            rendered.Tag=row;
            rendered.Cells[1].ToolTipText=name.Trim();
            return rendered;
        }
        private static IEnumerable<string> Executables(string path,string package)
        {
            string expected=ToolsDeployment.ShortcutExecutable(package);
            if(expected==null)return new string[0];
            try
            {
                if(Directory.Exists(path))return ToolsDeployment.ShortcutCandidates(path,expected).Select(name=>name.Replace(Path.DirectorySeparatorChar,(char)47)).ToArray();
                if(!ToolArchives.IsZip(path))
                    return ToolArchives.Entries(path).Where(entry=>System.IO.Path.GetFileName(entry).Equals(expected,StringComparison.OrdinalIgnoreCase)).ToArray();
                using(var input=File.OpenRead(path))
                using(var archive=new ZipArchive(input,ZipArchiveMode.Read))
                {
                    string prefix=System.IO.Path.GetFileNameWithoutExtension(path)+"/";
                    bool wrapped=archive.Entries.All(entry=>entry.FullName.Replace('\\','/').StartsWith(prefix,StringComparison.OrdinalIgnoreCase));
                    return archive.Entries.Where(entry=>entry.Name.Equals(expected,StringComparison.OrdinalIgnoreCase))
                        .Select(entry=>entry.FullName.Replace('\\','/')).Select(name=>wrapped?name.Substring(prefix.Length):name)
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name=>name,StringComparer.CurrentCultureIgnoreCase).ToArray();
                }
            }
            catch(InvalidDataException){return new string[0];}
            catch(IOException){return new string[0];}
            catch(UnauthorizedAccessException){return new string[0];}
        }
        internal bool WantsDownload(string package){Row row;return package!=null&&packages.TryGetValue(package,out row)&&row.Selected;}
        internal string[] DamagedPackages { get { return packages.Values.Where(row=>row.Damaged).Select(row=>row.Package).ToArray(); } }
        internal bool HasLocal {get{return packages.Values.Any(row=>row.Ready);}}
        internal string[] Deployment {get{return packages.Values.Where(row=>row.Ready&&row.Selected&&(!row.HasVariants||VariantRows(row.Package).Any(child=>child.Selected))).Select(row=>row.Package).ToArray();}}
        private IEnumerable<Row> VariantRows(string package)
        {
            return grid.Rows.Cast<DataGridViewRow>().Select(row=>(Row)row.Tag)
                .Where(row=>row.Path!=null&&String.Equals(row.Package,package,StringComparison.OrdinalIgnoreCase));
        }
        internal Dictionary<string,HashSet<string>> Variants
        {
            get
            {
                return packages.Values.Where(row=>row.Selected&&row.HasVariants)
                    .ToDictionary(row=>row.Package,row=>new HashSet<string>(VariantRows(row.Package)
                        .Where(child=>child.Selected).Select(child=>child.Path),StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
            }
        }
        internal Dictionary<string,HashSet<string>> Shortcuts
        {
            get
            {
                return grid.Rows.Cast<DataGridViewRow>().Select(row=>(Row)row.Tag)
                    .Where(row=>(row.Path!=null||row.SingleExecutable!=null)&&row.Shortcut&&row.Selected&&packages[row.Package].Selected)
                    .GroupBy(row=>row.Package,StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group=>group.Key,group=>new HashSet<string>(group.Select(row=>row.Path??row.SingleExecutable),StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
            }
        }
        private void Toggle(int index,int column)
        {
            if(index<0||column<0||!Enabled)return;
            var row=(Row)grid.Rows[index].Tag;
            if(row==null)return;
            if(row.Path==null&&column==1&&row.HasVariants)
            {
                row.Expanded=!row.Expanded;
                grid.CurrentCell=grid.Rows[index].Cells[1];
                grid.Rows[index].Cells[1].Value=(row.Expanded?"▾  ":"▸  ")+row.Package;
                foreach(DataGridViewRow variant in grid.Rows)
                {
                    var child=(Row)variant.Tag;
                    if(child.Path!=null&&child.Package==row.Package)variant.Visible=row.Expanded;
                }
                Fit();
            }
            else if(row.Path==null&&(column==0||column==1)&&(row.Ready||row.CanDownload))
            {
                row.Selected=!row.Selected;
                grid.Rows[index].Cells[0].Value=row.Selected;
                foreach(var child in VariantRows(row.Package))child.Selected=row.Selected;
            }
            else if((column==0||column==1)&&row.Path!=null)
            {
                row.Selected=!row.Selected;
                packages[row.Package].Selected=VariantRows(row.Package).Any(child=>child.Selected);
                grid.Rows[index].Cells[0].Value=row.Selected;
            }
            else if(column==3&&(row.Path!=null||row.SingleExecutable!=null)&&row.Selected&&packages[row.Package].Selected)
            {
                row.Shortcut=!row.Shortcut;
                grid.Rows[index].Cells[3].Value=row.Shortcut;
            }
            else return;
            grid.Invalidate();
            var changed=SelectionChanged;if(changed!=null)changed(this,EventArgs.Empty);
        }
        private void PaintCheck(object sender,DataGridViewCellPaintingEventArgs e)
        {
            if(e.RowIndex<0||e.ColumnIndex!=0&&e.ColumnIndex!=3)return;
            var row=(Row)grid.Rows[e.RowIndex].Tag;
            if(row==null)return;
            e.Paint(e.CellBounds,DataGridViewPaintParts.Background|DataGridViewPaintParts.SelectionBackground);
            bool shown=e.ColumnIndex==0?(row.CanDownload||row.Ready):row.Path!=null||row.SingleExecutable!=null;
            if(shown)
            {
                bool enabled=e.ColumnIndex!=3||row.Selected&&packages[row.Package].Selected;
                bool chosen=e.ColumnIndex==0?row.Selected:row.Shortcut&&enabled;
                int size=(int)Math.Round(18*e.Graphics.DpiX/96F);
                var rect=new Rectangle(e.CellBounds.X+(e.CellBounds.Width-size)/2,e.CellBounds.Y+(e.CellBounds.Height-size)/2,size,size);
                var graphicsState=e.Graphics.Save();
                try
                {
                    e.Graphics.SetClip(e.CellBounds,System.Drawing.Drawing2D.CombineMode.Intersect);
                    e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using(var fill=new SolidBrush(chosen?colors.Accent:colors.Surface))e.Graphics.FillEllipse(fill,rect);
                    using(var edge=new Pen(chosen?colors.Accent:colors.Border))e.Graphics.DrawEllipse(edge,rect);
                    if(chosen)using(var dot=new SolidBrush(colors.AccentText))e.Graphics.FillEllipse(dot,Rectangle.Inflate(rect,-size/3,-size/3));
                }
                finally{e.Graphics.Restore(graphicsState);}
            }
            e.Paint(e.CellBounds,DataGridViewPaintParts.Border|DataGridViewPaintParts.Focus);
            e.Handled=true;
        }
    }
}
