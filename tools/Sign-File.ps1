[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string[]] $FilePath,
    [string] $CertificateThumbprint,
    [string] $TimestampServer = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $taskSettings = Join-Path $env:LOCALAPPDATA 'BARS\Bars Service Manager 3000\signing.json'
    if (-not (Test-Path -LiteralPath $taskSettings)) { throw 'Сначала выполни tools\New-SigningCertificate.ps1 либо передай -CertificateThumbprint.' }
    $CertificateThumbprint = (Get-Content -LiteralPath $taskSettings -Raw | ConvertFrom-Json).Thumbprint
}
$CertificateThumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
if ($CertificateThumbprint -notmatch '^[0-9A-F]{40}$') { throw 'Некорректный отпечаток сертификата.' }
$taskCertificate = Get-Item -LiteralPath ('Cert:\CurrentUser\My\' + $CertificateThumbprint)
if (-not $taskCertificate.HasPrivateKey) { throw 'Для подписи нужен закрытый ключ в личном хранилище Windows.' }
if ((Get-Date) -lt $taskCertificate.NotBefore -or (Get-Date) -ge $taskCertificate.NotAfter) { throw 'Сертификат ещё не действует либо срок его действия истёк.' }
if (-not ($taskCertificate.EnhancedKeyUsageList | Where-Object { [string]$_.ObjectId -eq '1.3.6.1.5.5.7.3.3' })) { throw 'Сертификат не предназначен для подписи программ.' }
foreach ($taskInput in $FilePath) {
    $taskFile = (Get-Item -LiteralPath $taskInput).FullName
    if ([IO.Path]::GetExtension($taskFile) -notin @('.exe', '.dll', '.ps1')) { throw 'Поддерживаются .exe, .dll и .ps1.' }
    $taskTemporary = Join-Path (Split-Path $taskFile -Parent) ('.sign-' + [Guid]::NewGuid().ToString('N') + [IO.Path]::GetExtension($taskFile))
    try {
        Copy-Item -LiteralPath $taskFile -Destination $taskTemporary
        $taskArguments = @{ LiteralPath = $taskTemporary; Certificate = $taskCertificate; HashAlgorithm = 'SHA256'; IncludeChain = 'Signer' }
        if (-not [string]::IsNullOrWhiteSpace($TimestampServer)) { $taskArguments.TimestampServer = $TimestampServer }
        $null = Set-AuthenticodeSignature @taskArguments
        $taskSignature = Get-AuthenticodeSignature -LiteralPath $taskTemporary
        if (-not $taskSignature.SignerCertificate -or $taskSignature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) { throw 'В файле не обнаружена ожидаемая подпись.' }
        # Untrusted self-signed certificates stay untrusted; do not change Windows stores.
        $taskVerification = & (Join-Path $PSScriptRoot 'Test-FileSignature.ps1') -FilePath $taskTemporary -CertificateThumbprint $CertificateThumbprint
        if (-not [string]::IsNullOrWhiteSpace($TimestampServer) -and -not $taskSignature.TimeStamperCertificate) { throw 'Сервер не выдал метку времени. Исходный файл не изменён. Для подписи без метки передай -TimestampServer "".' }
        Copy-Item -LiteralPath $taskTemporary -Destination $taskFile -Force
        Write-Output ('Подписано SHA-256: ' + $taskFile)
        Write-Output ('Издатель: ' + $taskSignature.SignerCertificate.Subject)
        Write-Output ('Целостность подписи проверена; доверие Windows: ' + $taskVerification.TrustedByWindows + '; метка времени: ' + [bool]$taskSignature.TimeStamperCertificate)
    }
    finally { if (Test-Path -LiteralPath $taskTemporary) { Remove-Item -LiteralPath $taskTemporary -Force } }
}
