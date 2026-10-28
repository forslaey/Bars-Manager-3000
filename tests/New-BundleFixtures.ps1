param([string]$OutputDirectory=(Join-Path $PSScriptRoot '..\work\package-fixtures'))
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $root -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach($item in @(@('old','8.0.1','x64'),@('new','8.0.2','x64'),@('otherarch','8.0.2','x86'),@('otherbranch','9.0.1','x64'))){
 $name=$item[0];$v=$item[1];$arch=$item[2]
 $text='using System.Reflection; [assembly:AssemblyProduct("Microsoft .NET Runtime '+$v+' ('+$arch+')")] [assembly:AssemblyVersion("'+$v+'.0")] [assembly:AssemblyFileVersion("'+$v+'.0")] class Fixture {static int Main(){return 0;}}'
 $file=Join-Path $root ($name+'.cs');[IO.File]::WriteAllText($file,$text)
 & $csc /nologo /target:exe (('/platform:')+$arch) (('/out:')+(Join-Path $root ($name+'.exe'))) $file
 if($LASTEXITCODE -ne 0){throw 'EXE fixture compilation failed'}
}
function Call($object,$name,$arguments){$object.GetType().InvokeMember($name,[Reflection.BindingFlags]::InvokeMethod,$null,$object,$arguments)}
function SetProperty($object,$name,$index,$value){$object.GetType().InvokeMember($name,[Reflection.BindingFlags]::SetProperty,$null,$object,@($index,$value))|Out-Null}
$installer=New-Object -ComObject WindowsInstaller.Installer
try{
 foreach($item in @(@('old','1.0.0','Intel','{11111111-1111-1111-1111-111111111111}'),@('new','1.1.0','Intel','{11111111-1111-1111-1111-111111111111}'),@('otherarch','1.1.0','x64','{11111111-1111-1111-1111-111111111111}'),@('otherproduct','1.1.0','Intel','{22222222-2222-2222-2222-222222222222}'))){
  $path=Join-Path $root ($item[0]+'.msi');if(Test-Path $path){Remove-Item -LiteralPath $path}
  $db=$installer.OpenDatabase($path,3)
  try{
   $view=Call $db 'OpenView' @('CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) LOCALIZABLE PRIMARY KEY `Property`)');try{Call $view 'Execute' @()|Out-Null}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)}
   $properties=@{ProductName='BARS inert metadata fixture';ProductVersion=$item[1];ProductCode=([guid]::NewGuid().ToString('B').ToUpperInvariant());UpgradeCode=$item[3]}
   foreach($key in $properties.Keys){$sql="INSERT INTO ``Property`` (``Property``, ``Value``) VALUES ('$key', '$($properties[$key])')";$view=Call $db 'OpenView' @($sql);try{Call $view 'Execute' @()|Out-Null}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)}}
   $summary=$db.GetType().InvokeMember('SummaryInformation',[Reflection.BindingFlags]::GetProperty,$null,$db,@(20));try{SetProperty $summary 'Property' 7 ($item[2]+';1033');Call $summary 'Persist' @()|Out-Null}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary)}
   Call $db 'Commit' @()|Out-Null
  }finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)}
 }
}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)}
Write-Output "Generated inert EXE/MSI metadata fixtures: $root. Never executed or installed."
