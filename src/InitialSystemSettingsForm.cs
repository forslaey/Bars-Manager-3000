using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace ServiceTool
{
    internal sealed class InitialSystemSettingsForm : UtilityModuleForm
    {
        private readonly bool preview;
        private bool kioskBackup, kioskAvailable=true;
        private readonly ServiceGrid grid=new ServiceGrid();
        private readonly LibraryGridScrollBar gridScroll=new LibraryGridScrollBar();
        private readonly JournalTextBox journal=new JournalTextBox();
        private readonly BrandButton refresh,applyButton,standardButton;
        private readonly Font headerFont=new Font("Segoe UI",10.5F,FontStyle.Bold);
        private static readonly string[][] Settings={
            new[]{"passwords","Политика паролей системы Windows","Профиль BARS по согласованному списку. Существующие пароли не изменяются.","Применить политики","Вернуть стандартные политики"},
            new[]{"admin","Контроль учётных записей","Для существующих администраторов. После изменения требуется перезагрузка.","Применить политики","Вернуть стандартные политики"},
            new[]{"firewall","Брандмауэр Windows","Все профили: доменный, частный и общедоступный.","Отключить все профили","Включить все профили"},
            new[]{"defender","Защитник Windows","Временно отключить защиту в реальном времени. Windows может включить её обратно.","Временно отключить защиту","Включить защиту"},
            new[]{"smartscreen","SmartScreen для приложений","Предупреждения при запуске загруженных приложений. Для всех пользователей компьютера.","Применить политики","Вернуть стандартные политики"},
            new[]{"folders","Права папок DataKrat и Bars","Полный доступ всем пользователям. Папки и файлы на локальных дисках и флешках.","Выдать полный доступ","Проверить папки"},
            new[]{"kiosk","Сенсорный режим КСО","Запрет краевых жестов и центра уведомлений для всех пользователей. Прокрутка внутри программ сохраняется.","Применить настройки КСО","Вернуть прежние настройки"}
        };
        internal InitialSystemSettingsForm(bool previewMode=false):base("Первичные системные настройки",new Size(1080,840))
        {
            FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=true;preview=previewMode;MinimumSize=new Size(950,680);Root.AutoScroll=false;Root.RowStyles[0].Height=72;Body.MinimumSize=Size.Empty;
            Body.RowCount=5;Body.RowStyles.Clear();
            Body.RowStyles.Add(new RowStyle(SizeType.Absolute,42));Body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            Body.RowStyles.Add(new RowStyle(SizeType.Absolute,80));Body.RowStyles.Add(new RowStyle(SizeType.Absolute,130));Body.RowStyles.Add(new RowStyle(SizeType.Absolute,68));
            var footer=Body.GetControlFromPosition(0,3);Body.SetRow(footer,4);footer.Padding=new Padding(0,12,0,12);Activity.AutoEllipsis=true;Activity.Margin=Padding.Empty;Activity.TextAlign=ContentAlignment.MiddleLeft;Back.Anchor=AnchorStyles.Right;
            ConfigureGrid();
            var table=new Panel{Dock=DockStyle.Fill,Padding=Padding.Empty,Margin=Padding.Empty};
            var slot=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};gridScroll.Dock=DockStyle.Right;gridScroll.Width=14;gridScroll.AlignToRows=true;gridScroll.Bind(grid);slot.Controls.Add(grid);table.Controls.Add(slot);Body.Controls.Add(table,0,1);gridScroll.Dock=DockStyle.None;Controls.Add(gridScroll);Action alignScroll=()=>{if(!IsHandleCreated||table.IsDisposed)return;var point=PointToClient(table.PointToScreen(new Point(table.ClientSize.Width,0)));gridScroll.Bounds=new Rectangle(point.X,point.Y,Math.Max(1,(int)Math.Round(14F*Font.Height/16F)),grid.Height);gridScroll.BringToFront();};Layout+=delegate{alignScroll();};table.SizeChanged+=delegate{alignScroll();};table.LocationChanged+=delegate{alignScroll();};slot.LocationChanged+=delegate{alignScroll();};Root.Layout+=delegate{if(IsHandleCreated)BeginInvoke((MethodInvoker)delegate{if(!IsDisposed)alignScroll();});};grid.SizeChanged+=delegate{alignScroll();};Shown+=delegate{alignScroll();};
            var actions=new BufferedLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(0,20,0,20)};
            foreach(int weight in new[]{34,33,33})actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,weight));actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            applyButton=Button("Применить профиль",true);applyButton.Click+=async delegate{await Apply(SelectedGroup,false);};
            standardButton=Button("Стандарт Windows",false);standardButton.Click+=async delegate{if(SelectedGroup=="folders")await Check(true);else await Apply(SelectedGroup,true);};
            refresh=Button("Проверить настройки",false);refresh.Margin=Padding.Empty;refresh.Click+=async delegate{await Check(true);};
            actions.Controls.Add(applyButton,0,0);actions.Controls.Add(standardButton,1,0);actions.Controls.Add(refresh,2,0);Body.Controls.Add(actions,0,2);
            var log=JournalPanel.Create(journal,"Журнал проверки и настройки",Colors);log.Margin=Padding.Empty;Body.Controls.Add(log,0,3);
            Body.Controls.Remove(log);Body.Controls.Remove(footer);Root.Controls.Remove(Body);
            Body.RowCount=3;while(Body.RowStyles.Count>3)Body.RowStyles.RemoveAt(Body.RowStyles.Count-1);
            OperationView=new DownloadProgressView();
            var compact=new CompactJournalLayout(Body,1,grid,OperationView.WithJournal(log),footer,176,68){StableSettingsRows=true,IncludeStatusColumn=true};OperationView.Margin=new Padding(0,0,0,8);compact.RowCount=3;compact.RowStyles[1]=new RowStyle(SizeType.Percent,100);Root.Controls.Add(compact,0,1);Root.Layout+=delegate{using(var graphics=CreateGraphics())Back.Height=(int)Math.Round(40F*graphics.DpiY/96F);};
            Shown+=delegate{BeginInvoke((MethodInvoker)delegate{if(IsDisposed)return;int height=actions.Controls.Cast<Control>().Max(c=>c.Height);Body.RowStyles[2].Height=height+actions.Margin.Vertical;});};
            grid.SelectionChanged+=delegate{UpdateButtons();};grid.CurrentCellChanged+=delegate{UpdateButtons();};SetTheme(Colors.Dark,false);UpdateButtons();
            Shown+=async delegate{if(preview){foreach(DataGridViewRow row in grid.Rows)row.Cells[2].Value="Не проверено · демонстрация";}else await Check();};
        }
        private string SelectedGroup {get{return grid.CurrentRow==null?"":Convert.ToString(grid.CurrentRow.Tag);}}
        private void ConfigureGrid()
        {
            grid.Dock=DockStyle.Fill;grid.Margin=Padding.Empty;grid.BorderStyle=BorderStyle.None;grid.ReadOnly=true;grid.RowHeadersVisible=false;
            grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=grid.AllowUserToResizeRows=grid.AllowUserToResizeColumns=grid.AllowUserToOrderColumns=false;
            grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
            grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersHeight=44;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.Font=headerFont;grid.ColumnHeadersDefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;
            grid.Font=new Font("Segoe UI",9F);grid.DefaultCellStyle.Padding=new Padding(8,3,8,3);grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleLeft;
            grid.CellBorderStyle=DataGridViewCellBorderStyle.Single;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.Single;grid.ScrollBars=ScrollBars.None;grid.TouchScrollingEnabled=true;
            string[] names={"Настройка","Описание","Состояние"};int[] weights={29,42,29};
            for(int i=0;i<names.Length;i++){int index=grid.Columns.Add("c"+i,names[i]);grid.Columns[index].FillWeight=weights[i];grid.Columns[index].MinimumWidth=140;grid.Columns[index].SortMode=DataGridViewColumnSortMode.NotSortable;}
            grid.Columns[2].DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.RowTemplate.MinimumHeight=44;
            foreach(var setting in Settings){int index=grid.Rows.Add(setting[1],setting[2],"Не проверено");grid.Rows[index].Tag=setting[0];}
        }
        private void UpdateButtons()
        {
            if(applyButton==null)return;
            var setting=Settings.FirstOrDefault(s=>s[0]==SelectedGroup);applyButton.Enabled=standardButton.Enabled=!Busy&&setting!=null;
            if(setting!=null){applyButton.Text=setting[3];standardButton.Text=setting[4];if(SelectedGroup=="kiosk"){applyButton.Enabled &= kioskAvailable;standardButton.Enabled &= kioskBackup;}}
        }
        protected override void OnThemeChanged(){
            base.OnThemeChanged();
            if(journal!=null){journal.BackColor=Colors.Surface;journal.ForeColor=Colors.Text;}
            if(grid==null)return;grid.BackgroundColor=Colors.Canvas;grid.GridColor=Colors.Border;gridScroll.MaximumThumbLength=80;gridScroll.Palette=Colors;gridScroll.BackColor=Colors.Surface;
            grid.DefaultCellStyle.BackColor=Colors.Surface;grid.DefaultCellStyle.ForeColor=Colors.Text;grid.DefaultCellStyle.SelectionBackColor=Colors.Selection;grid.DefaultCellStyle.SelectionForeColor=Colors.SelectionText;
            grid.ColumnHeadersDefaultCellStyle.BackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.ForeColor=Colors.Text;grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=Colors.Text;grid.Invalidate();
        }
        private void Lock(bool value){if(value)CancelRequested=false;if(value)OperationView.BeginOperation(Scanning?"Проверяем настройки…":"Применяем настройки…",!Scanning);else if(OperationView.Snapshot.Active||Scanning)OperationView.FinishOperation(CancelRequested?"Проверка отменена":Activity.Text);Busy=value;Back.Enabled=!value||Scanning;refresh.Enabled=!value;UpdateButtons();}
        protected override void Progress(string text){base.Progress("Windows проверяет или применяет настройки…");}
        private void Log(string text){journal.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);journal.SelectionStart=journal.TextLength;journal.ScrollToCaret();}
        private void ShowState(string text)
        {
            var values=WindowsComponents.Parse(text);
            string backup,support;kioskBackup=values.TryGetValue("kiosk.Backup",out backup)&&backup=="1";kioskAvailable=!values.TryGetValue("kiosk.Support",out support)||support!="Unavailable";
            foreach(DataGridViewRow row in grid.Rows)
            {
                string group=Convert.ToString(row.Tag),error,status;
                status=SystemSettingsPresentation.Status(group,values);
                row.Cells[2].Value=status.Replace("\\r","").Replace("\\n","; ").Replace("\r\n","; ").Replace("\r","; ").Replace("\n","; ");row.Cells[2].ToolTipText=status;
                Log(Settings.First(s=>s[0]==group)[1]+": "+status.Replace("\nПодробности в журнале","").Replace(Environment.NewLine,". ").Replace("\n",". "));
                if(values.TryGetValue(group+".Error",out error))Log(SystemSettingsPresentation.Error(error));
                foreach(string detail in SystemSettingsPresentation.Details(group,values))Log(detail);
            }
            UpdateButtons();
        }
        private async Task Check(bool userRequested=false){if(Busy||preview)return;Scanning=true;CancelRequested=false;Lock(true);if(userRequested)OperationView.BeginOperation("Проверяем настройки…",true,2);try{var result=await Execute("initial-settings","query:all","");if(IsDisposed||CancelRequested)return;if(!LibraryPlan.Success(result.ExitCode))throw new System.IO.IOException(result.Message);OperationView.CompleteStep();ShowState(result.Message);OperationView.CompleteStep();Activity.Text="Проверка завершена";}catch(Exception ex){if(IsDisposed||CancelRequested)return;foreach(DataGridViewRow row in grid.Rows)row.Cells[2].Value="Проверка не выполнена";Log("Не удалось проверить настройки. "+SystemSettingsPresentation.Error(ex.Message));Activity.Text="Проверка не завершена";}finally{OperationView.FinishOperation(CancelRequested?"Проверка отменена":Activity.Text,success:userRequested&&!CancelRequested&&Activity.Text=="Проверка завершена",failed:!CancelRequested&&Activity.Text=="Проверка не завершена");Scanning=false;if(!IsDisposed)Lock(false);}}
        private async Task Apply(string group,bool defaults)
        {
            if(Busy||preview)return;
            string warning=defaults?"Применить стандартные значения Windows в этой группе? Это не восстановление прежней конфигурации.":"Применить настройки BARS в этой группе?";
            if(group=="kiosk")warning=defaults?"Восстановить прежние настройки краевых жестов и центра уведомлений для всех пользователей? Будут восстановлены значения, сохранённые перед первым применением.":"Отключить краевые жесты и центр уведомлений для всех пользователей компьютера? Прокрутка внутри программ сохранится. В Windows Home действие политик не подтверждено. После перезагрузки проверь жесты на КСО. Эта настройка не блокирует все способы сворачивания PosKiosk.";
            if(group=="admin")warning+="\r\nИзменится режим UAC для администраторов. Потребуется перезагрузка.";
            if(group=="folders")warning+="\r\nПолный доступ группе Пользователи, Администраторам и SYSTEM: существующие папки DataKrat и Bars в корне локальных дисков и флешек, включая вложенные файлы. Пользователи смогут заменять программы и изменять разрешения. Существующие запреты не удаляются. Ссылки и неподдерживаемые файловые системы пропускаются.";
            if(group=="passwords")warning+="\r\nМеняются требования к локальным паролям, существующие пароли не меняются. Доменные политики имеют приоритет.";
            if(group=="firewall"||group=="defender"||group=="smartscreen")warning+="\r\nОперация меняет защиту Windows. Действующие политики организации могут ограничить изменение.";
            if(BrandMessageBox.Show(this,warning,Text,MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            Lock(true);bool offer=false;
            try{Log(Settings.First(s=>s[0]==group)[1]+": "+(defaults?(group=="kiosk"?"восстанавливаем прежние настройки":"возвращаем стандартные настройки"):"применяем выбранные настройки"));var results=await ExecuteAll(new[]{new[]{"initial-settings",(defaults?"defaults:":"apply:")+group,""},new[]{"initial-settings","query:all",""}},result=>{if(group=="kiosk"&&result.Reboot)offer=true;});var first=results.First();offer=LibraryPlan.Success(first.ExitCode)&&(group=="admin"||group=="passwords"||(group=="kiosk"&&first.Reboot));Log(LibraryPlan.Success(first.ExitCode)?"Настройки сохранены. Проверяем результат.":"Настройки применены не полностью. "+SystemSettingsPresentation.Error(first.Message));if(results.Count>1)ShowState(results.Last().Message);Activity.Text=LibraryPlan.Success(first.ExitCode)?"Операция завершена":"Не выполнено полностью — проверь журнал";bool confirmed=LibraryPlan.Success(first.ExitCode)&&LibraryPlan.Success(results.Last().ExitCode)&&!WindowsComponents.Parse(results.Last().Message).ContainsKey(group+".Error");if(!confirmed)Activity.Text="Результат не подтверждён полностью — проверь журнал";OperationView.FinishOperation(Activity.Text,success:confirmed,failed:!confirmed);}
            catch(Exception ex){Log("Операция не подтверждена. "+SystemSettingsPresentation.Error(ex.Message));Activity.Text="Не выполнено полностью — проверь журнал";OperationView.FinishOperation(Activity.Text,failed:true);}
            finally{Lock(false);}
            if(offer)await Restart();
        }
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)headerFont.Dispose();}
    }
}
