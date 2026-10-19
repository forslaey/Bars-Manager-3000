[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskCache = Join-Path $env:LOCALAPPDATA 'BARS\BuildReferences\net46-1.0.3'
$taskRef = Join-Path $taskCache 'build\.NETFramework\v4.6'
if (Test-Path -LiteralPath (Join-Path $taskRef 'mscorlib.dll')) { Write-Output $taskRef; return }
$taskZip = Join-Path ([IO.Path]::GetTempPath()) ('bars-net46-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net46/1.0.3/microsoft.netframework.referenceassemblies.net46.1.0.3.nupkg' -OutFile $taskZip
    New-Item -ItemType Directory -Path $taskCache -Force | Out-Null
    Expand-Archive -LiteralPath $taskZip -DestinationPath $taskCache -Force
    if (-not (Test-Path -LiteralPath (Join-Path $taskRef 'mscorlib.dll'))) { throw 'Не найдены reference assemblies .NET 4.6.' }
    Write-Output $taskRef
}
finally { if (Test-Path -LiteralPath $taskZip) { Remove-Item -LiteralPath $taskZip -Force } }
