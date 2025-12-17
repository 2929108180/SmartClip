# SmartClip Install Script
# Run this script to install certificate and application

param(
    [switch]$Uninstall,
    [switch]$Silent
)

$ErrorActionPreference = "Stop"
$installDir = "$env:ProgramFiles\SmartClip"
$startMenuDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"
$startupDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"
$desktopDir = [Environment]::GetFolderPath("Desktop")

function Write-ColorHost($message, $color = "White") {
    if (-not $Silent) {
        Write-Host $message -ForegroundColor $color
    }
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Check admin
if (-not (Test-Administrator)) {
    Write-ColorHost "Requesting admin privileges..." "Yellow"
    $scriptPath = $MyInvocation.MyCommand.Path
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`""
    if ($Uninstall) { $arguments += " -Uninstall" }
    if ($Silent) { $arguments += " -Silent" }
    Start-Process powershell -Verb RunAs -ArgumentList $arguments
    exit
}

# Uninstall mode
if ($Uninstall) {
    Write-ColorHost ""
    Write-ColorHost "========================================" "Cyan"
    Write-ColorHost "  SmartClip Uninstaller" "Cyan"
    Write-ColorHost "========================================" "Cyan"
    Write-ColorHost ""

    $process = Get-Process -Name "SmartClip" -ErrorAction SilentlyContinue
    if ($process) {
        Write-ColorHost "[1/4] Stopping SmartClip..." "Green"
        Stop-Process -Name "SmartClip" -Force
        Start-Sleep -Seconds 1
    }

    Write-ColorHost "[2/4] Removing files..." "Green"
    if (Test-Path $installDir) {
        Remove-Item $installDir -Recurse -Force
        Write-ColorHost "  Removed: $installDir" "Gray"
    }

    Write-ColorHost "[3/4] Removing shortcuts..." "Green"
    $shortcuts = @(
        "$startMenuDir\SmartClip.lnk",
        "$startupDir\SmartClip.lnk",
        "$desktopDir\SmartClip.lnk"
    )
    foreach ($shortcut in $shortcuts) {
        if (Test-Path $shortcut) {
            Remove-Item $shortcut -Force
            Write-ColorHost "  Removed: $shortcut" "Gray"
        }
    }

    Write-ColorHost "[4/4] Removing certificate..." "Green"
    $certs = Get-ChildItem -Path "Cert:\LocalMachine\Root" | Where-Object { $_.Subject -like "*SmartClip*" }
    foreach ($cert in $certs) {
        Remove-Item $cert.PSPath -Force
        Write-ColorHost "  Removed: $($cert.Subject)" "Gray"
    }

    Write-ColorHost ""
    Write-ColorHost "========================================" "Green"
    Write-ColorHost "  Uninstall Complete!" "Green"
    Write-ColorHost "========================================" "Green"

    if (-not $Silent) {
        Write-ColorHost ""
        Read-Host "Press Enter to exit"
    }
    exit 0
}

# Install mode
Write-ColorHost ""
Write-ColorHost "========================================" "Cyan"
Write-ColorHost "  SmartClip Installer" "Cyan"
Write-ColorHost "========================================" "Cyan"
Write-ColorHost ""

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exePath = Join-Path $scriptDir "SmartClip.exe"
$cerPath = Join-Path $scriptDir "SmartClip.cer"

# Check files
if (-not (Test-Path $exePath)) {
    Write-ColorHost "[Error] SmartClip.exe not found" "Red"
    Write-ColorHost "Make sure Install.ps1 is in the same folder as SmartClip.exe" "Yellow"
    if (-not $Silent) { Read-Host "Press Enter to exit" }
    exit 1
}

if (-not (Test-Path $cerPath)) {
    Write-ColorHost "[Error] SmartClip.cer not found" "Red"
    Write-ColorHost "Make sure Install.ps1 is in the same folder as SmartClip.cer" "Yellow"
    if (-not $Silent) { Read-Host "Press Enter to exit" }
    exit 1
}

# Stop running process
$process = Get-Process -Name "SmartClip" -ErrorAction SilentlyContinue
if ($process) {
    Write-ColorHost "[Prep] Stopping running SmartClip..." "Yellow"
    Stop-Process -Name "SmartClip" -Force
    Start-Sleep -Seconds 1
}

Write-ColorHost "[1/5] Installing certificate to Trusted Root..." "Green"

$existingCert = Get-ChildItem -Path "Cert:\LocalMachine\Root" | Where-Object { $_.Subject -like "*SmartClip*" }

if ($existingCert) {
    Write-ColorHost "  Certificate already installed, skipping" "Gray"
} else {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
    $store.Open("ReadWrite")
    $store.Add($cert)
    $store.Close()
    Write-ColorHost "  Certificate installed successfully" "Gray"
}

Write-ColorHost "[2/5] Creating install directory..." "Green"

if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}
Write-ColorHost "  Directory: $installDir" "Gray"

Write-ColorHost "[3/5] Copying files..." "Green"

Copy-Item $exePath "$installDir\SmartClip.exe" -Force
Write-ColorHost "  Copied SmartClip.exe" "Gray"

Copy-Item $cerPath "$installDir\SmartClip.cer" -Force

Write-ColorHost "[4/5] Creating Start Menu shortcut..." "Green"

$WshShell = New-Object -ComObject WScript.Shell
$shortcut = $WshShell.CreateShortcut("$startMenuDir\SmartClip.lnk")
$shortcut.TargetPath = "$installDir\SmartClip.exe"
$shortcut.WorkingDirectory = $installDir
$shortcut.Description = "SmartClip Clipboard Manager"
$shortcut.Save()
Write-ColorHost "  Start Menu shortcut created" "Gray"

Write-ColorHost "[5/6] Creating Desktop shortcut..." "Green"

$desktopShortcut = $WshShell.CreateShortcut("$desktopDir\SmartClip.lnk")
$desktopShortcut.TargetPath = "$installDir\SmartClip.exe"
$desktopShortcut.WorkingDirectory = $installDir
$desktopShortcut.Description = "SmartClip Clipboard Manager"
$desktopShortcut.Save()
Write-ColorHost "  Desktop shortcut created" "Gray"

Write-ColorHost "[6/6] Setting up auto-start..." "Green"

$startupShortcut = $WshShell.CreateShortcut("$startupDir\SmartClip.lnk")
$startupShortcut.TargetPath = "$installDir\SmartClip.exe"
$startupShortcut.WorkingDirectory = $installDir
$startupShortcut.Description = "SmartClip Clipboard Manager"
$startupShortcut.Save()
Write-ColorHost "  Auto-start configured" "Gray"

Write-ColorHost ""
Write-ColorHost "========================================" "Green"
Write-ColorHost "  Installation Complete!" "Green"
Write-ColorHost "========================================" "Green"
Write-ColorHost ""
Write-ColorHost "Install location: $installDir" "Cyan"
Write-ColorHost ""
Write-ColorHost "Usage:" "Cyan"
Write-ColorHost "  - Press Win+V to open clipboard history" "White"
Write-ColorHost "  - App will start automatically on login" "White"
Write-ColorHost ""

if (-not $Silent) {
    $startNow = Read-Host "Start SmartClip now? (Y/n)"
    if ($startNow -ne "n" -and $startNow -ne "N") {
        Start-Process "$installDir\SmartClip.exe"
        Write-ColorHost "SmartClip started!" "Green"
    }
    Write-ColorHost ""
    Read-Host "Press Enter to exit"
}
