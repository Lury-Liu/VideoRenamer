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
            SetProgressColumnVisible(true);
            ResetProgressBars();
            SetOperationUiEnabled(false);
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
                },
                delegate(ExportController.ExportOutcome outcome)
                {
                    foreach (RenameOperation op in outcome.Successes)
                    {
                        PlanExecutor.PatchRowFileList(op);
                    }

                    RenderAll();
                    SetProgressColumnVisible(false);
                    SetOperationUiEnabled(true);

                    if (outcome.Failures.Count > 0)
                    {
                        MessageBox.Show(this, string.Join("\r\n", outcome.Failures.Take(8).ToArray()), "部分视频导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
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
