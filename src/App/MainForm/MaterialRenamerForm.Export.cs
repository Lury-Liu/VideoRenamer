using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace VideoRenamer
{
    public partial class MaterialRenamerForm
    {
        // 导出编排在 ExportController / VideoExportService；这里只剩 UI 侧：
        // 启动前的界面锁定、完成后的写回+刷新+对话框。逐行进度列已移除，
        // 导出进度仅由左下角总进度条呈现（按已完成文件数推进）。
        // renameTargets=false 表示“仅导出高清、不重命名”：通常不改写行模型
        // 文件路径（原名不变或生成 _1080p 副本）；但覆盖导出到外部目录时，
        // 源文件会被成功生成的目标文件替代，必须同步写回新的路径。
        private void StartExport1080p(List<RenamePlan> plan, string ffmpegPath, ExportOutputMode outputMode, bool watermarkEnabled, bool renameTargets)
        {
            SetOperationUiEnabled(false);
            SetOperationProgressVisible(true);
            if (renameTargets)
            {
                StatusText = outputMode == ExportOutputMode.OverwriteOriginal
                    ? (watermarkEnabled ? "正在导出并添加文件名水印，请等待..." : "正在导出并覆盖原文件，请等待...")
                    : (watermarkEnabled ? "正在导出 1080x1920 新文件并添加文件名水印，请等待..." : "正在导出 1080x1920 新文件，请等待...");
            }
            else
            {
                StatusText = outputMode == ExportOutputMode.OverwriteOriginal
                    ? "正在导出高清并覆盖原文件（不重命名），请等待..."
                    : "正在导出高清为新文件（不重命名），请等待...";
            }

            exportController.Start(
                plan,
                ffmpegPath,
                outputMode,
                watermarkEnabled,
                delegate(string fileName)
                {
                    SetOperationProgressFile(fileName);
                },
                delegate(int overallPercent)
                {
                    SetOperationProgressValue(overallPercent);
                },
                delegate(ExportController.ExportOutcome outcome)
                {
                    foreach (RenameOperation op in outcome.Successes)
                    {
                        if (PlanExecutor.ShouldPatchRowFileListAfterExport(
                            renameTargets,
                            outputMode,
                            op.OriginalPath,
                            op.RenamedPath))
                        {
                            PlanExecutor.PatchRowFileList(op);
                        }
                        // 覆盖导出改变了文件内容，旧缓存必须失效，否则详情面板
                        // 会一直显示导出前的大小/分辨率。
                        videoInfoCache.Remove(op.OriginalPath);
                        videoInfoCache.Remove(op.RenamedPath);
                    }

                    RenderAll();
                    SetOperationProgressVisible(false);
                    SetOperationUiEnabled(true);

                    if (outcome.Failures.Count > 0)
                    {
                        MessageBox.Show(this, string.Join("\r\n", outcome.Failures.Take(8).ToArray()), "部分视频导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    if (outcome.Cancelled)
                    {
                        // 阶段10d 新行为：取消后如实汇报——已完成的保持完成。
                        StatusText = string.Format("导出已取消：已完成 {0} 个，其余未处理。", outcome.Successes.Count);
                        MessageBox.Show(
                            this,
                            string.Format("导出已取消。\r\n\r\n已完成 {0} 个视频（保持完成状态），其余未处理。", outcome.Successes.Count),
                            "导出已取消",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    else if (outcome.Failures.Count == 0)
                    {
                        string finishMessage;
                        if (renameTargets)
                        {
                            finishMessage = outputMode == ExportOutputMode.OverwriteOriginal
                                ? "已处理 " + outcome.Successes.Count + " 个视频，并覆盖原文件。"
                                : "已导出 " + outcome.Successes.Count + " 个 1080x1920 新文件，原始素材已保留。";
                        }
                        else
                        {
                            finishMessage = outputMode == ExportOutputMode.OverwriteOriginal
                                ? "已导出 " + outcome.Successes.Count + " 个高清视频并覆盖原文件（文件名未变）。"
                                : "已导出 " + outcome.Successes.Count + " 个高清视频（文件名未变，另存为 _1080p 副本）。";
                        }
                        MessageBox.Show(this, finishMessage, "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                });
        }

        // 独立入口：仅导出高清（1080p），不重命名。与重命名+导出共用
        // ExportController / VideoExportService，仅计划派生（PrepareExportOnly）
        // 与完成后写回策略（renameTargets=false）不同，保持单一执行路径。
        private void Export1080pOnly()
        {
            if (operationRunning)
            {
                StatusText = "当前正在处理视频，请等待完成。";
                return;
            }

            RefreshPreview();
            if (currentPlan.Count == 0)
            {
                MessageBox.Show(this, "请先把视频拖到「主要素材」或「备用素材」列。", "没有素材", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ExportOutputMode outputMode;
            if (!TryChooseExportOutputMode(out outputMode))
            {
                return;
            }

            string ffmpegPath = FfmpegLocator.Resolve();
            if (string.IsNullOrWhiteSpace(ffmpegPath))
            {
                MessageBox.Show(
                    this,
                    "没有找到 ffmpeg.exe，无法导出 1080x1920 视频。\r\n\r\n请把 ffmpeg.exe 放到软件 EXE 同目录，或放到软件目录下的 tools 文件夹中，然后重新运行。",
                    "缺少 FFmpeg",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            List<RenamePlan> exportPlan;
            try
            {
                exportPlan = ExportPlanBuilder.PrepareExportOnly(currentPlan, outputMode, GetOutputDirectoryForExport());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "导出目标异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (exportPlan.Count == 0)
            {
                MessageBox.Show(this, "没有可导出的素材（源文件均缺失）。", "没有素材", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<string> exportPreview = exportPlan.Take(8).Select(p => p.OldName + "  ->  " + p.NewName).ToList();
            if (exportPlan.Count > 8)
            {
                exportPreview.Add("... 另有 " + (exportPlan.Count - 8) + " 个文件");
            }

            string modeText = outputMode == ExportOutputMode.OverwriteOriginal
                ? "即将导出 1080x1920 高清视频并覆盖原文件，文件名保持不变。是否继续？"
                : "即将导出 1080x1920 高清新视频，文件名保持原名（另存为 _1080p 副本），原素材保留。是否继续？";
            bool watermarkEnabled = IsExportWatermarkEnabled();
            if (watermarkEnabled)
            {
                modeText += "\r\n\r\n导出画面左上角会加入原文件名水印。";
            }
            string exportMessage = modeText + "\r\n\r\n" + string.Join("\r\n", exportPreview.ToArray());
            DialogResult exportConfirm = MessageBox.Show(this, exportMessage, "确认仅导出高清", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (exportConfirm != DialogResult.Yes)
            {
                return;
            }

            StartExport1080p(exportPlan, ffmpegPath, outputMode, watermarkEnabled, false);
        }

        private bool TryChooseExportOutputMode(out ExportOutputMode outputMode)
        {
            outputMode = ExportOutputMode.OverwriteOriginal;
            DialogResult result = MessageBox.Show(
                this,
                "请选择 1080x1920 导出后的保存方式：\r\n\r\n是：覆盖原文件（默认，原文件会被替换）\r\n否：另存为新文件（保留原文件）\r\n取消：不执行",
                "选择导出保存方式",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (result == DialogResult.Cancel)
            {
                return false;
            }

            outputMode = result == DialogResult.Yes ? ExportOutputMode.OverwriteOriginal : ExportOutputMode.SaveAsNewFile;
            return true;
        }
    }
}
