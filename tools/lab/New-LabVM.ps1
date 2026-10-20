<#
.SYNOPSIS
    Creates the isolated BARS test VM (Hyper-V, generation 2) from a Windows ISO and starts it.
.DESCRIPTION
    Run from an elevated Windows PowerShell 5.1 (powershell.exe).
    - Refuses to run if a VM with the same name or a non-empty VM folder already exists (never overwrites).
    - Applies the Windows image from the ISO straight to a new dynamic VHDX (DISM), configures UEFI boot,
      and places an unattend file in the guest only (never written to host disk outside the guest image).
    - Generates a random admin password; it is stored only as a DPAPI-protected credential file
      (Export-Clixml) inside the lab folder, which is outside Git and ACL-restricted.
    - The VM gets no access to host disks; only the NAT 'Default Switch' network is used.
    Real installs/uninstalls of libraries are performed only inside the guest, never on the host.
.PARAMETER IsoPath
    Path to a fully downloaded Windows 11 ISO (cloud placeholders are rejected).
.PARAMETER ExpectedSha256
    Optional expected SHA-256 of the ISO. When given and different, the script stops.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $IsoPath,
    [string] $ExpectedSha256,
    [string] $LabRoot = 'C:\BARS-TestLab',
    [string] $VmName = 'BARS-Test-W11',
    [string] $GuestName = 'BARS-LAB-01',
    [string] $AdminUser = 'BarsLabAdmin',
    [int] $ProcessorCount = 4,
    [long] $MemoryStartupBytes = 8GB,
    [long] $DiskSizeBytes = 80GB,
    [int] $ImageIndex = 1,
    [string] $SwitchName = 'Default Switch'
)

$ErrorActionPreference = 'Stop'

function New-LabPassword([int] $Length = 24) {
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $limit = 256 - (256 % $alphabet.Length)
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        while ($true) {
            $builder = New-Object Text.StringBuilder
            $buffer = New-Object byte[] 1
            while ($builder.Length -lt $Length) {
                $rng.GetBytes($buffer)
                if ($buffer[0] -lt $limit) { [void] $builder.Append($alphabet[$buffer[0] % $alphabet.Length]) }
            }
            $candidate = $builder.ToString()
            if ($candidate -cmatch '[A-Z]' -and $candidate -cmatch '[a-z]' -and $candidate -match '[0-9]') { return $candidate }
        }
    }
    finally { $rng.Dispose() }
}

# ---------- Preconditions (nothing is changed before all of them pass) ----------
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Run this script with Windows PowerShell 5.1 (powershell.exe), not pwsh.exe.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal] $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script as Administrator.' }
if (-not (Get-Command New-VM -ErrorAction SilentlyContinue)) { throw 'Hyper-V PowerShell module is not available.' }
if ((Get-Service vmms -ErrorAction Stop).Status -ne 'Running') { throw 'Hyper-V Virtual Machine Management service (vmms) is not running. Reboot after enabling Hyper-V.' }
if (Get-VM -Name $VmName -ErrorAction SilentlyContinue) { throw "VM '$VmName' already exists. Refusing to overwrite it." }
if (-not (Get-VMSwitch -Name $SwitchName -ErrorAction SilentlyContinue)) { throw "Virtual switch '$SwitchName' not found." }

$vmDir = Join-Path $LabRoot $VmName
if ((Test-Path -LiteralPath $vmDir) -and (Get-ChildItem -LiteralPath $vmDir -Force | Select-Object -First 1)) {
    throw "Folder '$vmDir' already exists and is not empty. Refusing to reuse it."
}
$labDrive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot([IO.Path]::GetFullPath($LabRoot)))
if ($labDrive.AvailableFreeSpace -lt 60GB) { throw ('Not enough free space on {0} ({1} GB free, 60 GB required).' -f $labDrive.Name, [math]::Round($labDrive.AvailableFreeSpace / 1GB, 1)) }

