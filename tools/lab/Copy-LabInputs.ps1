<#
.SYNOPSIS
    Copies the unsigned test build and the .NET 6 installers into the lab VM (C:\Lab\Input) and verifies SHA-256 in the guest.
.DESCRIPTION
    Run from an elevated Windows PowerShell 5.1 as the same Windows user that created the VM.
    - Host side is read-only: files are hashed and their Microsoft signatures are verified before copying.
    - Only the files required by tests\integration\Run-Net6.ps1 are copied (four .NET 6.0.36 installers,
      the test build renamed to 'Bars Manager 3000.exe', and Net6-Integration.exe).
    - No signing keys, Git tokens, user configuration or host disks are exposed to the guest.
    - The VM is started if it is off and is left running afterwards.
.PARAMETER LibraryRoot
    Folder with the vendor installers on the host. Default: the 'Biblioteki' folder in the drive root
    (path built from character codes so the script file stays ASCII-only).
.PARAMETER BuildDirectory
    Folder with 'Bars Service Manager 3000.exe' and 'Net6-Integration.exe' built by Build.ps1 / csc.
#>
[CmdletBinding()]
param(
    [string] $LibraryRoot,
    [string] $BuildDirectory,
    [string] $LabRoot = 'C:\BARS-TestLab',
    [string] $VmName = 'BARS-Test-W11',
    [string] $GuestName = 'BARS-LAB-01'
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Run this script with Windows PowerShell 5.1 (powershell.exe), not pwsh.exe.' }

if (-not $BuildDirectory) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $BuildDirectory = Join-Path (Split-Path -Parent (Split-Path -Parent $scriptDirectory)) 'work\lab-build'
}
if (-not $LibraryRoot) {
    $folder = -join ([char[]](0x411, 0x438, 0x431, 0x43b, 0x438, 0x43e, 0x442, 0x435, 0x43a, 0x438))
    $LibraryRoot = 'C:\' + $folder
}
$net6Folder = Join-Path $LibraryRoot 'Microsoft .NET\.NET 6'

# ---------- Host-side checks (read-only) ----------
$installers = @(Get-ChildItem -LiteralPath $net6Folder -Filter '*6.0.36*.exe' -File)
if ($installers.Count -ne 4) { throw ('Expected 4 .NET 6.0.36 installers in ' + $net6Folder + ', found ' + $installers.Count) }

$manager = Join-Path $BuildDirectory 'Bars Service Manager 3000.exe'
$integration = Join-Path $BuildDirectory 'Net6-Integration.exe'
foreach ($required in @($manager, $integration)) { if (-not (Test-Path -LiteralPath $required)) { throw "Build artifact not found: $required" } }
if ((Get-AuthenticodeSignature -LiteralPath $manager).Status -ne 'NotSigned') { throw 'The test build must be unsigned (project rule: do not sign by default).' }

$plan = New-Object System.Collections.Generic.List[object]
foreach ($file in $installers) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Microsoft Corporation*') {
        throw ('Microsoft signature was not verified: ' + $file.Name + ' (' + $signature.Status + ')')
    }
    $plan.Add([pscustomobject]@{ Source = $file.FullName; Target = $file.Name; Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash })
}
$plan.Add([pscustomobject]@{ Source = $manager; Target = 'Bars Manager 3000.exe'; Sha256 = (Get-FileHash -LiteralPath $manager -Algorithm SHA256).Hash })
$plan.Add([pscustomobject]@{ Source = $integration; Target = 'Net6-Integration.exe'; Sha256 = (Get-FileHash -LiteralPath $integration -Algorithm SHA256).Hash })

Write-Output 'Files to copy (host SHA-256):'
$plan | ForEach-Object { Write-Output ('  ' + $_.Target.PadRight(52) + $_.Sha256) }

# ---------- VM checks ----------
$vm = Get-VM -Name $VmName
if ($vm.Notes -notlike '*Dedicated BARS disposable test VM*') { throw 'This is not the dedicated BARS test VM. Refusing to continue.' }
if ($vm.State -eq 'Off') { Start-VM -Name $VmName }
$credential = Import-Clixml -LiteralPath (Join-Path (Join-Path $LabRoot $VmName) 'guest-credential.xml')

$deadline = [DateTime]::UtcNow.AddMinutes(15)
$session = $null
while ([DateTime]::UtcNow -lt $deadline -and -not $session) {
    try { $session = New-PSSession -VMId $vm.Id -Credential $credential -ErrorAction Stop } catch { Start-Sleep -Seconds 10 }
}
if (-not $session) { throw 'PowerShell Direct did not become available.' }

try {
    $actualName = Invoke-Command -Session $session -ScriptBlock { $env:COMPUTERNAME }
    if ($actualName -ne $GuestName) { throw "Unexpected guest name '$actualName'. Stopping." }
    Invoke-Command -Session $session -ScriptBlock { New-Item -ItemType Directory -Path 'C:\Lab\Input' -Force | Out-Null; New-Item -ItemType Directory -Path 'C:\Lab\Reports' -Force | Out-Null }

    foreach ($item in $plan) {
        Copy-Item -LiteralPath $item.Source -Destination ('C:\Lab\Input\' + $item.Target) -ToSession $session -Force
    }

    $mismatch = 0
    foreach ($item in $plan) {
        $guestHash = Invoke-Command -Session $session -ArgumentList $item.Target -ScriptBlock { param($name) (Get-FileHash -LiteralPath ('C:\Lab\Input\' + $name) -Algorithm SHA256).Hash }
        $ok = ($guestHash -eq $item.Sha256)
        if (-not $ok) { $mismatch++ }
        Write-Output ('  guest ' + $item.Target.PadRight(52) + $(if ($ok) { 'OK' } else { 'MISMATCH' }))
    }
    if ($mismatch -gt 0) { throw "$mismatch file(s) have a different SHA-256 in the guest." }
    Write-Output 'All files copied and verified in the guest.'
}
finally {
    if ($session) { Remove-PSSession -Session $session }
}
