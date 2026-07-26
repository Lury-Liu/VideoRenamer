# ============================================================================
# build-common.ps1 - shared helpers for the build / package / publish scripts.
# Dot-source from a repo-root script:
#     . (Join-Path $PSScriptRoot "scripts\build-common.ps1")
#
# FROZEN CONTRACTS - relied upon by every installed copy of the app.
# Changing any of these bricks auto-update, media features, or activations:
#   1. Embedded ffmpeg resource name : VideoMaterialRenamer.ffmpeg.exe
#      (producer: csc /resource flag below; consumer: ExtractEmbeddedFfmpeg)
#   2. App EXE base name             : one loose 视频素材镜头表命名工具.exe in dist\
#      (installer.iss and the publish script's hyphen-free heuristic assume it)
#   3. Release artifacts             : tag v{FileVersion}, asset VideoRenamer-v{ver}.exe,
#                                      plus a file literally named latest.json
#   4. FileVersion source            : src/AssemblyInfo.cs AssemblyFileVersion
#      (drives the publish script's tag/asset/manifest derivation)
#   5. License/DPAPI formats & paths : %LocalAppData%\VideoMaterialRenamer,
#                                      license.v2.dat / license.state.v2.dat, "LicenseStateV2"
#   6. Installer AppId GUID          : installer.iss - never regenerate
#   7. Loader switches               : -SelfTest / -SmokeTest printing exactly
#                                      "SelfTest OK" / "SmokeTest OK"
#   8. Runtime ffmpeg search order   : baseDir > baseDir\tools > cwd > cwd\tools
#                                      > PATH > embedded resource
#   9. Encoding                      : .ps1/.iss containing Chinese = UTF-8 with BOM;
#                                      .cs = UTF-8 without BOM
# ============================================================================

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:AppExeName = "视频素材镜头表命名工具.exe"
$script:FfmpegResourceName = "VideoMaterialRenamer.ffmpeg.exe"

function Get-SourceFiles {
    param([string]$Root = $script:RepoRoot)
    $srcDir = Join-Path $Root "src"
    if (!(Test-Path -LiteralPath $srcDir)) {
        throw "Source directory not found: $srcDir"
    }
    $files = Get-ChildItem -LiteralPath $srcDir -Recurse -Filter *.cs |
        Sort-Object FullName |
        Select-Object -ExpandProperty FullName
    if (!$files) {
        throw "No .cs source files found under $srcDir"
    }
    return $files
}

function Get-ReferenceAssemblies {
    return @(
        "System.Windows.Forms.dll",
        "System.Drawing.dll",
        "System.Core.dll",
        "System.Security.dll"
    )
}

function Get-AppVersion {
    # Numeric version from src/AppInfo.cs (display form "V1.0.6.0" -> returns "1.0.6.0").
    param([string]$Root = $script:RepoRoot)
    $appInfoPath = Join-Path $Root "src\AppInfo.cs"
    if (!(Test-Path -LiteralPath $appInfoPath)) {
        throw "AppInfo.cs not found: $appInfoPath"
    }
    $text = [System.IO.File]::ReadAllText($appInfoPath)
    $match = [regex]::Match($text, 'Version\s*=\s*"V?([\d\.]+)"')
    if (-not $match.Success) {
        throw "Could not parse Version constant from $appInfoPath"
    }
    return $match.Groups[1].Value
}

function Get-AssemblyFileVersion {
    param([string]$Root = $script:RepoRoot)
    $asmInfoPath = Join-Path $Root "src\AssemblyInfo.cs"
    if (!(Test-Path -LiteralPath $asmInfoPath)) {
        throw "AssemblyInfo.cs not found: $asmInfoPath"
    }
    $text = [System.IO.File]::ReadAllText($asmInfoPath)
    $match = [regex]::Match($text, 'AssemblyFileVersion\("([\d\.]+)"\)')
    if (-not $match.Success) {
        throw "Could not parse AssemblyFileVersion from $asmInfoPath"
    }
    return $match.Groups[1].Value
}

function Assert-VersionConsistency {
    param([string]$Root = $script:RepoRoot)
    $appVersion = Get-AppVersion -Root $Root
    $asmVersion = Get-AssemblyFileVersion -Root $Root
    if ($appVersion -ne $asmVersion) {
        throw ("Version drift: src/AppInfo.cs says {0} but src/AssemblyInfo.cs AssemblyFileVersion says {1}. " -f $appVersion, $asmVersion) +
              "They must match - the publish script derives the release tag from the EXE FileVersion."
    }
    return $appVersion
}

function Get-EmbeddedResourceNames {
    # Reflects over the EXE in a CHILD process so this session never locks the file.
    # Returns an empty list for non-assembly files (the caller treats that as
    # "resource missing" and fails its check with a readable message).
    param([Parameter(Mandatory = $true)][string]$ExePath)
    $full = (Resolve-Path -LiteralPath $ExePath).Path
    $code = "try {{ [System.Reflection.Assembly]::LoadFile('{0}').GetManifestResourceNames() | ForEach-Object {{ `$_ }} }} catch {{ }}" -f ($full -replace "'", "''")
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $names = & powershell.exe -NoProfile -Command $code 2>$null
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }
    return @($names | Where-Object { $_ })
}

function Invoke-TestGate {
    # Hard gate: runs the self-test and smoke test through the dev loader and
    # aborts unless each prints its exact OK marker (frozen contract #7).
    param([string]$Root = $script:RepoRoot)
    $loader = Join-Path $Root "video_material_renamer.ps1"
    if (!(Test-Path -LiteralPath $loader)) {
        throw "Dev loader not found: $loader"
    }

    Write-Host "[gate] running self-test..."
    $selfOutput = & powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File $loader -SelfTest 2>&1 | Out-String
    if ($selfOutput -notmatch "SelfTest OK") {
        throw "Self-test gate FAILED. Output:`n$selfOutput"
    }
    Write-Host "[gate] SelfTest OK"

    Write-Host "[gate] running smoke test..."
    $smokeOutput = & powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File $loader -SmokeTest 2>&1 | Out-String
    if ($smokeOutput -notmatch "SmokeTest OK") {
        throw "Smoke-test gate FAILED. Output:`n$smokeOutput"
    }
    Write-Host "[gate] SmokeTest OK"
}