if (-not (Test-Path -LiteralPath $IsoPath)) { throw "ISO not found: $IsoPath" }
$isoItem = Get-Item -LiteralPath $IsoPath -Force
if (([int] $isoItem.Attributes -band 0x1000) -ne 0) { throw 'The ISO is a cloud placeholder (Offline attribute). Download it fully first.' }
$isoHash = (Get-FileHash -LiteralPath $isoItem.FullName -Algorithm SHA256).Hash
Write-Output ('ISO:     ' + $isoItem.FullName)
Write-Output ('SHA-256: ' + $isoHash)
if ($ExpectedSha256 -and ($isoHash -ne $ExpectedSha256.Trim().ToUpperInvariant())) { throw 'ISO SHA-256 does not match the expected value.' }

$templatePath = Join-Path $PSScriptRoot 'Autounattend.template.xml'
if (-not (Test-Path -LiteralPath $templatePath)) { throw "Answer-file template not found: $templatePath" }

# ---------- Lab folder with restricted ACL ----------
New-Item -ItemType Directory -Path $vmDir -Force | Out-Null
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
$inherit = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
foreach ($sid in @($identity.User, (New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544'), (New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'))) {
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', $inherit, 'None', 'Allow')))
}
Set-Acl -LiteralPath $LabRoot -AclObject $acl

# ---------- Credential (DPAPI-protected, never printed) ----------
$password = New-LabPassword
$securePassword = ConvertTo-SecureString $password -AsPlainText -Force
$credential = New-Object Management.Automation.PSCredential(($GuestName + '\' + $AdminUser), $securePassword)
$credentialPath = Join-Path $vmDir 'guest-credential.xml'
$credential | Export-Clixml -LiteralPath $credentialPath

$timeZone = (Get-TimeZone).Id
$answerFile = (Get-Content -LiteralPath $templatePath -Raw -Encoding UTF8).
    Replace('__GUEST_NAME__', $GuestName).Replace('__ADMIN_USER__', $AdminUser).
    Replace('__ADMIN_PASSWORD__', $password).Replace('__TIMEZONE__', $timeZone)
if ($answerFile -match '__[A-Z_]+__') { throw 'Unresolved placeholder left in the answer file.' }

# ---------- Build the VHDX ----------
$vhdPath = Join-Path $vmDir ($VmName + '.vhdx')
$isoMounted = $false
$vhdMounted = $false
try {
    Write-Output 'Mounting ISO (read-only)...'
    Mount-DiskImage -ImagePath $isoItem.FullName -StorageType ISO -Access ReadOnly | Out-Null
    $isoMounted = $true
    $isoLetter = $null
    for ($attempt = 0; $attempt -lt 30 -and -not $isoLetter; $attempt++) {
        $isoVolume = Get-DiskImage -ImagePath $isoItem.FullName | Get-Volume -ErrorAction SilentlyContinue
        if ($isoVolume -and $isoVolume.DriveLetter) { $isoLetter = [string] $isoVolume.DriveLetter } else { Start-Sleep -Seconds 1 }
    }
    if (-not $isoLetter) { throw 'The ISO mounted without a drive letter.' }
    $imagePath = $isoLetter + ':\sources\install.wim'
    if (-not (Test-Path -LiteralPath $imagePath)) { $imagePath = $isoLetter + ':\sources\install.esd' }
    if (-not (Test-Path -LiteralPath $imagePath)) { throw 'install.wim / install.esd not found in the ISO.' }
    $imageInfo = Get-WindowsImage -ImagePath $imagePath -Index $ImageIndex
    Write-Output ('Image:   ' + $imageInfo.ImageName + ' (index ' + $ImageIndex + ')')

    Write-Output 'Creating VHDX...'
    New-VHD -Path $vhdPath -SizeBytes $DiskSizeBytes -Dynamic -BlockSizeBytes 32MB | Out-Null
    $disk = Mount-VHD -Path $vhdPath -Passthru | Get-Disk
    $vhdMounted = $true
    Initialize-Disk -Number $disk.Number -PartitionStyle GPT
    $esp = New-Partition -DiskNumber $disk.Number -Size 300MB -GptType '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}' -AssignDriveLetter
    New-Partition -DiskNumber $disk.Number -Size 16MB -GptType '{e3c9e316-0b5c-4db8-817d-f92df00215ae}' | Out-Null
    $osPartition = New-Partition -DiskNumber $disk.Number -UseMaximumSize -GptType '{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}' -AssignDriveLetter
    Format-Volume -Partition $esp -FileSystem FAT32 -NewFileSystemLabel 'System' -Confirm:$false | Out-Null
    Format-Volume -Partition $osPartition -FileSystem NTFS -NewFileSystemLabel 'Windows' -Confirm:$false | Out-Null
    $espLetter = (Get-Partition -DiskNumber $disk.Number -PartitionNumber $esp.PartitionNumber).DriveLetter
    $osLetter = (Get-Partition -DiskNumber $disk.Number -PartitionNumber $osPartition.PartitionNumber).DriveLetter
    if (-not $espLetter -or -not $osLetter) { throw 'Failed to obtain drive letters for the new VHDX partitions.' }

    Write-Output ('Applying Windows image to ' + $osLetter + ': (this takes several minutes)...')
    Expand-WindowsImage -ImagePath $imagePath -Index $ImageIndex -ApplyPath ($osLetter + ':\') | Out-Null

    Write-Output 'Configuring UEFI boot files...'
    & bcdboot.exe ($osLetter + ':\Windows') /s ($espLetter + ':') /f UEFI | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "bcdboot failed with code $LASTEXITCODE" }

    $pantherDir = $osLetter + ':\Windows\Panther'
    New-Item -ItemType Directory -Path $pantherDir -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $pantherDir 'unattend.xml'), $answerFile, (New-Object Text.UTF8Encoding($false)))
}
finally {
    if ($vhdMounted) { Dismount-VHD -Path $vhdPath -ErrorAction SilentlyContinue }
    if ($isoMounted) { Dismount-DiskImage -ImagePath $isoItem.FullName -ErrorAction SilentlyContinue | Out-Null }
    $password = $null
    $answerFile = $null
}

