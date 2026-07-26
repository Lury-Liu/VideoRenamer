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

        private static void ReplaceOriginalWithExport(string tempPath, RenamePlan entry)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
            {
                File.Replace(tempPath, entry.OldPath, null);
                return;
            }

            if (File.Exists(entry.TargetPath))
            {
                throw new IOException("目标文件已存在。");
            }

            File.Move(tempPath, entry.TargetPath);
            try
            {
                File.Delete(entry.OldPath);
            }
            catch (Exception ex)
            {
                throw new IOException("已生成新文件，但原文件删除失败：" + ex.Message);
            }
        }

        private static void ExportOneVideoTo1080p(string ffmpegPath, RenamePlan entry, ExportOutputMode outputMode, bool watermarkEnabled, Action<int> progressCallback)
        {
            if (entry == null)
            {
                throw new InvalidOperationException("导出记录无效。");
            }

            if (!File.Exists(entry.OldPath))
            {
                throw new FileNotFoundException("源文件不存在。", entry.OldPath);
            }

            if (outputMode == ExportOutputMode.SaveAsNewFile &&
                File.Exists(entry.TargetPath) &&
                !StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
            {
                throw new IOException("目标文件已存在。");
            }

            string directory = outputMode == ExportOutputMode.OverwriteOriginal ? Path.GetDirectoryName(entry.OldPath) : Path.GetDirectoryName(entry.TargetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string outputPath = entry.TargetPath;
            string tempPath = "";
            string watermarkText = watermarkEnabled ? entry.NewName : "";
            if (outputMode == ExportOutputMode.OverwriteOriginal)
            {
                tempPath = Path.Combine(directory, ".vmr_" + Guid.NewGuid().ToString("N") + Path.GetExtension(entry.TargetPath));
                outputPath = tempPath;
            }

            try
            {
                try
                {
                    FfmpegRunner.RunExport(ffmpegPath, entry.OldPath, outputPath, true, watermarkText, progressCallback);
                }
                catch
                {
                    if (File.Exists(outputPath))
                    {
                        File.Delete(outputPath);
                    }
                    if (progressCallback != null)
                    {
                        progressCallback(0);
                    }
                    FfmpegRunner.RunExport(ffmpegPath, entry.OldPath, outputPath, false, watermarkText, progressCallback);
                }

                if (outputMode == ExportOutputMode.OverwriteOriginal)
                {
                    ReplaceOriginalWithExport(tempPath, entry);
                    tempPath = "";
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void StartExport1080p(List<RenamePlan> plan, string ffmpegPath, ExportOutputMode outputMode, bool watermarkEnabled)
        {
            SetProgressColumnVisible(true);
            ResetProgressBars();
            SetOperationUiEnabled(false);
            statusLabel.Text = outputMode == ExportOutputMode.OverwriteOriginal
                ? (watermarkEnabled ? "正在导出并添加文件名水印，请等待..." : "正在导出并覆盖原文件，请等待...")
                : (watermarkEnabled ? "正在导出 1080x1920 新文件并添加文件名水印，请等待..." : "正在导出 1080x1920 新文件，请等待...");

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> failures = new List<string>();
                List<RenameOperation> successfulOperations = new List<RenameOperation>();
                Dictionary<ShotRow, int> rowTotals = new Dictionary<ShotRow, int>();
                Dictionary<ShotRow, int> rowCompleted = new Dictionary<ShotRow, int>();
                foreach (RenamePlan item in plan)
                {
                    if (item.Row == null)
                    {
                        continue;
                    }
                    if (!rowTotals.ContainsKey(item.Row))
                    {
                        rowTotals[item.Row] = 0;
                        rowCompleted[item.Row] = 0;
                    }
                    rowTotals[item.Row]++;
                }

                int total = plan.Count;
                int index = 0;

                foreach (RenamePlan entry in plan)
                {
                    index++;
                    int currentIndex = index;
                    QueueOnUi(delegate
                    {
                        statusLabel.Text = string.Format("正在导出 {0}/{1}：{2}", currentIndex, total, entry.NewName);
                    });

                    try
                    {
                        Action<int> progressCallback = delegate(int percent)
                        {
                            if (entry.Row == null)
                            {
                                return;
                            }

                            int completed = rowCompleted.ContainsKey(entry.Row) ? rowCompleted[entry.Row] : 0;
                            int rowTotal = rowTotals.ContainsKey(entry.Row) ? Math.Max(1, rowTotals[entry.Row]) : 1;
                            int safePercent = Math.Max(0, Math.Min(100, percent));
                            int rowPercent = (int)Math.Max(0, Math.Min(100, Math.Round((completed + safePercent / 100.0) * 100.0 / rowTotal)));
                            QueueOnUi(delegate
                            {
                                entry.Row.ProgressPercent = rowPercent;
                                RenderGridProgress(entry.RowIndex - 1);
                                statusLabel.Text = string.Format("正在导出 {0}/{1}：{2}（{3}%）", currentIndex, total, entry.NewName, safePercent);
                            });
                        };

                        ExportOneVideoTo1080p(ffmpegPath, entry, outputMode, watermarkEnabled, progressCallback);
                        if (entry.Row != null && rowCompleted.ContainsKey(entry.Row))
                        {
                            rowCompleted[entry.Row]++;
                            progressCallback(100);
                        }
                        successfulOperations.Add(new RenameOperation
                        {
                            Row = entry.Row,
                            RowIndex = entry.RowIndex,
                            IsMain = entry.IsMain,
                            FileIndex = entry.FileIndex,
                            OriginalPath = entry.OldPath,
                            RenamedPath = entry.TargetPath
                        });
                    }
                    catch (Exception ex)
                    {
                        failures.Add(entry.OldName + ": " + ex.Message);
                        if (entry.Row != null && rowCompleted.ContainsKey(entry.Row))
                        {
                            rowCompleted[entry.Row]++;
                        }
                    }
                }

                QueueOnUi(delegate
                {
                    foreach (RenameOperation op in successfulOperations)
                    {
                        if (op.Row == null)
                        {
                            continue;
                        }

                        List<string> files = op.IsMain ? op.Row.MainFiles : op.Row.BackupFiles;
                        if (op.FileIndex >= 0 && op.FileIndex < files.Count && StringComparer.OrdinalIgnoreCase.Equals(files[op.FileIndex], op.OriginalPath))
                        {
                            files[op.FileIndex] = op.RenamedPath;
                        }
                        else
                        {
                            int currentIndex = files.FindIndex(p => StringComparer.OrdinalIgnoreCase.Equals(p, op.OriginalPath));
                            if (currentIndex >= 0)
                            {
                                files[currentIndex] = op.RenamedPath;
                            }
                        }
                    }

                    RenderAll();
                    SetProgressColumnVisible(false);
                    SetOperationUiEnabled(true);

                    if (failures.Count > 0)
                    {
                        MessageBox.Show(this, string.Join("\r\n", failures.Take(8).ToArray()), "部分视频导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        string finishMessage = outputMode == ExportOutputMode.OverwriteOriginal
                            ? "已处理 " + successfulOperations.Count + " 个视频，并覆盖原文件。"
                            : "已导出 " + successfulOperations.Count + " 个 1080x1920 新文件，原始素材已保留。";
                        MessageBox.Show(this, finishMessage, "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                });
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
