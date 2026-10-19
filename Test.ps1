param([string] $BuildDirectory, [switch] $Sign)
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) {
    $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Не найден компилятор .NET Framework.' }
$taskSource = Join-Path $PSScriptRoot 'src'
$taskTests = Join-Path $PSScriptRoot 'tests'
$taskAssets = Join-Path $PSScriptRoot 'assets'
$taskBuild = if ([String]::IsNullOrWhiteSpace($BuildDirectory)) {
    Join-Path ([IO.Path]::GetTempPath()) ('sm3t-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
} else {
    [IO.Path]::GetFullPath($BuildDirectory)
}
New-Item -ItemType Directory -Path $taskBuild -Force | Out-Null
$taskPackage = Join-Path $taskBuild 'package'
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $taskPackage -Sign:$Sign
$taskPackageAssembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $taskPackage 'Bars Service Manager 3000.exe')))
foreach ($taskArchitecture in @('x86','x64')) {
    $taskResource = $taskPackageAssembly.GetManifestResourceStream("BARS.Helper.$taskArchitecture")
    $taskResourceOutput = [IO.File]::Create((Join-Path $taskBuild "Installer.$taskArchitecture.exe"))
    try { $taskResource.CopyTo($taskResourceOutput) } finally { $taskResourceOutput.Dispose(); $taskResource.Dispose() }
}

function Invoke-TaskTests([string] $taskName, [string] $taskMain, [string[]] $taskInputs) {
    $taskOutput = Join-Path $taskBuild ($taskName + '.exe')
    $taskArguments = @('/nologo', '/target:exe', '/platform:anycpu', '/utf8output', '/codepage:65001', "/out:$taskOutput", "/main:$taskMain", '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Xml.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll', '/reference:System.Configuration.Install.dll', '/reference:System.ServiceProcess.dll', '/reference:System.Security.dll', "/win32manifest:$(Join-Path $taskSource 'app.manifest')", "/resource:$(Join-Path $taskAssets 'BarsHeader.png'),BARS.Logo.png", "/resource:$(Join-Path $taskAssets 'BarsHeaderDark.png'),BARS.Logo.Dark.png", "/resource:$(Join-Path $taskAssets 'Bars.ico'),BARS.Icon.ico")
    foreach ($taskArchitecture in @('x86','x64')) { $taskArguments += "/resource:$(Join-Path $taskBuild "Installer.$taskArchitecture.exe"),BARS.Helper.$taskArchitecture" }
    & $taskCompiler @taskArguments @taskInputs
    if ($LASTEXITCODE -ne 0) { throw "Не удалось собрать $taskName" }
    & $taskOutput
    if ($LASTEXITCODE -ne 0) { throw "Ошибка проверок $taskName" }
}

$taskCore = Join-Path $taskSource 'ServiceCore.cs'
$taskManagement = Join-Path $taskSource 'ServiceManagement.cs'
Invoke-TaskTests 'CoreTests' 'CoreTests' @($taskCore, (Join-Path $taskTests 'CoreTests.cs'))
Invoke-TaskTests 'ManagementTests' 'ManagementTests' @($taskCore, $taskManagement, (Join-Path $taskTests 'ManagementTests.cs'))
Invoke-TaskTests 'ManagedServiceStoreTests' 'ManagedServiceStoreTests' @($taskCore, (Join-Path $taskSource 'ApplicationStorage.cs'), (Join-Path $taskSource 'ManagedServiceStore.cs'), (Join-Path $taskTests 'ManagedServiceStoreTests.cs'))
$taskUiSources = @((Join-Path $taskSource 'Program.cs'), $taskCore, $taskManagement, (Join-Path $taskSource 'Theme.cs'), (Join-Path $taskSource 'BrandAssets.cs'), (Join-Path $taskSource 'ManagedServiceStore.cs'), (Join-Path $taskSource 'JournalControls.cs'), (Join-Path $taskSource 'AboutDialog.cs'), (Join-Path $taskSource 'ServiceLogArchive.cs'), (Join-Path $taskSource 'LogArchiveDialog.cs'), (Join-Path $taskSource 'ServiceInstallProfile.cs'), (Join-Path $taskSource 'ServiceProfileInstaller.cs'), (Join-Path $taskSource 'ServiceAccountDialog.cs'), (Join-Path $taskSource 'WindowsAccountInfo.cs'))
$taskUiSources += Join-Path $taskSource 'EmbeddedHelpers.cs'
$taskUiSources += Join-Path $taskSource 'AssemblyInfo.cs'
$taskUiSources += Join-Path $taskSource 'ApplicationStorage.cs'
$taskUiSources += Join-Path $taskSource 'ProcedureWait.cs'
Invoke-TaskTests 'UiTests' 'ServiceTool.UiTests' ($taskUiSources + (Join-Path $taskTests 'UiTests.cs'))
Invoke-TaskTests 'ThemeTests' 'ServiceTool.ThemeTests' ($taskUiSources + (Join-Path $taskTests 'ThemeTests.cs'))
Invoke-TaskTests 'ManagedCatalogUiTests' 'ServiceTool.ManagedCatalogUiTests' ($taskUiSources + (Join-Path $taskTests 'ManagedCatalogUiTests.cs'))
Invoke-TaskTests 'JournalScrollTests' 'ServiceTool.JournalScrollTests' ($taskUiSources + (Join-Path $taskTests 'JournalScrollTests.cs'))
Invoke-TaskTests 'LogArchiveTests' 'ServiceTool.LogArchiveTests' @((Join-Path $taskSource 'ServiceLogArchive.cs'), (Join-Path $taskTests 'LogArchiveTests.cs'))
Invoke-TaskTests 'LogArchiveUiTests' 'ServiceTool.LogArchiveUiTests' ($taskUiSources + (Join-Path $taskTests 'LogArchiveUiTests.cs'))
Invoke-TaskTests 'ServiceInstallProfileTests' 'ServiceInstallProfileTests' ($taskUiSources + (Join-Path $taskTests 'ServiceInstallProfileTests.cs'))
Invoke-TaskTests 'ProductTests' 'ServiceTool.ProductTests' ($taskUiSources + (Join-Path $taskTests 'ProductTests.cs'))
Invoke-TaskTests 'RegressionTests' 'ServiceTool.RegressionTests' ($taskUiSources + (Join-Path $taskTests 'RegressionTests.cs'))
Write-Output "Все проверки пройдены. Временные сборки: $taskBuild"
Write-Output 'Реальные процедуры установки, удаления и управления службами не выполнялись.'
