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
            RefreshPreview(null);
        }

        // prebuiltPlan：调用方（如增量刷新的回退路径）已构建好的计划——
        // 直接采用，避免同一次刷新里 BuildPlan 跑两遍（每遍都对每个文件
        // 做两次 File.Exists）。传 null 则正常构建。
        private void RefreshPreview(List<RenamePlan> prebuiltPlan)
        {
            if (previewList == null)
            {
                return;
            }

            // 阶段11b：单次刷新内 FileExists 记忆化（重复路径不再重复打磁盘）。
            ReplaceCurrentPlan(prebuiltPlan ?? RenamePlanBuilder.BuildPlan(rows, ReadNamingSettings(), new CachedFileSystemProbe(RealFileSystemProbe.Instance)));

            previewList.BeginUpdate();
            try
            {
                previewList.Items.Clear();
                previewList.Groups.Clear();
                previewItemsByPath.Clear();
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
                    item.SubItems.Add(PlanStatusText.For(entry.Status));
                    item.SubItems.Add(GetListVideoSummary(entry.OldPath));
                    if (firstInGroup)
                    {
                        if (previewGroupFont == null || !previewGroupFont.FontFamily.Equals(previewList.Font.FontFamily))
                        {
                            if (previewGroupFont != null) { previewGroupFont.Dispose(); }
                            previewGroupFont = new Font(previewList.Font, FontStyle.Bold);
                        }
                        item.Font = previewGroupFont;
                    }
                    ApplyPreviewItemStatusStyle(item, entry);

                    previewList.Items.Add(item);

                    // OldPath → 项目 反查表：元数据回填从每次全表线性扫描
                    // 降为 O(1)（n 个文件全部回填时原来是 O(n²)）。
                    List<ListViewItem> pathItems;
                    if (!previewItemsByPath.TryGetValue(entry.OldPath, out pathItems))
                    {
                        pathItems = new List<ListViewItem>();
                        previewItemsByPath[entry.OldPath] = pathItems;
                    }
                    pathItems.Add(item);
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
            else if (entry.Status == PlanStatus.Unchanged)
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
                StatusText = "把视频拖到表格 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 单元格。";
            }
            else if (errors > 0)
            {
                StatusText = string.Format("共 {0} 个视频，发现 {1} 个命名问题；请处理后再执行。", currentPlan.Count, errors);
            }
            else if (IsExport1080pEnabled())
            {
                StatusText = IsExportWatermarkEnabled()
                    ? string.Format("共 {0} 个视频，预览无冲突；导出时会在左上角加入新文件名水印。", currentPlan.Count)
                    : string.Format("共 {0} 个视频，预览无冲突；执行时可选择覆盖原文件或另存为新文件。", currentPlan.Count);
            }
            else
            {
                StatusText = string.Format("共 {0} 个视频，预览无冲突。{1} 列先编号，{2} 列接着编号。", currentPlan.Count, GetMainColumnLetter(), GetBackupColumnLetter());
            }
        }

        // 增量刷新：镜号/场号/尾段变化时，复用现有预览项，仅更新文本与配色，避免整表重建。
        private void RefreshPreviewNamesOnly()
        {
            if (previewList == null)
            {
                return;
            }

            List<RenamePlan> rebuilt = RenamePlanBuilder.BuildPlan(rows, ReadNamingSettings(), new CachedFileSystemProbe(RealFileSystemProbe.Instance));
            if (rebuilt.Count != previewList.Items.Count)
            {
                // 计数变化 → 整表重建；把已建好的计划递交过去，不再二次 BuildPlan。
                RefreshPreview(rebuilt);
                return;
            }

            ReplaceCurrentPlan(rebuilt);

            previewList.BeginUpdate();
            try
            {
                for (int i = 0; i < previewList.Items.Count; i++)
                {
                    ListViewItem item = previewList.Items[i];
                    RenamePlan entry = rebuilt[i];
                    item.Tag = entry;
                    if (item.SubItems.Count > 6)
                    {
                        item.SubItems[1].Text = entry.ShotLabel;
                        item.SubItems[2].Text = entry.TailSegment;
                        item.SubItems[5].Text = entry.NewName;
                        item.SubItems[6].Text = PlanStatusText.For(entry.Status);
                    }

                    // 分组标题包含场号/镜号，增量路径也要同步（修复既有缺陷：
                    // 网格内编辑场/镜号后分组标题一直显示旧值）。
                    if (item.Group != null)
                    {
                        string header = string.Format("第 {0} 行 / 场号 {1} / 镜号 {2}", entry.RowIndex, entry.Scene, entry.ShotLabel);
                        if (item.Group.Header != header)
                        {
                            item.Group.Header = header;
                        }
                    }

                    ApplyPreviewItemStatusStyle(item, entry);
                }
                UpdatePreviewStatusSummary();
            }
            finally
            {
                previewList.EndUpdate();
            }

            SchedulePlanStatusChecks();
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
                        item.SubItems[6].Text = PlanStatusText.For(entry.Status);
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
                .Where(p => p.Status == PlanStatus.TargetExists && !string.IsNullOrWhiteSpace(p.TargetPath))
                .ToList();

            if (targets.Count == 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int offset = 0; offset < targets.Count; offset += PlanStatusCheckBatchSize)
                {
                    // 循环内检查版本：被新一轮刷新取代的旧工作线程立即停止
                    //（原实现只在 UI 回投时过滤，旧线程会继续每 20ms 排它
                    // 打开文件探测锁，快速刷新时多个探测循环叠加）。
                    if (version != planCheckVersion)
                    {
                        return;
                    }

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
                                if (entry.Status == PlanStatus.TargetExists && lockedPaths.Contains(entry.TargetPath))
                                {
                                    entry.Status = PlanStatus.TargetLocked;
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
                    PlanStatusText.For(issue.Status)));
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
