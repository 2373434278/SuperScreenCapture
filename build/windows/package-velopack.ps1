param(
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [string]$Version = "1.0.0",
    [string]$PackId = "SuperScreenCapture"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

if (-not (Test-Path (Join-Path $PublishDir "SSC.App.exe"))) {
    throw "Published executable not found: $PublishDir\SSC.App.exe"
}

Copy-Item -Path (Join-Path $repoRoot "LICENSE") -Destination $PublishDir -Force
Copy-Item -Path (Join-Path $repoRoot "NOTICE") -Destination $PublishDir -Force

$notices = Join-Path $repoRoot "build\third-party-notices.txt"
if (Test-Path $notices) {
    Copy-Item -Path $notices -Destination (Join-Path $PublishDir "THIRD-PARTY-NOTICES.txt") -Force
}

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "Installing Velopack CLI"
    dotnet tool install --global vpk
    $env:PATH = "$env:PATH;$env:USERPROFILE\.dotnet\tools"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

vpk pack `
    --packId $PackId `
    --packVersion $Version `
    --packDir $PublishDir `
    --mainExe "SSC.App.exe" `
    --packTitle "SuperScreenCapture" `
    --packAuthors "SuperScreenCapture contributors" `
    --outputDir $OutputDir

if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed with exit code $LASTEXITCODE"
}

Get-ChildItem -Path $OutputDir -File | Where-Object { $_.Name -notlike "*.sha256" } | ForEach-Object {
    $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)" | Set-Content -Path "$($_.FullName).sha256" -Encoding ascii
}

Write-Host "Created installer artifacts in $OutputDir"
