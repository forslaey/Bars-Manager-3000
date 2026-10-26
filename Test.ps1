param([string] $BuildDirectory, [switch] $Sign, [string] $ReferenceAssemblies, [switch] $UtilitySmoke, [switch] $SoftwareSmoke)
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
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $taskPackage -Sign:$Sign -ReferenceAssemblies $ReferenceAssemblies
$taskVersionMatch = [regex]::Match((Get-Content -LiteralPath (Join-Path $taskSource 'AssemblyInfo.cs') -Raw), 'AssemblyFileVersion\("([0-9.]+)"\)')
if (-not $taskVersionMatch.Success) { throw 'Не найдена версия сборки в AssemblyInfo.cs.' }
$taskPackageName = 'Bars Manager 3000 ' + ([version]$taskVersionMatch.Groups[1].Value).ToString(3) + '.exe'
$taskPackageAssembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $taskPackage $taskPackageName)))
foreach ($taskArchitecture in @('x86','x64')) {
    $taskResource = $taskPackageAssembly.GetManifestResourceStream("BARS.Helper.$taskArchitecture")
    $taskResourceOutput = [IO.File]::Create((Join-Path $taskBuild "Installer.$taskArchitecture.exe"))
    try { $taskResource.CopyTo($taskResourceOutput) } finally { $taskResourceOutput.Dispose(); $taskResource.Dispose() }
}

