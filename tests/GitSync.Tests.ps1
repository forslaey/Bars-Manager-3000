# Integration checks use only a disposable local Git server; no builds or GitHub writes.
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$syncScript = Join-Path $sourceRoot 'Git-Sync.ps1'
$lab = Join-Path $sourceRoot ('work\git-sync-tests-' + [Guid]::NewGuid().ToString('N'))
$server = Join-Path $lab 'server.git'
$first = Join-Path $lab 'computer one'
$second = Join-Path $lab 'computer two'
$script:checks = 0
New-Item -ItemType Directory -Path $lab -Force | Out-Null
$git = (Get-Command git.exe).Source
$oldGlobal = $env:GIT_CONFIG_GLOBAL
$oldSystem = $env:GIT_CONFIG_NOSYSTEM
$oldPrompt = $env:GIT_TERMINAL_PROMPT
$env:GIT_CONFIG_GLOBAL = Join-Path $lab 'empty-git-config'
$env:GIT_CONFIG_NOSYSTEM = '1'
$env:GIT_TERMINAL_PROMPT = '0'
[IO.File]::WriteAllText($env:GIT_CONFIG_GLOBAL, '')
function G {
    param([string[]]$A)
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & $git @A 2>&1
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    if ($code -ne 0) { throw ($output -join "`n") }
    return $output
}
function Check {
    param([bool]$Condition, [string]$Name)
    if (!$Condition) { throw "FAIL: $Name" }
    $script:checks++
    Write-Host "PASS: $Name"
}
function Sync {
    param([string]$Folder, [string]$Mode, [int]$Expected = 0, [string]$InputText = '', [switch]$Cmd)
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = 'powershell.exe'
    $info.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $syncScript + '" -Action ' + $Mode + ' -ProjectPath "' + $Folder + '" -RepositoryUrl "' + $server + '"'
    if ($Mode -eq 'Upload' -and !$InputText) { $info.Arguments += ' -Yes -Message "integration test"' }
    if ($Cmd) {
        $info.FileName = 'cmd.exe'
        $info.Arguments = '/d /c ""' + (Join-Path $sourceRoot ('Git-' + $Mode + '.cmd')) + '" -ProjectPath "' + $Folder + '""'
    }
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.RedirectStandardInput = $true
    $process = [Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if ($InputText) { $process.StandardInput.WriteLine($InputText) }
    $process.StandardInput.Close()
    if (!$process.WaitForExit(30000)) { $process.Kill(); throw 'Sync test timed out' }
    $output = $stdout.Result + $stderr.Result
    if ($process.ExitCode -ne $Expected) { throw "Unexpected sync exit $($process.ExitCode), expected $Expected`n$output" }
    $process.Dispose()
}
try {
    G -A @('init', '--bare', '--initial-branch=main', $server) | Out-Null
    G -A @('init', '--initial-branch=main', $first) | Out-Null
    G -A @('-C', $first, 'config', 'user.name', 'Sync Test') | Out-Null
    G -A @('-C', $first, 'config', 'user.email', 'sync-test@example.invalid') | Out-Null
    G -A @('-C', $first, 'remote', 'add', 'origin', $server) | Out-Null
    [IO.File]::WriteAllText((Join-Path $first '.gitignore'), "work/`n*.pfx`n")
    [IO.File]::WriteAllText((Join-Path $first 'project.txt'), 'initial')
    G -A @('-C', $first, 'add', '--all') | Out-Null
    G -A @('-C', $first, 'commit', '-m', 'initial') | Out-Null
    G -A @('-C', $first, 'push', '-u', 'origin', 'main') | Out-Null
    Sync -Folder $second -Mode Download
    Check (Test-Path (Join-Path $second 'project.txt')) 'Initial clone into a new folder'
    G -A @('-C', $second, 'config', 'user.name', 'Sync Test') | Out-Null
    G -A @('-C', $second, 'config', 'user.email', 'sync-test@example.invalid') | Out-Null
    [IO.File]::WriteAllText((Join-Path $first 'project.txt'), 'updated')
    [IO.File]::WriteAllText((Join-Path $first 'файл с пробелами.txt'), 'unicode')
    [IO.File]::WriteAllText((Join-Path $first 'secret.pfx'), 'ignored test fixture')
    Sync -Folder $first -Mode Upload -InputText 'NO'
    Check (@(G -A @('-C', $first, 'diff', '--cached', '--name-only')).Count -eq 0) 'Cancel leaves the index untouched'
    Sync -Folder $first -Mode Upload
    Sync -Folder $second -Mode Download
    Check ([IO.File]::ReadAllText((Join-Path $second 'project.txt')) -eq 'updated') 'Upload and fast-forward download'
    Check (Test-Path (Join-Path $second 'файл с пробелами.txt')) 'Unicode filename and paths containing spaces'
    Check (!(Test-Path (Join-Path $second 'secret.pfx'))) 'Ignored secrets are not uploaded'
    [IO.File]::WriteAllText((Join-Path $second 'local.txt'), 'keep')
    Sync -Folder $second -Mode Download -Expected 1
    Check ([IO.File]::ReadAllText((Join-Path $second 'local.txt')) -eq 'keep') 'Dirty download refuses and preserves untracked files'
    Sync -Folder $second -Mode Upload
    Sync -Folder $first -Mode Download
    Check (Test-Path (Join-Path $first 'local.txt')) 'Reverse transfer from computer two to one'
    Remove-Item -LiteralPath (Join-Path $first 'project.txt')
    Sync -Folder $first -Mode Upload
    Sync -Folder $second -Mode Download
    Check (!(Test-Path (Join-Path $second 'project.txt'))) 'Tracked deletion is transferred'
    $before = [string](G -A @('-C', $first, 'rev-parse', 'HEAD'))
    Sync -Folder $first -Mode Upload
    Check (([string](G -A @('-C', $first, 'rev-parse', 'HEAD'))) -eq $before) 'No-op upload creates no empty commit'
    Sync -Folder $first -Mode Upload -Cmd
    Sync -Folder $first -Mode Download -Cmd
    Check $true 'Both CMD launchers run successfully and finish their pause with redirected input'
    G -A @('-C', $first, 'remote', 'set-url', '--push', 'origin', (Join-Path $lab 'missing.git')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $first 'retry.txt'), 'retry')
    Sync -Folder $first -Mode Upload -Expected 1
    Check (([string](G -A @('-C', $first, 'rev-parse', 'HEAD'))) -ne $before) 'Failed push keeps the newly created local commit'
    G -A @('-C', $first, 'remote', 'set-url', '--push', 'origin', $server) | Out-Null
    Sync -Folder $first -Mode Upload
    Sync -Folder $second -Mode Download
    Check (Test-Path (Join-Path $second 'retry.txt')) 'Retry uploads an existing commit without creating another'
    $before = [string](G -A @('-C', $first, 'rev-parse', 'HEAD'))
    G -A @('-C', $first, 'add', '-f', 'secret.pfx') | Out-Null
    Sync -Folder $first -Mode Upload -Expected 1
    Check (([string](G -A @('-C', $first, 'rev-parse', 'HEAD'))) -eq $before) 'Already staged private key blocks publication'
    G -A @('-C', $first, 'reset', '--', 'secret.pfx') | Out-Null
    G -A @('-C', $first, 'switch', '-c', 'other') | Out-Null
    Sync -Folder $first -Mode Upload -Expected 1
    Check (([string](G -A @('-C', $first, 'branch', '--show-current'))) -eq 'other') 'Wrong branch refuses without switching'
    G -A @('-C', $first, 'switch', 'main') | Out-Null
    [IO.File]::WriteAllText((Join-Path $first 'first.txt'), 'one')
    Sync -Folder $first -Mode Upload
    [IO.File]::WriteAllText((Join-Path $second 'second.txt'), 'two')
    G -A @('-C', $second, 'add', '--all') | Out-Null
    G -A @('-C', $second, 'commit', '-m', 'divergent') | Out-Null
    $before = [string](G -A @('-C', $second, 'rev-parse', 'HEAD'))
    Sync -Folder $second -Mode Download -Expected 1
    Sync -Folder $second -Mode Upload -Expected 1
    Check (([string](G -A @('-C', $second, 'rev-parse', 'HEAD'))) -eq $before) 'Diverged history refuses both directions without resetting'
    Sync -Folder $second -Mode Download -Expected 1 -Cmd
    Check $true 'CMD launcher preserves a nonzero exit code on failure'
    Write-Host "Completed: $script:checks checks. Fixtures: $lab"
}
finally {
    $env:GIT_CONFIG_GLOBAL = $oldGlobal
    $env:GIT_CONFIG_NOSYSTEM = $oldSystem
    $env:GIT_TERMINAL_PROMPT = $oldPrompt
}
