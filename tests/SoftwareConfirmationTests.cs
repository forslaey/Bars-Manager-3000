using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class SoftwareConfirmationTests
    {
        static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);}
        [STAThread] static int Main(){try{
            Application.EnableVisualStyles();
            foreach(bool dark in new[]{false,true})using(var form=new SoftwareManagerForm(true))
            {
                form.SetTheme(dark,false);var remote=Field<BrandCheckBox>(form,"remote");Check(!remote.Checked,"Remote task initially unchecked");
                var checks=Field<Dictionary<string,SoftwareCheck>>(form,"checks");foreach(string id in new[]{"chrome","notepad","7zip","anydesk","rudesktop"})checks[id]=new SoftwareCheck{Id=id,State=LibraryState.Installed};
                foreach(var ids in new[]{new[]{"chrome"},new[]{"notepad","7zip"}}){var text=form.InstallConfirmation(ids);Check(text=="Установить или обновить выбранные программы?","No defaults or unrelated warnings");}
                string update=form.InstallConfirmation(new[]{"anydesk"});Check(update.Contains("Обновление AnyDesk")&&!update.Contains("RuDesktop")&&!update.Contains("пароль")&&!update.Contains("ассоциации"),"Only actual remote update warns");
                checks["anydesk"].State=LibraryState.Missing;Check(!form.InstallConfirmation(new[]{"anydesk"}).Contains("удалённый сеанс"),"New install does not warn about existing session");
                remote.Checked=true;Check(form.InstallConfirmation(new[]{"anydesk"}).Contains("постоянный пароль"),"Selected remote task warns");Check(!form.InstallConfirmation(new[]{"chrome"}).Contains("пароль"),"Remote task never affects Chrome");
                Check(!ManagerSystemActions.Known("software-defaults"),"Removed privileged defaults action is rejected");
                typeof(SoftwareManagerForm).GetMethod("OpenDefaultApps",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);Check(true,"Preview cannot launch Windows settings");
            }
            Console.WriteLine("SoftwareConfirmationTests: "+count+" passed; remote warnings and removed association action, no installations or settings changes.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
    }
}