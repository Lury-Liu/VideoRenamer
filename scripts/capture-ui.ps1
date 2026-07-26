# 主窗体截图采集：用真实源码经 Add-Type 构造窗体，填入固定样例数据，
# DrawToBitmap 输出浅色/深色两张 PNG，供视觉阶段（9/10）每次提交后人工核对。
# 用法： powershell -NoProfile -STA -ExecutionPolicy Bypass -File scripts\capture-ui.ps1 [-OutDir <dir>]
# 注意：DrawToBitmap 对个别 OS 绘制部件（滚动条/分组头）还原度有限，
# 用于核对配色与布局结构足够；最终仍以人工运行核对为准。
param(
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne "STA") {
    throw "必须以 -STA 运行（WinForms 控件要求）。"
}

Set-Location (Join-Path $PSScriptRoot "..")
. .\scripts\build-common.ps1
Add-Type -Path (Get-SourceFiles) -ReferencedAssemblies (Get-ReferenceAssemblies)

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path (Get-Location) "dist\ui-captures"
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$fixtureDir = Join-Path ([System.IO.Path]::GetTempPath()) ("VmrCapture_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $fixtureDir | Out-Null

try {
    $flags = [System.Reflection.BindingFlags]"NonPublic,Instance"

    foreach ($dark in @($false, $true)) {
        $form = New-Object VideoMaterialRenamer.MaterialRenamerForm

        # 固定样例：3 行，含主/备素材与一个缺失文件（触发状态配色）。
        $rowsField = [VideoMaterialRenamer.MaterialRenamerForm].GetField("rows", $flags)
        $rows = $rowsField.GetValue($form)
        $rows.Clear()
        for ($r = 1; $r -le 3; $r++) {
            $row = New-Object VideoMaterialRenamer.ShotRow
            $row.Scene = 1
            $row.Sequence = $r
            for ($f = 1; $f -le 2; $f++) {
                $p = Join-Path $fixtureDir ("clip{0}_{1}.mp4" -f $r, $f)
                Set-Content -LiteralPath $p -Value "x"
                $row.MainFiles.Add($p)
            }
            $rows.Add($row)
        }
        $rows[1].ShotSuffix = "A"
        $rows[2].MainFiles.Add((Join-Path $fixtureDir "missing.mp4"))  # 源文件丢失

        $darkField = [VideoMaterialRenamer.MaterialRenamerForm].GetField("darkMode", $flags)
        $darkField.SetValue($form, $dark)
        $applyTheme = [VideoMaterialRenamer.MaterialRenamerForm].GetMethod("ApplyTheme", $flags)
        $applyTheme.Invoke($form, @()) | Out-Null
        $renderAll = [VideoMaterialRenamer.MaterialRenamerForm].GetMethod("RenderAll", $flags)
        $renderAll.Invoke($form, @()) | Out-Null

        # 不 Show()：CreateControl + 指定尺寸即可离屏渲染。
        $form.StartPosition = "Manual"
        $form.Location = New-Object System.Drawing.Point(-10000, -10000)
        $form.Show()
        [System.Windows.Forms.Application]::DoEvents()

        $bmp = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
        $form.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, $form.Width, $form.Height)))
        $name = if ($dark) { "main-dark.png" } else { "main-light.png" }
        $outPath = Join-Path $OutDir $name
        $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $form.Close()
        $form.Dispose()
        Write-Output ("CAPTURED " + $outPath)
    }
}
finally {
    Remove-Item -LiteralPath $fixtureDir -Recurse -Force -ErrorAction SilentlyContinue
}
