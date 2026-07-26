param(
    [string]$Owner = "Lury-Liu",
    [string]$Repo = "VideoRenamer",
    [string]$ExePath = "",
    [string]$Notes = "Improve user experience and fix known issues.",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
if ($PSScriptRoot) {
    Set-Location -LiteralPath $PSScriptRoot
}

. (Join-Path $PSScriptRoot "scripts\build-common.ps1")

Add-Type -AssemblyName System.Windows.Forms

function Show-Confirm($message, $title) {
    $result = [System.Windows.Forms.MessageBox]::Show(
        $message,
        $title,
        [System.Windows.Forms.MessageBoxButtons]::OKCancel,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )
    return $result -eq [System.Windows.Forms.DialogResult]::OK
}

function Show-YesNo($message, $title) {
    $result = [System.Windows.Forms.MessageBox]::Show(
        $message,
        $title,
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )
    return $result -eq [System.Windows.Forms.DialogResult]::Yes
}

function Invoke-Gh($arguments) {
    & gh @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub command failed: gh $($arguments -join ' ')"
    }
}

function Test-Gh($arguments) {
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $null = & gh @arguments 2>$null
        return $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }
}

function Test-RepoEmpty($fullRepo) {
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $json = & gh repo view $fullRepo --json isEmpty 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) {
            return $false
        }

        $info = $json | ConvertFrom-Json
        return [bool]$info.isEmpty
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }
}

function Ensure-RepoHasInitialCommit($fullRepo) {
    if (!(Test-RepoEmpty $fullRepo)) {
        return
    }

    Write-Host "Repository is empty. Creating initial README.md commit..."
    $readme = @"
# VideoRenamer

This repository stores release files used by the VideoRenamer updater.
"@
    $base64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($readme))
    Invoke-Gh @(
        "api",
        "repos/$fullRepo/contents/README.md",
        "-X",
        "PUT",
        "-f",
        "message=Initialize update repository",
        "-f",
        "content=$base64"
    )
    Start-Sleep -Seconds 2
}

function Remove-LegacyExeAssets($fullRepo, $tag, $keepAssetName) {
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $json = & gh release view $tag --repo $fullRepo --json assets 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) {
            return
        }

        $release = $json | ConvertFrom-Json
        foreach ($asset in $release.assets) {
            if ($asset.name -like "*.exe" -and $asset.name -ne $keepAssetName) {
                Invoke-Gh @("release", "delete-asset", $tag, $asset.name, "--repo", $fullRepo, "--yes")
            }
        }
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }
}

function Resolve-DefaultExePath {
    $dist = Join-Path (Get-Location).Path "dist"
    if (!(Test-Path -LiteralPath $dist)) {
        throw "dist directory does not exist."
    }

    $formal = Get-ChildItem -LiteralPath $dist -Filter "*.exe" -File |
        Where-Object { $_.BaseName -notmatch "-" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -ne $formal) {
        return $formal.FullName
    }

    $latest = Get-ChildItem -LiteralPath $dist -Filter "*.exe" -File |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -ne $latest) {
        return $latest.FullName
    }

    throw "No EXE found in dist."
}

function Get-Sha256Hex([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hash = $sha.ComputeHash($stream)
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    return (($hash | ForEach-Object { $_.ToString("x2") }) -join "")
}

if (-not $DryRun) {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -eq $gh) {
        throw "GitHub CLI was not found. Install GitHub CLI and run: gh auth login"
    }

    $authToken = & gh auth token 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($authToken)) {
        throw "GitHub CLI is not logged in. Run this first: gh auth login"
    }
    $authToken = $null
}

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $resolvedExe = Resolve-DefaultExePath
}
else {
    $resolvedExe = (Resolve-Path -LiteralPath $ExePath).Path
}

$file = Get-Item -LiteralPath $resolvedExe
$version = $file.VersionInfo.FileVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Cannot read EXE file version: $resolvedExe"
}

