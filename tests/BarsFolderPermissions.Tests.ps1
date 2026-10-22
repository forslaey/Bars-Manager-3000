param([Parameter(Mandatory=$true)][string]$ScriptFolder)
$ErrorActionPreference='Stop'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('bars-permissions-'+[guid]::NewGuid().ToString('N'))
try {
 New-Item -ItemType Directory -Path (Join-Path $fixture 'DataKrat\nested') -Force|Out-Null
 New-Item -ItemType Directory -Path (Join-Path $fixture 'outside') -Force|Out-Null
 $root=Join-Path $fixture 'DataKrat';$child=Join-Path $root 'nested\test.txt';[IO.File]::WriteAllText($child,'fixture')
 $outside=Join-Path $fixture 'outside';$before=(Get-Acl -LiteralPath $outside).Sddl
 New-Item -ItemType Junction -Path (Join-Path $root 'outside-link') -Target $outside|Out-Null
 # Substitute discovery with one owned temporary directory: never enumerate or modify real BARS folders.
 $script=[IO.File]::ReadAllText((Join-Path $ScriptFolder 'apply-folders.ps1'))
 $start=$script.IndexOf('$roots=');$end=$script.IndexOf('function GrantBarsFolder')
 if($start -lt 0 -or $end -le $start){throw 'Discovery boundaries changed'}
 $script=$script.Substring(0,$start)+"`$roots=@('"+$root.Replace("'","''")+"');`r`n"+$script.Substring($end)
 $tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseInput($script,[ref]$tokens,[ref]$errors)|Out-Null
 if($errors.Count){throw ($errors.Message -join '; ')}
 & ([scriptblock]::Create($script))
 foreach($path in @($root,(Join-Path $root 'nested'),$child)){
  $rules=(Get-Acl -LiteralPath $path).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])
  foreach($id in @('S-1-5-32-545','S-1-5-32-544','S-1-5-18')){
   if(-not @($rules|Where-Object {$_.IdentityReference.Value -eq $id -and $_.AccessControlType -eq 'Allow' -and (($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -eq [Security.AccessControl.FileSystemRights]::FullControl)}).Count){throw ('Missing full control: '+$path+' '+$id)}
  }
 }
 # Idempotency, preservation of content and of an unrelated sibling's ACL.
 & ([scriptblock]::Create($script))|Out-Null
 if((Get-Acl -LiteralPath $outside).Sddl -ne $before -or [IO.File]::ReadAllText($child) -ne 'fixture'){throw 'Unrelated content or permissions changed'}
 $acl=Get-Acl -LiteralPath $child
 $sid=New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')
 $deny=New-Object Security.AccessControl.FileSystemAccessRule($sid,'WriteData','Deny');$acl.AddAccessRule($deny);Set-Acl -LiteralPath $child -AclObject $acl
 $rejected=$false;try{& ([scriptblock]::Create($script))|Out-Null}catch{$rejected=$true}
 if(-not $rejected){throw 'Existing deny incorrectly reported as full success'}
 Write-Output 'PASS: recursive full control, repeated application, preserved content, skipped junction and unrelated ACL. Temporary fixture only.'
} finally {if(Test-Path -LiteralPath $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force}}
