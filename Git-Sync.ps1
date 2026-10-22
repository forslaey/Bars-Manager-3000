# Windows PowerShell 5.1. CMD entry points keep the result visible after double-click.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Upload', 'Download')][string]$Action,
    [string]$ProjectPath,
    [string]$RepositoryUrl = 'https://github.com/forslaey/Bars-Manager-3000.git',
    [string]$Message,
    [switch]$Yes
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectPath)) { $ProjectPath = $PSScriptRoot }

function Invoke-Git {
    param([string[]]$GitArguments)
    # Native stderr is diagnostic text; the exit code determines success in PS 5.1.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $script:gitExe @GitArguments 2>&1 | ForEach-Object { $_.ToString() })
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    if ($code -ne 0) {
        throw ("Git завершился с кодом {0}: git {1}`n{2}" -f $code, ($GitArguments -join ' '), ($lines -join "`n"))
    }
    return $lines
}

function Project-Git {
    param([string[]]$GitArguments)
    Invoke-Git -GitArguments (@('-C', $script:projectRoot) + $GitArguments)
}

try {
    $gitCommand = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($gitCommand) { $script:gitExe = $gitCommand.Source }
    else {
        $candidates = @(
            "$env:ProgramFiles\Git\cmd\git.exe",
            "${env:ProgramFiles(x86)}\Git\cmd\git.exe",
            "$env:LOCALAPPDATA\Programs\Git\cmd\git.exe",
            "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\git\cmd\git.exe"
        )
        $script:gitExe = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
    if (!$script:gitExe) { throw 'Git не найден. Установи Git for Windows с git-scm.com, затем повтори запуск.' }
    $script:projectRoot = [IO.Path]::GetFullPath($ProjectPath).TrimEnd('\', '/')
    if (!(Test-Path -LiteralPath (Join-Path $projectRoot '.git'))) {
        if ($Action -ne 'Download') { throw 'Здесь ещё нет репозитория Git. Сначала скачай проект.' }
        if ((Test-Path -LiteralPath $projectRoot) -and
            @(Get-ChildItem -LiteralPath $projectRoot -Force).Count -gt 0) {
            throw "Папка $projectRoot не пуста и не содержит .git. Существующие файлы сохранены. Для первого скачивания укажи новую пустую папку через -ProjectPath."
        }
        Invoke-Git -GitArguments @('clone', '--branch', 'main', '--single-branch', '--', $repositoryUrl, $projectRoot) | Write-Host
        Write-Host "Проект скачан: $projectRoot" -ForegroundColor Green
        exit 0
    }
    $actualRoot = [string](Project-Git -GitArguments @('rev-parse', '--show-toplevel'))
    if ([IO.Path]::GetFullPath($actualRoot).TrimEnd('\', '/') -ne $projectRoot) {
        throw 'Укажи корневую папку проекта, а не вложенную папку.'
    }
    $branch = [string](Project-Git -GitArguments @('symbolic-ref', '--quiet', '--short', 'HEAD'))
    if ($branch -ne 'main') { throw "Открыта ветка $branch. Эти скрипты работают только с main. Файлы не изменены." }
    foreach ($marker in @('MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'rebase-merge', 'rebase-apply', 'BISECT_LOG')) {
        $markerPath = [string](Project-Git -GitArguments @('rev-parse', '--git-path', $marker))
        if (![IO.Path]::IsPathRooted($markerPath)) { $markerPath = Join-Path $projectRoot $markerPath }
        if (Test-Path -LiteralPath $markerPath) { throw 'Сначала заверши текущую операцию Git (слияние, rebase или другую).' }
    }
    if (@(Project-Git -GitArguments @('ls-files', '--unmerged')).Count -gt 0) { throw 'В проекте есть неразрешённые конфликты Git.' }
    $remote = [string](Project-Git -GitArguments @('remote', 'get-url', 'origin'))
    Write-Host "Проект: $projectRoot`nВетка: main`nСервер: $remote"
    $changes = @(Project-Git -GitArguments @('-c', 'core.quotepath=false', 'status', '--short', '--untracked-files=all'))
    if ($Action -eq 'Download' -and $changes.Count -gt 0) {
        $changes | Write-Host
        throw 'Скачивание остановлено: есть локальные изменения. Сначала выгрузи их с этого компьютера. Если на GitHub тоже есть новые изменения, потребуется согласовать их вручную. Ничего не удалено.'
    }
    Project-Git -GitArguments @('fetch', 'origin', 'refs/heads/main:refs/remotes/origin/main') | Write-Host
    $counts = ([string](Project-Git -GitArguments @('rev-list', '--left-right', '--count', 'HEAD...refs/remotes/origin/main'))).Trim() -split '\s+'
    $ahead = [int]$counts[0]
    $behind = [int]$counts[1]
    if ($ahead -gt 0 -and $behind -gt 0) {
        throw 'На обоих компьютерах появились отдельные коммиты: история разошлась. Нужна ручная проверка и слияние. Скрипт сохранил все файлы и коммиты; force push не применяется.'
    }
    if ($Action -eq 'Download') {
        if ($behind -gt 0) { Project-Git -GitArguments @('merge', '--ff-only', 'refs/remotes/origin/main') | Write-Host }
        if ($ahead -gt 0) { Write-Host "Есть локальные коммиты ($ahead), которые ещё не отправлены. Запусти Git-Upload.cmd." -ForegroundColor Yellow }
        else { Write-Host 'Скачивание завершено. Проект соответствует main на сервере.' -ForegroundColor Green }
        exit 0
    }
    if ($behind -gt 0) { throw 'На GitHub есть новые коммиты. Сначала выполни скачивание. При наличии локальных изменений сначала потребуется согласовать их вручную; скрипт их не стирает.' }
    # Check even tracked files: .gitignore alone does not protect files already in Git.
    $files = @(Project-Git -GitArguments @('-c', 'core.quotepath=false', 'ls-files', '--cached', '--others', '--exclude-standard'))
    foreach ($file in $files) {
        if (!(Test-Path -LiteralPath (Join-Path $projectRoot $file) -PathType Leaf)) { continue }
        if ($file -match '(?i)(^|/)(bin|obj|work|signed|\.vs|Библиотеки|LibraryReports)/' -or
            $file -match '(?i)\.(pfx|p12|key|snk|log|InstallLog|InstallState)$' -or
            $file -match '(?i)(^|/)(\.env(\..*)?|signing\.json|services\.xml|install-profiles\.xml|theme\.txt|library-catalog\.xml|library-source\.txt)$') {
            throw "В Git обнаружен локальный или закрытый файл: $file. Отправка остановлена. Проверь .gitignore и список отслеживаемых файлов."
        }
    }
    if ($changes.Count -eq 0 -and $ahead -eq 0) {
        Write-Host 'Выгружать нечего: проект уже синхронизирован.' -ForegroundColor Green
        exit 0
    }
    Write-Host "`nБудут отправлены все текущие изменения проекта и локальные коммиты ($ahead). Удаления файлов тоже входят в коммит."
    $changes | Write-Host
    if (!$Yes) {
        $answer = Read-Host 'Для подтверждения введи YES (любой другой ответ — отмена)'
        if ($answer -cne 'YES') { Write-Host 'Отменено. Файлы и индекс не изменены.'; exit 0 }
    }
    if ($changes.Count -gt 0) {
        if ([string]::IsNullOrWhiteSpace($Message) -and !$Yes) { $Message = Read-Host 'Описание изменений (Enter — дата и имя компьютера)' }
        if ([string]::IsNullOrWhiteSpace($Message)) { $Message = 'Ручная синхронизация: ' + $env:COMPUTERNAME + ', ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') }
        Project-Git -GitArguments @('add', '--all') | Write-Host
        Project-Git -GitArguments @('commit', '-m', $Message) | Write-Host
    }
    Project-Git -GitArguments @('push', 'origin', 'HEAD:refs/heads/main') | Write-Host
    Write-Host 'Выгрузка завершена. На другом компьютере запусти Git-Download.cmd.' -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "`nОШИБКА: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Текущие файлы не удаляются. Если коммит уже создан, он остаётся локально: после устранения причины повтори выгрузку.'
    exit 1
}
