using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Xml;
using Microsoft.Win32;

namespace ServiceTool
{
    // Machine policies only: never use the elevated administrator's HKCU as the kiosk profile.
    internal static class KioskTouchSettings
    {
        internal const string EdgePath=@"SOFTWARE\Policies\Microsoft\Windows\EdgeUI";
        internal const string CenterPath=@"SOFTWARE\Policies\Microsoft\Windows\Explorer";
        internal const string BackupPath=@"SOFTWARE\BARS\Manager\KioskTouch";
        internal const string BackupName="OriginalSettingsV1";
        internal static readonly string[] Paths={EdgePath,CenterPath};
        internal static readonly string[] Names={"AllowEdgeSwipe","DisableNotificationCenter"};
        internal static readonly int[] Desired={0,1};
        internal sealed class Entry
        {
            internal bool Exists; internal RegistryValueKind Kind; internal object Value;
            internal static Entry Missing(){return new Entry();}
            internal static Entry Dword(int value){return new Entry{Exists=true,Kind=RegistryValueKind.DWord,Value=value};}
        }
        internal interface Store
        {
            Entry Read(string path,string name);
            void Write(string path,string name,Entry entry);
        }
        internal sealed class MachineStore : Store, IDisposable
        {
            private readonly RegistryKey root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,Environment.Is64BitOperatingSystem?RegistryView.Registry64:RegistryView.Registry32);
            public Entry Read(string path,string name)
            {
                using(var key=root.OpenSubKey(path,false))
                {
                    if(key==null||!key.GetValueNames().Contains(name,StringComparer.OrdinalIgnoreCase))return Entry.Missing();
                    return new Entry{Exists=true,Kind=key.GetValueKind(name),Value=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames)};
                }
            }
            public void Write(string path,string name,Entry entry)
            {
                if(!entry.Exists){using(var key=root.OpenSubKey(path,true)){if(key!=null)key.DeleteValue(name,false);}return;}
                if(path==BackupPath)
                {
                    var security=new RegistrySecurity();security.SetAccessRuleProtection(true,false);
                    foreach(string sid in new[]{"S-1-5-18","S-1-5-32-544"})security.AddAccessRule(new RegistryAccessRule(new SecurityIdentifier(sid),RegistryRights.FullControl,InheritanceFlags.ContainerInherit,PropagationFlags.None,AccessControlType.Allow));
                    using(var key=root.CreateSubKey(path,RegistryKeyPermissionCheck.ReadWriteSubTree,RegistryOptions.None,security))
                    {key.SetAccessControl(security);key.SetValue(name,entry.Value,entry.Kind);}return;
                }
                using(var key=root.CreateSubKey(path)){key.SetValue(name,entry.Value,entry.Kind);}
            }
            public void Dispose(){root.Dispose();}
        }
        internal sealed class Platform
        {
            internal string Edition; internal bool Client, Supported;
            internal static Platform Detect(Store store)
            {
                const string path=@"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
                string edition=Convert.ToString(store.Read(path,"EditionID").Value),type=Convert.ToString(store.Read(path,"InstallationType").Value);
                int build;Int32.TryParse(Convert.ToString(store.Read(path,"CurrentBuildNumber").Value),out build);
                bool client=type.Equals("Client",StringComparison.OrdinalIgnoreCase)&&build>=10240;
                bool supported=new[]{"Professional","Enterprise","Education","IoTEnterprise"}.Any(p=>edition.StartsWith(p,StringComparison.OrdinalIgnoreCase));
                return new Platform{Edition=edition,Client=client,Supported=client&&supported&&build>=14393};
            }
        }
        internal static bool Same(Entry a,Entry b)
        {
            if(a.Exists!=b.Exists)return false;if(!a.Exists)return true;if(a.Kind!=b.Kind)return false;
            if(a.Value is byte[]&&b.Value is byte[])return ((byte[])a.Value).SequenceEqual((byte[])b.Value);
            if(a.Value is string[]&&b.Value is string[])return ((string[])a.Value).SequenceEqual((string[])b.Value);
            return Object.Equals(a.Value,b.Value);
        }
        private static byte[] Encode(Entry[] entries)
        {
            var doc=new XmlDocument();var root=doc.CreateElement("KioskTouchBackup");root.SetAttribute("version","1");doc.AppendChild(root);
            foreach(var entry in entries)
            {
                var node=doc.CreateElement("Value");root.AppendChild(node);node.SetAttribute("exists",entry.Exists?"1":"0");
                if(!entry.Exists)continue;node.SetAttribute("kind",((int)entry.Kind).ToString(CultureInfo.InvariantCulture));
                if(entry.Kind==RegistryValueKind.MultiString){foreach(string value in (string[])entry.Value){var item=doc.CreateElement("Item");item.InnerText=value;node.AppendChild(item);}}
                else if(entry.Kind==RegistryValueKind.Binary||entry.Kind==RegistryValueKind.None)node.InnerText=Convert.ToBase64String((byte[])entry.Value);
                else node.InnerText=Convert.ToString(entry.Value,CultureInfo.InvariantCulture);
            }
            return Encoding.UTF8.GetBytes(doc.OuterXml);
        }
        private static Entry[] Decode(Entry snapshot)
        {
            if(!snapshot.Exists)return null;
            try
            {
                if(snapshot.Kind!=RegistryValueKind.Binary||!(snapshot.Value is byte[])||((byte[])snapshot.Value).Length>65536)throw new InvalidDataException();
                var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536};var doc=new XmlDocument{XmlResolver=null};
                using(var reader=XmlReader.Create(new MemoryStream((byte[])snapshot.Value),settings))doc.Load(reader);
                var root=doc.DocumentElement;if(root.Name!="KioskTouchBackup"||root.GetAttribute("version")!="1"||root.ChildNodes.Count!=2)throw new InvalidDataException();
                var result=new List<Entry>();
                foreach(XmlElement node in root.ChildNodes)
                {
                    if(node.Name!="Value"||(node.GetAttribute("exists")!="0"&&node.GetAttribute("exists")!="1"))throw new InvalidDataException();
                    if(node.GetAttribute("exists")=="0"){result.Add(Entry.Missing());continue;}
                    var kind=(RegistryValueKind)Int32.Parse(node.GetAttribute("kind"),CultureInfo.InvariantCulture);object value;
                    switch(kind)
                    {
                        case RegistryValueKind.DWord:value=Int32.Parse(node.InnerText,CultureInfo.InvariantCulture);break;
                        case RegistryValueKind.QWord:value=Int64.Parse(node.InnerText,CultureInfo.InvariantCulture);break;
                        case RegistryValueKind.Binary:case RegistryValueKind.None:value=Convert.FromBase64String(node.InnerText);break;
                        case RegistryValueKind.MultiString:value=node.ChildNodes.Cast<XmlElement>().Select(e=>e.InnerText).ToArray();break;
                        case RegistryValueKind.String:case RegistryValueKind.ExpandString:value=node.InnerText;break;
                        default:throw new InvalidDataException();
                    }
                    result.Add(new Entry{Exists=true,Kind=kind,Value=value});
                }
                return result.ToArray();
            }
            catch(Exception ex){throw new InvalidDataException("Режим КСО: сохранённая конфигурация повреждена. Изменения не выполнены; прежнюю копию не перезаписываем.",ex);}
        }
        internal static string Query(Store store,Platform platform)
        {
            bool configured=Names.Select((name,i)=>Same(store.Read(Paths[i],name),Entry.Dword(Desired[i]))).All(x=>x);
            var snapshot=store.Read(BackupPath,BackupName);bool backup=false;string backupState="Нет";
            try{backup=Decode(snapshot)!=null;backupState=backup?"Есть":"Нет";}catch(InvalidDataException){backupState="Повреждена";}
            return "kiosk.Configured="+(configured?"1":"0")+"\nkiosk.Support="+(!platform.Client?"Unavailable":platform.Supported?"Supported":"Unconfirmed")+"\nkiosk.Backup="+(backup?"1":"0")+"\nkiosk.BackupState="+backupState+"\nkiosk.Edition="+platform.Edition;
        }
        internal static bool Change(Store store,Platform platform,bool restore)
        {
            if(!restore&&!platform.Client)throw new InvalidOperationException("Режим КСО: эта функция предназначена для Windows 10/11 с рабочим столом.");
            Entry[] before=Paths.Select((path,i)=>store.Read(path,Names[i])).ToArray();
            Entry[] original=Decode(store.Read(BackupPath,BackupName));
            if(restore&&original==null)throw new InvalidOperationException("Режим КСО: сохранённых прежних настроек нет. Ничего не изменено.");
            if(restore)
            {
                for(int i=0;i<2;i++)if(!Same(before[i],Entry.Dword(Desired[i]))&&!Same(before[i],original[i]))throw new InvalidOperationException("Режим КСО: параметры изменены вне менеджера. Автоматическое восстановление остановлено, чтобы не затереть эти изменения.");
            }
            else if(original==null)
            {
                var snapshot=new Entry{Exists=true,Kind=RegistryValueKind.Binary,Value=Encode(before)};
                Decode(snapshot); // Reject an unrepresentable backup before touching any policy.
                store.Write(BackupPath,BackupName,snapshot);
                if(!Same(snapshot,store.Read(BackupPath,BackupName)))throw new IOException("Режим КСО: прежние настройки не удалось сохранить. Политики не изменены.");
            }
            bool changed=false;
            for(int i=0;i<2;i++)
            {
                var target=restore?original[i]:Entry.Dword(Desired[i]);
                if(!Same(before[i],target)){store.Write(Paths[i],Names[i],target);changed=true;}
                if(!Same(store.Read(Paths[i],Names[i]),target))throw new IOException("Режим КСО: Windows не подтвердила запись. Сохранённые прежние настройки оставлены для восстановления.");
            }
            if(restore){store.Write(BackupPath,BackupName,Entry.Missing());if(store.Read(BackupPath,BackupName).Exists)throw new IOException("Режим КСО: прежние значения восстановлены, но сохранённую копию не удалось закрыть. Повтори восстановление.");}
            return changed;
        }
        internal static LibraryInstallResult Run(string operation,Action<string> progress)
        {
            if(operation!="query"&&operation!="apply"&&operation!="defaults")throw new InvalidDataException("Неизвестная операция КСО");
            using(var store=new MachineStore())
            {
                var platform=Platform.Detect(store);if(operation=="query")return new LibraryInstallResult{ExitCode=0,Message=Query(store,platform)};
                using(var gate=new Mutex(false,@"Global\BARS.Manager.KioskTouch.V1"))
                {
                    bool owned=false;try{try{owned=gate.WaitOne(0);}catch(AbandonedMutexException){owned=true;}if(!owned)throw new InvalidOperationException("Режим КСО уже изменяется другим экземпляром менеджера.");
                        bool changed=Change(store,platform,operation=="defaults");
                        string message=(operation=="defaults"?"Прежние настройки КСО восстановлены.":"Параметры КСО сохранены для всех пользователей компьютера.");
                        if(!platform.Supported&&operation=="apply")message+=" В этой редакции Windows действие политик не подтверждено: проверь жесты после перезагрузки.";
                        if(progress!=null)progress(message);
                        return new LibraryInstallResult{ExitCode=changed?3010:0,Message=message+(changed?"\nBARS_RESTART_REQUIRED":"")+"\n"+Query(store,platform)};
                    }finally{if(owned)gate.ReleaseMutex();}
                }
            }
        }
    }
}