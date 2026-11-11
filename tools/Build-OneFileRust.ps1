param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$Cargo = 'cargo.exe',
    [string]$ResourceCompiler = 'rsrc.exe',
    [switch]$Sign,
    [string]$CertificateThumbprint,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$localTools = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Codex'
$localRust = Join-Path $localTools 'bars-rust-toolchain'
$localCargo = Join-Path $localRust 'cargo\bin\cargo.exe'
$useLocalRust = $Cargo -eq 'cargo.exe' -and !(Get-Command $Cargo -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath $localCargo)
if ($useLocalRust) { $Cargo = $localCargo }
if (!(Get-Command $Cargo -ErrorAction SilentlyContinue)) { throw 'Установи Rust с целью x86_64-pc-windows-gnu или укажи путь через -Cargo.' }
if ($ResourceCompiler -eq 'rsrc.exe' -and !(Get-Command $ResourceCompiler -ErrorAction SilentlyContinue)) {
    $localResourceCompiler = Join-Path $localTools 'bars-go-toolchain\bin\rsrc.exe'
    if (Test-Path -LiteralPath $localResourceCompiler) { $ResourceCompiler = $localResourceCompiler }
}
if (!(Get-Command $ResourceCompiler -ErrorAction SilentlyContinue)) { throw 'Укажи путь к rsrc.exe через -ResourceCompiler.' }
$stage = Join-Path ([IO.Path]::GetTempPath()) ('bars-rust-build-' + [guid]::NewGuid())
$previous = @{BARS_EMBEDDED_MANAGER=$env:BARS_EMBEDDED_MANAGER; RUSTFLAGS=$env:RUSTFLAGS; CARGO_TARGET_DIR=$env:CARGO_TARGET_DIR; RUSTUP_HOME=$env:RUSTUP_HOME; CARGO_HOME=$env:CARGO_HOME}
[IO.Directory]::CreateDirectory($stage) | Out-Null
try {
    if ($useLocalRust) {
        $env:RUSTUP_HOME = Join-Path $localRust 'rustup'
        $env:CARGO_HOME = Join-Path $localRust 'cargo'
    }
    $options = @{OutputDirectory=$stage; Sign=$Sign; CertificateThumbprint=$CertificateThumbprint; TimestampServer=$TimestampServer}
    & (Join-Path $project 'Build.ps1') @options
    $manager = Get-ChildItem -LiteralPath $stage -Filter 'Bars Manager 3000 *.exe' | Select-Object -First 1
    if (!$manager) { throw 'Не найден EXE встроенного менеджера.' }
    $resource = Join-Path $stage 'bars.syso'
    & $ResourceCompiler -arch amd64 -ico (Join-Path $project 'assets\Bars.ico') -o $resource
    if ($LASTEXITCODE) { throw 'Ошибка сборки значка.' }
    $env:BARS_EMBEDDED_MANAGER = $manager.FullName
    $env:CARGO_TARGET_DIR = Join-Path $stage 'target'
    $env:RUSTFLAGS = '-Clinker=rust-lld -Clink-self-contained=yes -Clink-arg=' + $resource
    & $Cargo build --manifest-path (Join-Path $PSScriptRoot 'RustLauncher\Cargo.toml') --target x86_64-pc-windows-gnu --release --locked
    if ($LASTEXITCODE) { throw 'Ошибка сборки запускателя Rust.' }
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    [IO.Directory]::CreateDirectory($output) | Out-Null
    $launcher = Join-Path $env:CARGO_TARGET_DIR 'x86_64-pc-windows-gnu\release\bars-framework-launcher.exe'
    & (Join-Path $PSScriptRoot 'Copy-VersionResource.ps1') -Source $manager.FullName -Target $launcher
    if ($Sign) {
        & (Join-Path $PSScriptRoot 'Sign-File.ps1') -FilePath $launcher -CertificateThumbprint $CertificateThumbprint -TimestampServer $TimestampServer
    }
    Copy-Item -LiteralPath $launcher -Destination (Join-Path $output $manager.Name) -Force
}
finally {
    $env:BARS_EMBEDDED_MANAGER = $previous.BARS_EMBEDDED_MANAGER
    $env:RUSTFLAGS = $previous.RUSTFLAGS
    $env:RUSTUP_HOME = $previous.RUSTUP_HOME
    $env:CARGO_HOME = $previous.CARGO_HOME
    $env:CARGO_TARGET_DIR = $previous.CARGO_TARGET_DIR
    if ([IO.Path]::GetDirectoryName($stage).TrimEnd('\') -eq ([IO.Path]::GetTempPath()).TrimEnd('\') -and [IO.Path]::GetFileName($stage) -like 'bars-rust-build-*') {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}