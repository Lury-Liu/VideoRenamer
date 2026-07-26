param(
    [switch]$SelfTest,
    [switch]$SmokeTest
)

$ErrorActionPreference = "Stop"
if ($PSScriptRoot) {
    Set-Location -LiteralPath $PSScriptRoot
}

# Source is modularized under src/. All *.cs files are compiled together
# into a single in-memory assembly (equivalent to the former single here-string).
# NOTE: this dev loop has no embedded ffmpeg resource, so media features
# (thumbnails / hover-scrub / export) silently no-op unless a loose ffmpeg.exe
# is found on the runtime search path. Build the EXE for full functionality.
. (Join-Path $PSScriptRoot "scripts\build-common.ps1")

$sourceFiles = Get-SourceFiles

Add-Type -Path $sourceFiles -ReferencedAssemblies (Get-ReferenceAssemblies)

if ($SelfTest) {
    [VideoMaterialRenamer.MaterialRenamerForm]::RunSelfTest()
    return
}

if ($SmokeTest) {
    [VideoMaterialRenamer.MaterialRenamerForm]::RunSmokeTest()
    return
}

[VideoMaterialRenamer.Program]::Run()
