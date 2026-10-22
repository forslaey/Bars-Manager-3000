using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool {
 internal static class NewSettingsTests {
  static int count;
  static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;}
  static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(value);}
  static IEnumerable<Control> All(Control p){foreach(Control c in p.Controls){yield return c;foreach(var n in All(c))yield return n;}}
  static void Show(Form form){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-22000,-22000);form.Show();Application.DoEvents();}
  static void Capture(Form form,string name){using(var b=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name+".png"));}}
  [STAThread] public static int Main(){try{
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"query-all.ps1"),InitialSystemSettings.QueryScript(),new System.Text.UTF8Encoding(true));
   Check(InitialSystemSettings.AdminPolicies.Length==14,"All 14 screenshot policies");Check(InitialSystemSettings.PasswordPolicies.Length==8,"All 8 password policies");
   Check(InitialSystemSettings.AdminPolicies.Single(v=>v.Name=="TypeOfAdminApprovalMode").Bars==1,"Legacy mode has value 1, not 0");
   foreach(string group in new[]{"admin","passwords","firewall","defender","smartscreen"})foreach(string mode in new[]{"query","apply","defaults"}){
    string script=InitialSystemSettings.Script(mode,group);File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,mode+"-"+group+".ps1"),script,new System.Text.UTF8Encoding(true));
    Check(!script.Contains("Stop-Service")&&!script.Contains("shutdown")&&!script.Contains("Add-LocalGroupMember"),"No implicit service stopping, restart, membership changes");
    if(mode=="query")Check(!script.Contains("Set-MpPreference")&&!script.Contains("Set-NetFirewallProfile")&&!script.Contains("/configure"),"Query does not apply settings");
    if(group=="firewall"&&mode!="query")Check(script.Contains("Domain,Private,Public")&&script.Contains("ActiveStore"),"All profiles checked effectively");
    if(group=="defender"&&mode!="query")Check(script.Contains("RealTimeProtectionEnabled -ne")&& !script.Contains("DisableTamperProtection"),"Defender verified without tamper bypass");
   }
   foreach(string mode in new[]{"query","apply"})File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,mode+"-folders.ps1"),InitialSystemSettings.Script(mode,"folders"),new System.Text.UTF8Encoding(true));
   string folderScript=InitialSystemSettings.Script("apply","folders");Check(folderScript.Contains("S-1-5-32-545")&&folderScript.Contains("FullControl")&&folderScript.Contains("ReparsePoint"),"Users full control and reparse point exclusion");
   Check(!folderScript.Contains("takeown")&&!folderScript.Contains("SetOwner"),"No ownership takeover");
   string pass=InitialSystemSettings.Script("defaults","passwords");Check(pass.Contains("defltbase.inf")&&!pass.Contains("/cfg (Join-Path $env:windir"),"Only whitelisted password values extracted from Windows factory template");
   var chrome=SoftwareSources.ParseChrome("{\"versions\":[{\"version\":\"156.0.8078.48\"},{\"version\":\"155.0.1.9\"}]}");Check(chrome.Version=="156.0.8078.48"&&chrome.Architecture=="x64","Google stable metadata");
   Check(SoftwareCatalog.Apps[0].Id=="chrome"&&SoftwareCatalog.Apps.Length==5,"Chrome first");
   Check(SoftwareSources.Allowed("chrome",new Uri(chrome.Url)),"Official Google package allowed");Check(!SoftwareSources.Allowed("chrome",new Uri("https://dl.google.com.evil.example/dl/chrome/install/googlechromestandaloneenterprise64.msi")),"Lookalike domain rejected");
   Check(SoftwareCatalog.PackageId(chrome.File)=="chrome","Chrome recognized offline");
   var command=SoftwareInstaller.Command("chrome",@"C:\fixture path\chrome.msi",@"C:\fixture path\install.log");Check(command.Arguments.Contains("/qn /norestart REBOOT=ReallySuppress"),"Silent Chrome and reboot suppression");
   Check(SoftwareMaintenance.MachineSettings("chrome").Length==0,"Chrome never cleans RuDesktop settings");
   Check(!SoftwareDetection.ShouldInstall(new SoftwareCheck{State=LibraryState.Installed,Version=chrome.Version},chrome),"Equal Chrome skipped");
   string msi=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,chrome.File);if(File.Exists(msi)){var actual=SoftwareCatalog.Describe("chrome",msi,"fixture read only");Check(actual.Architecture=="x64"&&actual.Version==chrome.Version,"Actual Google MSI metadata");SoftwareTrust.Verify(actual,false);Check(true,"Actual Google signature verified");}
   foreach(bool dark in new[]{false,true})foreach(string id in new[]{"anydesk","rudesktop"})using(var dialog=new SoftwareRemoteDialog(ThemePalette.Create(dark),null,id,false)){
    Show(dialog);var input=Field<BrandTextBox>(dialog,"input");var confirmation=Field<BrandTextBox>(dialog,"confirmation");var standard=Field<BrandCheckBox>(dialog,"standard");
    input.Text="custom123";confirmation.Text="custom123";Check(!standard.Checked,"Default password opt-in");
    standard.Checked=true;Check(dialog.Password=="Dk3651340"&&confirmation.Text==dialog.Password&&!input.Enabled&&!confirmation.Enabled,"Preset fills and disables both fields");
    Check(All(dialog).OfType<PasswordEyeButton>().All(b=>!b.Enabled),"Preset cannot expose password through eye");standard.Checked=false;Check(input.Text=="custom123"&&confirmation.Text==input.Text&&input.Enabled,"Custom password restored");
    foreach(var eye in All(dialog).OfType<PasswordEyeButton>()){eye.PerformClick();Check(eye.Revealed,"Eye reveals independently");eye.PerformClick();Check(!eye.Revealed,"Eye hides");}
    dialog.UpdateKeyboardState("en",true);Check(Field<List<Label>>(dialog,"caps").All(l=>l.Visible),"Caps Lock warnings visible for both fields");Check(Field<List<Label>>(dialog,"languages").All(l=>l.Text=="ENG"),"English indicator updates");dialog.UpdateKeyboardState("ru",false);Check(Field<List<Label>>(dialog,"caps").All(l=>!l.Visible),"Caps Lock warning clears");Check(Field<List<Label>>(dialog,"languages").All(l=>l.Text=="РУС"),"Russian indicator updates");
    Check(All(dialog).OfType<PasswordEyeButton>().Count()==2,"Two eyes");Check(SoftwareRemoteDialog.LanguageLabel("ru")=="РУС"&&SoftwareRemoteDialog.LanguageLabel("en")=="ENG","Layout labels");
    foreach(var button in All(dialog).OfType<Button>())Check(button.Parent.ClientRectangle.Contains(button.Bounds),"Dialog button fits");
    Capture(dialog,"password-"+id+"-"+dark);dialog.Close();
   }
   foreach(bool dark in new[]{false,true})foreach(Form form in new Form[]{new InitialSystemSettingsForm(true),new ManagerHub(),new SoftwareManagerForm(true)})using(form){((ManagerModuleForm)form).SetTheme(dark,false);Show(form);foreach(var b in All(form).OfType<BrandButton>()){Check(b.Parent.ClientRectangle.Contains(b.Bounds),"Button fits "+form.GetType().Name+" "+b.Text);var size=TextRenderer.MeasureText(b.Text,b.Font,new Size(Math.Max(1,b.Width-16),int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);Check(size.Height<=b.Height-8,"Button caption fits "+b.Text);}
    Capture(form,form.GetType().Name+"-"+dark);form.Close();}
   Console.WriteLine("NewSettingsTests: "+count+" passed. No system changes or installer execution.");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 }
}
