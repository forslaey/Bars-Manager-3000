[CmdletBinding()]
param([Parameter(Mandatory = $true)] [string] $ScriptFile)
$ErrorActionPreference = 'Stop'
if (-not (Get-Command Invoke-Command).Parameters.ContainsKey('VMName')) { throw 'Run this script with Windows PowerShell 5.1 (powershell.exe), not pwsh.exe.' }
$vm = Get-VM -Name 'BARS-Test-W11'
if ($vm.State -ne 'Running' -or $vm.Notes -notlike '*Dedicated BARS disposable test VM*') { throw 'Dedicated test VM is unavailable.' }
$credential = Import-Clixml -LiteralPath 'C:\BARS-TestLab\BARS-Test-W11\guest-credential.xml'
Invoke-Command -VMId $vm.Id -Credential $credential -FilePath (Get-Item -LiteralPath $ScriptFile).FullName
