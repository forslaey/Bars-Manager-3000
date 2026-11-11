using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class SoftwareMaintenanceTests
 {
  private static int count;
  private static void Check(bool value,string text){count++;if(!value)throw new Exception(text);}
  private static void Reject(Action action,string text){try{action();}catch(IOException){count++;return;}catch(InvalidDataException){count++;return;}throw new Exception(text);}
  private static T Field<T>(object o,string name){return (T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
  private static void Call(object o,string name,params object[] args){o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,args);}
  private static void Show(Form form){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=form is SoftwareRemoteDialog ? Screen.PrimaryScreen.WorkingArea.Location : new Point(-20000,-20000);form.Show();Application.DoEvents();}
  [STAThread]internal static int Main(string[] args)
  {
   if(args.Length==1&&args[0]=="--linger"){System.Threading.Thread.Sleep(30000);return 0;}
   string root=Path.Combine(Path.GetTempPath(),"bars-software-clean-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   try
   {
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
    var clock=Stopwatch.StartNew();int ended=SoftwareMaintenance.RunRemoval(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--linger"){UseShellExecute=false,CreateNoWindow=true},s=>{},()=>clock.ElapsedMilliseconds>=300);
    Check(ended==0&&clock.Elapsed.TotalSeconds<12,"Resident uninstaller job ends after stable removal confirmation");
    bool userCancelled=false;LibraryCancellation.Current=()=>true;try{SoftwareMaintenance.RunRemoval(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--linger"){UseShellExecute=false,CreateNoWindow=true},s=>{},()=>true);}catch(OperationCanceledException){userCancelled=true;}finally{LibraryCancellation.Current=null;}
    Check(userCancelled,"Explicit user cancellation is never converted to success");
    string profile=Path.Combine(root,"profile"),store=Path.Combine(root,"saved");Directory.CreateDirectory(Path.Combine(profile,"backup"));File.WriteAllText(Path.Combine(profile,"backup","unsaved.txt"),"unsaved fixture");
    SoftwareMaintenance.PreserveBackupDirectory(profile,store,s=>{});SoftwareMaintenance.PreserveBackupDirectory(profile,store,s=>{});
    Check(!Directory.Exists(Path.Combine(profile,"backup"))&&Directory.GetFiles(store,"unsaved.txt",SearchOption.AllDirectories).Length==1,"Unsaved text preserved before uninstaller; repeat is idempotent");
    var package=new SoftwarePackage{Id="notepad",Version="8.9.8.1"};
    Check(SoftwareMaintenance.CanInstall(new SoftwareCheck{State=LibraryState.Missing},package),"Missing app can install");
    Check(!SoftwareMaintenance.CanInstall(new SoftwareCheck{State=LibraryState.Installed,Version="8.9.8.1"},package),"Same installed version blocks install");
    Check(!SoftwareMaintenance.CanInstall(new SoftwareCheck{State=LibraryState.Installed,Version="9.0"},package),"Newer installed version blocks downgrade");
    Check(SoftwareMaintenance.CanInstall(new SoftwareCheck{State=LibraryState.Installed,Version="8.8"},package),"Only newer candidate enables update");
    Check(!SoftwareMaintenance.CanInstall(new SoftwareCheck{State=LibraryState.Unknown},package),"Uncertain inventory cannot install");
    Check(!SoftwareMaintenance.CanInstall(new SoftwareCheck{State=LibraryState.Installed,Version="9.8.2"},new SoftwarePackage{Id="anydesk",Version="0.0"}),"Unknown download version cannot pretend to be update");
    foreach(string id in new[]{"notepad","7zip","anydesk","rudesktop"})
    {
     var command=SoftwareMaintenance.RemovalCommand(id,@"C:\fixture with spaces\app.exe","{12345678-1234-1234-1234-123456789ABC}",@"C:\fixture with spaces\remove.log");
     Check(!command.UseShellExecute,"No command shell for removal");
     if(id=="rudesktop")Check(command.Arguments.Contains("/x \"{12345678-1234-1234-1234-123456789abc}\" /qn /norestart REBOOT=ReallySuppress"),"MSI exact product, silent, no reboot");
     else Check(command.Arguments==(id=="anydesk"?"--remove":"/S"),"Documented removal switch "+id);
    }
    Reject(()=>SoftwareMaintenance.RemovalCommand("rudesktop",@"C:\fixture\app.exe","not-a-product-code","log"),"MSI requires valid product code");
    string settings=Path.Combine(root,"AppSettings"),other=Path.Combine(root,"Other");Directory.CreateDirectory(Path.Combine(settings,"nested"));Directory.CreateDirectory(other);File.WriteAllText(Path.Combine(other,"keep.txt"),"keep");string readonlyFile=Path.Combine(settings,"nested","settings.ini");File.WriteAllText(readonlyFile,"inert fixture");File.SetAttributes(readonlyFile,FileAttributes.ReadOnly);
    Reject(()=>SoftwareMaintenance.DeleteSettings(root,root),"Parent itself cannot be erased");
    Reject(()=>SoftwareMaintenance.DeleteSettings(other,settings),"Outside target cannot be erased");
    SoftwareMaintenance.DeleteSettings(settings,root);Check(!Directory.Exists(settings)&&File.Exists(Path.Combine(other,"keep.txt")),"Only designated settings tree removed, unrelated data intact");
    foreach(bool dark in new[]{false,true})
    {
     using(var form=new SoftwareManagerForm(true))
     {
      form.SetTheme(dark,false);Show(form);Call(form,"ToggleSelection",1);
      Check(!Field<BrandButton>(form,"install").Enabled&&Field<BrandButton>(form,"remove").Enabled&&!Field<BrandButton>(form,"access").Enabled,"Current text editor removable but not reinstallable or remote-configurable");
      Call(form,"ToggleSelection",1);var checks=Field<Dictionary<string,SoftwareCheck>>(form,"checks");var packages=Field<Dictionary<string,SoftwarePackage>>(form,"packages");checks["anydesk"]=new SoftwareCheck{Id="anydesk",State=LibraryState.Installed,Version="9.8.2",Detail="Установлена · x86"};packages["anydesk"].Version="9.8.2";Call(form,"UpdateRows");Call(form,"ToggleSelection",3);
      Check(!Field<BrandButton>(form,"install").Enabled&&Field<BrandButton>(form,"remove").Enabled&&Field<BrandButton>(form,"access").Enabled,"Already installed remote app enables delete/access but not same-version installation");
      packages["anydesk"].Version="9.8.3";Call(form,"UpdateRows");Check(Field<BrandButton>(form,"install").Enabled,"Genuine remote-app update available");Call(form,"ToggleSelection",3);Check(!Field<BrandButton>(form,"remove").Enabled&&!Field<BrandButton>(form,"access").Enabled,"Cleared selection immediately disables actions");form.Close();
     }
     using(var form=new WindowsFeaturesForm(true))
     {
      form.SetTheme(dark,false);Show(form);var states=Field<Dictionary<string,string>>(form,"states");foreach(string key in states.Keys.ToArray())states[key]="Enabled";Call(form,"ApplyStates");var action=Field<BrandButton>(form,"action");Check(action.Text=="Удалить выбранные"&&!action.Enabled,"Installed Windows components show remove caption even without selection; no implicit removal");form.Close();
     }
     foreach(string id in new[]{"anydesk","rudesktop"})using(var dialog=new SoftwareRemoteDialog(ThemePalette.Create(dark),null,id))
     {
      Show(dialog);string previews=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"previews");Directory.CreateDirectory(previews);using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(previews,"password-"+id+"-"+(dark?"dark":"light")+".png"));}dialog.Close();
     }
    }
    Check(!SoftwareRemoteDialog.PasswordsMatch("short","short")&&!SoftwareRemoteDialog.PasswordsMatch("FixturePassword!","fixturePassword!")&&SoftwareRemoteDialog.PasswordsMatch("FixturePassword!","FixturePassword!"),"Confirmation validates length and exact case-sensitive equality");
    Console.WriteLine("PASS: "+count+" software maintenance command/version/UI and temporary settings-cleanup checks. No installed app or real setting changed.");return 0;
   }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
   finally{if(Directory.Exists(root))Directory.Delete(root,true);}
  }
 }
}
