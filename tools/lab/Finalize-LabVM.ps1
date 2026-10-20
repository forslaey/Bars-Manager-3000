<#
.SYNOPSIS
    Waits for the first boot of the lab VM, removes the answer file from the guest, and creates a clean checkpoint.
.DESCRIPTION
    Run from an elevated Windows PowerShell 5.1 as the same Windows user that ran New-LabVM.ps1
    (the credential file is DPAPI-protected for that user).
    Everything inside the guest is read-only inventory except removing the leftover answer files
    and creating C:\Lab\Input. The host is not modified besides the VM and its checkpoint.
#>
[CmdletBinding()]
param(
    [string] $LabRoot = 'C:\BARS-TestLab',
    [string] $VmName = 'BARS-Test-W11',
    [string] $GuestName = 'BARS-LAB-01',
    [int] $TimeoutMinutes = 45,
    [string] $CheckpointName = 'Clean-Windows'
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Run this script with Windows PowerShell 5.1 (powershell.exe), not pwsh.exe.' }

$vm = Get-VM -Name $VmName
if ($vm.Notes -notlike '*Dedicated BARS disposable test VM*') { throw 'This is not the dedicated BARS test VM. Refusing to continue.' }
if ($vm.State -ne 'Running') { throw ('VM is not running (state: ' + $vm.State + ').') }
if (Get-VMSnapshot -VMName $VmName -Name $CheckpointName -ErrorAction SilentlyContinue) { throw "Checkpoint '$CheckpointName' already exists." }

$credentialPath = Join-Path (Join-Path $LabRoot $VmName) 'guest-credential.xml'
$credential = Import-Clixml -LiteralPath $credentialPath

# ---------- Wait for PowerShell Direct ----------
$deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
$actualName = $null
Write-Output 'Waiting for the guest (first boot, specialize and OOBE take several minutes)...'
while ([DateTime]::UtcNow -lt $deadline) {
    try {
        $actualName = Invoke-Command -VMId $vm.Id -Credential $credential -ScriptBlock { $env:COMPUTERNAME } -ErrorAction Stop
        break
    }
    catch { Start-Sleep -Seconds 20 }
}
if (-not $actualName) { throw 'PowerShell Direct did not become available within the timeout. Check the VM console in Hyper-V Manager.' }
if ($actualName -ne $GuestName) { throw "Unexpected guest name '$actualName' (expected '$GuestName'). Stopping." }
Write-Output ('Guest answered: ' + $actualName)

# ---------- Wait for OOBE to finish (best effort) ----------
$stateDeadline = [DateTime]::UtcNow.AddMinutes(10)
$imageState = $null
while ([DateTime]::UtcNow -lt $stateDeadline) {
    try { $imageState = Invoke-Command -VMId $vm.Id -Credential $credential -ScriptBlock { (Get-ItemProperty 'HKLM:\SYSTEM\Setup\State').ImageState } -ErrorAction Stop } catch { $imageState = $null }
    if ($imageState -eq 'IMAGE_STATE_COMPLETE') { break }
    Start-Sleep -Seconds 15
}
if ($imageState -ne 'IMAGE_STATE_COMPLETE') { Write-Warning ('Setup state is "' + $imageState + '", not IMAGE_STATE_COMPLETE. Continuing anyway.') }

# ---------- Clean up answer files and take an inventory ----------
$secret = $credential.GetNetworkCredential().Password
$report = Invoke-Command -VMId $vm.Id -Credential $credential -ArgumentList $secret -ScriptBlock {
    param($Secret)
    foreach ($path in @('C:\Windows\Panther\unattend.xml', 'C:\Windows\Panther\unattend', 'C:\Windows\System32\Sysprep\unattend.xml', 'C:\Windows\System32\Sysprep\Panther\unattend.xml')) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue }
    }
    New-Item -ItemType Directory -Path 'C:\Lab\Input' -Force | Out-Null
    $leaks = 0
    foreach ($file in @(Get-ChildItem -Path 'C:\Windows\Panther' -Recurse -File -Include *.xml, *.log -ErrorAction SilentlyContinue)) {
        if (Select-String -LiteralPath $file.FullName -SimpleMatch $Secret -Quiet -ErrorAction SilentlyContinue) { $leaks++ }
    }
    $os = Get-CimInstance Win32_OperatingSystem
    $arp = @(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName }).Count
    [pscustomobject]@{
        ComputerName    = $env:COMPUTERNAME
        OS              = $os.Caption
        Build           = $os.BuildNumber
        Architecture    = $os.OSArchitecture
        DotNetCliFound  = (Test-Path (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'))
        ArpEntries      = $arp
        AnswerFilesLeft = @(Get-ChildItem -Path 'C:\Windows\Panther' -Recurse -File -Filter 'unattend*.xml' -ErrorAction SilentlyContinue).Count
        FilesWithSecret = $leaks
    }
}
$secret = $null
$report | Format-List | Out-String | Write-Output
if ($report.FilesWithSecret -gt 0 -or $report.AnswerFilesLeft -gt 0) { Write-Warning 'Some answer files or logs still contain sensitive data. Investigate before sharing this VM.' }

# ---------- Graceful shutdown and checkpoint ----------
Write-Output 'Shutting the guest down gracefully...'
Stop-VM -Name $VmName
$offDeadline = [DateTime]::UtcNow.AddMinutes(10)
while ((Get-VM -Name $VmName).State -ne 'Off') {
    if ([DateTime]::UtcNow -gt $offDeadline) { throw 'The guest did not shut down in time. Not forcing power-off; check the VM manually.' }
    Start-Sleep -Seconds 5
}
Checkpoint-VM -Name $VmName -SnapshotName $CheckpointName
Write-Output ('Checkpoint "' + $CheckpointName + '" created while the VM is off. Start it with: Start-VM -Name ' + $VmName)
