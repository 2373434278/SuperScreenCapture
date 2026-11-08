param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$CertificateBase64 = $env:WINDOWS_CERT_PFX,
    [string]$CertificatePassword = $env:WINDOWS_CERT_PASSWORD,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($CertificateBase64)) {
    Write-Host "WINDOWS_CERT_PFX not set; skipping Windows signing."
    exit 0
}

$signtool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "\\x64\\" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $signtool) {
    $signtool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
}

if (-not $signtool) {
    throw "signtool.exe not found. Install the Windows SDK signing tools."
}

$pfxPath = Join-Path ([System.IO.Path]::GetTempPath()) ("ssc-cert-" + [Guid]::NewGuid().ToString("N") + ".pfx")
try {
    [System.IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($CertificateBase64))

    $files = Get-ChildItem -Path $Path -Recurse -File -Include "*.exe", "*.dll"
    foreach ($file in $files) {
        Write-Host "Signing $($file.FullName)"
        & $signtool.FullName sign /fd SHA256 /tr $TimestampUrl /td SHA256 /f $pfxPath /p $CertificatePassword $file.FullName
        if ($LASTEXITCODE -ne 0) {
            throw "signtool failed for $($file.FullName)"
        }
    }
}
finally {
    Remove-Item -Path $pfxPath -Force -ErrorAction SilentlyContinue
}

Write-Host "Windows signing complete"
