using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
namespace ServiceTool
{
    internal static class KioskTouchTests
    {
        static int checks, constrainedViewports;
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        sealed class Fake : KioskTouchSettings.Store
        {
            internal Dictionary<string,KioskTouchSettings.Entry> Values=new Dictionary<string,KioskTouchSettings.Entry>();
            internal int Writes,FailAt;internal bool IgnoreBackup;
            public KioskTouchSettings.Entry Read(string path,string name){KioskTouchSettings.Entry value;return Values.TryGetValue(path+"|"+name,out value)?value:KioskTouchSettings.Entry.Missing();}
            public void Write(string path,string name,KioskTouchSettings.Entry value)
            {
                Writes++;if(FailAt==Writes)throw new IOException("fixture write failure");if(IgnoreBackup&&path==KioskTouchSettings.BackupPath)return;
                if(value.Exists)Values[path+"|"+name]=value;else Values.Remove(path+"|"+name);
            }
        }
        static KioskTouchSettings.Platform Pro(){return new KioskTouchSettings.Platform{Client=true,Supported=true,Edition="Professional"};}
        static void ExpectFailure(Action action,string name){bool failed=false;try{action();}catch{failed=true;}Check(failed,name);}
        static KioskTouchSettings.Entry Read(Fake f,int index){return f.Read(KioskTouchSettings.Paths[index],KioskTouchSettings.Names[index]);}
        static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
        static void Core()
        {
            var f=new Fake();string query=KioskTouchSettings.Query(f,Pro());Check(f.Writes==0&&query.Contains("kiosk.Configured=0"),"Read-only query");
            Check(KioskTouchSettings.Change(f,Pro(),false),"Initial apply changes values");
            Check(Read(f,0).Kind==RegistryValueKind.DWord&&(int)Read(f,0).Value==0,"Edge policy machine DWORD 0");
            Check((int)Read(f,1).Value==1,"Notification machine DWORD 1");
            var snapshot=f.Read(KioskTouchSettings.BackupPath,KioskTouchSettings.BackupName);var bytes=(byte[])snapshot.Value;
            Check(!KioskTouchSettings.Change(f,Pro(),false),"Repeated apply idempotent");
            Check(bytes.SequenceEqual((byte[])f.Read(KioskTouchSettings.BackupPath,KioskTouchSettings.BackupName).Value),"Original snapshot unchanged");
            Check(KioskTouchSettings.Change(f,Pro(),true),"Restore changes values");Check(!Read(f,0).Exists&&!Read(f,1).Exists,"Originally absent values removed");
            Check(!f.Read(KioskTouchSettings.BackupPath,KioskTouchSettings.BackupName).Exists,"Backup consumed only after verified restore");
            int writes=f.Writes;ExpectFailure(()=>KioskTouchSettings.Change(f,Pro(),true),"No backup means no restore");Check(f.Writes==writes,"No writes without backup");
            var entries=new[]{KioskTouchSettings.Entry.Dword(1),new KioskTouchSettings.Entry{Exists=true,Kind=RegistryValueKind.String,Value="custom & <value>"},new KioskTouchSettings.Entry{Exists=true,Kind=RegistryValueKind.ExpandString,Value="%PATH%"},new KioskTouchSettings.Entry{Exists=true,Kind=RegistryValueKind.MultiString,Value=new[]{"one","two"}},new KioskTouchSettings.Entry{Exists=true,Kind=RegistryValueKind.Binary,Value=new byte[]{0,255}},new KioskTouchSettings.Entry{Exists=true,Kind=RegistryValueKind.QWord,Value=1234567890123L},new KioskTouchSettings.Entry{Exists=true,Kind=RegistryValueKind.None,Value=new byte[]{1,2}}};
            foreach(var entry in entries){f=new Fake();f.Write(KioskTouchSettings.EdgePath,KioskTouchSettings.Names[0],entry);f.Write(KioskTouchSettings.CenterPath,KioskTouchSettings.Names[1],KioskTouchSettings.Entry.Dword(0));KioskTouchSettings.Change(f,Pro(),false);KioskTouchSettings.Change(f,Pro(),true);Check(KioskTouchSettings.Same(Read(f,0),entry),"Exact type and data restored: "+entry.Kind);Check((int)Read(f,1).Value==0,"Prior explicit zero restored");}
            f=new Fake();KioskTouchSettings.Change(f,Pro(),false);f.Write(KioskTouchSettings.EdgePath,KioskTouchSettings.Names[0],KioskTouchSettings.Entry.Dword(9));writes=f.Writes;ExpectFailure(()=>KioskTouchSettings.Change(f,Pro(),true),"External change blocks rollback");Check(f.Writes==writes&&(int)Read(f,0).Value==9,"No external configuration overwritten");
            f=new Fake{IgnoreBackup=true};ExpectFailure(()=>KioskTouchSettings.Change(f,Pro(),false),"Backup verification failure blocks policies");Check(!Read(f,0).Exists&&!Read(f,1).Exists,"No policy write without durable backup");
            f=new Fake{FailAt=3};ExpectFailure(()=>KioskTouchSettings.Change(f,Pro(),false),"Second policy failure propagated");Check(f.Read(KioskTouchSettings.BackupPath,KioskTouchSettings.BackupName).Exists,"Backup retained after partial apply");f.FailAt=0;KioskTouchSettings.Change(f,Pro(),true);Check(!Read(f,0).Exists&&!Read(f,1).Exists,"Partial apply can be restored");
            f=new Fake();KioskTouchSettings.Change(f,Pro(),false);f.FailAt=f.Writes+2;ExpectFailure(()=>KioskTouchSettings.Change(f,Pro(),true),"Partial rollback failure propagated");Check(f.Read(KioskTouchSettings.BackupPath,KioskTouchSettings.BackupName).Exists,"Backup survives failed rollback");f.FailAt=0;KioskTouchSettings.Change(f,Pro(),true);Check(!Read(f,0).Exists&&!Read(f,1).Exists,"Rollback retry completes");
            f=new Fake();f.Write(KioskTouchSettings.BackupPath,KioskTouchSettings.BackupName,new KioskTouchSettings.Entry{Exists=true,Kind=RegistryValueKind.Binary,Value=Encoding.UTF8.GetBytes("<bad/>")});writes=f.Writes;ExpectFailure(()=>KioskTouchSettings.Change(f,Pro(),false),"Corrupt backup blocks apply");ExpectFailure(()=>KioskTouchSettings.Change(f,Pro(),true),"Corrupt backup blocks restore");Check(f.Writes==writes&&KioskTouchSettings.Query(f,Pro()).Contains("Повреждена"),"Corruption shown without destructive changes");
            var home=new KioskTouchSettings.Platform{Edition="Core",Client=true,Supported=false};f=new Fake();KioskTouchSettings.Change(f,home,false);Check(KioskTouchSettings.Query(f,home).Contains("kiosk.Support=Unconfirmed"),"Home never claims supported policy enforcement");Check(KioskTouchSettings.Query(f,home).Contains("kiosk.Configured=1"),"Home distinguishes stored state");
            var server=new KioskTouchSettings.Platform{Client=false,Edition="ServerStandard"};f=new Fake();ExpectFailure(()=>KioskTouchSettings.Change(f,server,false),"Server not blindly configured");Check(f.Writes==0,"Unsupported OS left intact");
            foreach(string edition in new[]{"Professional","ProfessionalN","EnterpriseS","Education","IoTEnterpriseS","Core","CoreSingleLanguage","Unknown"}){f=new Fake();const string path=@"SOFTWARE\Microsoft\Windows NT\CurrentVersion";f.Write(path,"EditionID",new KioskTouchSettings.Entry{Exists=true,Value=edition});f.Write(path,"InstallationType",new KioskTouchSettings.Entry{Exists=true,Value="Client"});f.Write(path,"CurrentBuildNumber",new KioskTouchSettings.Entry{Exists=true,Value="19045"});var platform=KioskTouchSettings.Platform.Detect(f);Check(platform.Client,"Client platform "+edition);Check(platform.Supported==(!edition.StartsWith("Core")&&edition!="Unknown"),"Edition classified "+edition);}
        }
        static void UI()
        {
            foreach(bool dark in new[]{false,true})foreach(float scale in new[]{1F,1.25F,1.5F,2F})using(var form=new InitialSystemSettingsForm(true))
            {
                form.SetTheme(dark,false);form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.ShowInTaskbar=false;form.Show();Application.DoEvents();
                if(scale!=1F){form.Scale(new SizeF(scale,scale));Application.DoEvents();}
                var grid=Field<ServiceGrid>(form,"grid");var row=grid.Rows.Cast<DataGridViewRow>().Single(r=>Convert.ToString(r.Tag)=="kiosk");grid.CurrentCell=row.Cells[0];Application.DoEvents();
                var apply=Field<BrandButton>(form,"applyButton");var restore=Field<BrandButton>(form,"standardButton");Check(apply.Text=="Применить настройки КСО"&&apply.Enabled,"Kiosk selected action");Check(restore.Text=="Вернуть прежние настройки"&&!restore.Enabled,"No blind default restore");
                var show=typeof(InitialSystemSettingsForm).GetMethod("ShowState",BindingFlags.Instance|BindingFlags.NonPublic);
                show.Invoke(form,new object[]{"kiosk.Configured=1\nkiosk.Support=Supported\nkiosk.Backup=1\nkiosk.BackupState=Есть"});grid.CurrentCell=row.Cells[0];Check(restore.Enabled,"Valid backup enables restore");Check(Convert.ToString(row.Cells[2].Value).Contains("всех пользователей"),"Computer-wide status");
                show.Invoke(form,new object[]{"kiosk.Configured=1\nkiosk.Support=Unconfirmed\nkiosk.Backup=1"});Check(Convert.ToString(row.Cells[2].Value).Contains("не подтверждено"),"Home warning visible");
                show.Invoke(form,new object[]{"kiosk.Configured=0\nkiosk.Support=Unavailable\nkiosk.Backup=0"});grid.CurrentCell=grid.Rows[0].Cells[0];grid.CurrentCell=row.Cells[0];Check(!apply.Enabled&&!restore.Enabled,"Unsupported OS disables mutation");
                foreach(var size in new[]{new Size(1080,840),new Size(1500,900),new Size(850,1100)}){form.ClientSize=new Size((int)(size.Width*scale),(int)(size.Height*scale));Application.DoEvents();Check(apply.Width>=240&&restore.Width>=240,"Button width preserved");Check(!apply.Bounds.IntersectsWith(restore.Bounds),"Actions do not overlap");Check(grid.TouchScrollingEnabled,"Touch scrolling remains enabled");for(int tick=0;tick<3;tick++)Application.DoEvents();if(grid.ClientSize.Height>=grid.ColumnHeadersHeight+row.Height){grid.FirstDisplayedScrollingRowIndex=grid.RowCount-1;Application.DoEvents();Check(grid.GetRowDisplayRectangle(row.Index,false).Height>0,"Last setting reachable after resize");}else constrainedViewports++;}
                if(scale==1F){form.Size=new Size(1080,840);show.Invoke(form,new object[]{"kiosk.Configured=1\nkiosk.Support=Supported\nkiosk.Backup=1\nkiosk.BackupState=Есть"});grid.CurrentCell=row.Cells[0];for(int i=0;i<5;i++)Application.DoEvents();grid.FirstDisplayedScrollingRowIndex=row.Index;for(int i=0;i<5;i++)Application.DoEvents();Check(grid.GetRowDisplayRectangle(row.Index,false).Height>0,"Kiosk row reachable on final page");using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"kiosk-"+dark+".png"));}}
                form.Close();
            }
        }
        [STAThread] static int Main(){try{Application.EnableVisualStyles();Core();UI();Console.WriteLine("KioskTouchTests: "+checks+" passed; "+constrainedViewports+" synthetic oversized-DPI viewports could not fit a complete row and were not scroll-verified. In-memory registry fixtures and preview UI only; no Windows policy changed.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
    }
}