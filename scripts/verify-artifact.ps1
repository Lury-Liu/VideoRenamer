# ============================================================================
# verify-artifact.ps1 - post-build assertions over the release EXE.
# Converts the known silent-ship failure modes into hard build failures:
#   - EXE missing the embedded ffmpeg resource (media features would no-op)
#   - FileVersion drifting from src/AppInfo.cs / src/AssemblyInfo.cs
#   - Extra files in dist\ breaking the single-loose-EXE contract
# Run automatically at the end of 构建EXE.ps1; can also be run standalone.
# ============================================================================
param(
    [string]$ExePath = "",
    [switch]$AllowNoFfmpeg
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "build-common.ps1")

$root = $script:RepoRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path (Join-Path $root "dist") $script:AppExeName
}
if (!(Test-Path -LiteralPath $ExePath)) {
    throw "verify-artifact: EXE not found: $ExePath"
}

$failures = @()
$exeItem = Get-Item -LiteralPath $ExePath
$resourceNames = Get-EmbeddedResourceNames -ExePath $ExePath

# --- 1. Version consistency: AppInfo.cs == AssemblyInfo.cs == built EXE ---
$appVersion = Get-AppVersion
$asmVersion = Get-AssemblyFileVersion
$fileVersion = $exeItem.VersionInfo.FileVersion
if ($appVersion -ne $asmVersion) {
    $failures += "AppInfo.Version ($appVersion) != AssemblyFileVersion ($asmVersion)"
}
if ($fileVersion -ne $appVersion) {
    $failures += "EXE FileVersion ($fileVersion) != AppInfo.Version ($appVersion) - stale build?"
}

# --- 2. Embedded ffmpeg resource by its exact frozen name ---
if (-not $AllowNoFfmpeg) {
    if ($resourceNames -notcontains $script:FfmpegResourceName) {
        $failures += ("EXE lacks embedded resource '{0}' (found: {1}) - media features would silently no-op" -f `
            $script:FfmpegResourceName, ($resourceNames -join ", "))
    }

    # --- 3. Size floor: an EXE without the ~100MB ffmpeg payload is a broken ship ---
    if ($exeItem.Length -lt 90MB) {
        $failures += ("EXE size {0:N0} bytes is below the 90MB ffmpeg floor" -f $exeItem.Length)
    }
}

# --- 4. Startup icons stay embedded so auto-update remains a single EXE. ---
for ($index = 1; $index -le 9; $index++) {
    $expectedIconResource = $script:StartupIconResourcePrefix + ("{0:D2}.ico" -f $index)
    if ($resourceNames -notcontains $expectedIconResource) {
        $failures += "EXE lacks startup icon resource '$expectedIconResource'"
    }
}

# --- 5. Exactly one hyphen-free EXE in dist (publish-script selection heuristic) ---
$distDir = Split-Path -Parent $ExePath
$hyphenFree = @(Get-ChildItem -LiteralPath $distDir -Filter *.exe -File |
    Where-Object { $_.BaseName -notmatch "-" })
if ($hyphenFree.Count -ne 1) {
    $failures += ("Expected exactly one hyphen-free EXE in {0}, found {1}" -f $distDir, $hyphenFree.Count)
}

# --- 6. No stray DLLs beside the EXE (single-file contract) ---
$strayDlls = @(Get-ChildItem -LiteralPath $distDir -Filter *.dll -File)
if ($strayDlls.Count -gt 0) {
    $failures += ("Stray DLLs in {0}: {1}" -f $distDir, (($strayDlls | ForEach-Object Name) -join ", "))
}

if ($failures.Count -gt 0) {
    foreach ($f in $failures) {
        Write-Host "[verify-artifact] FAIL: $f" -ForegroundColor Red
    }
    throw ("verify-artifact: {0} check(s) failed for {1}" -f $failures.Count, $ExePath)
}

Write-Host ("[verify-artifact] PASS: version={0}, size={1:N0} bytes, ffmpeg resource {2}, 9 startup icon resources, single hyphen-free EXE, no stray DLLs" -f `
    $fileVersion, $exeItem.Length, $(if ($AllowNoFfmpeg) { "check skipped (-AllowNoFfmpeg)" } else { "present" }))
