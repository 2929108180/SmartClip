# SmartClip 签名和发布脚本
# 用于对编译后的应用程序进行代码签名

param(
    [string]$Configuration = "Release",
    [string]$CertPath = "..\Certificates\SmartClip.pfx",
    [string]$OutputDir = "..\Publish"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  SmartClip 签名和发布工具" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$projectDir = Join-Path $PSScriptRoot "..\SmartClip"
$certFullPath = Join-Path $PSScriptRoot $CertPath
$outputFullPath = Join-Path $PSScriptRoot $OutputDir

# 检查证书是否存在
if (-not (Test-Path $certFullPath)) {
    Write-Host "[错误] 证书文件不存在: $certFullPath" -ForegroundColor Red
    Write-Host "请先运行 CreateCertificate.ps1 生成证书" -ForegroundColor Yellow
    exit 1
}

# 获取证书密码
$password = Read-Host "请输入证书密码" -AsSecureString

Write-Host "[1/5] 切换到 UIAccess manifest..." -ForegroundColor Green

# 备份并替换 manifest 配置
$csprojPath = Join-Path $projectDir "SmartClip.csproj"
$csprojContent = Get-Content $csprojPath -Raw
$originalContent = $csprojContent

# 替换为 UIAccess manifest
$csprojContent = $csprojContent -replace 'app\.manifest\.dev', 'app.manifest'
Set-Content $csprojPath $csprojContent -NoNewline

try {
    Write-Host "[2/5] 编译项目 (UIAccess 模式)..." -ForegroundColor Green

    # 发布项目
    $publishDir = Join-Path $outputFullPath "SmartClip"
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    dotnet publish $projectDir `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[错误] 编译失败！" -ForegroundColor Red
        exit 1
    }
}
finally {
    # 恢复原始 csproj
    Write-Host "[3/5] 恢复开发 manifest..." -ForegroundColor Green
    Set-Content $csprojPath $originalContent -NoNewline
}

Write-Host "[4/5] 签名应用程序..." -ForegroundColor Green

# 找到 signtool.exe
$signtoolPaths = @(
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22000.0\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe",
    "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\signtool.exe"
)

$signtool = $null
foreach ($path in $signtoolPaths) {
    if (Test-Path $path) {
        $signtool = $path
        break
    }
}

if (-not $signtool) {
    Write-Host "[警告] 未找到 signtool.exe，尝试使用 .NET 内置签名..." -ForegroundColor Yellow
    Write-Host "建议安装 Windows SDK 以获得更好的签名支持" -ForegroundColor Yellow

    # 使用 PowerShell 签名
    $exePath = Join-Path $publishDir "SmartClip.exe"
    $cert = Get-PfxCertificate -FilePath $certFullPath
    Set-AuthenticodeSignature -FilePath $exePath -Certificate $cert -TimestampServer "http://timestamp.digicert.com"
} else {
    Write-Host "  使用 signtool: $signtool" -ForegroundColor Gray

    $exePath = Join-Path $publishDir "SmartClip.exe"

    # 将 SecureString 转换为普通字符串
    $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
    $plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)

    & $signtool sign /f $certFullPath /p $plainPassword /fd SHA256 /tr "http://timestamp.digicert.com" /td SHA256 /v $exePath

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[错误] 签名失败！" -ForegroundColor Red
        exit 1
    }
}

Write-Host "[5/6] 验证签名..." -ForegroundColor Green

$signature = Get-AuthenticodeSignature (Join-Path $publishDir "SmartClip.exe")
if ($signature.Status -eq "Valid") {
    Write-Host "  签名有效: $($signature.SignerCertificate.Subject)" -ForegroundColor Gray
} else {
    Write-Host "[警告] 签名状态: $($signature.Status)" -ForegroundColor Yellow
}

Write-Host "[6/6] 复制证书文件..." -ForegroundColor Green

# 复制公钥证书供用户安装
$cerSource = Join-Path $PSScriptRoot "..\Certificates\SmartClip.cer"
if (Test-Path $cerSource) {
    Copy-Item $cerSource $publishDir
    Write-Host "  已复制 SmartClip.cer" -ForegroundColor Gray
}

# 创建安装说明
$readmeContent = @"
SmartClip 安装说明
==================

为了让 SmartClip 能够显示在 Windows 开始菜单、搜索框等系统界面之上，
需要完成以下步骤：

## 1. 安装证书（首次使用需要）

1. 右键点击 SmartClip.cer 文件，选择 [安装证书]
2. 选择 [本地计算机]，点击 [下一步]
3. 选择 [将所有的证书都放入下列存储]
4. 点击 [浏览]，选择 [受信任的根证书颁发机构]
5. 点击 [下一步] 然后 [完成]
6. 在弹出的安全警告中点击 [是]

## 2. 安装 SmartClip

将 SmartClip.exe 复制到以下位置之一：
- C:\Program Files\SmartClip\
- C:\Program Files (x86)\SmartClip\

注意：UIAccess 应用程序必须位于受保护的目录中才能正常工作。

## 3. 运行

从安装位置运行 SmartClip.exe，按 Win+V 即可唤出剪贴板历史。

## 常见问题

Q: 为什么需要安装证书？
A: Windows 要求 UIAccess 应用程序必须使用受信任的证书签名。

Q: 为什么必须安装到 Program Files？
A: 这是 Windows 的安全要求，防止恶意软件获得 UIAccess 权限。

Q: 安装证书安全吗？
A: 此证书仅用于 SmartClip 应用程序签名，不会影响系统其他部分。
"@

$readmeContent | Out-File (Join-Path $publishDir "安装说明.txt") -Encoding UTF8

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  发布完成！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "输出目录: $publishDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "发布内容：" -ForegroundColor Cyan
Get-ChildItem $publishDir | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor White
}
Write-Host ""
