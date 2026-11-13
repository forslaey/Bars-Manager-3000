param([string] $OutputDirectory = $PSScriptRoot, [switch] $Sign, [string] $CertificateThumbprint, [string] $TimestampServer = 'http://timestamp.digicert.com', [string] $ReferenceAssemblies)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = $PSScriptRoot }
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Не найден компилятор .NET Framework. Нужен компилятор .NET Framework 4.x.' }
$taskSource = Join-Path $PSScriptRoot 'src'
$taskAssets = Join-Path $PSScriptRoot 'assets'
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskBuild = Join-Path ([IO.Path]::GetTempPath()) ('bars-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskBuild | Out-Null
$taskReferences = @('System.dll','System.Core.dll','System.Xml.dll','System.Drawing.dll','System.Windows.Forms.dll','System.IO.Compression.dll','System.Configuration.Install.dll','System.ServiceProcess.dll','System.Security.dll','System.Web.Extensions.dll')
$taskCommon = @('/nologo','/target:winexe','/optimize+','/utf8output','/codepage:65001', "/win32manifest:$(Join-Path $taskSource 'app.manifest')", "/win32icon:$(Join-Path $taskAssets 'Bars.ico')")
if ([String]::IsNullOrWhiteSpace($ReferenceAssemblies)) { $ReferenceAssemblies = & (Join-Path $PSScriptRoot 'tools\Prepare-References.ps1') }
$taskReferenceRoot = [IO.Path]::GetFullPath($ReferenceAssemblies)
if (-not (Test-Path -LiteralPath (Join-Path $taskReferenceRoot 'mscorlib.dll'))) { throw 'Нет reference assemblies .NET Framework 4.8.' }
$taskCommon += @('/noconfig','/nostdlib+',('/reference:' + (Join-Path $taskReferenceRoot 'mscorlib.dll')))
$taskCommon += @($taskReferences | ForEach-Object { '/reference:' + (Join-Path $taskReferenceRoot $_) })
try {
    $taskHelperSources = @('ServiceCore.cs','ServiceInstallProfile.cs','ServiceProfileInstaller.cs','WindowsAccountInfo.cs','EmbeddedHelpers.cs','ApplicationStorage.cs','AssemblyInfo.cs','Helpers\HelperProgram.cs') | ForEach-Object { Join-Path $taskSource $_ }
    foreach ($taskArchitecture in @('x86','x64')) {
        $taskHelper = Join-Path $taskBuild ("Installer.$taskArchitecture.exe")
        & $taskCompiler @taskCommon "/platform:$taskArchitecture" '/main:ServiceTool.HelperProgram' "/out:$taskHelper" @taskHelperSources
        if ($LASTEXITCODE -ne 0) { throw "Не удалось собрать встроенный установщик $taskArchitecture" }
    }
    if ($Sign) { foreach ($taskArchitecture in @('x86','x64')) { & (Join-Path $PSScriptRoot 'tools\Sign-File.ps1') -FilePath (Join-Path $taskBuild "Installer.$taskArchitecture.exe") -CertificateThumbprint $CertificateThumbprint -TimestampServer $TimestampServer } }
    $taskSources = @(Get-ChildItem -LiteralPath $taskSource -File -Filter '*.cs' | ForEach-Object FullName)
    $taskResources = @("/resource:$(Join-Path $taskAssets 'BundleManifest.json'),BARS.BundleManifest.json", "/resource:$(Join-Path $taskAssets 'BarsHeader.png'),BARS.Logo.png", "/resource:$(Join-Path $taskAssets 'BarsHeaderDark.png'),BARS.Logo.Dark.png", "/resource:$(Join-Path $taskAssets '12-novyy-god-300dpi.png'),BARS.NewYear.png", "/resource:$(Join-Path $taskAssets 'Bars.ico'),BARS.Icon.ico", "/resource:$(Join-Path $taskAssets 'AnyDesk.png'),BARS.AnyDesk.png", "/resource:$(Join-Path $taskAssets 'RuDesktop.png'),BARS.RuDesktop.png")
    foreach ($taskArchitecture in @('x86','x64')) { $taskResources += "/resource:$(Join-Path $taskBuild "Installer.$taskArchitecture.exe"),BARS.Helper.$taskArchitecture" }
    $taskStaged = Join-Path $taskBuild 'Bars Manager 3000.exe'
    & $taskCompiler @taskCommon @taskResources '/platform:anycpu' "/out:$taskStaged" @taskSources
    if ($LASTEXITCODE -ne 0) { throw "Ошибка компиляции: $LASTEXITCODE" }
    if ($Sign) { & (Join-Path $PSScriptRoot 'tools\Sign-File.ps1') -FilePath $taskStaged -CertificateThumbprint $CertificateThumbprint -TimestampServer $TimestampServer }
    $taskVersion = ([version][Diagnostics.FileVersionInfo]::GetVersionInfo($taskStaged).FileVersion).ToString(3)
    $taskBinary = Join-Path $taskOutput ("Bars Manager 3000 $taskVersion.exe")
    Copy-Item -LiteralPath $taskStaged -Destination $taskBinary -Force
    foreach ($taskOld in @('Bars Service Manager 3000.exe','ServiceManager3000.exe','ServiceManager3000.exe.config','ServiceManager3000.x86.exe','ServiceManager3000.x86.exe.config','ServiceManager3000.x64.exe','ServiceManager3000.x64.exe.config','Bars Service Manager 3000.exe.config')) {
        $taskOldPath = Join-Path $taskOutput $taskOld
        if (Test-Path -LiteralPath $taskOldPath) { Remove-Item -LiteralPath $taskOldPath -Force }
    }
    Write-Output "Собрано: $taskBinary (один файл; установщики x86/x64 встроены)"
}
finally {
    foreach ($taskName in @('Installer.x86.exe','Installer.x64.exe','Bars Manager 3000.exe')) {
        $taskFile = Join-Path $taskBuild $taskName
        if (Test-Path -LiteralPath $taskFile) { Remove-Item -LiteralPath $taskFile -Force }
    }
    Remove-Item -LiteralPath $taskBuild -ErrorAction SilentlyContinue
}