# Hard version guard: the EXE being published must match src/AppInfo.cs exactly.
# (Get-AppVersion throws if the constant cannot be parsed - no silent skip.)
$sourceVersion = Get-AppVersion
if ($version -ne $sourceVersion) {
    throw "Selected EXE version is $version, but source version is $sourceVersion. Build the current version before publishing."
}
Assert-VersionConsistency | Out-Null

# Hard test gate: refuse to publish unless self-test and smoke test pass.
Invoke-TestGate

$tag = "v$version"
$displayVersion = "V$version"
$fullRepo = "$Owner/$Repo"
$assetName = "$($script:AppName)-$tag.exe"
$encodedAssetName = [System.Uri]::EscapeDataString($assetName)
$downloadUrl = "https://github.com/$fullRepo/releases/download/$tag/$encodedAssetName"
$sha256 = Get-Sha256Hex -Path $resolvedExe

$updatesDir = Join-Path (Get-Location).Path "updates"
New-Item -ItemType Directory -Force -Path $updatesDir | Out-Null
$manifestPath = Join-Path $updatesDir "latest.json"
$uploadExePath = Join-Path $updatesDir $assetName
Copy-Item -LiteralPath $resolvedExe -Destination $uploadExePath -Force

$manifest = [ordered]@{
    appId = $script:AppName
    version = $version
    displayVersion = $displayVersion
    fileName = $assetName
    downloadUrl = $downloadUrl
    sha256 = $sha256
    notes = $Notes
    publishedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
}

$json = $manifest | ConvertTo-Json -Depth 4
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($manifestPath, $json, $utf8NoBom)

if ($DryRun) {
    Write-Host ""
    Write-Host "[DryRun] All publish gates passed. Stopping before upload." -ForegroundColor Green
    Write-Host "[DryRun] Would publish: $displayVersion to $fullRepo"
    Write-Host "[DryRun] Asset: $uploadExePath"
    Write-Host "[DryRun] Manifest: $manifestPath"
    Write-Host "[DryRun] sha256: $sha256"
    return
}

$confirmText = @"
Ready to upload update to GitHub.

Repository: $fullRepo
Version: $displayVersion
EXE: $resolvedExe
Upload asset: $uploadExePath
Manifest: $manifestPath

Other computers will read:
https://github.com/$fullRepo/releases/latest/download/latest.json

Click OK to upload.
"@

if (!(Show-Confirm $confirmText "Publish update to GitHub")) {
    Write-Host "Upload canceled."
    return
}

if (!(Test-Gh @("repo", "view", $fullRepo))) {
    if (!(Show-YesNo "Repository $fullRepo does not exist. Create it as a public repository?" "Create GitHub repository")) {
        throw "Repository does not exist. Publish canceled."
    }

    Invoke-Gh @("repo", "create", $fullRepo, "--public", "--description", "Video material renamer update releases")
}

Ensure-RepoHasInitialCommit $fullRepo

if (Test-Gh @("release", "view", $tag, "--repo", $fullRepo)) {
    Remove-LegacyExeAssets $fullRepo $tag $assetName
    Invoke-Gh @("release", "upload", $tag, $uploadExePath, $manifestPath, "--repo", $fullRepo, "--clobber")
    Invoke-Gh @("release", "edit", $tag, "--repo", $fullRepo, "--title", $displayVersion, "--notes", $Notes)
}
else {
    Invoke-Gh @("release", "create", $tag, $uploadExePath, $manifestPath, "--repo", $fullRepo, "--title", $displayVersion, "--notes", $Notes)
}

[System.Windows.Forms.MessageBox]::Show(
    "Published: $displayVersion`r`n`r`n$fullRepo",
    "GitHub publish complete",
    [System.Windows.Forms.MessageBoxButtons]::OK,
    [System.Windows.Forms.MessageBoxIcon]::Information
) | Out-Null

Write-Host "Published: $displayVersion"
Write-Host "Manifest URL: https://github.com/$fullRepo/releases/latest/download/latest.json"
