param([Parameter(Mandatory=$true)][string]$ScriptFolder)
$ErrorActionPreference='Stop'
$folder=$ScriptFolder
foreach($file in Get-ChildItem -LiteralPath $folder -Filter '*-*.ps1' | Where-Object {$_.Name -match '^(query|apply|defaults)-'}){
 $tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)|Out-Null
 if($errors.Count){throw ($file.Name+': '+($errors.Message -join '; '))}
}
function Get-CimInstance($ClassName){if($ClassName -eq 'Win32_OperatingSystem'){return [pscustomobject]@{Caption='Test Windows';ProductType=1}};return [pscustomobject]@{DomainRole=0;PartOfDomain=$false}}
$script:registry=@{}
function Get-Item($LiteralPath){if($LiteralPath -notlike 'HKLM:*'){throw 'Unexpected registry path'};$key=New-Object PSObject -Property @{Path=$LiteralPath};$key|Add-Member ScriptMethod GetValueNames {$prefix=$this.Path+'|';return @($script:registry.Keys|Where-Object {$_.StartsWith($prefix)}|ForEach-Object {$_.Substring($prefix.Length)})};$key|Add-Member ScriptMethod GetValue {param($name) return $script:registry[$this.Path+'|'+$name]};return $key}
function New-Item($Path,[switch]$Force){if($Path -notlike 'HKLM:*'){throw 'Unexpected write path'}}
function New-ItemProperty($LiteralPath,$Name,$Value,$PropertyType,[switch]$Force){$script:registry[$LiteralPath+'|'+$Name]=[string]$Value}
function Remove-ItemProperty($LiteralPath,$Name,$ErrorAction){$script:registry.Remove($LiteralPath+'|'+$Name)}
$script:profiles=@('Domain','Private','Public')|ForEach-Object {[pscustomobject]@{Name=$_;Enabled=$true}}
function Set-NetFirewallProfile($Profile,$Enabled,$PolicyStore){if(($Profile -join ',') -ne 'Domain,Private,Public'){throw 'Wrong profiles'};foreach($p in $script:profiles){$p.Enabled=[bool]::Parse($Enabled)}}
function Get-NetFirewallProfile($PolicyStore){return $script:profiles}
$script:realtime=$true;$script:blocked=$false
function Set-MpPreference($DisableRealtimeMonitoring){if(-not $script:blocked){$script:realtime=-not $DisableRealtimeMonitoring}}
function Get-MpComputerStatus {return [pscustomobject]@{RealTimeProtectionEnabled=$script:realtime;IsTamperProtected=$script:blocked}}
foreach($group in @('admin','firewall','defender','smartscreen')){foreach($mode in @('query','apply','defaults')){
 $s=[IO.File]::ReadAllText((Join-Path $folder ($mode+'-'+$group+'.ps1')))
 $result=& ([scriptblock]::Create($s))
 if($group -eq 'admin' -and $mode -eq 'apply' -and $script:registry['HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System|EnableLUA'] -ne '0'){throw 'UAC profile not applied'}
 if($group -eq 'admin' -and $mode -eq 'defaults' -and $script:registry['HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System|EnableLUA'] -ne '1'){throw 'UAC default not restored'}
 if($group -eq 'firewall' -and $mode -eq 'apply' -and @($script:profiles | Where-Object Enabled).Count -ne 0){throw 'Firewall profiles not disabled'}
 if($group -eq 'defender' -and $mode -eq 'apply' -and $script:realtime){throw 'Realtime not disabled'}
 if($group -eq 'smartscreen' -and $mode -eq 'apply' -and $script:registry['HKLM:\SOFTWARE\Policies\Microsoft\Windows\System|EnableSmartScreen'] -ne '0'){throw 'SmartScreen not disabled'}
 if($group -eq 'smartscreen' -and $mode -eq 'defaults' -and ($script:registry.ContainsKey('HKLM:\SOFTWARE\Policies\Microsoft\Windows\System|EnableSmartScreen') -or $script:registry['HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer|SmartScreenEnabled'] -ne 'Warn')){throw 'SmartScreen standard not restored'}
 Write-Output ('Mock passed: '+$mode+' '+$group)
}}
$script:blocked=$true;$script:realtime=$true;$rejected=$false
try {& ([scriptblock]::Create([IO.File]::ReadAllText((Join-Path $folder 'apply-defender.ps1'))))|Out-Null}catch{$rejected=$true}
if(-not $rejected){throw 'Tamper block falsely reported success'}
Write-Output 'Syntax of all generated Windows PowerShell scripts passed. Mock changes only; tamper rejection checked.'
# Test the password-policy template with a fake secedit command; never call the real utility.
$script:passwords=@{MinimumPasswordAge='3';MaximumPasswordAge='20';MinimumPasswordLength='9';PasswordComplexity='1';PasswordHistorySize='4';ClearTextPassword='0';LockoutBadCount='7'}
function Fake-SecEdit {
 $cfg=$args[[array]::IndexOf($args,'/cfg')+1]
 if($args[0] -eq '/export'){$lines=@('[System Access]');foreach($key in $script:passwords.Keys){$lines+=($key+' = '+$script:passwords[$key])};[IO.File]::WriteAllLines($cfg,$lines,[Text.Encoding]::Unicode)}
 elseif($args[0] -eq '/configure'){foreach($line in Get-Content -LiteralPath $cfg){if($line -match '^(\w+) = (-?\d+)$'){$script:passwords[$matches[1]]=$matches[2]}}}
 else{throw 'Unexpected fake secedit command'}
 $global:LASTEXITCODE=0
}
foreach($mode in @('query','apply','defaults')){
 $s=[IO.File]::ReadAllText((Join-Path $folder ($mode+'-passwords.ps1')))
 $s=$s.Replace("& (Join-Path `$env:windir 'System32\secedit.exe')",'Fake-SecEdit')
 $s=$s.Replace('[IO.Directory]::CreateDirectory($temp,$acl)','[IO.Directory]::CreateDirectory($temp)')
 if($s.Contains('secedit.exe')){throw 'Unmocked secedit reference'}
 & ([scriptblock]::Create($s))|Out-Null
 if($mode -eq 'apply' -and ($script:passwords.MaximumPasswordAge -ne '-1' -or $script:passwords.PasswordComplexity -ne '0')){throw 'Wrong BARS password profile'}
 if($mode -eq 'defaults' -and $script:passwords.MaximumPasswordAge -ne '42'){throw 'Factory password defaults not used'}
 if($script:passwords.LockoutBadCount -ne '7'){throw 'Unrelated policy changed'}
 Write-Output ('Mock passed: '+$mode+' passwords')
}
