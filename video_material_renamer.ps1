param(
    [switch]$SelfTest,
    [switch]$SmokeTest
)

$ErrorActionPreference = "Stop"
if ($PSScriptRoot) {
    Set-Location -LiteralPath $PSScriptRoot
}

# Source is now modularized under src/. All *.cs files are compiled together
# into a single in-memory assembly (equivalent to the former single here-string).
$srcDir = Join-Path $PSScriptRoot "src"
if (!(Test-Path -LiteralPath $srcDir)) {
    throw "找不到源码目录：$srcDir（从源码运行需要 src\ 文件夹与本脚本放在一起）。"
}

$sourceFiles = Get-ChildItem -LiteralPath $srcDir -Recurse -Filter *.cs |
    Sort-Object FullName |
    Select-Object -ExpandProperty FullName

Add-Type -Path $sourceFiles -ReferencedAssemblies @("System.Windows.Forms.dll", "System.Drawing.dll", "System.Core.dll", "System.Security.dll")

if ($SelfTest) {
    [VideoMaterialRenamer.MaterialRenamerForm]::RunSelfTest()
    return
}

if ($SmokeTest) {
    [VideoMaterialRenamer.MaterialRenamerForm]::RunSmokeTest()
    return
}

[VideoMaterialRenamer.Program]::Run()
