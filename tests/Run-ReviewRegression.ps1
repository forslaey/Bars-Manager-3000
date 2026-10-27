[CmdletBinding()]
param([string]$BuildDirectory,[string[]]$TestNames=@('ReviewRegressionTests','UtilityModuleTests','SoftwareTests','SoftwareMaintenanceTests','SoftwareConfirmationTests','SoftwareStorageTests','Ui2332Tests','LayoutFitTests','UserGuideTests','KioskTouchTests'))
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if([string]::IsNullOrWhiteSpace($BuildDirectory)){$BuildDirectory=Join-Path $root 'work\review-regression'}
New-Item -ItemType Directory -Path $BuildDirectory -Force | Out-Null
$BuildDirectory=[IO.Path]::GetFullPath($BuildDirectory)
$version=[regex]::Match([IO.File]::ReadAllText((Join-Path $root 'src\AssemblyInfo.cs')),'AssemblyFileVersion\("([0-9.]+)"\)').Groups[1].Value
$binary=Join-Path $root ('bin\Bars Manager 3000 '+([version]$version).ToString(3)+'.exe')
if(-not(Test-Path -LiteralPath $binary)){throw 'Build the current version into bin first'}
& (Join-Path $PSScriptRoot 'WindowsFeatureCommands.Tests.ps1') -AssemblyPath $binary
$assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($binary))
$resources=@()
foreach($name in $assembly.GetManifestResourceNames()){
    $path=Join-Path $BuildDirectory $name
    $input=$assembly.GetManifestResourceStream($name);$output=[IO.File]::Create($path)
    try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()}
    $resources+='/resource:'+$path+','+$name
}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
$references=& (Join-Path $root 'tools\Prepare-References.ps1')
$args=@('/nologo','/target:exe','/platform:anycpu','/utf8output','/codepage:65001','/noconfig','/nostdlib+',('/win32manifest:'+(Join-Path $root 'src\app.manifest')))
foreach($dll in @('mscorlib','System','System.Core','System.Xml','System.Drawing','System.Windows.Forms','System.IO.Compression','System.IO.Compression.FileSystem','System.Configuration.Install','System.ServiceProcess','System.Security','System.Web.Extensions')){$args+='/reference:'+(Join-Path $references ($dll+'.dll'))}
$sources=@(Get-ChildItem (Join-Path $root 'src') -Filter *.cs -File | ForEach-Object FullName)
foreach($name in $TestNames){
    if($name -notmatch '\A[A-Za-z0-9]+Tests\z'){throw 'Invalid test name'}
    $file=Join-Path $PSScriptRoot ($name+'.cs')
    $main=if($name -in @('CoreTests','ManagementTests','ManagedServiceStoreTests')){$name}else{'ServiceTool.'+$name}
    $exe=Join-Path $BuildDirectory ($name+'.exe')
    & $compiler @args @resources ('/out:'+$exe) ('/main:'+$main) @sources $file
    if($LASTEXITCODE -ne 0){throw "Compilation failed: $name"}
    & $exe
    if($LASTEXITCODE -ne 0){throw "Test failed: $name"}
}
Write-Output 'Selected regression suites passed; no real installers or system settings were executed.'
