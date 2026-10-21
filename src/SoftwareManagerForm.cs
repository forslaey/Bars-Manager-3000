using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class SoftwareManagerForm : ManagerModuleForm
    {
        private readonly Dictionary<string,BrandCheckBox> choices=new Dictionary<string,BrandCheckBox>();
        private readonly Dictionary<string,Label> current=new Dictionary<string,Label>(), available=new Dictionary<string,Label>();
        private readonly Dictionary<string,SoftwarePackage> packages=new Dictionary<string,SoftwarePackage>(), offline=new Dictionary<string,SoftwarePackage>();
        private readonly Dictionary<string,SoftwareCheck> checks=new Dictionary<string,SoftwareCheck>();
        private readonly BrandCheckBox allow32=new BrandCheckBox(), defaults=new BrandCheckBox(), remote=new BrandCheckBox();
        private readonly Label sourceLabel,activity;
        private readonly BrandButton refresh,install,cancel,folderButton,back,associations;
        private readonly JournalTextBox journal=new JournalTextBox();
        private readonly bool preview;
        private string source;
        private bool busy,checking,closeAfter,applyingDefaults;
        private volatile bool cancelled;
        private LibrarySession session;
        internal bool MenuRequested {get;private set;}
        internal SoftwareManagerForm(bool previewMode) : base("Установка программ BARS",new Size(1120,860))
        {
            preview=previewMode; MinimumSize=new Size(1080,800);
            var body=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Margin=Padding.Empty}; body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(float height in new[]{80F,0F,116F,58F,86F,52F}) body.RowStyles.Add(new RowStyle(height==0 ? SizeType.Percent : SizeType.Absolute,height==0 ? 100 : height));
            var heading=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=new Padding(0,0,0,10)}; heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,230)); heading.RowStyles.Add(new RowStyle(SizeType.Absolute,34));heading.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var title=Label("Программы для рабочего места BARS",false);title.Font=new Font("Segoe UI",15,FontStyle.Bold);heading.Controls.Add(title,0,0);
            sourceLabel=Label("Локальные установщики: папка _soft",true);heading.Controls.Add(sourceLabel,0,1);
            folderButton=Button("Выбрать папку _soft…",false);folderButton.Margin=new Padding(0,8,0,8);folderButton.Click+=async delegate {if(busy)return;using(var dialog=new FolderBrowserDialog {Description="Выбери _soft или папку Библиотеки",ShowNewFolderButton=false}) if(dialog.ShowDialog(this)==DialogResult.OK){source=Directory.Exists(Path.Combine(dialog.SelectedPath,"_soft")) ? Path.Combine(dialog.SelectedPath,"_soft") : dialog.SelectedPath;await RefreshPackages();}};
            heading.Controls.Add(folderButton,1,0);heading.SetRowSpan(folderButton,2);body.Controls.Add(heading,0,0);
            var table=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=5,Margin=Padding.Empty}; table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,31));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,28));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,41));table.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
            for(int c=0;c<3;c++){var label=Label(new[]{"Программа","В системе","Доступно / источник"}[c],false);label.Font=new Font("Segoe UI",10.5F,FontStyle.Bold);label.TextAlign=ContentAlignment.MiddleCenter;table.Controls.Add(label,c,0);}
            for(int i=0;i<SoftwareCatalog.Apps.Length;i++)
            {
                var app=SoftwareCatalog.Apps[i];table.RowStyles.Add(new RowStyle(SizeType.Percent,25));
                var card=new Card {Dock=DockStyle.Fill,Margin=new Padding(0,0,0,8),Padding=new Padding(14,8,14,8)};
                var row=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};foreach(int weight in new[]{31,28,41})row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,weight));row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
                var choose=new BrandCheckBox {Text=app.Name+(app.Id=="anydesk" ? " · Windows" : " · x64"),Dock=DockStyle.Fill,Margin=Padding.Empty,Font=new Font("Segoe UI",11,FontStyle.Bold),Palette=Colors};choices[app.Id]=choose;
                var installed=Label("Не проверено",true);installed.AutoEllipsis=false;installed.Padding=new Padding(8,0,8,0);current[app.Id]=installed;
                var candidate=Label("Проверяем источники…",true);candidate.AutoEllipsis=false;candidate.Padding=new Padding(8,0,0,0);available[app.Id]=candidate;
                row.Controls.Add(choose,0,0);row.Controls.Add(installed,1,0);row.Controls.Add(candidate,2,0);card.Controls.Add(row);table.Controls.Add(card,0,i+1);table.SetColumnSpan(card,3);
            }
            body.Controls.Add(table,0,1);
            var options=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=new Padding(4,2,0,8)};options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(var item in new[]{defaults,remote,allow32}) {options.RowStyles.Add(new RowStyle(SizeType.Percent,33.333F));item.Dock=DockStyle.Fill;item.Margin=Padding.Empty;item.Palette=Colors;}
            defaults.Text="Применить ассоциации Notepad++ и 7-Zip для всех пользователей при входе";defaults.Checked=true;
            remote.Text="Настроить неконтролируемый доступ AnyDesk / RuDesktop — пароль перед установкой";remote.Checked=true;
            allow32.Text="Разрешить исключение: официальный AnyDesk x86 на Windows x64";allow32.Checked=false;allow32.CheckedChanged+=async delegate {if(!busy&&!preview)await RefreshPackages();else if(!busy)UpdateRows();};options.Controls.Add(defaults,0,0);options.Controls.Add(remote,0,1);options.Controls.Add(allow32,0,2);body.Controls.Add(options,0,2);
            var actions=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=new Padding(0,0,0,10)};foreach(int weight in new[]{29,25,28,18})actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,weight));actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            install=Button("Установить выбранные",true);install.Click+=async delegate {await Install();};refresh=Button("Проверить версии",false);refresh.Click+=async delegate {await RefreshPackages();};
            associations=Button("Применить ассоциации",false);associations.Click+=async delegate {await ApplyDefaults();};cancel=Button("Отменить",false);cancel.Margin=Padding.Empty;cancel.Enabled=false;cancel.Click+=delegate {if(busy && !applyingDefaults && MessageBox.Show(this,LibraryManagerForm.CancelWarning,"Отмена",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes)RequestCancel(false);};
            actions.Controls.Add(install,0,0);actions.Controls.Add(refresh,1,0);actions.Controls.Add(associations,2,0);actions.Controls.Add(cancel,3,0);body.Controls.Add(actions,0,3);
            var logCard=new Card {Dock=DockStyle.Fill,Padding=new Padding(12,8,12,8),Margin=Padding.Empty};journal.Dock=DockStyle.Fill;journal.Font=new Font("Consolas",9);var scroll=new JournalScrollBar {Dock=DockStyle.Right,Width=14};scroll.Bind(journal);logCard.Controls.Add(journal);logCard.Controls.Add(scroll);body.Controls.Add(logCard,0,4);
            var footer=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty,Padding=new Padding(0,10,0,0)};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            activity=Label("Установка начнётся только по кнопке",true);activity.Font=new Font("Segoe UI",9);footer.Controls.Add(activity,0,0);
            var about=Button("О программе",false);about.Click+=delegate{using(var dialog=new AboutDialog(Colors,Icon,"Программы BARS","Установка и настройка рабочего места"))dialog.ShowDialog(this);};footer.Controls.Add(about,1,0);
            back=Button("В меню",false);back.Margin=Padding.Empty;back.Click+=delegate{MenuRequested=true;Close();};footer.Controls.Add(back,2,0);body.Controls.Add(footer,0,5);Root.Controls.Add(body,0,1);
            try{if(!preview)source=SoftwareCatalog.FindFolder();}catch(Exception ex){Log(ex.Message);}
            SetTheme(Colors.Dark,false);
            Shown+=async delegate {if(preview)FillPreview();else await RefreshPackages();};
            FormClosing+=delegate(object sender,FormClosingEventArgs e)
            {
                if(!busy)return;e.Cancel=true;
                if(applyingDefaults){MenuRequested=false;MessageBox.Show(this,"Дождись завершения применения системного профиля",Text);return;}
                if(cancelled){closeAfter=true;return;}
                if(checking || MessageBox.Show(this,LibraryManagerForm.CancelWarning+"\r\n\r\nЗакрыть менеджер после отмены?","Завершение работы",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes)RequestCancel(true);else MenuRequested=false;
            };
        }
        protected override void OnThemeChanged()
        {
            foreach(var item in choices.Values)item.Palette=Colors;foreach(var item in new[]{allow32,defaults,remote})item.Palette=Colors;journal.BackColor=Colors.Surface;journal.ForeColor=Colors.Text;
        }
        private void Log(string text){if(IsDisposed)return;journal.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);}
        private void Progress(string text){if(IsDisposed||!IsHandleCreated)return;try{BeginInvoke((MethodInvoker)delegate{if(!IsDisposed)activity.Text=text;});}catch(InvalidOperationException){}}
        private void RequestCancel(bool close){closeAfter|=close;cancelled=true;cancel.Enabled=false;if(session!=null)session.Cancel();activity.Text="Отменяем операцию…";}
        private void Lock(bool value)
        {
            busy=value;folderButton.Enabled=refresh.Enabled=install.Enabled=associations.Enabled=back.Enabled=allow32.Enabled=defaults.Enabled=remote.Enabled=!value;cancel.Enabled=value&&!applyingDefaults;
            foreach(var choice in choices.Values)choice.Enabled=!value;
            if(!value){UpdateRows();if(closeAfter)BeginInvoke((MethodInvoker)delegate{Close();});}
        }
        private async Task RefreshPackages()
        {
            if(busy||preview)return;bool allowAny=allow32.Checked;checking=true;cancelled=false;Lock(true);sourceLabel.Text=source??"_soft не найден: можно выбрать папку или получить программы из сети";
            try
            {
                var local=await Task.Run(()=>{var list=SoftwareCatalog.Local(source,Progress);if(Directory.Exists(SoftwareSources.CacheFolder))foreach(string folder in Directory.GetDirectories(SoftwareSources.CacheFolder))list.AddRange(SoftwareCatalog.Local(folder,Progress,"Кэш менеджера"));return list;});
                offline.Clear();packages.Clear();checks.Clear();
                foreach(var app in SoftwareCatalog.Apps){var candidate=SoftwareSources.Best(local,app.Id);if(candidate!=null){offline[app.Id]=candidate;packages[app.Id]=candidate;}checks[app.Id]=await Task.Run(()=>SoftwareDetection.Check(app.Id,allowAny));}
                if(checks.Values.Any(c=>c.State==LibraryState.Unknown && c.Detail.Contains("доступ")))
                {
                    Progress("Уточняем регистрацию с правами администратора");
                    using(var elevated=await Task.Run(()=>LibrarySession.Start(()=>cancelled))){session=elevated;var snapshot=await Task.Run(()=>elevated.ExecuteSystem("software-query",allowAny?"allow-anydesk32":"x64-only","",Progress));if(snapshot.ExitCode!=0)throw new InvalidOperationException(snapshot.Message);foreach(var check in SoftwareDetection.ParseQuery(snapshot.Message))checks[check.Id]=check;session=null;}
                }
                var updates=await Task.WhenAll(SoftwareCatalog.Apps.Where(a=>a.Id!="anydesk"||allow32.Checked).Select(app=>Task.Run(()=>
                {
                    LibraryCancellation.Current=()=>cancelled;
                    try{return SoftwareSources.Latest(app.Id);}catch(Exception ex){Progress(app.Name+": официальный источник недоступен. "+ex.Message);return null;}finally{LibraryCancellation.Current=null;}
                })));
                foreach(var candidate in updates.Where(p=>p!=null)) {SoftwarePackage old;if(!packages.TryGetValue(candidate.Id,out old)||candidate.Version=="0.0"||SoftwareCatalog.Compare(candidate.Version,old.Version)>=0)packages[candidate.Id]=candidate;}
                if(cancelled)throw new OperationCanceledException();
                foreach(var app in SoftwareCatalog.Apps){choices[app.Id].Checked=packages.ContainsKey(app.Id)&&(checks[app.Id].State==LibraryState.Missing || checks[app.Id].State==LibraryState.Installed && packages[app.Id].Version!="0.0" && SoftwareCatalog.Compare(packages[app.Id].Version,checks[app.Id].Version)>0)&&(app.Id!="anydesk"||allow32.Checked);}
                Log("Проверка завершена. Если официальный источник недоступен, предлагается проверенный _soft или кэш");activity.Text="Выбери программы и проверь источник установщиков";
            }
            catch(OperationCanceledException){Log("Проверка отменена");}catch(Exception ex){Log("Проверка не завершена: "+ex.Message);}
            finally{checking=false;Lock(false);}
        }
        private void UpdateRows()
        {
            foreach(var app in SoftwareCatalog.Apps)
            {
                SoftwareCheck check;SoftwarePackage package;checks.TryGetValue(app.Id,out check);packages.TryGetValue(app.Id,out package);
                current[app.Id].Text=check==null ? "Не проверено" : check.State==LibraryState.Installed ? check.Version+"\r\n"+check.Detail : check.Detail;
                available[app.Id].Text=package==null ? "Пакет не найден\r\nВыбери _soft или проверь доступ к сайту" : (package.Version=="0.0" ? "Версия после получения" : package.Version)+" · "+package.Architecture+"\r\n"+package.Source;
                bool allowed=package!=null && check!=null && check.State!=LibraryState.Unknown && (package.Architecture=="x64" || package.Id=="anydesk"&&allow32.Checked);
                choices[app.Id].Enabled=!busy&&allowed;if(!allowed)choices[app.Id].Checked=false;
                if(app.Id=="anydesk"&&!allow32.Checked)available[app.Id].Text="Официальный клиент x86\r\nНужно явное разрешение исключения";
            }
        }
        private async Task Install()
        {
            if(busy||preview)return;var ids=SoftwareCatalog.Apps.Where(a=>choices[a.Id].Checked).Select(a=>a.Id).ToArray();if(ids.Length==0){activity.Text="Отметь программы для установки";return;}
            var secrets=new Dictionary<string,string>();
            if(remote.Checked)foreach(string id in ids.Where(id=>SoftwareCatalog.Find(id).Remote))using(var dialog=new SoftwareRemoteDialog(Colors,Icon,id)) {if(dialog.ShowDialog(this)!=DialogResult.OK)return;secrets[id]=dialog.Password;}
            if(MessageBox.Show(this,"Установить или обновить выбранные программы?\r\n\r\nОбновление AnyDesk / RuDesktop может прервать текущий удалённый сеанс. Изменение ассоциаций потребует повторного входа пользователей в Windows.",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            cancelled=false;Lock(true);bool reboot=false;
            var ready=new List<SoftwarePackage>();
            try
            {
                foreach(string id in ids)
                {
                    if(cancelled)break;var package=packages[id];
                    if(!Path.IsPathRooted(package.File)||!File.Exists(package.File))
                    {
                        try{package=await Task.Run(()=>SoftwareSources.Download(package,SoftwareSources.CacheFolder,Progress,()=>cancelled));packages[id]=package;}
                        catch(OperationCanceledException){throw;}
                        catch(Exception ex)
                        {
                            SoftwarePackage fallback;if(!offline.TryGetValue(id,out fallback))throw new IOException(SoftwareCatalog.Find(id).Name+": загрузка не удалась, проверенного локального пакета нет. "+ex.Message);
                            Log(SoftwareCatalog.Find(id).Name+": загрузка не удалась. Локальный пакет "+fallback.Version);
                            if(MessageBox.Show(this,"Использовать локальный "+SoftwareCatalog.Find(id).Name+" "+fallback.Version+" вместо сетевого пакета?",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)throw new OperationCanceledException();package=fallback;
                        }
                    }
                    SoftwareCatalog.Validate(package,allow32.Checked);
                    ready.Add(package);
                }
                if(cancelled)throw new OperationCanceledException();
                using(var active=await Task.Run(()=>LibrarySession.Start(()=>cancelled)))
                {
                    session=active;bool complete=true;
                    foreach(var package in ready)
                    {
                        if(cancelled){complete=false;break;}string id=package.Id;
                        var request=new SoftwareInstallRequest {Id=id,Version=package.Version,Sha256=package.Sha256,AllowAnyDesk32=allow32.Checked,ConfigureRemote=secrets.ContainsKey(id),Password=secrets.ContainsKey(id)?secrets[id]:""};
                        var result=await Task.Run(()=>active.ExecuteSystem("software-install",request.Serialize(),package.File,Progress));
                        Log(SoftwareCatalog.Find(id).Name+": "+result.Message);bool allow=allow32.Checked;checks[id]=await Task.Run(()=>SoftwareDetection.Check(id,allow));UpdateRows();
                        if(result.Reboot){reboot=true;complete=false;break;}if(!LibraryPlan.Success(result.ExitCode)||result.State!=LibraryState.Installed){complete=false;break;}
                    }
                    if(complete&&!cancelled&&!reboot&&defaults.Checked)await DefaultsCore(active);
                }
            }
            catch(OperationCanceledException){Log("Отменено. Проверь программы перед повтором");}catch(Exception ex){Log("Очередь остановлена: "+ex.Message);}
            finally{session=null;Lock(false);secrets.Clear();}
            if(reboot&&!closeAfter)await RestartOffer.Show(this);
        }
        private async Task DefaultsCore(LibrarySession existing=null)
        {
            var ids=new[]{"notepad","7zip"}.Where(id=>checks.ContainsKey(id)&&checks[id].State==LibraryState.Installed).ToArray();if(ids.Length==0){Log("Ассоциации не изменены: Notepad++ и 7-Zip x64 не подтверждены");return;}
            applyingDefaults=true;cancel.Enabled=false;
            try
            {
                LibraryInstallResult result;
                if(existing!=null)result=await Task.Run(()=>existing.ExecuteSystem("software-defaults",String.Join(",",ids),"",Progress));
                else using(var active=await Task.Run(()=>LibrarySession.Start())){session=active;result=await Task.Run(()=>active.ExecuteSystem("software-defaults",String.Join(",",ids),"",Progress));}
                if(result.ExitCode!=0)throw new InvalidOperationException(result.Message);Log(result.Message);activity.Text=result.Message;
            }
            finally{session=null;applyingDefaults=false;}
        }
        private async Task ApplyDefaults()
        {
            if(busy||preview)return;if(MessageBox.Show(this,"Применить системный профиль Notepad++ и 7-Zip для пользователей компьютера? Сохраним исходные ассоциации. Для применения существующим пользователям нужен повторный вход в Windows.",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            Lock(true);try{await DefaultsCore();}catch(Exception ex){Log(ex.Message);}finally{Lock(false);}
        }
        private void FillPreview()
        {
            foreach(var app in SoftwareCatalog.Apps){checks[app.Id]=new SoftwareCheck {Id=app.Id,Version=app.Id=="notepad"?"8.9.8.1":"26.04",State=app.Id=="notepad"?LibraryState.Installed:LibraryState.Missing,Detail=app.Id=="notepad"?"Регистрация и EXE подтверждены · x64":"Не установлено"};packages[app.Id]=new SoftwarePackage {Id=app.Id,Version=app.Id=="notepad"?"8.9.8.1":app.Id=="7zip"?"26.04":app.Id=="rudesktop"?"3.0.1563":"9.8",Architecture=app.Id=="anydesk"?"x86":"x64",Source=app.Id=="notepad"?"GitHub · официальный релиз":"Локальный _soft"};}
            UpdateRows();Log("Проверка завершена. Для отсутствующих программ доступны установщики");
        }
    }
    internal sealed class SoftwareRemoteDialog : Form
    {
        private readonly BrandTextBox input=new BrandTextBox();private readonly BrandCheckBox remember=new BrandCheckBox();private readonly string id;
        internal string Password {get{return input.Text;}}
        internal SoftwareRemoteDialog(ThemePalette colors,System.Drawing.Icon icon,string appId,bool loadSaved=true)
        {
            id=appId;Text="Неконтролируемый доступ · "+SoftwareCatalog.Find(id).Name;Icon=icon==null?null:(Icon)icon.Clone();ClientSize=new Size(620,290);Font=new Font("Segoe UI",10);BackColor=colors.Canvas;ForeColor=colors.Text;StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;
            var layout=new BufferedLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=5};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int height in new[]{58,42,44,40,50})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            var text=new Label {Text="Задай пароль для подключения без подтверждения.\r\nПароль не попадёт в журнал менеджера.",Dock=DockStyle.Fill,ForeColor=colors.Text};layout.Controls.Add(text,0,0);
            input.Dock=DockStyle.Fill;input.UseSystemPasswordChar=true;input.MaxLength=128;input.Palette=colors;input.BackColor=colors.SurfaceAlt;input.ForeColor=colors.Text;layout.Controls.Add(input,0,1);
            remember.Text="Запомнить на этом компьютере для моей учётной записи";remember.Dock=DockStyle.Fill;remember.Palette=colors;layout.Controls.Add(remember,0,2);
            var note=new Label {Text=id=="rudesktop"?"RuDesktop передаёт пароль параметром процесса; он может быть виден локальному администратору во время настройки":"Пароль передаётся AnyDesk через защищённый канал ввода",Dock=DockStyle.Fill,ForeColor=colors.Muted,Font=new Font("Segoe UI",9)};layout.Controls.Add(note,0,3);
            var actions=new BufferedLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            var cancel=new BrandButton {Text="Отмена",Palette=colors,Dock=DockStyle.Fill,DialogResult=DialogResult.Cancel,Margin=new Padding(0,0,10,0)};actions.Controls.Add(cancel,0,0);
            var ok=new BrandButton {Text="Продолжить",Palette=colors,Primary=true,Dock=DockStyle.Fill,Margin=Padding.Empty};ok.Click+=delegate{if(input.Text.Length<8||input.Text.Any(Char.IsControl)){MessageBox.Show(this,"Введи от 8 до 128 символов без переводов строки",Text);return;}try{if(remember.Checked)SoftwarePasswordStore.Save(id,input.Text);else SoftwarePasswordStore.Forget(id);DialogResult=DialogResult.OK;}catch(Exception){MessageBox.Show(this,"Не удалось сохранить пароль. Можно продолжить без запоминания",Text);}};actions.Controls.Add(ok,1,0);layout.Controls.Add(actions,0,4);Controls.Add(layout);CancelButton=cancel;AcceptButton=ok;
            if(loadSaved)try{input.Text=SoftwarePasswordStore.Load(id);remember.Checked=input.Text.Length>0;}catch(Exception){}
        }
    }
}
