[CmdletBinding()]
param([Parameter(Mandatory = $true)] [string] $Destination)
$ErrorActionPreference = 'Stop'
$taskSettings = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'BARS\Bars Service Manager 3000\signing.json') -Raw | ConvertFrom-Json
$taskCertificate = Get-Item -LiteralPath ('Cert:\CurrentUser\My\' + $taskSettings.Thumbprint)
if (Test-Path -LiteralPath $Destination) { throw 'Файл уже существует. Выбери новый путь.' }
$taskPassword = Read-Host 'Пароль резервной копии ключа (не отправляй его в чат)' -AsSecureString
if ($taskPassword.Length -lt 12) { $taskPassword.Dispose(); throw 'Используй пароль длиной не менее 12 символов.' }
try { Export-PfxCertificate -Cert $taskCertificate -FilePath ([IO.Path]::GetFullPath($Destination)) -Password $taskPassword -ChainOption EndEntityCertOnly -CryptoAlgorithmOption AES256_SHA256 | Out-Null }
finally { $taskPassword.Dispose() }
Write-Output 'Создан защищённый PFX. Храни отдельно от программы; никому не передавай файл и пароль.'