# ---------- Create and start the VM ----------
Write-Output 'Creating VM...'
$vm = New-VM -Name $VmName -Generation 2 -MemoryStartupBytes $MemoryStartupBytes -VHDPath $vhdPath -SwitchName $SwitchName -Path $LabRoot
Set-VM -VM $vm -ProcessorCount $ProcessorCount -StaticMemory -AutomaticCheckpointsEnabled $false -CheckpointType Standard -Notes 'Dedicated BARS disposable test VM'
Set-VMFirmware -VM $vm -EnableSecureBoot On -SecureBootTemplate MicrosoftWindows
Set-VMKeyProtector -VM $vm -NewLocalKeyProtector
Enable-VMTPM -VM $vm
# The optional 'Guest Service Interface' is not enabled: its name is localized and PowerShell Direct does not need it.

$buildInfo = @(
    'Created (UTC): ' + [DateTime]::UtcNow.ToString('s'),
    'ISO file:      ' + $isoItem.Name,
    'ISO SHA-256:   ' + $isoHash,
    'Image:         ' + $imageInfo.ImageName,
    'Guest name:    ' + $GuestName,
    'Admin user:    ' + $AdminUser + ' (password only in the DPAPI credential file)'
)
Set-Content -LiteralPath (Join-Path $vmDir 'build-info.txt') -Value $buildInfo -Encoding UTF8

Start-VM -VM $vm
Write-Output ''
Write-Output ('VM "' + $VmName + '" created and started. Credential file: ' + $credentialPath)
Write-Output 'Next: run tools\lab\Finalize-LabVM.ps1 (waits for first boot, cleans answer file, checkpoint).'
