using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace ServiceTool
{
 internal sealed class DotNetUpdatesForm:ManagerModuleForm
 {
  readonly ServiceGrid grid=new ServiceGrid();readonly Label status;readonly BrandButton check,download,back;
  readonly HashSet<string> saved=new HashSet<string>();
  readonly List<DotNetUpdate> updates=new List<DotNetUpdate>();readonly Func<string> source;
  CancellationTokenSource cancellation;bool busy,closing;
  internal bool FilesChanged {get;private set;}
  internal DotNetUpdatesForm(Func<string> chooseSource):base("Обновление установщиков Microsoft .NET",new Size(1060,700))
  {
   source=chooseSource;MinimumSize=new Size(900,600);
   var body=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   body.RowStyles.Add(new RowStyle(SizeType.Absolute,48));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,60));body.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
   body.Controls.Add(ModuleHeading("Установщики Microsoft .NET"),0,0);
   grid.Dock=DockStyle.Fill;grid.BorderStyle=BorderStyle.None;grid.CellBorderStyle=DataGridViewCellBorderStyle.Single;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.Single;grid.ColumnHeadersDefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;grid.ColumnHeadersDefaultCellStyle.Font=new Font("Segoe UI",10.5F,FontStyle.Bold);grid.ReadOnly=true;grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=grid.AllowUserToResizeRows=false;grid.RowHeadersVisible=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.EnableHeadersVisualStyles=false;grid.TouchScrollingEnabled=true;grid.ColumnHeadersHeight=44;
   grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="choose",HeaderText="",Width=40,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});
   foreach(string text in new[]{"Компонент","В библиотеке","В сети","Действие"})grid.Columns.Add(text,text);
   grid.Columns[1].FillWeight=220;grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.DefaultCellStyle.Padding=new Padding(6);grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
   grid.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Space&&!busy&&grid.CurrentRow!=null&&updates[grid.CurrentRow.Index].Newer&&!saved.Contains(updates[grid.CurrentRow.Index].Local.Id)){var cell=grid.CurrentRow.Cells[0];cell.Value=!Convert.ToBoolean(cell.Value);e.Handled=e.SuppressKeyPress=true;}};
   grid.CellPainting+=delegate(object sender,DataGridViewCellPaintingEventArgs e){if(e.RowIndex<0||e.ColumnIndex!=0)return;e.Paint(e.CellBounds,DataGridViewPaintParts.Background|DataGridViewPaintParts.SelectionBackground);int size=(int)Math.Round(18*e.Graphics.DpiX/96F);var rect=new Rectangle(e.CellBounds.Left+(e.CellBounds.Width-size)/2,e.CellBounds.Top+(e.CellBounds.Height-size)/2,size,size);bool chosen=Convert.ToBoolean(e.Value);var saved=e.Graphics.Save();try{e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var fill=new SolidBrush(chosen?Colors.Accent:Colors.Surface))e.Graphics.FillEllipse(fill,rect);using(var pen=new Pen(chosen?Colors.Accent:Colors.Border))e.Graphics.DrawEllipse(pen,rect);if(chosen)using(var dot=new SolidBrush(Colors.AccentText))e.Graphics.FillEllipse(dot,Rectangle.Inflate(rect,-size/3,-size/3));}finally{e.Graphics.Restore(saved);}e.Paint(e.CellBounds,DataGridViewPaintParts.Border);e.Handled=true;};
   grid.CellClick+=delegate(object s,DataGridViewCellEventArgs e){if(!busy&&e.RowIndex>=0&&e.ColumnIndex==0&&updates[e.RowIndex].Newer&&!saved.Contains(updates[e.RowIndex].Local.Id))grid.Rows[e.RowIndex].Cells[0].Value=!Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[0].Value);};var slot=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};var scroll=new LibraryGridScrollBar {Dock=DockStyle.Right,Width=14,Palette=Colors};grid.ScrollBars=ScrollBars.None;scroll.Bind(grid);slot.Controls.Add(grid);slot.Controls.Add(scroll);body.Controls.Add(slot,0,1);
   status=Label("Проверка получает только версии. Установка в Windows здесь не выполняется.",true);body.Controls.Add(status,0,2);
   var actions=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));foreach(int value in new[]{40,40,20})actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,value));
   check=Button("Проверить версии в сети",false);download=Button("Обновить выбранные установщики",true);back=Button("Закрыть",false);download.Enabled=false;
   check.Click+=async delegate{await Run(async()=>{var found=await Task.Run(()=>DotNetUpdates.Check(LibraryCatalog.All(),cancellation.Token));saved.Clear();updates.Clear();updates.AddRange(found);grid.Rows.Clear();foreach(var u in updates)grid.Rows.Add(false,(u.Local.RuntimeFamily=="Microsoft.NETCore.App"?".NET Runtime ":"ASP.NET Core ")+new Version(u.Local.Version).Major+"."+new Version(u.Local.Version).Minor+" ("+u.Local.Architecture+")",u.Local.Version,u.Version,u.Newer?"Доступно обновление":"Обновление не требуется");status.Text="Проверка завершена. Отметь установщики для обновления.";});};
   download.Click+=async delegate{
    var chosen=grid.Rows.Cast<DataGridViewRow>().Where(r=>Convert.ToBoolean(r.Cells[0].Value)).Select(r=>updates[r.Index]).Where(u=>u.Newer&&!saved.Contains(u.Local.Id)).ToList();if(chosen.Count==0){status.Text="Отметь нужные установщики в первом столбце";return;}
    string root=source();if(String.IsNullOrEmpty(root))return;
    await Run(async()=>{foreach(var u in chosen){cancellation.Token.ThrowIfCancellationRequested();status.Text="Загружаем "+u.Local.Name+" → "+u.Version;bool removed=await Task.Run(()=>DotNetUpdates.Download(u,root,cancellation.Token,null));FilesChanged=true;saved.Add(u.Local.Id);grid.Rows[updates.IndexOf(u)].Cells[2].Value=u.Version;grid.Rows[updates.IndexOf(u)].Cells[0].Value=false;grid.Rows[updates.IndexOf(u)].Cells[4].Value=removed?"Сохранён в библиотеку":"Сохранён; старый файл оставлен (изменён или занят)";}status.Text="Файлы обновлены. Компоненты Windows не устанавливались.";});
   };
   back.Click+=delegate{Close();};actions.Controls.Add(check,0,0);actions.Controls.Add(download,1,0);actions.Controls.Add(back,2,0);body.Controls.Add(actions,0,3);Root.Controls.Add(body,0,1);SetTheme(Colors.Dark,false);
   FormClosing+=delegate(object s,FormClosingEventArgs e){if(busy){closing=true;cancellation.Cancel();e.Cancel=true;status.Text="Отменяем загрузку; старые установщики сохраняются…";}};
  }
  async Task Run(Func<Task> action){if(busy)return;busy=true;cancellation=new CancellationTokenSource();check.Enabled=download.Enabled=false;try{await action();}catch(Exception ex){status.Text=cancellation.IsCancellationRequested?"Операция отменена":"Не выполнено: "+ex.Message;}finally{busy=false;cancellation.Dispose();cancellation=null;if(!IsDisposed){check.Enabled=!FilesChanged;download.Enabled=updates.Any(u=>u.Newer&&!saved.Contains(u.Local.Id));if(closing)Close();}}}
  protected override void OnThemeChanged(){if(grid==null)return;foreach(var scroll in grid.Parent==null?new Control[0]:grid.Parent.Controls.Cast<Control>().ToArray()){var bar=scroll as LibraryGridScrollBar;if(bar!=null){bar.Palette=Colors;bar.BackColor=Colors.Surface;}}grid.BackgroundColor=Colors.Surface;grid.GridColor=Colors.Border;grid.DefaultCellStyle.BackColor=Colors.Surface;grid.DefaultCellStyle.ForeColor=Colors.Text;grid.DefaultCellStyle.SelectionBackColor=Colors.Selection;grid.DefaultCellStyle.SelectionForeColor=Colors.SelectionText;grid.ColumnHeadersDefaultCellStyle.BackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.ForeColor=Colors.Text;grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=Colors.Text;}
 }
}
