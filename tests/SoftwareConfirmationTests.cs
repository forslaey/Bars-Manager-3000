using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class SoftwareConfirmationTests
    {
        static int count;
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
        static T Field<T>(object form,string name){return (T)form.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);}
        [STAThread] static int Main()
        {
            try
            {
                Application.EnableVisualStyles();
                foreach(bool dark in new[]{false,true})using(var form=new SoftwareManagerForm(true))
                {
                    form.SetTheme(dark,false);
                    var chrome=Field<BrandCheckBox>(form,"chromeDefaults");Check(chrome.Checked,"Chrome defaults initially checked");chrome.Checked=false;
                    var defaults=Field<BrandCheckBox>(form,"defaults");var remote=Field<BrandCheckBox>(form,"remote");
                    Check(!defaults.Checked&&!remote.Checked,"Optional tasks initially unchecked");
                    var checks=Field<Dictionary<string,SoftwareCheck>>(form,"checks");
                    foreach(string id in new[]{"chrome","notepad","7zip","anydesk","rudesktop"})checks[id]=new SoftwareCheck{Id=id,State=LibraryState.Installed};
                    Check(form.InstallConfirmation(new[]{"chrome"})=="Установить или обновить выбранные программы?","Chrome only has no unrelated warnings");
                    chrome.Checked=true;Check(form.InstallConfirmation(new[]{"chrome"}).Contains("зарегистрированных им типов файлов и ссылок"),"Chrome scope explained");
                    chrome.Checked=false;Check(!form.InstallConfirmation(new[]{"chrome"}).Contains("типов файлов"),"Unchecked Chrome defaults stay absent");
                    remote.Checked=true;Check(!form.InstallConfirmation(new[]{"chrome"}).Contains("пароль"),"Remote option does not apply to Chrome");remote.Checked=false;
                    Check(!form.InstallConfirmation(new[]{"notepad","7zip"}).Contains("ассоциации"),"Unchecked associations stay absent");
                    defaults.Checked=true;Check(form.InstallConfirmation(new[]{"chrome"}).Contains("ассоциации"),"Explicit association task is explained when editors are already installed");
                    defaults.Checked=false;
                    string update=form.InstallConfirmation(new[]{"anydesk"});Check(update.Contains("Обновление AnyDesk")&&!update.Contains("RuDesktop")&&!update.Contains("пароль")&&!update.Contains("ассоциации"),"Only selected remote update warns independently of password task");
                    checks["anydesk"].State=LibraryState.Missing;Check(!form.InstallConfirmation(new[]{"anydesk"}).Contains("удалённый сеанс"),"New remote install has no existing-session update warning");
                    remote.Checked=true;Check(form.InstallConfirmation(new[]{"anydesk"}).Contains("постоянный пароль"),"Explicit password task is explained");remote.Checked=false;
                    foreach(string id in new[]{"notepad","7zip"})checks[id].State=LibraryState.Missing;
                    defaults.Checked=true;Check(!form.InstallConfirmation(new[]{"chrome"}).Contains("ассоциации"),"No association targets means no warning");
                    Check(form.InstallConfirmation(new[]{"7zip"}).Contains("ассоциации"),"New selected editor/archive install supports requested association task");
                }
                using(var reopened=new SoftwareManagerForm(true))Check(!Field<BrandCheckBox>(reopened,"defaults").Checked&&!Field<BrandCheckBox>(reopened,"remote").Checked,"Reopening does not keep optional tasks enabled");
                Console.WriteLine("SoftwareConfirmationTests: "+count+" passed. Both themes, no installations or system changes.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