function Invoke-TaskTests([string] $taskName, [string] $taskMain, [string[]] $taskInputs) {
    $taskOutput = Join-Path $taskBuild ($taskName + '.exe')
    $taskArguments = @('/nologo', '/target:exe', '/platform:anycpu', '/utf8output', '/codepage:65001', "/out:$taskOutput", "/main:$taskMain", '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Xml.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll', '/reference:System.Configuration.Install.dll', '/reference:System.ServiceProcess.dll', '/reference:System.Security.dll', '/reference:System.Web.Extensions.dll', "/win32manifest:$(Join-Path $taskSource 'app.manifest')", "/resource:$(Join-Path $taskAssets 'BarsHeader.png'),BARS.Logo.png", "/resource:$(Join-Path $taskAssets 'BarsHeaderDark.png'),BARS.Logo.Dark.png", "/resource:$(Join-Path $taskAssets 'Bars.ico'),BARS.Icon.ico", "/resource:$(Join-Path $taskAssets 'AnyDesk.png'),BARS.AnyDesk.png", "/resource:$(Join-Path $taskAssets 'RuDesktop.png'),BARS.RuDesktop.png")
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
$taskUiSources += Join-Path $taskSource 'SoftwarePackageStorage.cs'
$taskUiSources += Join-Path $taskSource 'GuideSectionListBox.cs'
$taskUiSources += Join-Path $taskSource 'TouchGrid.cs'
$taskUiSources += Join-Path $taskSource 'BoundedContentPanel.cs'
$taskUiSources += Join-Path $taskSource 'LibraryJournal.cs'
$taskUiSources += Join-Path $taskSource 'BarsFolderPermissions.cs'
$taskUiSources += @('KioskTouchSettings.cs','PasswordEyeButton.cs','InitialSystemSettings.cs','InitialSystemSettingsForm.cs') | ForEach-Object { Join-Path $taskSource $_ }
$taskUiSources += @('LibraryCatalog.cs','LibraryCore.cs','LibraryDiscovery.cs','LibraryInstaller.cs','ManagerHub.cs','UserGuideForm.cs','LibraryManagerForm.cs','LibraryGridScrollBar.cs','LibraryHeartbeat.cs','LibraryMaintenance.cs','LibraryActionDialog.cs','LibraryVersions.cs','LibraryVcProfiles.cs','LibraryCancellation.cs','WindowsComponents.cs','ToolsDeployment.cs','UtilityModules.cs','InstallerRebootGuard.cs','PausedLibraryQueue.cs','SoftwareCatalog.cs','SoftwareSources.cs','SoftwareDetection.cs','SoftwareInstaller.cs','SoftwareDefaults.cs','SoftwareManagerForm.cs') | ForEach-Object { Join-Path $taskSource $_ }
$taskUiSources += @('BrandMessageBox.cs','JournalPanel.cs','SystemSettingsPresentation.cs','GuideReader.cs','ModuleNavigation.cs') | ForEach-Object { Join-Path $taskSource $_ }
Invoke-TaskTests 'UtilityModuleTests' 'ServiceTool.UtilityModuleTests' ($taskUiSources + (Join-Path $taskTests 'UtilityModuleTests.cs'))
if ($UtilitySmoke) { Invoke-TaskTests 'UtilitySmokeTests' 'ServiceTool.UtilitySmokeTests' ($taskUiSources + (Join-Path $taskTests 'UtilitySmokeTests.cs')) }
Invoke-TaskTests 'SoftwareTests' 'ServiceTool.SoftwareTests' ($taskUiSources + (Join-Path $taskTests 'SoftwareTests.cs'))
Invoke-TaskTests 'SoftwareMaintenanceTests' 'ServiceTool.SoftwareMaintenanceTests' ($taskUiSources + (Join-Path $taskTests 'SoftwareMaintenanceTests.cs'))
Invoke-TaskTests 'LayoutFitTests' 'ServiceTool.LayoutFitTests' ($taskUiSources + (Join-Path $taskTests 'LayoutFitTests.cs'))
Invoke-TaskTests 'ResponsiveLayoutTests' 'ServiceTool.ResponsiveLayoutTests' ($taskUiSources + (Join-Path $taskTests 'ResponsiveLayoutTests.cs'))
Invoke-TaskTests 'HeaderStyleTests' 'ServiceTool.HeaderStyleTests' ($taskUiSources + (Join-Path $taskTests 'HeaderStyleTests.cs'))
Invoke-TaskTests 'RapidSelectionTests' 'ServiceTool.RapidSelectionTests' ($taskUiSources + (Join-Path $taskTests 'RapidSelectionTests.cs'))
Invoke-TaskTests 'StartupModeUiTests' 'ServiceTool.StartupModeUiTests' ($taskUiSources + (Join-Path $taskTests 'StartupModeUiTests.cs'))
Invoke-TaskTests 'NewSettingsTests' 'ServiceTool.NewSettingsTests' ($taskUiSources + (Join-Path $taskTests 'NewSettingsTests.cs'))
& (Join-Path $taskTests 'InitialSystemSettings.Tests.ps1') -ScriptFolder $taskBuild
& (Join-Path $taskTests 'BarsFolderPermissions.Tests.ps1') -ScriptFolder $taskBuild
Invoke-TaskTests 'GuideDpiTests' 'ServiceTool.GuideDpiTests' ($taskUiSources + (Join-Path $taskTests 'GuideDpiTests.cs'))
Invoke-TaskTests 'UserGuideTests' 'ServiceTool.UserGuideTests' ($taskUiSources + (Join-Path $taskTests 'UserGuideTests.cs'))
Invoke-TaskTests 'WindowsRestartTests' 'ServiceTool.WindowsRestartTests' ($taskUiSources + (Join-Path $taskTests 'WindowsRestartTests.cs'))
if ($SoftwareSmoke) { Invoke-TaskTests 'SoftwareStartupTests' 'ServiceTool.SoftwareStartupTests' ($taskUiSources + (Join-Path $taskTests 'SoftwareStartupTests.cs')) }
if ($SoftwareSmoke) { Invoke-TaskTests 'SoftwareVersionCheckTests' 'ServiceTool.SoftwareVersionCheckTests' ($taskUiSources + (Join-Path $taskTests 'SoftwareVersionCheckTests.cs')) }
if ($SoftwareSmoke) { Invoke-TaskTests 'SoftwareStorageTests' 'ServiceTool.SoftwareStorageTests' ($taskUiSources + (Join-Path $taskTests 'SoftwareStorageTests.cs')) }
Invoke-TaskTests 'ToolsRemovalTests' 'ServiceTool.ToolsRemovalTests' ($taskUiSources + (Join-Path $taskTests 'ToolsRemovalTests.cs'))
if ($SoftwareSmoke) { Invoke-TaskTests 'SoftwareSmokeTests' 'ServiceTool.SoftwareSmokeTests' ($taskUiSources + (Join-Path $taskTests 'SoftwareSmokeTests.cs')) }
Invoke-TaskTests 'UiTests' 'ServiceTool.UiTests' ($taskUiSources + (Join-Path $taskTests 'UiTests.cs'))
Invoke-TaskTests 'ThemeTests' 'ServiceTool.ThemeTests' ($taskUiSources + (Join-Path $taskTests 'ThemeTests.cs'))
Invoke-TaskTests 'BrandInteractionTests' 'ServiceTool.BrandInteractionTests' ($taskUiSources + (Join-Path $taskTests 'BrandInteractionTests.cs'))
Invoke-TaskTests 'LibraryManagerTests' 'ServiceTool.LibraryManagerTests' ($taskUiSources + (Join-Path $taskTests 'LibraryManagerTests.cs'))
Invoke-TaskTests 'UiPolishTests' 'ServiceTool.UiPolishTests' ($taskUiSources + (Join-Path $taskTests 'UiPolishTests.cs'))
Invoke-TaskTests 'LibraryMaintenanceTests' 'ServiceTool.LibraryMaintenanceTests' ($taskUiSources + (Join-Path $taskTests 'LibraryMaintenanceTests.cs'))
Invoke-TaskTests 'LibraryCancellationTests' 'ServiceTool.LibraryCancellationTests' ($taskUiSources + (Join-Path $taskTests 'LibraryCancellationTests.cs'))
Invoke-TaskTests 'ManagedCatalogUiTests' 'ServiceTool.ManagedCatalogUiTests' ($taskUiSources + (Join-Path $taskTests 'ManagedCatalogUiTests.cs'))
Invoke-TaskTests 'JournalScrollTests' 'ServiceTool.JournalScrollTests' ($taskUiSources + (Join-Path $taskTests 'JournalScrollTests.cs'))
Invoke-TaskTests 'LogArchiveTests' 'ServiceTool.LogArchiveTests' @((Join-Path $taskSource 'ServiceLogArchive.cs'), (Join-Path $taskTests 'LogArchiveTests.cs'))
Invoke-TaskTests 'LogArchiveUiTests' 'ServiceTool.LogArchiveUiTests' ($taskUiSources + (Join-Path $taskTests 'LogArchiveUiTests.cs'))
Invoke-TaskTests 'ServiceInstallProfileTests' 'ServiceInstallProfileTests' ($taskUiSources + (Join-Path $taskTests 'ServiceInstallProfileTests.cs'))
Invoke-TaskTests 'ProductTests' 'ServiceTool.ProductTests' ($taskUiSources + (Join-Path $taskTests 'ProductTests.cs'))
Invoke-TaskTests 'RegressionTests' 'ServiceTool.RegressionTests' ($taskUiSources + (Join-Path $taskTests 'RegressionTests.cs'))

Invoke-TaskTests 'TouchGridTests' 'ServiceTool.TouchGridTests' ($taskUiSources + (Join-Path $taskTests 'TouchGridTests.cs'))
Invoke-TaskTests 'LibraryJournalTests' 'ServiceTool.LibraryJournalTests' ($taskUiSources + (Join-Path $taskTests 'LibraryJournalTests.cs'))

Invoke-TaskTests 'SoftwareConfirmationTests' 'ServiceTool.SoftwareConfirmationTests' ($taskUiSources + (Join-Path $taskTests 'SoftwareConfirmationTests.cs'))

Invoke-TaskTests 'SystemSettingsRegressionTests' 'ServiceTool.SystemSettingsRegressionTests' ($taskUiSources + (Join-Path $taskTests 'SystemSettingsRegressionTests.cs'))

Invoke-TaskTests 'AssociationProfileTests' 'ServiceTool.AssociationProfileTests' ($taskUiSources + (Join-Path $taskTests 'AssociationProfileTests.cs'))
Invoke-TaskTests 'RequestedUi2322Tests' 'ServiceTool.RequestedUi2322Tests' ($taskUiSources + (Join-Path $taskTests 'RequestedUi2322Tests.cs'))

Invoke-TaskTests 'Ui2323Tests' 'ServiceTool.Ui2323Tests' ($taskUiSources + (Join-Path $taskTests 'Ui2323Tests.cs'))
Invoke-TaskTests 'FolderAuditTests' 'ServiceTool.FolderAuditTests' ($taskUiSources + (Join-Path $taskTests 'FolderAuditTests.cs'))

Invoke-TaskTests 'Ui2324Tests' 'ServiceTool.Ui2324Tests' ($taskUiSources + (Join-Path $taskTests 'Ui2324Tests.cs'))
Invoke-TaskTests 'Ui2328Tests' 'ServiceTool.Ui2328Tests' ($taskUiSources + (Join-Path $taskTests 'Ui2328Tests.cs'))
Invoke-TaskTests 'Ui2329Tests' 'ServiceTool.Ui2329Tests' ($taskUiSources + (Join-Path $taskTests 'Ui2329Tests.cs'))
Invoke-TaskTests 'Ui2330Tests' 'ServiceTool.Ui2330Tests' ($taskUiSources + (Join-Path $taskTests 'Ui2330Tests.cs'))

Invoke-TaskTests 'KioskTouchTests' 'ServiceTool.KioskTouchTests' ($taskUiSources + (Join-Path $taskTests 'KioskTouchTests.cs'))

Write-Output "Все проверки пройдены. Временные сборки: $taskBuild"
Write-Output 'Реальные процедуры установки, удаления и управления службами не выполнялись.'
