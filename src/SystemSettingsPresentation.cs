using System;
using System.Collections.Generic;
using System.Linq;
namespace ServiceTool
{
    internal static class SystemSettingsPresentation
    {
        private static string Get(IDictionary<string,string> values,string key){string value;return values.TryGetValue(key,out value)?value:"";}
        internal static string Enabled(string value)
        {return value.Equals("True",StringComparison.OrdinalIgnoreCase)||value=="1"?"включено":value.Equals("False",StringComparison.OrdinalIgnoreCase)||value=="0"?"отключено":"не удалось проверить";}
        internal static string Status(string group,IDictionary<string,string> values)
        {
            if(values.ContainsKey(group+".Error"))return "Проверка не выполнена\nПодробности в журнале";
            if(group=="kiosk")
            {
                string support=Get(values,"kiosk.Support"),state=Get(values,"kiosk.Configured");
                if(support=="Unavailable")return "Недоступно в этой версии Windows";
                string status=state=="1"?"Ограничения записаны для всех пользователей":state=="0"?"Ограничения КСО не заданы полностью":"Не удалось проверить настройки КСО";
                if(support=="Unconfirmed")status+="\nДействие в этой редакции не подтверждено";
                else if(state=="1")status+="\nПроверь жесты после перезагрузки";
                return status;
            }
            if(group=="admin")return Capital(Enabled(Get(values,"admin.EnableLUA")));
            if(group=="firewall")return "Доменный: "+Enabled(Get(values,"firewall.Domain"))+"\nЧастный: "+Enabled(Get(values,"firewall.Private"))+"\nОбщедоступный: "+Enabled(Get(values,"firewall.Public"));
            if(group=="defender"){
                string v=Get(values,"defender.RealTimeProtection");
                return v.Equals("True",StringComparison.OrdinalIgnoreCase)?"Защита в реальном времени включена":v.Equals("False",StringComparison.OrdinalIgnoreCase)?"Защита в реальном времени отключена":"Не удалось проверить защиту";
            }
            if(group=="smartscreen")return Get(values,"smartscreen.Mode").Length>0?Get(values,"smartscreen.Mode"):"Не удалось проверить";
            if(group=="passwords")
            {
                bool different=false,unknown=false;
                foreach(var p in InitialSystemSettings.PasswordPolicies){string v=Get(values,"passwords."+p.Name);if(p.Modern&&(v=="Недоступно в этой Windows"))continue;if(v.Length==0){unknown=true;continue;}if(p.Name=="MaximumPasswordAge"&&v=="-1")v="0";if(v!=InitialSystemSettings.Value(p.Bars))different=true;}
                return (unknown?"Политики проверены не полностью":different?"Политики отличаются от профиля BARS":"Политики BARS применены")+"\nПодробности в журнале";
            }
            if(group=="folders")
            {
                int count,checkedCount,limited,errors,skipped;
                if(!Int32.TryParse(Get(values,"folders.Count"),out count))return "Не удалось проверить права\nПодробности в журнале";
                if(count==0)return (Get(values,"folders.Errors")!="0"?"Не удалось проверить все диски":Get(values,"folders.Skipped")!="0"?"Найденные папки не поддерживают проверку прав":"Папки DataKrat и Bars не найдены")+"\nПодробности в журнале";
                bool known=Int32.TryParse(Get(values,"folders.Checked"),out checkedCount)&Int32.TryParse(Get(values,"folders.Restricted"),out limited)&Int32.TryParse(Get(values,"folders.Errors"),out errors)&Int32.TryParse(Get(values,"folders.Skipped"),out skipped);
                return (known&&checkedCount>0&&limited==0&&errors==0&&skipped==0?"Полные права на папки выданы":known&&limited>0?"Полные права выданы не на всё содержимое":"Полный доступ подтверждён не для всех объектов")+"\nПодробности в журнале";
            }
            return "Не проверено";
        }
        private static string Capital(string value){return Char.ToUpper(value[0])+value.Substring(1);}
        internal static string Error(string text)
        {
            text=text??"";
            if(text.StartsWith("Режим КСО",StringComparison.Ordinal))return text;
            if(text.IndexOf("недоступ",StringComparison.OrdinalIgnoreCase)>=0||text.IndexOf("not recognized",StringComparison.OrdinalIgnoreCase)>=0)return "Эта функция недоступна в текущей конфигурации Windows.";
            if(text.IndexOf("denied",StringComparison.OrdinalIgnoreCase)>=0||text.IndexOf("доступ",StringComparison.OrdinalIgnoreCase)>=0)return "Windows не предоставила доступ к настройке. Проверь права администратора и ограничения организации.";
            if(text.IndexOf("Defender",StringComparison.OrdinalIgnoreCase)>=0)return "Защитник Windows не подтвердил изменение. Возможны ограничения политики или защита от изменений.";
            if(text.IndexOf("отмен",StringComparison.OrdinalIgnoreCase)>=0)return "Действие отменено.";
            return "Windows не подтвердила результат. Повтори проверку; настройки могли быть ограничены политиками системы.";
        }
        internal static IEnumerable<string> Details(string group,IDictionary<string,string> values)
        {
            if(group=="kiosk")
            {
                yield return "Настройки КСО применяются ко всему компьютеру, независимо от учётной записи.";
                string backup=Get(values,"kiosk.BackupState");if(backup.Length>0)yield return "Копия прежних настроек: "+backup.ToLowerInvariant()+".";
                if(Get(values,"kiosk.Support")=="Unconfirmed")yield return "Редакция Windows не входит в документированный список поддержки этих политик. Запись в реестр не подтверждает блокировку жестов.";
                yield return "Политики организации могут изменить эти параметры. Баннеры уведомлений и сочетания клавиш этим режимом не отключаются.";
            }
            if(group=="passwords"&&!values.ContainsKey("passwords.Error"))foreach(var p in InitialSystemSettings.PasswordPolicies)
            {
                string v=Get(values,"passwords."+p.Name);if(v=="-1"&&p.Name=="MaximumPasswordAge")v="0";
                if(p.Name=="PasswordComplexity"||p.Name=="ClearTextPassword"||p.Name=="RelaxMinimumPasswordLengthLimits")v=Enabled(v);
                yield return p.Title+": "+(v.Length>0?v:"не удалось проверить");
            }
            if(group=="folders")
            {
                foreach(var item in values.Where(p=>p.Key.StartsWith("folders.Root.",StringComparison.Ordinal)))yield return "Проверена папка: "+item.Value;
                if(values.ContainsKey("folders.Checked"))yield return "Проверено объектов: "+Get(values,"folders.Checked")+"; с ограничениями: "+Get(values,"folders.Restricted")+"; не удалось проверить: "+Get(values,"folders.Errors")+"; пропущено ссылок и неподдерживаемых папок: "+Get(values,"folders.Skipped");
            }
        }
    }
}
