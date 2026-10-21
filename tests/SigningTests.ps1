param([Parameter(Mandatory = $true)] [string] $SignedExecutable, [string] $CertificateThumbprint)
$ErrorActionPreference = 'Stop'
$taskProject = Split-Path $PSScriptRoot -Parent
$taskVerify = Join-Path $taskProject 'tools\Test-FileSignature.ps1'
$taskRoot = Join-Path ([IO.Path]::GetTempPath()) ('bars-sign-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRoot | Out-Null
$taskChecks = 0
try {
    $taskFiles = @((Get-Item -LiteralPath $SignedExecutable).FullName)
    $taskAssembly = [Reflection.Assembly]::ReflectionOnlyLoad([IO.File]::ReadAllBytes($taskFiles[0]))
    foreach ($taskArchitecture in @('x86', 'x64')) {
        $taskHelper = Join-Path $taskRoot ("Installer.$taskArchitecture.exe")
        $taskResource = $taskAssembly.GetManifestResourceStream("BARS.Helper.$taskArchitecture")
        $taskOutput = [IO.File]::Create($taskHelper)
        try { $taskResource.CopyTo($taskOutput) } finally { $taskResource.Dispose(); $taskOutput.Dispose() }
        $taskFiles += $taskHelper
    }
    $taskExpected = $CertificateThumbprint
    if ([string]::IsNullOrWhiteSpace($taskExpected)) { $taskExpected = (Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'BARS\Bars Service Manager 3000\signing.json') -Raw | ConvertFrom-Json).Thumbprint }
    $taskTrustBefore = @{}
    foreach ($taskHive in @('CurrentUser','LocalMachine')) { foreach ($taskStore in @('Root','TrustedPublisher')) {
        $taskStorePath = "Cert:\$taskHive\$taskStore\$taskExpected"
        $taskTrustBefore[$taskStorePath] = Test-Path -LiteralPath $taskStorePath
    } }
    foreach ($taskFile in $taskFiles) {
        $taskResult = & $taskVerify -FilePath $taskFile -CertificateThumbprint $taskExpected
        if (-not $taskResult.SignatureChecked -or -not $taskResult.Timestamped) { throw 'Отсутствует подпись или метка времени.' }
        $taskChecks++
        $taskWrongSignerRejected = $false
        try { $null = & $taskVerify -FilePath $taskFile -CertificateThumbprint ('0' * 40) } catch { $taskWrongSignerRejected = $true }
        if (-not $taskWrongSignerRejected) { throw 'Чужой сертификат ошибочно принят.' }
        $taskChecks++
        $taskTampered = Join-Path $taskRoot ('tampered-' + [IO.Path]::GetFileName($taskFile))
        $taskBytes = [IO.File]::ReadAllBytes($taskFile)
        $taskBytes[1024] = $taskBytes[1024] -bxor 1
        [IO.File]::WriteAllBytes($taskTampered, $taskBytes)
        $taskTamperingRejected = $false
        try { $null = & $taskVerify -FilePath $taskTampered -CertificateThumbprint $taskExpected } catch { $taskTamperingRejected = $true }
        if (-not $taskTamperingRejected) { throw 'Изменённый после подписи файл ошибочно принят.' }
        $taskChecks++
        Write-Output ('PASS: подпись, метка времени и отказ для повреждённого файла: ' + [IO.Path]::GetFileName($taskFile))
    }
    foreach ($taskStorePath in $taskTrustBefore.Keys) {
        if ((Test-Path -LiteralPath $taskStorePath) -ne $taskTrustBefore[$taskStorePath]) { throw 'Состояние доверенного хранилища изменилось во время проверки.' }
        $taskChecks++
    }
    Write-Output ("PASS: $taskChecks проверок подписи. Реальные службы и доверенные хранилища не изменялись.")
}
finally {
    # Only explicitly created fixture names inside this unique directory are deleted.
    foreach ($taskFixture in @('Installer.x86.exe','Installer.x64.exe','tampered-Installer.x86.exe','tampered-Installer.x64.exe',('tampered-' + [IO.Path]::GetFileName($SignedExecutable)))) {
        $taskPath = Join-Path $taskRoot $taskFixture
        if (Test-Path -LiteralPath $taskPath) { Remove-Item -LiteralPath $taskPath -Force }
    }
    Remove-Item -LiteralPath $taskRoot
}
