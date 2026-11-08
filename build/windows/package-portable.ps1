param(
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [string]$Rid = "win-x64",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

if (-not (Test-Path (Join-Path $PublishDir "SSC.App.exe"))) {
    throw "Published executable not found: $PublishDir\SSC.App.exe"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$stage = Join-Path ([System.IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $stage | Out-Null

try {
    Copy-Item -Path (Join-Path $PublishDir "*") -Destination $stage -Recurse -Force
    Copy-Item -Path (Join-Path $repoRoot "LICENSE") -Destination $stage -Force
    Copy-Item -Path (Join-Path $repoRoot "NOTICE") -Destination $stage -Force

    $notices = Join-Path $repoRoot "build\third-party-notices.txt"
    if (Test-Path $notices) {
        Copy-Item -Path $notices -Destination (Join-Path $stage "THIRD-PARTY-NOTICES.txt") -Force
    }

    $zipName = "SuperScreenCapture-$Rid-Portable-$Version.zip"
    $zipPath = Join-Path $OutputDir $zipName
    Remove-Item -Path $zipPath -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -CompressionLevel Optimal

    $hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $zipName" | Set-Content -Path "$zipPath.sha256" -Encoding ascii

    Write-Host "Created $zipPath"
}
finally {
    Remove-Item -Path $stage -Recurse -Force -ErrorAction SilentlyContinue
}
