using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace ServiceTool
{
    internal static class InitialSystemSettings
    {
        internal sealed class Policy
        {
            internal string Name,Title; internal int? Bars,Default; internal bool Modern;
            internal Policy(string name,string title,int? bars,int? standard,bool modern=false){Name=name;Title=title;Bars=bars;Default=standard;Modern=modern;}
        }
        internal static readonly Policy[] AdminPolicies={
            new Policy("ConsentPromptBehaviorEnhancedAdmin","Запрос для защищённых администраторов: учётные данные на безопасном рабочем столе",1,1,true),
            new Policy("EnableVirtualization","Виртуализация сбоев записи",0,1),
            new Policy("EnableLUA","Все администраторы работают в режиме одобрения",0,1),
            new Policy("TypeOfAdminApprovalMode","Тип режима одобрения: традиционный",1,1,true),
            new Policy("EnableInstallerDetection","Обнаружение установки приложений",0,1),
            new Policy("PromptOnSecureDesktop","Безопасный рабочий стол при повышении",0,1),
            new Policy("ConsentPromptBehaviorAdmin","Запрос администратору: без запроса / стандарт: согласие для сторонних программ",0,5),
            new Policy("ConsentPromptBehaviorUser","Запрос обычному пользователю: учётные данные",3,3),
            new Policy("EnableSecureUIAPaths","UIAccess только из безопасных мест",0,1),
            new Policy("ValidateAdminCodeSignatures","Повышение только подписанных программ",0,0),
            new Policy("EnableUIADesktopToggle","UIAccess: запрос вне безопасного рабочего стола",0,0),
            new Policy("EnableFullTokenForRemoteAuth","Повышенные права при удалённом входе доменных администраторов",0,0,true),
            new Policy("RemoteAdminAllowList","Список доменных пользователей и групп для повышенного удалённого входа",null,null,true),
            new Policy("FilterAdministratorToken","Одобрение для встроенного администратора",0,0)
        };
        internal static readonly Policy[] PasswordPolicies={
            new Policy("MinimumPasswordLengthAudit","Аудит минимальной длины пароля",null,null,true),
            new Policy("PasswordHistorySize","Количество сохранённых паролей",0,0),
            new Policy("MaximumPasswordAge","Максимальный срок действия, дней (0 — бессрочно)",0,42),
            new Policy("MinimumPasswordLength","Минимальная длина пароля",0,0),
            new Policy("MinimumPasswordAge","Минимальный срок действия, дней",0,0),
            new Policy("RelaxMinimumPasswordLengthLimits","Ослабить ограничение минимальной длины",1,0,true),
            new Policy("PasswordComplexity","Требования сложности",0,0),
            new Policy("ClearTextPassword","Хранить с обратимым шифрованием",0,0)
        };
        internal static string Value(int? value){return value.HasValue?value.Value.ToString():"Не определено";}
        internal static string Script(string operation,string group)
        {
            if(operation!="query"&&operation!="apply"&&operation!="defaults")throw new InvalidDataException("Неизвестная операция настроек");
            if(group=="folders"){if(operation=="defaults")throw new InvalidDataException("Для папок нет универсального стандарта разрешений Windows");return BarsFolderPermissions.Script(operation=="apply");}
            if(group!="admin"&&group!="passwords"&&group!="firewall"&&group!="defender"&&group!="smartscreen")throw new InvalidDataException("Неизвестная группа настроек");
            bool query=operation=="query",defaults=operation=="defaults";
            var s=new StringBuilder(@"$ErrorActionPreference='Stop';
$os=Get-CimInstance Win32_OperatingSystem; $computer=Get-CimInstance Win32_ComputerSystem;
$system='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'; $sam='HKLM:\SYSTEM\CurrentControlSet\Control\SAM';
function ReadValue($path,$name){$key=Get-Item -LiteralPath $path -ErrorAction SilentlyContinue; if($key -and $key.GetValueNames() -contains $name){return [string]$key.GetValue($name)};return 'Не определено'}
function Put($path,$name,$value){if($null -eq $value){Remove-ItemProperty -LiteralPath $path -Name $name -ErrorAction SilentlyContinue}else{if(-not(Test-Path -LiteralPath $path)){New-Item -Path $path -Force | Out-Null};New-ItemProperty -LiteralPath $path -Name $name -Value $value -PropertyType DWord -Force | Out-Null};$expected=if($null -eq $value){'Не определено'}else{[string]$value};if((ReadValue $path $name) -ne $expected){throw ('Windows не подтвердила параметр '+$name)}}
$definitions=[Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes((Join-Path $env:windir 'System32\wsecedit.dll')))+(Get-Content -LiteralPath (Join-Path $env:windir 'inf\sceregvl.inf') -Raw);
function Supported($name){return $definitions.Contains($name)}
");
            if(query)s.AppendLine("'Windows='+$os.Caption; 'DomainJoined='+$computer.PartOfDomain;");
            if(group=="smartscreen")
            {
                s.AppendLine(@"if(-not(Test-Path -LiteralPath (Join-Path $env:windir 'System32\smartscreen.exe'))){throw 'SmartScreen для приложений недоступен в этой Windows'}
$policy='HKLM:\SOFTWARE\Policies\Microsoft\Windows\System';$explorer='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer';");
                if(!query)
                {
                    s.AppendLine("Put $policy 'EnableSmartScreen' "+(defaults?"$null":"0")+";");
                    s.AppendLine("Remove-ItemProperty -LiteralPath $policy -Name ShellSmartScreenLevel -ErrorAction SilentlyContinue;");
                    s.AppendLine("if(-not(Test-Path -LiteralPath $explorer)){New-Item -Path $explorer -Force|Out-Null};New-ItemProperty -LiteralPath $explorer -Name SmartScreenEnabled -Value '"+(defaults?"Warn":"Off")+"' -PropertyType String -Force|Out-Null;");
                    s.AppendLine("if((ReadValue $explorer 'SmartScreenEnabled') -ne '"+(defaults?"Warn":"Off")+"'){throw 'Windows не подтвердила настройку SmartScreen'};");
                }
                s.AppendLine("$p=ReadValue $policy 'EnableSmartScreen';$local=ReadValue $explorer 'SmartScreenEnabled';'Policy='+$p;'Local='+$local;'Mode='+$(if($p -eq '0'){'Отключён политикой'}elseif($p -eq '1'){'Включён политикой'}elseif($local -eq 'Off'){'Отключён'}else{'Включён'});");
                if(!query)s.AppendLine("'Настройка сохранена. Если окно предупреждения уже открыто, закрой его и запусти приложение заново. Доменные политики и MDM имеют приоритет.';");
            }
            else if(group=="firewall")
            {
                if(!query)s.AppendLine("Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled "+(defaults?"True":"False")+" -PolicyStore PersistentStore;");
                s.AppendLine("$profiles=@(Get-NetFirewallProfile -PolicyStore ActiveStore);if($profiles.Count -ne 3){throw 'Windows не вернула все три профиля брандмауэра'};foreach($p in $profiles){$p.Name+'='+$p.Enabled}");
                if(!query)s.AppendLine("if(@($profiles | Where-Object {[string]$_.Enabled -ne '"+(defaults?"True":"False")+"'}).Count -gt 0){throw 'Действующая политика брандмауэра отличается от запрошенной. Проверь GPO/MDM'}");
            }
            else if(group=="defender")
            {
                s.AppendLine("if(-not(Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue)){throw 'Microsoft Defender недоступен в этой Windows'}");
                if(!query)s.AppendLine("Set-MpPreference -DisableRealtimeMonitoring $"+(defaults?"false":"true")+";");
                s.AppendLine("$mp=Get-MpComputerStatus;'RealTimeProtection='+$mp.RealTimeProtectionEnabled;'TamperProtection='+$mp.IsTamperProtected;");
                if(!query)s.AppendLine("if([bool]$mp.RealTimeProtectionEnabled -ne $"+(defaults?"true":"false")+"){throw 'Defender не подтвердил изменение. Возможны защита от изменений, политика организации или другой антивирус'}");
            }
            else if(group=="admin")
            {
                foreach(var p in AdminPolicies)
                {
                    string expected=Value(defaults?p.Default:p.Bars);string value=(defaults?p.Default:p.Bars).HasValue?expected:"$null";
                    if(p.Modern)s.AppendLine("if(Supported '"+p.Name+"'){");
                    if(!query)s.AppendLine("Put $system '"+p.Name+"' "+value+";");
                    s.AppendLine("'"+p.Name+"='+(ReadValue $system '"+p.Name+"');");
                    if(p.Modern)s.AppendLine("}else{'"+p.Name+"=Недоступно в этой Windows'}");
                }
                if(!query)s.AppendLine("'BARS_RESTART_REQUIRED';");
            }
            else
            {
                if(!query)s.AppendLine("if($computer.DomainRole -ge 4){throw 'На контроллере домена локальный профиль паролей не применяется. Используй доменную политику'};");
                s.AppendLine(@"$temp=Join-Path $env:TEMP ('bars-policy-'+[guid]::NewGuid().ToString('N'));$acl=New-Object System.Security.AccessControl.DirectorySecurity; $acl.SetAccessRuleProtection($true,$false);foreach($sid in @('S-1-5-18','S-1-5-32-544')){$identity=New-Object System.Security.Principal.SecurityIdentifier($sid);$rule=New-Object System.Security.AccessControl.FileSystemAccessRule($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow');$acl.AddAccessRule($rule)};[IO.Directory]::CreateDirectory($temp,$acl)|Out-Null;
try {
$export=Join-Path $temp 'export.inf'; & (Join-Path $env:windir 'System32\secedit.exe') /export /cfg $export /areas SECURITYPOLICY /quiet | Out-Null;if($LASTEXITCODE -ne 0){throw ('Не удалось прочитать политики: '+$LASTEXITCODE)};
function ReadPolicies($file){$values=@{};$section='';foreach($line in Get-Content -LiteralPath $file){if($line -match '^\[(.+)\]'){$section=$matches[1]}elseif($section -eq 'System Access' -and $line -match '^\s*(\w+)\s*=\s*(.+)$'){$values[$matches[1]]=$matches[2].Trim()}};return $values};$before=ReadPolicies $export;
");
                if(!query)
                {
                    s.AppendLine("$desired=@{};");
                    foreach(var p in PasswordPolicies.Where(p=>!p.Modern))s.AppendLine("$desired['"+p.Name+"']="+Value(defaults?p.Default:p.Bars)+";");
                    if(!defaults)s.AppendLine("$desired['MaximumPasswordAge']=-1;"); // secedit represents unlimited age as -1, while the UI displays 0.
                    // Read the operating system's own factory template, never apply the full template.
                    if(defaults)s.AppendLine("$factory=ReadPolicies (Join-Path $env:windir 'inf\\defltbase.inf');foreach($key in @($desired.Keys)){if(-not $factory.ContainsKey($key)){throw ('Windows не предоставила стандартное значение '+$key)};$desired[$key]=$factory[$key]}");
                    s.AppendLine("$lines=@('[Unicode]','Unicode=yes','[Version]','signature=\"$CHICAGO$\"','Revision=1','[System Access]');foreach($key in $desired.Keys){$lines+=($key+' = '+$desired[$key])};$cfg=Join-Path $temp 'profile.inf';[IO.File]::WriteAllLines($cfg,$lines,[Text.Encoding]::Unicode); & (Join-Path $env:windir 'System32\\secedit.exe') /configure /db (Join-Path $temp 'profile.sdb') /cfg $cfg /areas SECURITYPOLICY /quiet | Out-Null;if($LASTEXITCODE -ne 0){throw ('Политики применены не полностью. Код '+$LASTEXITCODE)};");
                    foreach(var p in PasswordPolicies.Where(p=>p.Modern)){s.AppendLine("if(Supported '"+p.Name+"'){Put $sam '"+p.Name+"' "+((defaults?p.Default:p.Bars).HasValue?Value(defaults?p.Default:p.Bars):"$null")+"}else{'"+p.Name+"=Недоступно в этой Windows'}");}
                    s.AppendLine("& (Join-Path $env:windir 'System32\\secedit.exe') /export /cfg $export /areas SECURITYPOLICY /quiet | Out-Null;if($LASTEXITCODE -ne 0){throw 'Не удалось проверить политики после изменения'};$after=ReadPolicies $export;foreach($key in $desired.Keys){if($after[$key] -ne [string]$desired[$key]){throw ('Политика не подтверждена: '+$key)}};$before=$after;");
                }
                foreach(var p in PasswordPolicies)s.AppendLine(p.Modern?"'"+p.Name+"='+$(if(Supported '"+p.Name+"'){ReadValue $sam '"+p.Name+"'}else{'Недоступно в этой Windows'});":"'"+p.Name+"='+$before['"+p.Name+"'];");
                s.AppendLine("}finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force}};");
            }
            return s.ToString();
        }
        internal static string QueryScript(){var query=new StringBuilder();foreach(string group in new[]{"admin","passwords","firewall","defender","smartscreen","folders"})query.Append("try{$lines=& {"+Script("query",group)+"};foreach($line in $lines){'"+group+".'+$line}}catch{'"+group+".Error='+$_.Exception.Message};");return query.ToString();}
        private static LibraryInstallResult RunScript(string script,Action<string> progress,bool cancellableRead=false){const string marker="BARS_SETTINGS_ERROR=";var result=WindowsComponents.Run("$ProgressPreference='SilentlyContinue';try{"+script+"}catch{[Console]::WriteLine('"+marker+"'+$_.Exception.Message)}",progress,cancellableRead);int error=result.Message.IndexOf(marker,StringComparison.Ordinal);if(error>=0)throw new InvalidOperationException(result.Message.Substring(error+marker.Length).Trim());return result;}
        internal static LibraryInstallResult Run(string data,Action<string> progress)
        {
            var parts=(data??"").Split(':');if(parts.Length!=2)throw new InvalidDataException("Неверный запрос настроек");
            if(parts[1]=="kiosk")return KioskTouchSettings.Run(parts[0],progress);
            if(parts[0]=="query"&&parts[1]=="all")
            {
                var result=RunScript(QueryScript(),progress,true);
                try{result.Message+=Environment.NewLine+KioskTouchSettings.Run("query",null).Message;}
                catch(Exception ex){result.Message+=Environment.NewLine+"kiosk.Error="+ex.Message.Replace("\r"," ").Replace("\n"," ");}
                return result;
            }
            return RunScript(Script(parts[0],parts[1]),progress);
        }
    }
}
