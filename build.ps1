<#
.SYNOPSIS
    Builds the two AltSpacy downloads into dist\release.

.DESCRIPTION
    1. Runs the tests (skip with -SkipTests).
    2. Publishes both exes:
         AltSpacy-<version>-x64-dotnet10.exe     small, uses the installed .NET 10+ Desktop Runtime
         AltSpacy-<version>-x64-standalone.exe   includes the runtime, nothing to install
    3. Starts each one in a no-UI mode to check it actually runs.
    4. Writes SHA256SUMS.txt and release-notes.md (the GitHub release text).

    Works in Windows PowerShell 5.1 and PowerShell 7. CI runs it from .github/workflows/release.yml.

.EXAMPLE
    ./build.ps1
.EXAMPLE
    ./build.ps1 -SkipTests
#>
[CmdletBinding()]
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$project = Join-Path $root 'src\AltSpacy\AltSpacy.csproj'
$dist = Join-Path $root 'dist'
$release = Join-Path $dist 'release'

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE." }
}

function Get-ReleaseNote([string]$version) {
    # ReleaseNotes.All in SmartInput.cs is the single source (also shown in About). Entries look like
    #   new("1.5.0", "text " +
    #             "more text"),
    $source = Get-Content (Join-Path $root 'src\AltSpacy\SmartInput.cs') -Raw
    $entry = [regex]::Match($source, 'new\("' + [regex]::Escape($version) + '",(?<body>(?:\s*\+?\s*"(?:[^"\\]|\\.)*")+)\s*\)')
    if (-not $entry.Success) { throw "No ReleaseNotes entry for $version in SmartInput.cs." }
    $parts = [regex]::Matches($entry.Groups['body'].Value, '"((?:[^"\\]|\\.)*)"') | ForEach-Object { $_.Groups[1].Value }
    return (($parts -join '') -replace '\\(.)', '$1')
}

function Test-Exe([string]$exe) {
    # --export-icon writes the app icon and exits before any window, tray icon or single-instance check:
    # proof that the runtime, WPF and the app assembly all load.
    $ico = Join-Path ([IO.Path]::GetTempPath()) ("altspacy-smoke-" + [guid]::NewGuid().ToString('N') + '.ico')
    try {
        $process = Start-Process -FilePath $exe -ArgumentList '--export-icon', "`"$ico`"" -PassThru -Wait
        if ($process.ExitCode -ne 0 -or -not (Test-Path $ico)) { throw "$exe did not start (exit code $($process.ExitCode))." }
    }
    finally { Remove-Item $ico -ErrorAction SilentlyContinue }
}

$version = (Select-Xml -Path $project -XPath '//Version').Node.InnerText
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Unexpected <Version> '$version' in AltSpacy.csproj." }
$notes = Get-ReleaseNote $version
Write-Host "AltSpacy $version" -ForegroundColor Cyan

$running = Get-Process AltSpacy -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($dist, [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw "AltSpacy is running from $dist. Exit it first (type :exit in the bar)." }

if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    Invoke-Dotnet test (Join-Path $root 'AltSpacy.slnx') --configuration Release
}

$builds = @(
    @{ Profile = 'FrameworkDependent'; Folder = 'framework-dependent'; Asset = "AltSpacy-$version-x64-dotnet10.exe" },
    @{ Profile = 'SelfContained';      Folder = 'self-contained';      Asset = "AltSpacy-$version-x64-standalone.exe" }
)

foreach ($folder in @($release) + ($builds | ForEach-Object { Join-Path $dist $_.Folder })) {
    if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
}
New-Item -ItemType Directory -Path $release | Out-Null

foreach ($build in $builds) {
    Write-Host "Publishing $($build.Asset)..." -ForegroundColor Cyan
    Invoke-Dotnet publish $project "-p:PublishProfile=$($build.Profile)"
    $exe = Join-Path $dist "$($build.Folder)\AltSpacy.exe"
    Test-Exe $exe
    Copy-Item $exe (Join-Path $release $build.Asset)
}

$assets = $builds | ForEach-Object { Get-Item (Join-Path $release $_.Asset) }
$sums = $assets | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" }
# LF line endings so `sha256sum -c SHA256SUMS.txt` works on Linux, macOS and Git Bash too.
[IO.File]::WriteAllText((Join-Path $release 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"))

function Format-Size($file) {
    if ($file.Length -ge 1MB) { return '{0:N0} MB' -f ($file.Length / 1MB) }
    return '{0:N1} MB' -f ($file.Length / 1MB)
}
$small = $assets[0]; $standalone = $assets[1]

$body = @"
## What's new

$notes

## Download

| File | Size | Pick it if |
|---|---|---|
| ``$($small.Name)`` | $(Format-Size $small) | You have the .NET 10 Desktop Runtime (or newer) installed. |
| ``$($standalone.Name)`` | $(Format-Size $standalone) | You don't, or you're not sure. Nothing else to install. |

Not sure which you have? Run ``dotnet --list-runtimes`` in a terminal. A line starting with
``Microsoft.WindowsDesktop.App 10.`` (or a higher number) means the small download will work. If it can't find
.NET, Windows shows a dialog with a link to install the runtime.

Both are portable: save the exe anywhere and run it, then press **Alt+Space**. Settings live in ``%APPDATA%\AltSpacy``.
Windows SmartScreen may warn about an unrecognized app because the exe isn't code-signed: choose
**More info > Run anyway**. Verify the download against ``SHA256SUMS.txt``.
"@
[IO.File]::WriteAllText((Join-Path $release 'release-notes.md'), $body.Replace("`r`n", "`n"))

Write-Host ''
Write-Host "Done: $release" -ForegroundColor Green
Get-ChildItem $release | Format-Table Name, @{ Name = 'Size'; Expression = { Format-Size $_ } } -AutoSize
