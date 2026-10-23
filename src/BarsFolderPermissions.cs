using System;
namespace ServiceTool
{
    internal static class BarsFolderPermissions
    {
        internal static string Script(bool apply)
        {
            return @"$ErrorActionPreference='Stop';
$errors=0;$issues=New-Object 'System.Collections.Generic.List[string]';$auditErrors=0;$auditSkipped=0;
$roots=New-Object 'System.Collections.Generic.List[string]';
foreach($drive in [IO.DriveInfo]::GetDrives()){
 try{
  if(-not $drive.IsReady -or $drive.DriveType -notin @([IO.DriveType]::Fixed,[IO.DriveType]::Removable)){continue}
  foreach($name in @('DataKrat','Bars')){
   $path=Join-Path $drive.RootDirectory.FullName $name;
   if(-not [IO.Directory]::Exists($path)){continue}
   $item=Get-Item -LiteralPath $path -Force;
   if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){$auditSkipped++;'Пропущена ссылка: '+$path;continue}
   if($drive.DriveFormat -notin @('NTFS','ReFS')){$auditSkipped++;'Разрешения Windows не поддерживаются: '+$path+' ('+$drive.DriveFormat+')';continue}
   $roots.Add($path)
  }
 }catch{$auditErrors++;'Не удалось проверить диск '+$drive.Name+': '+$_.Exception.Message}
}
function GrantBarsFolder($root){
 $pending=New-Object 'System.Collections.Generic.Stack[string]';$pending.Push($root);$changed=0;$failed=0;$skipped=0;
 while($pending.Count -gt 0){
  $path=$pending.Pop();$item=$null;
  try{
   $item=Get-Item -LiteralPath $path -Force;
   if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){$skipped++;continue}
   $acl=Get-Acl -LiteralPath $path;
   $inherit=if($item.PSIsContainer){[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'}else{[Security.AccessControl.InheritanceFlags]::None};
   foreach($id in @('S-1-5-32-545','S-1-5-32-544','S-1-5-18')){
    $sid=New-Object Security.Principal.SecurityIdentifier($id);
    $rule=New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl',$inherit,'None','Allow');$acl.SetAccessRule($rule)
   }
   Set-Acl -LiteralPath $path -AclObject $acl;
   $verify=Get-Acl -LiteralPath $path;$rules=$verify.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]);
   foreach($id in @('S-1-5-32-545','S-1-5-32-544','S-1-5-18')){
    if(-not @($rules|Where-Object {$_.IdentityReference.Value -eq $id -and $_.AccessControlType -eq 'Allow' -and (($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -eq [Security.AccessControl.FileSystemRights]::FullControl)}).Count){throw ('Не подтверждены права '+$id)}
   }
   if(@($rules|Where-Object {$_.AccessControlType -eq 'Deny'}).Count){$failed++;'Есть запрещающие правила, полный доступ не гарантирован: '+$path}else{$changed++}
  }catch{$failed++;'Не удалось применить права: '+$path+' — '+$_.Exception.Message}
  if($item -and $item.PSIsContainer -and (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0)){
   try{foreach($child in Get-ChildItem -LiteralPath $path -Force){if(($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){$skipped++}else{$pending.Push($child.FullName)}}}catch{$failed++;'Не удалось прочитать содержимое: '+$path+' — '+$_.Exception.Message}
  }
 }
 'Папка: '+$root+'; права подтверждены: '+$changed+'; ошибок / ограничений: '+$failed+'; ссылок пропущено: '+$skipped;
 return $failed
}
'Count='+$roots.Count;$rootIndex=0;$auditChecked=0;$auditRestricted=0;
foreach($root in $roots){'Папка: '+$root;
'Root.'+$rootIndex+'='+$root;$rootIndex++;
" + (apply ? @"
 $results=@(GrantBarsFolder $root);$results|Where-Object {$_ -is [string]};foreach($note in $results){if($note -is [string] -and $issues.Count -lt 50){$issues.Add($note)}};$errors+= [int]$results[-1];
" : @"
 $pending=New-Object 'System.Collections.Generic.Stack[string]';$pending.Push($root);
 while($pending.Count -gt 0){
  $path=$pending.Pop();$item=$null;
  try{
   $item=Get-Item -LiteralPath $path -Force;
   if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){$auditSkipped++;continue}
   $acl=Get-Acl -LiteralPath $path;$rules=$acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]);$ok=$true;
   foreach($id in @('S-1-5-32-545','S-1-5-32-544','S-1-5-18')){
    if(-not @($rules|Where-Object {$_.IdentityReference.Value -eq $id -and $_.AccessControlType -eq 'Allow' -and (($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -eq [Security.AccessControl.FileSystemRights]::FullControl) -and (($_.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -eq 0)}).Count){$ok=$false}
   }
   if(@($rules|Where-Object {$_.AccessControlType -eq 'Deny'}).Count){$ok=$false};$auditChecked++;if(-not $ok){$auditRestricted++}
  }catch{$auditErrors++}
  if($item -and $item.PSIsContainer -and (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0)){
   try{foreach($child in Get-ChildItem -LiteralPath $path -Force -ErrorAction Stop){$pending.Push($child.FullName)}}catch{$auditErrors++}
  }
 }
") + @"
}
'Checked='+$auditChecked;'Restricted='+$auditRestricted;'Errors='+$auditErrors;'Skipped='+$auditSkipped;
" + (apply ? "if($errors -gt 0){throw ('Часть папок или файлов не обработана полностью: '+$errors+'. '+[string]::Join([Environment]::NewLine,$issues))}" : "");
        }
    }
}
