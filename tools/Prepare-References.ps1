[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskProgramFiles = [Environment]::GetFolderPath('ProgramFilesX86')
if ($taskProgramFiles) {
    $taskInstalled = Join-Path $taskProgramFiles 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
    if (Test-Path -LiteralPath (Join-Path $taskInstalled 'mscorlib.dll')) { Write-Output $taskInstalled; return }
}
$taskCache = Join-Path $env:LOCALAPPDATA 'BARS\BuildReferences\net48-1.0.3'
$taskRef = Join-Path $taskCache 'build\.NETFramework\v4.8'
if (Test-Path -LiteralPath (Join-Path $taskRef 'mscorlib.dll')) { Write-Output $taskRef; return }
$taskZip = Join-Path ([IO.Path]::GetTempPath()) ('bars-net48-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net48/1.0.3/microsoft.netframework.referenceassemblies.net48.1.0.3.nupkg' -OutFile $taskZip
    New-Item -ItemType Directory -Path $taskCache -Force | Out-Null
    Expand-Archive -LiteralPath $taskZip -DestinationPath $taskCache -Force
    if (-not (Test-Path -LiteralPath (Join-Path $taskRef 'mscorlib.dll'))) { throw 'Не найдены reference assemblies .NET 4.8.' }
    Write-Output $taskRef
}
catch { throw "Не удалось подготовить эталонные сборки .NET Framework 4.8. Для офлайн-сборки укажи каталог reference assemblies через -ReferenceAssemblies. $($_.Exception.Message)" }
finally { if (Test-Path -LiteralPath $taskZip) { Remove-Item -LiteralPath $taskZip -Force } }
