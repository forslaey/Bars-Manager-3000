using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class BrandPathDialog : ManagerModuleForm, IMessageFilter
    {
        private readonly BrandTextBox path=new BrandTextBox{Dock=DockStyle.Fill};
        private readonly BrandTextBox name=new BrandTextBox{Dock=DockStyle.Fill};
        private readonly ServiceGrid grid=new ServiceGrid();
        private readonly Label status;
        private readonly bool folders, save, create;
        private readonly string[] patterns;
        private readonly string extension;
        private string directory;
        private readonly List<string> history=new List<string>();
        private int historyIndex=-1;
        private string lastTapPath;
        private long lastTapTime;
        internal string SelectedPath {get;private set;}
        internal BrandPathDialog(string title,string start,string file,string filter,string defaultExtension,bool folders,bool save,bool create)
            :base(title,new Size(670,500))
        {
            this.folders=folders;this.save=save;this.create=create;
            extension=defaultExtension;
            string[] filters=(filter??"").Split('|');
            patterns=filters.Length>1?filters.Where((value,index)=>index%2==1).SelectMany(value=>value.Split(';')).ToArray():new[]{"*"};
            directory=BundleFolders.PickerStart(start);
            ClientSize=new Size(670,500);MinimumSize=new Size(450,360);
            Root.RowStyles[0].Height=58;HideThemeSelector();
            var body=new BufferedLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Margin=Padding.Empty};
            foreach(int height in new[]{44,0,folders?0:38,28,50})
                body.RowStyles.Add(height==0?new RowStyle(folders&&body.RowStyles.Count==2?SizeType.Absolute:SizeType.Percent,folders&&body.RowStyles.Count==2?0:100):new RowStyle(SizeType.Absolute,height));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            var nav=new BufferedLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(0,0,0,8)};
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,96));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,96));
            nav.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            path.AccessibleName="Путь к папке";path.Margin=new Padding(0,0,10,0);
            path.Dock=DockStyle.None;path.Anchor=AnchorStyles.Left|AnchorStyles.Right;
            var pathFrame=new PathFieldFrame(path);nav.Controls.Add(pathFrame,0,0);
            nav.Layout+=delegate{int height=PathFieldMetrics.Measure(path).Height;if(pathFrame.Height!=height)pathFrame.Height=height;};
            var up=Button("Выше",false);up.Click+=delegate{if(String.IsNullOrEmpty(directory))return;var parent=Directory.GetParent(directory);if(parent==null)ShowDrives();else Navigate(parent.FullName);};
            var go=Button("Перейти",false);go.Click+=delegate{NavigateFromControls();};
            nav.Controls.Add(up,1,0);nav.Controls.Add(go,2,0);
            path.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Navigate(path.Text.Trim());}};
            body.Controls.Add(nav,0,0);
            grid.BorderStyle=BorderStyle.None;grid.ScrollBars=ScrollBars.None;grid.RowHeadersVisible=false;
            grid.CellBorderStyle=DataGridViewCellBorderStyle.Single;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.Single;grid.ReadOnly=true;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
            grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=grid.AllowUserToResizeRows=false;
            grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.RowTemplate.Height=30;grid.TouchScrollingEnabled=true;
            grid.Columns.Add("name","Имя");
            grid.Columns[0].SortMode=DataGridViewColumnSortMode.NotSortable;
            var scroll=new LibraryGridScrollBar{Dock=DockStyle.Right,Width=14,AlignToRows=true,UseCardBackground=true,LimitTrackToWholeRows=true};
            var card=new Card{Dock=DockStyle.Fill,Padding=new Padding(10),Margin=new Padding(0,0,0,8)};
            grid.Dock=DockStyle.Fill;card.Controls.Add(grid);card.Controls.Add(scroll);scroll.Bind(grid);
            body.Controls.Add(card,0,1);
            grid.SelectionChanged+=delegate
            {
                if(folders)return;
                string item=SelectedRowPath;
                if(File.Exists(item))name.Text=Path.GetFileName(item);
                else if(!save&&Directory.Exists(item))name.Clear();
            };
            grid.TouchCellTapped+=OnTouchCellTapped;
            grid.TouchTapInterrupted+=delegate{lastTapPath=null;};
            grid.CellDoubleClick+=delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex>=0)OpenRow(grid.Rows[e.RowIndex].Tag as string);};
            grid.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;OpenRow(SelectedRowPath);}};
            if(!folders)
            {
                name.Text=file??"";name.AccessibleName="Имя файла";name.Margin=new Padding(0,0,0,4);
                body.Controls.Add(new PathFieldFrame(name),0,2);
            }
            status=Label("",true);body.Controls.Add(status,0,3);
            var footer=new BufferedLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};
            var cancel=Button("Отмена",false);cancel.DialogResult=DialogResult.Cancel;CancelButton=cancel;
            var accept=Button(save?"Сохранить":folders?"Выбрать папку":"Открыть",true);accept.Click+=delegate{AcceptPath();};AcceptButton=accept;
            var make=Button("Новая папка",false);make.Click+=delegate{CreateFolder();};
            ModuleNavigation.StandardFooter(footer,create?new[]{make,cancel,accept}:new[]{cancel,accept});
            if(!create)make.Dispose();
            body.Controls.Add(footer,0,4);Root.Controls.Add(body,0,1);
            Shown+=delegate{Application.AddMessageFilter(this);Navigate(directory);if(!folders)name.Text=file??"";};
            SetTheme(Colors.Dark,false);
            {scroll.Palette=Colors;scroll.BackColor=Colors.Surface;}
        }
        protected override void OnThemeChanged()
        {
            base.OnThemeChanged();
            if(grid==null)return;
            grid.EnableHeadersVisualStyles=false;
            grid.BackgroundColor=grid.DefaultCellStyle.BackColor=Colors.Surface;
            grid.DefaultCellStyle.ForeColor=Colors.Text;grid.DefaultCellStyle.SelectionBackColor=Colors.Selection;
            grid.DefaultCellStyle.SelectionForeColor=Colors.SelectionText;grid.GridColor=Colors.Border;
            grid.ColumnHeadersDefaultCellStyle.BackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.ForeColor=Colors.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=Colors.Text;
            if(grid.Parent!=null)foreach(var scroll in grid.Parent.Controls.OfType<LibraryGridScrollBar>()){scroll.Palette=Colors;scroll.BackColor=Colors.Surface;}
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing)Application.RemoveMessageFilter(this);
            base.Dispose(disposing);
        }

        bool IMessageFilter.PreFilterMessage(ref Message message)
        {
            const int xButtonUp=0x020C;
            if(message.Msg!=xButtonUp||!Visible||!Enabled)return false;
            var control=Control.FromChildHandle(message.HWnd);
            if(control==null||control.FindForm()!=this)return false;
            int button=(int)((message.WParam.ToInt64()>>16)&0xFFFF);
            if(button!=1&&button!=2)return false;
            MoveHistory(button==1?-1:1);
            return true;
        }

        private void MoveHistory(int offset)
        {
            int target=historyIndex+offset;
            if(target<0||target>=history.Count)return;
            string address=history[target];
            if(address==null)ShowDrives(false);
            else if(!Navigate(address,false))return;
            historyIndex=target;
        }

        private void Remember(string address)
        {
            if(historyIndex>=0&&String.Equals(history[historyIndex],address,StringComparison.OrdinalIgnoreCase))return;
            history.RemoveRange(historyIndex+1,history.Count-historyIndex-1);
            history.Add(address);
            historyIndex=history.Count-1;
        }

        private void OnTouchCellTapped(object sender,DataGridViewCellEventArgs e)
        {
            string item=grid.Rows[e.RowIndex].Tag as string;
            long now=System.Diagnostics.Stopwatch.GetTimestamp();
            double elapsed=(now-lastTapTime)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            if(item!=null&&String.Equals(item,lastTapPath,StringComparison.OrdinalIgnoreCase)&&elapsed<=SystemInformation.DoubleClickTime)
            {
                lastTapPath=null;
                OpenRow(item);
                return;
            }
            lastTapPath=item;
            lastTapTime=now;
        }

        private void ShowDrives(bool remember=true)
        {
            grid.Rows.Clear();
            foreach(string root in Directory.GetLogicalDrives())grid.Rows[grid.Rows.Add(root)].Tag=root;
            grid.ClearSelection();
            directory=null;path.Clear();lastTapPath=null;
            if(remember)Remember(null);
            status.Text="Выбери диск.";
        }
        private bool Navigate(string value,bool remember=true)
        {
            try
            {
                if(!Path.IsPathRooted(value))throw new IOException("Укажи полный путь к папке.");
                string next=Path.GetFullPath(value);
                var dirs=Directory.GetDirectories(next).OrderBy(item=>item,StringComparer.CurrentCultureIgnoreCase).ToArray();
                var files=folders?new string[0]:patterns.SelectMany(pattern=>Directory.GetFiles(next,pattern))
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item=>item,StringComparer.CurrentCultureIgnoreCase).ToArray();
                grid.Rows.Clear();
                foreach(string item in dirs)grid.Rows[grid.Rows.Add("▸  "+Path.GetFileName(item))].Tag=item;
                foreach(string item in files)grid.Rows[grid.Rows.Add(Path.GetFileName(item))].Tag=item;
                grid.ClearSelection();
                directory=next;path.Text=next;status.Text="";
                if(!folders&&!save)name.Clear();
                lastTapPath=null;
                if(remember)Remember(next);
                return true;
            }
            catch(Exception ex){status.Text=OperationMessage.FinishSentence(ex.Message);return false;}
        }
        private string SelectedRowPath
        {
            get { return grid.SelectedRows.Count==1?grid.SelectedRows[0].Tag as string:null; }
        }

        private void NavigateFromControls()
        {
            string address=path.Text.Trim();
            string selected=SelectedRowPath;
            if(String.Equals(address,directory??"",StringComparison.OrdinalIgnoreCase)&&Directory.Exists(selected))
                address=selected;
            Navigate(address);
        }

        private void OpenRow(string item)
        {
            if(Directory.Exists(item))Navigate(item);
            else if(!folders&&File.Exists(item))
            {
                name.Text=Path.GetFileName(item);
                AcceptPath();
            }
        }
        private void AcceptPath()
        {
            try
            {
                if(!folders&&!save&&String.IsNullOrWhiteSpace(name.Text)&&Directory.Exists(SelectedRowPath))
                {
                    NavigateFromControls();
                    return;
                }
                string value=folders?(String.Equals(path.Text,directory,StringComparison.OrdinalIgnoreCase)&&grid.SelectedRows.Count==1?grid.SelectedRows[0].Tag as string:path.Text):name.Text.Trim();
                if(String.IsNullOrWhiteSpace(value))throw new IOException("Укажи имя файла или папку.");
                string result=Path.GetFullPath(Path.IsPathRooted(value)?value:Path.Combine(directory??"",value));
                if(folders&&!Directory.Exists(result)||!folders&&!save&&!File.Exists(result))throw new IOException("Выбранный путь не найден.");
                if(save)
                {
                    if(!Path.HasExtension(result)&&!String.IsNullOrEmpty(extension))result+="."+extension.TrimStart('.');
                    if(Directory.Exists(result))throw new IOException("Выбранный путь занят папкой. Укажи имя файла."); if(!Directory.Exists(Path.GetDirectoryName(result)))throw new IOException("Папка назначения не найдена.");
                    if(File.Exists(result)&&BrandMessageBox.Show(this,"Файл уже существует. Заменить его?",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
                }
                SelectedPath=result;DialogResult=DialogResult.OK;Close();
            }
            catch(Exception ex){status.Text=OperationMessage.FinishSentence(ex.Message);}
        }
        private void CreateFolder()
        {
            try
            {
                string candidate=Path.GetFullPath(path.Text);
                if(Directory.Exists(candidate))throw new IOException("Введи в поле пути полное имя новой папки.");
                Directory.CreateDirectory(candidate);Navigate(candidate);
            }
            catch(Exception ex){status.Text=OperationMessage.FinishSentence(ex.Message);}
        }
    }
}
