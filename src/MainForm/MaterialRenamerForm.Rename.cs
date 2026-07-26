using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VideoMaterialRenamer
{
    public partial class MaterialRenamerForm
    {

        private void RenameFiles()
        {
            if (operationRunning)
            {
                statusLabel.Text = "当前正在处理视频，请等待完成。";
                return;
            }

            RefreshPreview();
            if (currentPlan.Count == 0)
            {
                MessageBox.Show(this, "请先把视频拖到 " + GetMainColumnLetter() + " 或 " + GetBackupColumnLetter() + " 列。", "没有素材", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<RenamePlan> badRows = currentPlan.Where(RenamePlanBuilder.IsBlockingIssue).ToList();
            if (badRows.Count > 0)
            {
                MessageBox.Show(this, BuildIssueMessage(badRows), "存在文件问题", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<string> preview = currentPlan.Take(8).Select(p => p.OldName + "  ->  " + p.NewName).ToList();
            if (currentPlan.Count > 8)
            {
                preview.Add("... 另有 " + (currentPlan.Count - 8) + " 个文件");
            }

            bool export1080p = IsExport1080pEnabled();
            if (export1080p)
            {
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
                    exportPlan = ExportPlanBuilder.Prepare(currentPlan.ToList(), outputMode);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "导出目标异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                List<string> exportPreview = exportPlan.Take(8).Select(p => p.OldName + "  ->  " + p.NewName).ToList();
                if (exportPlan.Count > 8)
                {
                    exportPreview.Add("... 另有 " + (exportPlan.Count - 8) + " 个文件");
                }

                string modeText = outputMode == ExportOutputMode.OverwriteOriginal
                    ? "即将导出 1080x1920 并覆盖原文件。该操作不会保留原始 720p 文件，是否继续？"
                    : "即将导出 1080x1920 新视频文件，原始素材会保留。是否继续？";
                bool watermarkEnabled = IsExportWatermarkEnabled();
                if (watermarkEnabled)
                {
                    modeText += "\r\n\r\n导出画面左上角会加入新文件名水印。";
                }
                string exportMessage = modeText + "\r\n\r\n" + string.Join("\r\n", exportPreview.ToArray());
                DialogResult exportConfirm = MessageBox.Show(this, exportMessage, "确认导出1080p", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (exportConfirm != DialogResult.Yes)
                {
                    return;
                }

                StartExport1080p(exportPlan, ffmpegPath, outputMode, watermarkEnabled);
                return;
            }

            string message = "即将直接修改原视频文件名，是否继续？\r\n\r\n" + string.Join("\r\n", preview.ToArray());
            DialogResult confirm = MessageBox.Show(this, message, "确认重命名", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            List<string> failures = new List<string>();
            List<RenameOperation> successfulOperations = new List<RenameOperation>();
            foreach (RenamePlan entry in currentPlan)
            {
                try
                {
                    string originalPath = entry.OldPath;
                    string renamedPath = entry.TargetPath;
                    if (!StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
                    {
                        File.Move(entry.OldPath, entry.TargetPath);
                    }

                    if (entry.IsMain)
                    {
                        entry.Row.MainFiles[entry.FileIndex] = entry.TargetPath;
                    }
                    else
                    {
                        entry.Row.BackupFiles[entry.FileIndex] = entry.TargetPath;
                    }

                    if (!StringComparer.OrdinalIgnoreCase.Equals(originalPath, renamedPath))
                    {
                        successfulOperations.Add(new RenameOperation
                        {
                            Row = entry.Row,
                            RowIndex = entry.RowIndex,
                            IsMain = entry.IsMain,
                            FileIndex = entry.FileIndex,
                            OriginalPath = originalPath,
                            RenamedPath = renamedPath
                        });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(entry.OldName + ": " + ex.Message);
                }
            }

            if (successfulOperations.Count > 0)
            {
                undoStack.Push(successfulOperations);
                SaveRenameHistory(successfulOperations);
            }

            RenderAll();

            if (failures.Count > 0)
            {
                MessageBox.Show(this, string.Join("\r\n", failures.Take(8).ToArray()), "部分文件重命名失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                MessageBox.Show(this, "已处理 " + currentPlan.Count + " 个视频文件。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
