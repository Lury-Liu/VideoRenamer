using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace VideoMaterialRenamer
{
    public partial class MaterialRenamerForm
    {
        // 导出编排在 ExportController / VideoExportService；这里只剩 UI 侧：
        // 启动前的界面锁定、逐行进度渲染回调、完成后的写回+刷新+对话框。
        private void StartExport1080p(List<RenamePlan> plan, string ffmpegPath, ExportOutputMode outputMode, bool watermarkEnabled)
        {
            ResetProgressBars();
            SetOperationUiEnabled(false);
            SetOperationProgressVisible(true);
            StatusText = outputMode == ExportOutputMode.OverwriteOriginal
                ? (watermarkEnabled ? "正在导出并添加文件名水印，请等待..." : "正在导出并覆盖原文件，请等待...")
                : (watermarkEnabled ? "正在导出 1080x1920 新文件并添加文件名水印，请等待..." : "正在导出 1080x1920 新文件，请等待...");

            exportController.Start(
                plan,
                ffmpegPath,
                outputMode,
                watermarkEnabled,
                delegate(RenamePlan entry, int rowPercent)
                {
                    entry.Row.ProgressPercent = rowPercent;
                    RenderGridProgress(entry.RowIndex - 1);
                    // 执行中条的"N% · 文件名"（阶段10h）：行进度回调携带的
                    // entry 即当前正在导出的文件。
                    SetOperationProgressFile(entry.NewName);
                },
                delegate(int overallPercent)
                {
                    SetOperationProgressValue(overallPercent);
                },
                delegate(ExportController.ExportOutcome outcome)
                {
                    foreach (RenameOperation op in outcome.Successes)
                    {
                        PlanExecutor.PatchRowFileList(op);
                        // 修复既有缺陷：覆盖导出改变了文件内容，但旧缓存不失效，
                        // 详情面板会一直显示导出前的大小/分辨率/缩略图。
                        videoInfoCache.Remove(op.OriginalPath);
                        videoInfoCache.Remove(op.RenamedPath);
                        thumbnailCache.Remove(op.OriginalPath);
                        thumbnailCache.Remove(op.RenamedPath);
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
                        string finishMessage = outputMode == ExportOutputMode.OverwriteOriginal
                            ? "已处理 " + outcome.Successes.Count + " 个视频，并覆盖原文件。"
                            : "已导出 " + outcome.Successes.Count + " 个 1080x1920 新文件，原始素材已保留。";
                        MessageBox.Show(this, finishMessage, "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                });
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
