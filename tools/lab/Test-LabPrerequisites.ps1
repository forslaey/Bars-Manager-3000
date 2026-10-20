<#
.SYNOPSIS
    Диагностика хоста для развёртывания изолированной тестовой VM BARS.
.DESCRIPTION
    Скрипт только читает состояние системы и проверяет доступность образов.
    Ничего не устанавливает, не включает, не перезагружает и не создаёт.
    Не выводит пароли и содержимое защищённых файлов.
.PARAMETER IsoPath
    Необязательный путь к ISO для проверки читаемости и вычисления SHA-256.
.PARAMETER SkipHash
    Пропустить вычисление SHA-256 (быстро, только проверка чтения).
#>
[CmdletBinding()]
param(
    [string] $IsoPath,
    [switch] $SkipHash
)

$ErrorActionPreference = 'Continue'

function Write-Section([string] $title) {
    Write-Output ''
    Write-Output ('=== ' + $title + ' ===')
}

Write-Section 'Система'
$os = Get-CimInstance Win32_OperatingSystem
$cs = Get-CimInstance Win32_ComputerSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
Write-Output ('Caption:            ' + $os.Caption)
Write-Output ('Version/Build:      ' + $os.Version + ' / ' + $os.BuildNumber)
Write-Output ('EditionID:          ' + $cv.EditionID)
Write-Output ('DisplayVersion:     ' + $cv.DisplayVersion)
Write-Output ('ComputerName:       ' + $env:COMPUTERNAME)
Write-Output ('IsAdmin:            ' + ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
Write-Output ('PowerShell:         ' + $PSVersionTable.PSVersion.ToString())

Write-Section 'Виртуализация'
Write-Output ('CPU:                ' + $cpu.Name)
Write-Output ('Cores/Logical:      ' + $cpu.NumberOfCores + ' / ' + $cpu.NumberOfLogicalProcessors)
Write-Output ('VMFirmwareEnabled:  ' + $cpu.VirtualizationFirmwareEnabled)
Write-Output ('SLAT:               ' + $cpu.SecondLevelAddressTranslationExtensions)
Write-Output ('HypervisorPresent:  ' + $cs.HypervisorPresent)
Write-Output ('VBS status:         ' + (Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard).VirtualizationBasedSecurityStatus)
try { Write-Output ('SecureBoot(host):   ' + (Confirm-SecureBootUEFI)) } catch { Write-Output ('SecureBoot(host):   неизвестно (' + $_.Exception.Message + ')') }
$tpm = Get-Tpm -ErrorAction SilentlyContinue
if ($tpm) { Write-Output ('TPM:                Present=' + $tpm.TpmPresent + ' Ready=' + $tpm.TpmReady) }

Write-Section 'Ресурсы'
Write-Output ('RAM total GB:       ' + [math]::Round($cs.TotalPhysicalMemory / 1GB, 1))
Write-Output ('RAM free GB:        ' + [math]::Round($os.FreePhysicalMemory / 1MB, 1))
Get-Volume | Where-Object DriveLetter | ForEach-Object {
    Write-Output ('Drive ' + $_.DriveLetter + ': free GB ' + [math]::Round($_.SizeRemaining / 1GB, 1) + ' of ' + [math]::Round($_.Size / 1GB, 1))
}

Write-Section 'Компоненты Hyper-V'
Get-WindowsOptionalFeature -Online -ErrorAction SilentlyContinue |
    Where-Object { $_.FeatureName -like 'Microsoft-Hyper-V*' -or $_.FeatureName -in @('VirtualMachinePlatform', 'HypervisorPlatform') } |
    Sort-Object FeatureName |
    ForEach-Object { Write-Output ($_.FeatureName.PadRight(45) + $_.State) }
Get-Service -Name vmms, vmcompute, HvHost -ErrorAction SilentlyContinue |
    ForEach-Object { Write-Output ('service ' + $_.Name.PadRight(14) + $_.Status) }

Write-Section 'Ожидающие перезагрузки'
Write-Output ('CBS RebootPending:  ' + (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending'))
Write-Output ('WU RebootRequired:  ' + (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'))
Write-Output ('PendingFileRename:  ' + ([bool](Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue)))

Write-Section 'Существующие VM и коммутаторы'
if (Get-Command Get-VM -ErrorAction SilentlyContinue) {
    $vms = Get-VM -ErrorAction SilentlyContinue
    if ($vms) { $vms | ForEach-Object { Write-Output ('VM: ' + $_.Name + ' state=' + $_.State + ' gen=' + $_.Generation) } }
    else { Write-Output 'VM не найдены.' }
    $switches = Get-VMSwitch -ErrorAction SilentlyContinue
    if ($switches) { $switches | ForEach-Object { Write-Output ('Switch: ' + $_.Name + ' type=' + $_.SwitchType) } }
    else { Write-Output 'Виртуальные коммутаторы не найдены.' }
}
else {
    Write-Output 'Командлеты Hyper-V недоступны (модуль не загружен или компонент отключён).'
    Write-Output ('Hyper-V\Virtual Machines ключ существует: ' + (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization'))
}

Write-Section 'Каталог лаборатории'
if (Test-Path 'C:\BARS-TestLab') {
    Write-Output 'C:\BARS-TestLab уже существует — новая лаборатория не должна использовать это имя без явного согласия.'
}
else {
    Write-Output 'C:\BARS-TestLab отсутствует (конфликта нет).'
}

if ($IsoPath) {
    Write-Section 'Образ ISO'
    if (-not (Test-Path -LiteralPath $IsoPath)) { Write-Output ('Не найден: ' + $IsoPath) }
    else {
        $item = Get-Item -LiteralPath $IsoPath
        Write-Output ('Path:   ' + $item.FullName)
        Write-Output ('Bytes:  ' + $item.Length)
        Write-Output ('Attributes: ' + $item.Attributes)
        try {
            $stream = [IO.File]::Open($item.FullName, 'Open', 'Read', 'ReadWrite')
            try { $buffer = New-Object byte[] 4096; $read = $stream.Read($buffer, 0, 4096) } finally { $stream.Dispose() }
            Write-Output ('Read test: ok, ' + $read + ' bytes from offset 0 (локально доступен, не только облачная заглушка)')
        }
        catch { Write-Output ('Read test: ОШИБКА — ' + $_.Exception.Message) }
        if (-not $SkipHash) {
            $sw = [Diagnostics.Stopwatch]::StartNew()
            try {
                $hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
                Write-Output ('SHA-256: ' + $hash + ' (за ' + [math]::Round($sw.Elapsed.TotalSeconds, 1) + ' с)')
            }
            catch { Write-Output ('SHA-256: ОШИБКА — ' + $_.Exception.Message) }
        }
    }
}

Write-Output ''
Write-Output 'Диагностика завершена. Изменений в системе не вносилось.'
