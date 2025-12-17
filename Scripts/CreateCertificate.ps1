# SmartClip Certificate Generator
# Run as Administrator

param(
    [string]$CertName = "SmartClip Code Signing",
    [string]$OutputPath = "..\Certificates"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  SmartClip Certificate Generator" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check admin
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
if (-not $isAdmin) {
    Write-Host "[Error] Please run as Administrator!" -ForegroundColor Red
    exit 1
}

# Create output directory
$certDir = Join-Path $PSScriptRoot $OutputPath
if (-not (Test-Path $certDir)) {
    New-Item -ItemType Directory -Path $certDir -Force | Out-Null
}

$pfxPath = Join-Path $certDir "SmartClip.pfx"
$cerPath = Join-Path $certDir "SmartClip.cer"

# Check existing
if (Test-Path $pfxPath) {
    $overwrite = Read-Host "Certificate exists. Overwrite? (y/N)"
    if ($overwrite -ne "y" -and $overwrite -ne "Y") {
        Write-Host "Cancelled." -ForegroundColor Yellow
        exit 0
    }
}

Write-Host "[1/4] Generating self-signed certificate..." -ForegroundColor Green

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject "CN=$CertName, O=SmartClip, C=CN" `
    -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -NotAfter (Get-Date).AddYears(10) `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")

Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray

Write-Host "[2/4] Exporting certificate files..." -ForegroundColor Green

$password = Read-Host "Set certificate password" -AsSecureString

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $password | Out-Null
Write-Host "  PFX: $pfxPath" -ForegroundColor Gray

Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
Write-Host "  CER: $cerPath" -ForegroundColor Gray

Write-Host "[3/4] Installing to Trusted Root..." -ForegroundColor Green

$rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
$rootStore.Open("ReadWrite")
$rootStore.Add($cert)
$rootStore.Close()

Write-Host "  Installed to local machine trusted store" -ForegroundColor Gray

Write-Host "[4/4] Cleaning up..." -ForegroundColor Green

Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Force

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Certificate Created!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Files:" -ForegroundColor Cyan
Write-Host "  - SmartClip.pfx (private key, for signing)" -ForegroundColor White
Write-Host "  - SmartClip.cer (public key, for users)" -ForegroundColor White
Write-Host ""
Write-Host "User Installation:" -ForegroundColor Cyan
Write-Host "  1. Double-click SmartClip.cer" -ForegroundColor White
Write-Host "  2. Click [Install Certificate]" -ForegroundColor White
Write-Host "  3. Select [Local Machine]" -ForegroundColor White
Write-Host "  4. Select [Place all certificates in the following store]" -ForegroundColor White
Write-Host "  5. Browse and select [Trusted Root Certification Authorities]" -ForegroundColor White
Write-Host "  6. Finish" -ForegroundColor White
Write-Host ""
Write-Host "Thumbprint: $($cert.Thumbprint)" -ForegroundColor Yellow
Write-Host ""

$cert.Thumbprint | Out-File (Join-Path $certDir "thumbprint.txt") -Encoding UTF8
