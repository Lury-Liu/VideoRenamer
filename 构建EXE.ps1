param(
    [string]$OutputName = "视频素材镜头表命名工具.exe"
)

$ErrorActionPreference = "Stop"

if ($PSScriptRoot) {
    Set-Location -LiteralPath $PSScriptRoot
}

$root = (Get-Location).Path
$mainScript = Join-Path $root "video_material_renamer.ps1"
$buildDir = Join-Path $root "build"
$distDir = Join-Path $root "dist"
$generatedSource = Join-Path $buildDir "VideoMaterialRenamer.generated.cs"
$outputExe = Join-Path $distDir $OutputName
$iconPath = Join-Path $root "assets\app.ico"
$ffmpegResourceCandidates = @(
    (Join-Path $root "tools\ffmpeg.exe"),
    (Join-Path $root "dist\tools\ffmpeg.exe")
)

if (!(Test-Path -LiteralPath $mainScript)) {
    throw "找不到主程序脚本：$mainScript"
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
$scriptText = [System.IO.File]::ReadAllText($mainScript, $utf8)
$match = [regex]::Match($scriptText, '(?s)\$source\s*=\s*@"\r?\n(.*?)\r?\n"@')
if (!$match.Success) {
    throw "无法从 video_material_renamer.ps1 提取 C# 源码。"
}

New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
[System.IO.File]::WriteAllText($generatedSource, $match.Groups[1].Value, $utf8Bom)

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
    $arguments += "/resource:$ffmpegResource,VideoMaterialRenamer.ffmpeg.exe"
}
else {
    Write-Warning "未找到 ffmpeg.exe，生成的 EXE 将不包含内置 FFmpeg。"
}

$arguments += @(
    "/out:$outputExe",
    "/reference:System.Windows.Forms.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Core.dll",
    "/reference:System.Security.dll",
    $generatedSource
)

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
