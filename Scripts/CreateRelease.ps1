# SmartClip Release Builder
# Builds, signs and packages the application

param(
    [string]$Version = "1.0.0",
    [string]$CertPath = "..\Certificates\SmartClip.pfx"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  SmartClip Release Builder" -ForegroundColor Cyan
Write-Host "  Version: $Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$scriptDir = $PSScriptRoot
$projectDir = Join-Path $scriptDir "..\SmartClip"
$certFullPath = Join-Path $scriptDir $CertPath
$cerPath = Join-Path $scriptDir "..\Certificates\SmartClip.cer"
$outputDir = Join-Path $scriptDir "..\Release"
$releaseDir = Join-Path $outputDir "SmartClip-v$Version"
$zipPath = Join-Path $outputDir "SmartClip-v$Version.zip"

# Check certificate
if (-not (Test-Path $certFullPath)) {
    Write-Host "[Error] Certificate not found: $certFullPath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please run CreateCertificate.ps1 first" -ForegroundColor Yellow
    Write-Host ""
    Read-Host "Press Enter to exit"
    exit 1
}

$password = Read-Host "Enter certificate password" -AsSecureString

# Create output directory
if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

Write-Host "[1/6] Switching to UIAccess manifest..." -ForegroundColor Green

$csprojPath = Join-Path $projectDir "SmartClip.csproj"
$csprojContent = Get-Content $csprojPath -Raw
$originalContent = $csprojContent
$csprojContent = $csprojContent -replace 'app\.manifest\.dev', 'app.manifest'
Set-Content $csprojPath $csprojContent -NoNewline

try {
    Write-Host "[2/6] Building project..." -ForegroundColor Green

    $buildOutput = Join-Path $scriptDir "..\build_temp"
    if (Test-Path $buildOutput) {
        Remove-Item $buildOutput -Recurse -Force
    }

    $publishArgs = @(
        "publish"
        $projectDir
        "-c", "Release"
        "-r", "win-x64"
        "--self-contained", "true"
        "-p:PublishSingleFile=true"
        "-p:IncludeNativeLibrariesForSelfExtract=true"
        "-p:Version=$Version"
        "-o", $buildOutput
    )

    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }

    Write-Host "  Build successful" -ForegroundColor Gray
}
finally {
    # Restore csproj
    Set-Content $csprojPath $originalContent -NoNewline
}

Write-Host "[3/6] Restoring dev manifest..." -ForegroundColor Green
Write-Host "  Done" -ForegroundColor Gray

Write-Host "[4/6] Signing application..." -ForegroundColor Green

$exePath = Join-Path $buildOutput "SmartClip.exe"

# Find signtool
$signtool = $null
$signtoolPaths = @(
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22000.0\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"
)
foreach ($path in $signtoolPaths) {
    if (Test-Path $path) {
        $signtool = $path
        break
    }
}

# Convert password for use
$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
$plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
[System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)

if ($signtool) {
    & $signtool sign /f $certFullPath /p $plainPassword /fd SHA256 /tr "http://timestamp.digicert.com" /td SHA256 $exePath 2>&1 | Out-Null

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Signed with signtool" -ForegroundColor Gray
    } else {
        Write-Host "  signtool failed, trying PowerShell..." -ForegroundColor Yellow
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certFullPath, $plainPassword)
        Set-AuthenticodeSignature -FilePath $exePath -Certificate $cert -TimestampServer "http://timestamp.digicert.com" | Out-Null
    }
} else {
    Write-Host "  signtool not found, using PowerShell..." -ForegroundColor Yellow
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certFullPath, $plainPassword)
    Set-AuthenticodeSignature -FilePath $exePath -Certificate $cert -TimestampServer "http://timestamp.digicert.com" | Out-Null
}

# Verify signature
$sig = Get-AuthenticodeSignature $exePath
Write-Host "  Signature status: $($sig.Status)" -ForegroundColor Gray

Write-Host "[5/6] Copying release files..." -ForegroundColor Green

# Copy exe
Copy-Item $exePath $releaseDir
Write-Host "  Copied SmartClip.exe" -ForegroundColor Gray

# Copy certificate
Copy-Item $cerPath $releaseDir
Write-Host "  Copied SmartClip.cer" -ForegroundColor Gray

# Copy install scripts
Copy-Item (Join-Path $scriptDir "Install.ps1") $releaseDir
Copy-Item (Join-Path $scriptDir "Install.bat") $releaseDir
Copy-Item (Join-Path $scriptDir "Uninstall.bat") $releaseDir
Write-Host "  Copied install scripts" -ForegroundColor Gray

# Create README
$readmeContent = @"
# SmartClip v$Version

Clipboard History Manager for Windows

## Installation

1. Right-click Install.bat and select "Run as administrator"
2. Follow the prompts
3. Press Win+V to use

## Keyboard Shortcuts

- Win+V    : Open/close clipboard
- Enter    : Paste selected item
- Shift+Enter : Paste as plain text
- 1-9      : Quick paste
- Esc      : Close window
- Ctrl+P   : Pin/unpin item
- Delete   : Delete item

## Search Filters

- /img  : Show images only
- /file : Show files only
- /text : Show text only
- /rich : Show rich text only

## Uninstall

Run Uninstall.bat as administrator.
"@

$readmeContent | Out-File (Join-Path $releaseDir "README.txt") -Encoding ASCII
Write-Host "  Created README.txt" -ForegroundColor Gray

Write-Host "[6/6] Creating ZIP package..." -ForegroundColor Green

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path "$releaseDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "  Created: $zipPath" -ForegroundColor Gray

# Cleanup
if (Test-Path $buildOutput) {
    Remove-Item $buildOutput -Recurse -Force
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Release Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Output: $zipPath" -ForegroundColor Cyan
Write-Host ""
Write-Host "Contents:" -ForegroundColor Cyan
Get-ChildItem $releaseDir | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor White
}
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Upload ZIP to GitHub Releases" -ForegroundColor White
Write-Host "  2. Add release notes" -ForegroundColor White
Write-Host ""
Read-Host "Press Enter to exit"
