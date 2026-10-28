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
  readonly ServiceGrid grid=new ServiceGrid();readonly Label status;readonly BrandButton check,download,back,all;
  readonly HashSet<string> missing=new HashSet<string>();
  readonly HashSet<string> saved=new HashSet<string>();
  readonly List<DotNetUpdate> updates=new List<DotNetUpdate>();readonly Func<string> source;
  CancellationTokenSource cancellation;bool busy,closing;
  internal bool FilesChanged {get;private set;}
  internal DotNetUpdatesForm(Func<string> chooseSource,string libraryRoot=null):base("Обновление установщиков Microsoft .NET",new Size(1060,700))
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
   grid.CellClick+=delegate(object s,DataGridViewCellEventArgs e){if(!busy&&e.RowIndex>=0&&e.ColumnIndex>=0&&updates[e.RowIndex].Newer&&!saved.Contains(updates[e.RowIndex].Local.Id))grid.Rows[e.RowIndex].Cells[0].Value=!Convert.ToBoolean(grid.Rows[e.RowIndex].Cells[0].Value);};var slot=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};var scroll=new LibraryGridScrollBar {Dock=DockStyle.Right,Width=14,Palette=Colors};grid.ScrollBars=ScrollBars.None;scroll.Bind(grid);slot.Controls.Add(grid);slot.Controls.Add(scroll);body.Controls.Add(slot,0,1);
   status=Label("Проверка получает только версии. Установка в Windows здесь не выполняется.",true);body.Controls.Add(status,0,2);
   var actions=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));foreach(int value in new[]{25,25,25,25})actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,value));
   check=Button("Проверить версии в сети",false);download=Button("Обновить выбранные установщики",true);back=Button("Назад",false);all=Button("Выбрать всё",false);all.Click+=delegate{if(busy)return;var rows=grid.Rows.Cast<DataGridViewRow>().Where(row=>updates[row.Index].Newer&&!saved.Contains(updates[row.Index].Local.Id)).ToList();bool clear=rows.Count>0&&rows.All(row=>Convert.ToBoolean(row.Cells[0].Value));foreach(var row in rows)row.Cells[0].Value=!clear;UpdateButtons();};download.Enabled=false;
   check.Click+=async delegate{await Run(async()=>{var found=await Task.Run(()=>DotNetUpdates.Check(updates.Select(u=>u.Local).ToList(),cancellation.Token));saved.Clear();updates.Clear();updates.AddRange(found.OrderBy(u=>new Version(u.Local.Version.Split('-')[0])).ThenBy(u=>u.Local.RuntimeFamily).ThenBy(u=>u.Local.Architecture));grid.Rows.Clear();foreach(var u in updates)grid.Rows.Add(false,(u.Local.RuntimeFamily=="Microsoft.NETCore.App"?".NET Runtime ":"ASP.NET Core ")+new Version(u.Local.Version.Split('-')[0]).Major+"."+new Version(u.Local.Version.Split('-')[0]).Minor+" ("+u.Local.Architecture+")",missing.Contains(u.Local.Id)?"Нет в папке":u.Local.Version,u.Version,u.Newer?"Доступно обновление":"Обновление не требуется");foreach(DataGridViewRow row in grid.Rows)if(missing.Contains(updates[row.Index].Local.Id))row.DefaultCellStyle.ForeColor=Colors.Dark?Color.OrangeRed:Color.Red;Decorate();status.Text="Проверка завершена. Отметь установщики для обновления.";});};
   download.Click+=async delegate{
    var chosen=grid.Rows.Cast<DataGridViewRow>().Where(r=>Convert.ToBoolean(r.Cells[0].Value)).Select(r=>updates[r.Index]).Where(u=>u.Newer&&!saved.Contains(u.Local.Id)).ToList();if(chosen.Count==0){status.Text="Отметь нужные установщики в первом столбце";return;}
    string root=source();if(String.IsNullOrEmpty(root))return;
    await Run(async()=>{foreach(var u in chosen){cancellation.Token.ThrowIfCancellationRequested();ShowCurrent(u);status.Text="Загружаем "+u.Local.Name+" → "+u.Version;bool removed=await Task.Run(()=>DotNetUpdates.Download(u,root,cancellation.Token,null));FilesChanged=true;saved.Add(u.Local.Id);missing.Remove(u.Local.Id);grid.Rows[updates.IndexOf(u)].Cells[2].Value=u.Version;grid.Rows[updates.IndexOf(u)].Cells[0].Value=false;grid.Rows[updates.IndexOf(u)].Cells[4].Value=removed?"Сохранено в библиотеки":"Сохранён; старый файл оставлен (изменён или занят)";UpdateButtons();}Decorate();status.Text="Файлы обновлены. Компоненты Windows не устанавливались.";});
   };
   back.Click+=delegate{Close();};actions.Controls.Add(check,0,0);actions.Controls.Add(all,1,0);actions.Controls.Add(download,2,0);actions.Controls.Add(back,3,0);ModuleNavigation.EqualButtons(actions,scroll);body.Controls.Add(actions,0,3);Root.Controls.Add(body,0,1);SetTheme(Colors.Dark,false);
   var initial=LibraryCatalog.All().Where(DotNetUpdates.Supported).ToList();
   foreach(string arch in new[]{"x86","x64"})foreach(string family in new[]{"Microsoft.NETCore.App","Microsoft.AspNetCore.App"})if(!initial.Any(p=>p.Version.StartsWith("7.0.")&&p.Architecture==arch&&p.RuntimeFamily==family))initial.Add(new LibraryPackage {Id="net7-"+arch+family,Kind=LibraryKind.DotNet,Version="7.0.0",Architecture=arch,RuntimeFamily=family,Name=".NET 7 "+arch,RelativePath="Microsoft .NET\\.NET 7\\"+(family=="Microsoft.NETCore.App"?"dotnet":"aspnetcore")+"-missing-"+arch+".exe"});
   foreach(var entry in initial.OrderBy(p=>new Version(p.Version.Split('-')[0])).ThenBy(p=>p.RuntimeFamily).ThenBy(p=>p.Architecture)){var p=entry;bool present=!String.IsNullOrEmpty(libraryRoot)&&System.IO.File.Exists(System.IO.Path.Combine(libraryRoot,p.RelativePath));string shown=p.Version;if(!present){var copy=new LibraryPackage();foreach(var field in typeof(LibraryPackage).GetFields())field.SetValue(copy,field.GetValue(p));p=copy;p.Version=p.Preview?"11.0.0-rc.0":new Version(p.Version.Split('-')[0]).Major+".0.0";missing.Add(p.Id);}updates.Add(new DotNetUpdate {Local=p,Version=p.Version});int index=grid.Rows.Add(false,p.Name,present?shown:"Нет в папке","Не проверено","Нажми проверку версий");if(!present){grid.Rows[index].Cells[2].Value="Нет в папке";grid.Rows[index].DefaultCellStyle.ForeColor=Colors.Dark?Color.OrangeRed:Color.Red;}}
   grid.CellValueChanged+=delegate{UpdateButtons();};UpdateButtons();Decorate();FormClosing+=delegate(object s,FormClosingEventArgs e){if(busy){closing=true;cancellation.Cancel();e.Cancel=true;status.Text="Отменяем загрузку; старые установщики сохраняются…";}};
  }
  void UpdateButtons()
  {
   if(all==null||download==null||check==null)return;
   var rows=grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Index<updates.Count&&updates[r.Index].Newer&&!saved.Contains(updates[r.Index].Local.Id)).ToList();
   int selected=rows.Count(r=>Convert.ToBoolean(r.Cells[0].Value));
   all.Text=rows.Count>0&&selected==rows.Count?"Снять выбор":"Выбрать всё";
   all.Enabled=!busy&&rows.Count>0;download.Enabled=!busy&&selected>0;check.Enabled=!busy&&!FilesChanged;
  }
  void ShowCurrent(DotNetUpdate update)
  {
   int index=updates.IndexOf(update);if(index<0||index>=grid.Rows.Count)return;
   grid.CurrentCell=grid.Rows[index].Cells[1];
   var bounds=grid.GetRowDisplayRectangle(index,false);
   if(bounds.Top<grid.ColumnHeadersHeight||bounds.Bottom>grid.ClientSize.Height)grid.FirstDisplayedScrollingRowIndex=index;
   grid.Rows[index].Cells[4].Value="Скачивание…";
  }
  void Decorate(){foreach(DataGridViewRow row in grid.Rows){var u=updates[row.Index];Color color=missing.Contains(u.Local.Id)?(Colors.Dark?Color.OrangeRed:Color.Red):(!u.Newer||saved.Contains(u.Local.Id))?(Colors.Dark?Color.Yellow:Color.FromArgb(160,65,0)):Colors.Text;row.DefaultCellStyle.ForeColor=color;row.DefaultCellStyle.SelectionForeColor=color;foreach(DataGridViewCell cell in row.Cells)cell.ToolTipText=u.SupportInfo??((u.Local.Version=="6.0.36"||u.Local.Version=="7.0.20")?"Поддержка завершена. Последняя версия уже в библиотеке.":"Сведения о поддержке появятся после проверки в сети");}}
  async Task Run(Func<Task> action){if(busy)return;busy=true;cancellation=new CancellationTokenSource();check.Enabled=download.Enabled=all.Enabled=false;try{await action();}catch(Exception ex){status.Text=cancellation.IsCancellationRequested?"Операция отменена":ex is System.Net.WebException?"Невозможно установить удалённое соединение с серверами Microsoft":"Не выполнено: "+ex.Message;}finally{busy=false;cancellation.Dispose();cancellation=null;if(!IsDisposed){UpdateButtons();if(closing)Close();}}}
  protected override void OnThemeChanged(){if(grid==null)return;foreach(var scroll in grid.Parent==null?new Control[0]:grid.Parent.Controls.Cast<Control>().ToArray()){var bar=scroll as LibraryGridScrollBar;if(bar!=null){bar.Palette=Colors;bar.BackColor=Colors.Surface;}}grid.BackgroundColor=Colors.Canvas;grid.GridColor=Colors.Border;grid.DefaultCellStyle.BackColor=Colors.Surface;grid.DefaultCellStyle.ForeColor=Colors.Text;grid.DefaultCellStyle.SelectionBackColor=Colors.Selection;grid.DefaultCellStyle.SelectionForeColor=Colors.SelectionText;grid.ColumnHeadersDefaultCellStyle.BackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.ForeColor=Colors.Text;grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=Colors.Text;Decorate();}
 }
}
