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
        private readonly ServiceGrid grid=new ServiceGrid();
        private readonly LibraryGridScrollBar gridScroll=new LibraryGridScrollBar();
        private readonly JournalTextBox journal=new JournalTextBox();
        private readonly BrandButton refresh,applyButton,standardButton;
        private readonly Font headerFont=new Font("Segoe UI",10.5F,FontStyle.Bold);
        private static readonly string[][] Settings={
            new[]{"passwords","Политики паролей","Профиль BARS по согласованному списку. Существующие пароли не изменяются.","Применить профиль","Стандарт Windows"},
            new[]{"admin","Администратор без запросов UAC","Для существующих администраторов. После изменения требуется перезагрузка.","Применить профиль","Стандарт Windows"},
            new[]{"firewall","Брандмауэр Windows","Все профили: доменный, частный и общедоступный.","Отключить","Включить все профили"},
            new[]{"defender","Защита Defender","Временно отключить защиту в реальном времени. Windows может включить её обратно.","Временно отключить","Включить защиту"},
            new[]{"smartscreen","SmartScreen для приложений","Предупреждения при запуске загруженных приложений. Для всех пользователей компьютера.","Отключить","Стандарт Windows"},
            new[]{"folders","Права папок DataKrat и Bars","Полный доступ всем пользователям. Папки и файлы на локальных дисках и флешках.","Выдать полный доступ","Проверить папки"}
        };
        internal InitialSystemSettingsForm(bool previewMode=false):base("Первичные системные настройки",new Size(1080,840))
        {
            preview=previewMode;MinimumSize=new Size(950,680);Root.AutoScroll=false;Body.MinimumSize=Size.Empty;
            Body.RowCount=5;Body.RowStyles.Clear();
            Body.RowStyles.Add(new RowStyle(SizeType.Absolute,52));Body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            Body.RowStyles.Add(new RowStyle(SizeType.Absolute,62));Body.RowStyles.Add(new RowStyle(SizeType.Absolute,130));Body.RowStyles.Add(new RowStyle(SizeType.Absolute,54));
            var footer=Body.GetControlFromPosition(0,3);Body.SetRow(footer,4);Activity.AutoEllipsis=true;
            ConfigureGrid();
            var table=new Card{Dock=DockStyle.Fill,Padding=new Padding(10),Margin=Padding.Empty};
            var slot=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};gridScroll.Dock=DockStyle.Right;gridScroll.Width=14;gridScroll.Bind(grid);slot.Controls.Add(grid);slot.Controls.Add(gridScroll);table.Controls.Add(slot);Body.Controls.Add(table,0,1);
            var actions=new BufferedLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(0,10,0,6)};
            foreach(int weight in new[]{34,33,33})actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,weight));actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            applyButton=Button("Применить профиль",true);applyButton.Click+=async delegate{await Apply(SelectedGroup,false);};
            standardButton=Button("Стандарт Windows",false);standardButton.Click+=async delegate{if(SelectedGroup=="folders")await Check();else await Apply(SelectedGroup,true);};
            refresh=Button("Проверить настройки",false);refresh.Margin=Padding.Empty;refresh.Click+=async delegate{await Check();};
            actions.Controls.Add(applyButton,0,0);actions.Controls.Add(standardButton,1,0);actions.Controls.Add(refresh,2,0);Body.Controls.Add(actions,0,2);
            var log=new Card{Dock=DockStyle.Fill,Padding=new Padding(10),Margin=Padding.Empty};journal.Dock=DockStyle.Fill;journal.Font=new Font("Consolas",9);var scroll=new JournalScrollBar{Dock=DockStyle.Right,Width=14};scroll.Bind(journal);log.Controls.Add(journal);log.Controls.Add(scroll);Body.Controls.Add(log,0,3);
            grid.SelectionChanged+=delegate{UpdateButtons();};grid.CurrentCellChanged+=delegate{UpdateButtons();};SetTheme(Colors.Dark,false);UpdateButtons();
            Shown+=async delegate{if(preview){foreach(DataGridViewRow row in grid.Rows)row.Cells[2].Value="Не проверено · демонстрация";}else await Check();};
        }
        private string SelectedGroup {get{return grid.CurrentRow==null?"":Convert.ToString(grid.CurrentRow.Tag);}}
        private void ConfigureGrid()
        {
            grid.Dock=DockStyle.Fill;grid.Margin=Padding.Empty;grid.BorderStyle=BorderStyle.None;grid.ReadOnly=true;grid.RowHeadersVisible=false;
            grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=grid.AllowUserToResizeRows=grid.AllowUserToResizeColumns=grid.AllowUserToOrderColumns=false;
            grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
            grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersHeight=56;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle.Font=headerFont;grid.ColumnHeadersDefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Padding=new Padding(8);grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;grid.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleLeft;
            grid.CellBorderStyle=DataGridViewCellBorderStyle.Single;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.Single;grid.ScrollBars=ScrollBars.None;grid.TouchScrollingEnabled=true;
            string[] names={"Настройка","Описание","Состояние"};int[] weights={29,42,29};
            for(int i=0;i<names.Length;i++){int index=grid.Columns.Add("c"+i,names[i]);grid.Columns[index].FillWeight=weights[i];grid.Columns[index].MinimumWidth=140;grid.Columns[index].SortMode=DataGridViewColumnSortMode.NotSortable;}
            grid.RowTemplate.MinimumHeight=76;
            foreach(var setting in Settings){int index=grid.Rows.Add(setting[1],setting[2],"Не проверено");grid.Rows[index].Tag=setting[0];}
        }
        private void UpdateButtons()
        {
            if(applyButton==null)return;
            var setting=Settings.FirstOrDefault(s=>s[0]==SelectedGroup);applyButton.Enabled=standardButton.Enabled=!Busy&&setting!=null;
            if(setting!=null){applyButton.Text=setting[3];standardButton.Text=setting[4];}
        }
        protected override void OnThemeChanged()
        {
            if(journal!=null){journal.BackColor=Colors.Surface;journal.ForeColor=Colors.Text;}
            if(grid==null)return;grid.BackgroundColor=Colors.Surface;grid.GridColor=Colors.Border;gridScroll.Palette=Colors;gridScroll.BackColor=Colors.Surface;
            grid.DefaultCellStyle.BackColor=Colors.Surface;grid.DefaultCellStyle.ForeColor=Colors.Text;grid.DefaultCellStyle.SelectionBackColor=Colors.Selection;grid.DefaultCellStyle.SelectionForeColor=Colors.SelectionText;
            grid.ColumnHeadersDefaultCellStyle.BackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.ForeColor=Colors.Text;grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Colors.SurfaceAlt;grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=Colors.Text;grid.Invalidate();
        }
        private void Lock(bool value){Busy=value;Back.Enabled=refresh.Enabled=!value;UpdateButtons();}
        private void Log(string text){journal.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);journal.SelectionStart=journal.TextLength;journal.ScrollToCaret();}
        private void ShowState(string text)
        {
            var values=WindowsComponents.Parse(text);
            foreach(DataGridViewRow row in grid.Rows)
            {
                string group=Convert.ToString(row.Tag),error,status;
                if(values.TryGetValue(group+".Error",out error))status="Проверка не выполнена. Причина — в журнале.";
                else if(group=="admin")status=values.ContainsKey("admin.EnableLUA")?"UAC: "+(values["admin.EnableLUA"]=="0"?"отключён":"включён"):"Не проверено";
                else if(group=="firewall")status=String.Join("; ",new[]{"Domain","Private","Public"}.Select(k=>k+": "+(values.ContainsKey("firewall."+k)?values["firewall."+k]:"не проверено")));
                else if(group=="smartscreen")status=values.ContainsKey("smartscreen.Mode")?values["smartscreen.Mode"]:"Не проверено";
                else if(group=="defender")status=values.ContainsKey("defender.RealTimeProtection")?"Защита в реальном времени: "+values["defender.RealTimeProtection"]:"Не проверено";
                else status="Подробные результаты — в журнале";
                row.Cells[2].Value=status;row.Cells[2].ToolTipText=status;
            }
            Log(text);
        }
        private async Task Check(){if(Busy||preview)return;Lock(true);try{var result=await Execute("initial-settings","query:all","");ShowState(result.Message);Activity.Text="Проверка завершена";}catch(Exception ex){foreach(DataGridViewRow row in grid.Rows)row.Cells[2].Value="Проверка не выполнена";Log("Не удалось проверить настройки: "+ex.Message);Activity.Text="Проверка не завершена";}finally{Lock(false);}}
        private async Task Apply(string group,bool defaults)
        {
            if(Busy||preview)return;
            string warning=defaults?"Применить стандартные значения Windows в этой группе? Это не восстановление прежней конфигурации.":"Применить настройки BARS в этой группе?";
            if(group=="admin")warning+="\r\nИзменится режим UAC для администраторов. Потребуется перезагрузка.";
            if(group=="folders")warning+="\r\nПолный доступ группе Пользователи, Администраторам и SYSTEM: существующие папки DataKrat и Bars в корне локальных дисков и флешек, включая вложенные файлы. Пользователи смогут заменять программы и изменять разрешения. Существующие запреты не удаляются. Ссылки и неподдерживаемые файловые системы пропускаются.";
            if(group=="passwords")warning+="\r\nМеняются требования к локальным паролям, существующие пароли не меняются. Доменные политики имеют приоритет.";
            if(group=="firewall"||group=="defender"||group=="smartscreen")warning+="\r\nОперация меняет защиту Windows. Действующие политики организации могут ограничить изменение.";
            if(MessageBox.Show(this,warning,Text,MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            Lock(true);bool offer=false;
            try{var results=await ExecuteAll(new[]{new[]{"initial-settings",(defaults?"defaults:":"apply:")+group,""},new[]{"initial-settings","query:all",""}},result=>Log(result.Message));var first=results.First();offer=LibraryPlan.Success(first.ExitCode)&&(group=="admin"||group=="passwords");if(results.Count>1)ShowState(results.Last().Message);Activity.Text=LibraryPlan.Success(first.ExitCode)?"Операция завершена":"Не выполнено полностью — проверь журнал";}
            catch(Exception ex){Log("Операция не подтверждена: "+ex.Message);Activity.Text="Не выполнено полностью — проверь журнал";}
            finally{Lock(false);}
            if(offer)await Restart();
        }
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)headerFont.Dispose();}
    }
}
