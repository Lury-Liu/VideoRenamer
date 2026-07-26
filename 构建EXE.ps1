param(
    [string]$OutputName = "视频素材镜头表命名工具.exe",
    [switch]$AllowNoFfmpeg
)

$ErrorActionPreference = "Stop"

if ($PSScriptRoot) {
    Set-Location -LiteralPath $PSScriptRoot
}

. (Join-Path $PSScriptRoot "scripts\build-common.ps1")

$root = (Get-Location).Path
$distDir = Join-Path $root "dist"
$outputExe = Join-Path $distDir $OutputName
$iconPath = Join-Path $root "assets\app.ico"
$ffmpegResourceCandidates = @(
    (Join-Path $root "tools\ffmpeg.exe"),
    (Join-Path $root "dist\tools\ffmpeg.exe")
)

# 版本一致性检查：src/AppInfo.cs 与 src/AssemblyInfo.cs 必须一致（发布脚本依赖 EXE FileVersion）
$version = Assert-VersionConsistency
Write-Host "版本号：$version"

$sourceFiles = Get-SourceFiles

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$cscCandidates = @(
    "${env:WINDIR}\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "${env:WINDIR}\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($csc)) {
    throw "找不到 .NET Framework csc.exe，无法构建 WinForms EXE。"
}

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/utf8output"
)

if (Test-Path -LiteralPath $iconPath) {
    $arguments += "/win32icon:$iconPath"
}

$ffmpegResource = $ffmpegResourceCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (![string]::IsNullOrWhiteSpace($ffmpegResource)) {
    $arguments += "/resource:$ffmpegResource,$($script:FfmpegResourceName)"
}
elseif ($AllowNoFfmpeg) {
    Write-Warning "未找到 ffmpeg.exe，生成的 EXE 将不包含内置 FFmpeg（-AllowNoFfmpeg 已指定）。"
}
else {
    throw "未找到 tools\ffmpeg.exe。缺少内置 FFmpeg 的 EXE 会静默失去媒体功能，禁止发布。开发构建请使用 -AllowNoFfmpeg。"
}

$arguments += "/out:$outputExe"
foreach ($ref in (Get-ReferenceAssemblies)) {
    $arguments += "/reference:$ref"
}
$arguments += $sourceFiles

# 输出编译命令行到 dist\csc-cmdline.txt，供重构阶段做“无操作等价”对比验证
$cmdlineDump = Join-Path $distDir "csc-cmdline.txt"
($arguments -join "`n") | Set-Content -LiteralPath $cmdlineDump -Encoding UTF8
Write-Host "CSC: $csc"

& $csc @arguments
if ($LASTEXITCODE -ne 0) {
    throw "EXE 编译失败。"
}

$readme = Join-Path $root "README_视频素材重命名工具.md"
if (Test-Path -LiteralPath $readme) {
    Copy-Item -LiteralPath $readme -Destination (Join-Path $distDir "README_视频素材重命名工具.md") -Force
}

$changelog = Join-Path $root "CHANGELOG.md"
if (Test-Path -LiteralPath $changelog) {
    Copy-Item -LiteralPath $changelog -Destination (Join-Path $distDir "CHANGELOG.md") -Force
}

# 构建产物验证：内置 FFmpeg 资源名、版本一致、单一 EXE、无多余 DLL
& (Join-Path $root "scripts\verify-artifact.ps1") -ExePath $outputExe -AllowNoFfmpeg:$AllowNoFfmpeg

Write-Host ""
Write-Host "已生成 EXE："
Write-Host $outputExe
Write-Host ""
if (![string]::IsNullOrWhiteSpace($ffmpegResource)) {
    Write-Host "已内置 FFmpeg："
    Write-Host $ffmpegResource
    Write-Host ""
}
Write-Host "发送给使用者时，建议只发送 dist 目录里的 EXE，不要发送 生成授权密钥工具.ps1。"
