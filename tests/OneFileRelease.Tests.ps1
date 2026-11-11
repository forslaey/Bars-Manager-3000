param([Parameter(Mandatory=$true)][string]$Path,[Parameter(Mandatory=$true)][version]$Version,[switch]$Signed)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Path).ProviderPath
$info=[Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if($info.ProductName -ne 'Bars Manager 3000' -or [version]$info.FileVersion -ne $Version){throw 'Unexpected version metadata'}
if($Signed -and (Get-AuthenticodeSignature -LiteralPath $exe).Status -ne 'Valid'){throw 'Invalid signature'}
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class BarsReleaseIcons {
 [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
 public static extern uint ExtractIconEx(string path,int index,IntPtr large,IntPtr small,uint count);
}
"@
if([BarsReleaseIcons]::ExtractIconEx($exe,-1,0,0,0) -lt 1){throw 'Missing EXE icon'}
$png=Join-Path ([IO.Path]::GetTempPath()) ('bars-release-preview-'+[guid]::NewGuid()+'.png')
try{
 $p=Start-Process -FilePath $exe -ArgumentList "--preview-hub `"$png`" dark" -PassThru
 try{
  if(!$p.WaitForExit(15000)){[void]$p.CloseMainWindow();throw 'Launcher timed out'}
  if($p.ExitCode -ne 0 -or !(Test-Path -LiteralPath $png)){throw 'Embedded manager did not render preview'}
 }finally{$p.Dispose()}
}finally{[IO.File]::Delete($png)}
'PASS: Rust EXE version, icon, signature when requested and embedded manager preview'