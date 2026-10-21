using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ServiceTool
{
    internal static class SoftwareTests
    {
        private static int count;
        private static void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        private static void Reject(Action action,string message){try{action();}catch(Exception){count++;return;}throw new Exception(message);}
        private static IEnumerable<Control> Children(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
        private static void Show(Form form){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();}
        [STAThread] public static int Main()
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Check(SoftwareCatalog.Apps.Length==4&&!SoftwareCatalog.Apps.Any(a=>a.Id.Contains("atol")),"Only four requested programs, no ATOL");
                Check(SoftwareCatalog.Compare("26.04","26.4.0.0")==0,"Version normalization");Check(SoftwareCatalog.Compare("8.9.8.1","8.9.8")>0,"Revision comparison");Reject(()=>SoftwareCatalog.Compare("unknown","1.0"),"Unknown versions aren't guessed");
                string digest=new string('a',64),json="{\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"npp.8.9.8.1.Installer.x64.exe\",\"digest\":\"sha256:"+digest+"\",\"browser_download_url\":\"https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v8.9.8.1/npp.8.9.8.1.Installer.x64.exe\"}]}";
                var npp=SoftwareSources.ParseGitHub("notepad",json);Check(npp.Version=="8.9.8.1"&&npp.Architecture=="x64"&&npp.Sha256==digest,"Notepad++ x64 release parsed from GitHub, no website dependency");
                Reject(()=>SoftwareSources.ParseGitHub("notepad",json.Replace("\"prerelease\":false","\"prerelease\":true")),"Preview rejected");Reject(()=>SoftwareSources.ParseGitHub("notepad",json.Replace("sha256:","md5:")),"Missing digest rejected");Reject(()=>SoftwareSources.ParseGitHub("notepad",json.Replace("https://github.com/","http://github.com/")),"Non-HTTPS release rejected");Reject(()=>SoftwareSources.ParseGitHub("notepad",json.Replace("/notepad-plus-plus/notepad-plus-plus/releases/","/evil/repo/releases/")),"Wrong repo rejected");Reject(()=>SoftwareSources.ParseGitHub("notepad",json.Replace("Installer.x64.exe","Installer.arm64.exe")),"ARM package not mistaken for x64");
                foreach(string url in new[]{"http://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/a/file.exe","https://github.com.evil.test/notepad-plus-plus/notepad-plus-plus/releases/download/a/file.exe","https://user:pass@github.com/notepad-plus-plus/notepad-plus-plus/releases/download/a/file.exe","https://notepad-plus-plus.org/downloads/","https://github.com/evil/repo/releases/download/a/file.exe"})Check(!SoftwareSources.Allowed("notepad",new Uri(url)),"Unapproved source rejected");
                var ru=SoftwareSources.ParseRuDesktop("<a href='https://storage.rudesktop.ru/download/rudesktop-3.0.1563-x64.msi'>a</a><a href='https://storage.rudesktop.ru/download/rudesktop-3.0.1500-x64.msi'>b</a><a href='https://storage.rudesktop.ru/download/rudesktop-4.0.0-x32.msi'>c</a>");Check(ru.Version=="3.0.1563"&&ru.Architecture=="x64","Latest x64 RuDesktop selected; x86 ignored");Reject(()=>SoftwareSources.ParseRuDesktop("https://malicious.example/rudesktop-3.0.1563-x64.msi"),"Third-party download rejected");
                Check(SoftwareDetection.NormalizePath("\"C:\\Program Files (x86)\\AnyDesk\"")==@"C:\Program Files (x86)\AnyDesk", "Quoted InstallLocation accepted");
                Check(SoftwareDetection.NormalizePath("C:\\bad\"path")==null && SoftwareDetection.NormalizePath("relative/path")==null, "Broken registry paths don't crash inventory");
                Check(SoftwareDetection.NormalizeVersion("anydesk","ad 9.8.1")=="9.8.1", "AnyDesk vendor ARP prefix normalized without losing patch version");
                Check(SoftwareDetection.NormalizeVersion("anydesk","ad unknown")=="", "Unknown AnyDesk version stays unknown");
                var any=new SoftwarePackage {Id="anydesk",Version="9.8",Architecture="x86",Sha256=digest};Reject(()=>SoftwareCatalog.Validate(any,false),"AnyDesk x86 blocked by default");SoftwareCatalog.Validate(any,true);count++;var bad=new SoftwarePackage {Id="notepad",Version="1.0",Architecture="x86",Sha256=digest};Reject(()=>SoftwareCatalog.Validate(bad,true),"AnyDesk exception never permits x86 Notepad++");
                var local=new SoftwarePackage {Id="notepad",Version="8.9.8.1"};var old=new SoftwarePackage {Id="notepad",Version="8.8"};Check(SoftwareSources.Best(new[]{local,old},"notepad")==local,"Newest local package selected");
                Check(!SoftwareDetection.ShouldInstall(new SoftwareCheck {State=LibraryState.Installed,Version="8.9.9"},local),"No downgrade");Check(SoftwareDetection.ShouldInstall(new SoftwareCheck {State=LibraryState.Missing},local),"Missing installation planned");Reject(()=>SoftwareDetection.ShouldInstall(new SoftwareCheck {State=LibraryState.Unknown},local),"Uncertain registration blocks installation");
                var request=new SoftwareInstallRequest {Id="rudesktop",Version="3.0.1563",Sha256=digest,ConfigureRemote=true,Password="Fixture<&\"Pass\\",AllowAnyDesk32=false};Check(SoftwareInstallRequest.Parse(request.Serialize()).Password==request.Password,"Secret transported correctly through escaped XML");Reject(()=>SoftwareInstallRequest.Parse(request.Serialize().Replace("id=\"rudesktop\"","id=\"notepad\"")),"Remote settings rejected for a text editor");Reject(()=>SoftwareInstallRequest.Parse("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///C:/Windows/win.ini'>]><software>&e;</software>"),"XML external entities rejected");request.Password="bad\nsecret";Reject(()=>SoftwareInstallRequest.Parse(request.Serialize()),"Multiline password rejected");
                var command=SoftwareInstaller.Command("anydesk",@"C:\fixture\AnyDesk.exe","x.log");Check(command.Arguments.Contains("--silent")&&!command.Arguments.Contains("--install-printer"),"AnyDesk silent profile excludes printer flag");Check(command.Arguments.Contains("--update-manually"),"Updates controlled by manager");
                Check(SoftwareInstaller.AnyDeskProfile=="_unattended_access","AnyDesk password targets the documented Unattended Access profile, value only via stdin");
                command=SoftwareInstaller.Command("rudesktop",@"C:\fixture\rudesktop.msi",@"C:\logs\install.log");Check(command.Arguments.Contains("/qn /norestart REBOOT=ReallySuppress")&&command.Arguments.Contains("/L*v"),"RuDesktop silent MSI, no reboot, log");Check(SoftwareInstaller.Command("notepad","x.exe","x.log").Arguments=="/S"&&SoftwareInstaller.Command("7zip","x.exe","x.log").Arguments=="/S","Exact case-sensitive silent switches");
                string original="<DefaultAssociations><Association Identifier=\".pdf\" ProgId=\"Existing.Pdf\" ApplicationName=\"Reader\"/><Association Identifier=\".zip\" ProgId=\"Old.Zip\" ApplicationName=\"Old\"/><Association Identifier=\".txt\" ProgId=\"Old.Text\"/></DefaultAssociations>";
                var merged=SoftwareDefaults.Merge(original,new[]{"notepad","7zip"});Check(merged.SelectSingleNode("/DefaultAssociations/Association[@Identifier='.pdf']").Attributes["ProgId"].Value=="Existing.Pdf","Unrelated associations preserved");foreach(string ext in SoftwareDefaults.TextExtensions.Concat(SoftwareDefaults.ArchiveExtensions))Check(merged.SelectNodes("/DefaultAssociations/Association[@Identifier='"+ext+"']").Count==1,"Unique requested association");Check(!SoftwareDefaults.ArchiveExtensions.Intersect(new[]{".exe",".dll",".msi",".sys",".ps1",".lnk",".chm"}).Any(),"System/executable types remain untouched");Check(SoftwareDefaults.Merge(merged.OuterXml,new[]{"notepad","7zip"}).OuterXml==merged.OuterXml,"Profile merge idempotent");Reject(()=>SoftwareDefaults.Read("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///C:/Windows/win.ini'>]><DefaultAssociations>&e;</DefaultAssociations>"),"Policy XML external entities rejected");
                string fixture=@"Software\BARS\Tests\"+Guid.NewGuid().ToString("N");try{using(var key=Registry.CurrentUser.CreateSubKey(fixture)){SoftwareDefaults.Register(key,"notepad",@"C:\fixture with spaces\notepad++.exe");using(var value=key.OpenSubKey(@"Software\Classes\BARS.NotepadPlusPlus.Text\shell\open\command"))Check(Convert.ToString(value.GetValue("")).Contains("\"C:\\fixture with spaces\\notepad++.exe\" \"%1\""),"Quoted association command; no command shell");using(var mapping=key.OpenSubKey(@"Software\BARS\Manager\Defaults\notepad\Capabilities\FileAssociations"))Check(mapping.GetValueNames().Length==4,"All four text extensions registered");}}finally{Registry.CurrentUser.DeleteSubKeyTree(fixture);}
                foreach(bool dark in new[]{false,true})foreach(float scale in new[]{1F,1.25F,1.5F,2F})using(var form=new SoftwareManagerForm(true))
                {
                    form.SetTheme(dark,false);Show(form);if(scale!=1){form.Scale(new SizeF(scale,scale));Application.DoEvents();}
                    var controls=Children(form).ToArray();var boxes=controls.OfType<BrandCheckBox>().ToArray();Check(boxes.Length==7,"Four programs and three explicit settings");var exception=boxes.Single(c=>c.Text.Contains("исключение"));Check(!exception.Checked,"No implicit AnyDesk x86 consent");Check(!boxes.Single(c=>c.Text.StartsWith("AnyDesk")).Enabled,"AnyDesk row blocked until explicit exception");
                    foreach(var button in controls.OfType<BrandButton>()){Check(button.Parent.ClientRectangle.Contains(button.Bounds),"Buttons remain inside slots at all scales");Check(button.Height>=35*scale,"Useful action button height");foreach(string line in button.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries))Check(TextRenderer.MeasureText(line,button.Font).Width<button.Width-6,"Button text fits");}
                    foreach(var checkbox in boxes)Check(checkbox.Width>250*scale,"Large checkbox click area");using(var image=new Bitmap(form.Width,form.Height))form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                    controls.OfType<Button>().Single(b=>b.Text=="В меню").PerformClick();Check(form.MenuRequested&&!form.Visible,"New module returns to menu");
                }
                foreach(bool dark in new[]{false,true})using(var dialog=new SoftwareRemoteDialog(ThemePalette.Create(dark),null,"rudesktop",false)){Show(dialog);var input=Children(dialog).OfType<BrandTextBox>().Single();Check(input.UseSystemPasswordChar&&input.MaxLength==128,"Masked credential input, no live saved credentials read in tests");foreach(var button in Children(dialog).OfType<Button>())Check(button.Parent.ClientRectangle.Contains(button.Bounds),"Credential actions inside dialog");dialog.Close();}
                Console.WriteLine("PASS: "+count+" software metadata/source/version/command/policy/UI checks. No real installer, defaults policy, remote-access configuration or reboot executed.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
