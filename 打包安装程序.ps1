param(
    [switch]$SkipExeBuild,
    [switch]$SkipTests
)

# 一键打包安装程序：
#   1) 调用 构建EXE.ps1 生成内置 FFmpeg 的 EXE（含产物验证）
#   2) 运行自检/冒烟测试门禁（必须输出 SelfTest OK / SmokeTest OK）
#   3) 定位 Inno Setup 编译器 (ISCC.exe)
#   4) 用源码里的版本号编译 installer.iss -> installer\ 目录
$ErrorActionPreference = "Stop"
if ($PSScriptRoot) { Set-Location -LiteralPath $PSScriptRoot }
$root = (Get-Location).Path

. (Join-Path $PSScriptRoot "scripts\build-common.ps1")

# --- 1. 构建 EXE ---
if (-not $SkipExeBuild) {
    Write-Host "== 步骤 1/4：构建 EXE ==" -ForegroundColor Cyan
    & (Join-Path $root "构建EXE.ps1")
}
else {
    Write-Host "== 跳过 EXE 构建（-SkipExeBuild）==" -ForegroundColor Yellow
}

$exePath = Join-Path $root "dist\VideoRenamer.exe"
if (!(Test-Path -LiteralPath $exePath)) { throw "找不到已构建的 EXE：$exePath" }

# --- 2. 测试门禁 ---
if (-not $SkipTests) {
    Write-Host "== 步骤 2/4：测试门禁 ==" -ForegroundColor Cyan
    Invoke-TestGate
}
else {
    Write-Host "== 跳过测试门禁（-SkipTests）==" -ForegroundColor Yellow
}

# --- 3. 提取版本号 ---
$version = Get-AppVersion
Write-Host ("版本号：{0}" -f $version)

# --- 4. 定位 ISCC ---
Write-Host "== 步骤 3/4：定位 Inno Setup 编译器 ==" -ForegroundColor Cyan
$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    Write-Warning "未检测到 Inno Setup（ISCC.exe）。"
    Write-Host ""
    Write-Host "请先安装 Inno Setup 6，然后重新运行本脚本：" -ForegroundColor Yellow
    Write-Host "  官网下载： https://jrsoftware.org/isdl.php"
    Write-Host "  或用 winget： winget install JRSoftware.InnoSetup"
    Write-Host ""
    Write-Host "安装后也可手动编译： 用 Inno Setup 打开 installer.iss 点 Compile。"
    throw "缺少 Inno Setup 编译器。"
}
Write-Host ("ISCC：{0}" -f $iscc)

# --- 5. 编译安装程序 ---
Write-Host "== 步骤 4/4：编译安装程序 ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path (Join-Path $root "installer") | Out-Null
& $iscc "/DAppVersion=$version" (Join-Path $root "installer.iss")
if ($LASTEXITCODE -ne 0) { throw "安装程序编译失败。" }

$setupPath = Join-Path $root ("installer\VideoRenamer-Setup-v{0}.exe" -f $version)
Write-Host ""
Write-Host "打包完成！" -ForegroundColor Green
if (Test-Path -LiteralPath $setupPath) {
    Write-Host ("安装程序：{0}" -f $setupPath)
}
Write-Host "发给使用者的就是这个安装程序 EXE（已内置 FFmpeg，双击即可安装）。"
