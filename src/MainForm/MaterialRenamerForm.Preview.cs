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

        private void RenderAll()
        {
            RenderGrid();
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (previewList == null)
            {
                return;
            }

            currentPlan.Clear();
            currentPlan.AddRange(RenamePlanBuilder.BuildPlan(rows, (int)numEpisode.Value, GetDefaultScene(), chkKeepExtension.Checked, IsExport1080pEnabled(), IsRowSceneEnabled()));

            previewList.BeginUpdate();
            try
            {
                previewList.Items.Clear();
                previewList.Groups.Clear();
                Dictionary<int, ListViewGroup> previewGroups = new Dictionary<int, ListViewGroup>();
                foreach (RenamePlan entry in currentPlan)
                {
                    bool firstInGroup = !previewGroups.ContainsKey(entry.RowIndex);
                    if (firstInGroup)
                    {
                        ListViewGroup group = new ListViewGroup(
                            string.Format("第 {0} 行 / 场号 {1} / 镜号 {2}", entry.RowIndex, entry.Scene, entry.ShotLabel),
                            HorizontalAlignment.Left);
                        previewGroups[entry.RowIndex] = group;
                        previewList.Groups.Add(group);
                    }

                    ListViewItem item = new ListViewItem(firstInGroup ? "▶ " + entry.RowIndex : "  " + entry.RowIndex);
                    item.Tag = entry;
                    item.Group = previewGroups[entry.RowIndex];
                    item.SubItems.Add(entry.ShotLabel);
                    item.SubItems.Add(entry.TailSegment);
                    item.SubItems.Add(entry.ColumnName);
                    item.SubItems.Add(entry.OldName);
                    item.SubItems.Add(entry.NewName);
                    item.SubItems.Add(entry.Status);
                    item.SubItems.Add(GetListVideoSummary(entry.OldPath));
                    if (firstInGroup)
                    {
                        item.Font = new Font(previewList.Font, FontStyle.Bold);
                    }
                    ApplyPreviewItemStatusStyle(item, entry);

                    previewList.Items.Add(item);
                }

                UpdatePreviewStatusSummary();
            }
            finally
            {
                previewList.EndUpdate();
            }

            SchedulePreviewColumnResize();
            SchedulePlanStatusChecks();
            if (previewList.SelectedItems.Count == 0)
            {
                UpdateSelectedCellDetails();
            }
        }

        private void SchedulePreviewColumnResize()
        {
            if (previewList == null)
            {
                return;
            }

            if (previewColumnResizeTimer == null)
            {
                previewColumnResizeTimer = new System.Windows.Forms.Timer();
                previewColumnResizeTimer.Interval = 120;
                previewColumnResizeTimer.Tick += delegate
                {
                    previewColumnResizeTimer.Stop();
                    AutoResizePreviewColumns();
                };
            }

            previewColumnResizeTimer.Stop();
            previewColumnResizeTimer.Start();
        }

        private void AutoResizePreviewColumns()
        {
            if (previewList == null || previewList.Columns.Count < 8)
            {
                return;
            }

            const int originalNameColumn = 4;
            previewList.Columns[originalNameColumn].Width = 144;
            for (int index = 0; index < previewList.Columns.Count; index++)
            {
                if (index == originalNameColumn)
                {
                    continue;
                }

                previewList.Columns[index].Width = GetPreviewColumnWidth(index);
            }
        }

        private int GetPreviewColumnWidth(int columnIndex)
        {
            int width = MeasurePreviewText(previewList.Columns[columnIndex].Text, previewList.Font) + 20;
            int measuredRows = 0;
            foreach (ListViewItem item in previewList.Items)
            {
                if (columnIndex < item.SubItems.Count)
                {
                    width = Math.Max(width, MeasurePreviewText(item.SubItems[columnIndex].Text, item.Font) + 20);
                }

                measuredRows++;
                if (measuredRows >= 120)
                {
                    break;
                }
            }

            return Math.Min(GetPreviewColumnMaximumWidth(columnIndex), Math.Max(GetPreviewColumnMinimumWidth(columnIndex), width));
        }

        private static int MeasurePreviewText(string text, Font font)
        {
            return TextRenderer.MeasureText(string.IsNullOrEmpty(text) ? " " : text, font).Width;
        }

        private static int GetPreviewColumnMinimumWidth(int columnIndex)
        {
            switch (columnIndex)
            {
                case 0:
                    return 48;
                case 1:
                    return 54;
                case 2:
                    return 60;
                case 3:
                    return 76;
                case 5:
                    return 160;
                case 6:
                    return 86;
                case 7:
                    return 120;
                default:
                    return 72;
            }
        }

        private static int GetPreviewColumnMaximumWidth(int columnIndex)
        {
            switch (columnIndex)
            {
                case 0:
                    return 84;
                case 1:
                    return 92;
                case 2:
                    return 180;
                case 3:
                    return 120;
                case 5:
                    return 420;
                case 6:
                    return 160;
                case 7:
                    return 220;
                default:
                    return 240;
            }
        }

        private void ApplyPreviewItemStatusStyle(ListViewItem item, RenamePlan entry)
        {
            if (item == null || entry == null)
            {
                return;
            }

            item.BackColor = entry.RowIndex % 2 == 0 ? UiTheme.PreviewAltBack(darkMode) : UiTheme.ControlBack(darkMode);
            item.ForeColor = UiTheme.TextColor(darkMode);

            if (RenamePlanBuilder.IsBlockingIssue(entry))
            {
                item.BackColor = UiTheme.PreviewErrorBack(darkMode);
            }
            else if (entry.Status == "未变化")
            {
                item.BackColor = UiTheme.PreviewNeutralBack(darkMode);
            }
        }

        private void UpdatePreviewStatusSummary()
        {
            if (statusLabel == null)
            {
                return;
            }

            int errors = currentPlan.Count(RenamePlanBuilder.IsBlockingIssue);
            if (currentPlan.Count == 0)
            {
                statusLabel.Text = "把视频拖到表格 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 单元格。";
            }
            else if (errors > 0)
            {
                statusLabel.Text = string.Format("共 {0} 个视频，发现 {1} 个命名问题；请处理后再执行。", currentPlan.Count, errors);
            }
            else if (IsExport1080pEnabled())
            {
                statusLabel.Text = IsExportWatermarkEnabled()
                    ? string.Format("共 {0} 个视频，预览无冲突；导出时会在左上角加入新文件名水印。", currentPlan.Count)
                    : string.Format("共 {0} 个视频，预览无冲突；执行时可选择覆盖原文件或另存为新文件。", currentPlan.Count);
            }
            else
            {
                statusLabel.Text = string.Format("共 {0} 个视频，预览无冲突。{1} 列先编号，{2} 列接着编号。", currentPlan.Count, GetMainColumnLetter(), GetBackupColumnLetter());
            }
        }

        private void RefreshPreviewStatusOnly()
        {
            if (previewList == null)
            {
                return;
            }

            previewList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in previewList.Items)
                {
                    RenamePlan entry = item.Tag as RenamePlan;
                    if (entry == null)
                    {
                        continue;
                    }

                    if (item.SubItems.Count > 6)
                    {
                        item.SubItems[6].Text = entry.Status;
                    }
                    ApplyPreviewItemStatusStyle(item, entry);
                }
            }
            finally
            {
                previewList.EndUpdate();
            }

            UpdatePreviewStatusSummary();
        }

        private void SchedulePlanStatusChecks()
        {
            int version = ++planCheckVersion;
            List<RenamePlan> targets = currentPlan
                .Where(p => p.Status == "目标已存在" && !string.IsNullOrWhiteSpace(p.TargetPath))
                .ToList();

            if (targets.Count == 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int offset = 0; offset < targets.Count; offset += PlanStatusCheckBatchSize)
                {
                    List<RenamePlan> batch = targets.Skip(offset).Take(PlanStatusCheckBatchSize).ToList();
                    HashSet<string> lockedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (RenamePlan entry in batch)
                    {
                        if (RenamePlanBuilder.IsFileLocked(entry.TargetPath))
                        {
                            lockedPaths.Add(entry.TargetPath);
                        }
                    }

                    if (lockedPaths.Count > 0)
                    {
                        QueueOnUi(delegate
                        {
                            if (version != planCheckVersion)
                            {
                                return;
                            }

                            foreach (RenamePlan entry in currentPlan)
                            {
                                if (entry.Status == "目标已存在" && lockedPaths.Contains(entry.TargetPath))
                                {
                                    entry.Status = "目标文件被占用";
                                }
                            }

                            RefreshPreviewStatusOnly();
                        });
                    }

                    Thread.Sleep(20);
                }
            });
        }

        private void ShowDuplicateFileWarning(List<string> duplicateNames)
        {
            if (duplicateNames == null || duplicateNames.Count == 0)
            {
                return;
            }

            List<string> lines = duplicateNames.Take(10).Select(name => " - " + name).ToList();
            if (duplicateNames.Count > 10)
            {
                lines.Add("... 另有 " + (duplicateNames.Count - 10) + " 个重复文件");
            }

            MessageBox.Show(
                this,
                "检测到重复文件，已自动跳过，不会重复加入表格。\r\n\r\n" + string.Join("\r\n", lines.ToArray()),
                "重复文件已跳过",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private void ShowCurrentPlanIssueWarning()
        {
            if (currentPlan == null || currentPlan.Count == 0)
            {
                return;
            }

            List<RenamePlan> issues = currentPlan.Where(RenamePlanBuilder.IsBlockingIssue).ToList();
            if (issues.Count == 0)
            {
                return;
            }

            MessageBox.Show(
                this,
                BuildIssueMessage(issues),
                "检测到文件问题",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private static string BuildIssueMessage(List<RenamePlan> issues)
        {
            List<string> lines = new List<string>();
            lines.Add("检测到以下问题，请处理后再执行重命名：");
            lines.Add("");

            foreach (RenamePlan issue in issues.Take(10))
            {
                lines.Add(string.Format(
                    "第 {0} 行 {1} {2}：{3}",
                    issue.RowIndex,
                    issue.ColumnName,
                    issue.TailSegment,
                    issue.Status));
                lines.Add("  原文件：" + issue.OldName);
                lines.Add("  目标名：" + issue.NewName);
            }

            if (issues.Count > 10)
            {
                lines.Add("");
                lines.Add("... 另有 " + (issues.Count - 10) + " 个问题");
            }

            return string.Join("\r\n", lines.ToArray());
        }
    }
}
