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

        private bool QueueOnUi(MethodInvoker action)
        {
            if (action == null || IsDisposed || !IsHandleCreated)
            {
                return false;
            }

            try
            {
                BeginInvoke(action);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void StartStaBackground(ThreadStart action)
        {
            if (action == null)
            {
                return;
            }

            Thread thread = new Thread(action);
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private VideoFileInfo GetFastVideoInfo(string path)
        {
            VideoFileInfo info = new VideoFileInfo();
            info.Path = path ?? "";
            info.FileName = string.IsNullOrWhiteSpace(path) ? "" : Path.GetFileName(path);
            info.SizeText = "";
            info.ResolutionText = "读取中";
            info.ModifiedText = "";
            info.Exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);

            if (!info.Exists)
            {
                info.ResolutionText = "未知";
                return info;
            }

            try
            {
                FileInfo file = new FileInfo(path);
                info.SizeText = VideoMetadataReader.FormatBytes(file.Length);
                info.ModifiedText = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
            }

            return info;
        }

        private void QueueVideoInfoLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || videoInfoCache.ContainsKey(path) || pendingVideoInfoLoads.Contains(path))
            {
                return;
            }

            pendingVideoInfoLoads.Add(path);
            StartStaBackground(delegate
            {
                VideoFileInfo info = VideoMetadataReader.Read(path);
                QueueOnUi(delegate
                {
                    pendingVideoInfoLoads.Remove(path);
                    videoInfoCache[path] = info;
                    UpdatePreviewListInfoSummary(path, info);
                    if (IsCurrentDetailPath(path))
                    {
                        RenderVideoInfo(info, currentDetailNewName, currentDetailContext);
                    }
                });
            });
        }

        private bool TryGetThumbnailFromCache(string path, out Image image)
        {
            image = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (!thumbnailCache.TryGetValue(path, out image))
            {
                return false;
            }

            TouchThumbnailCacheNode(path);
            return image != null;
        }

        private void TouchThumbnailCacheNode(string path)
        {
            LinkedListNode<string> node;
            if (!thumbnailCacheNodes.TryGetValue(path, out node))
            {
                node = thumbnailCacheOrder.AddLast(path);
                thumbnailCacheNodes[path] = node;
                return;
            }

            thumbnailCacheOrder.Remove(node);
            thumbnailCacheOrder.AddLast(node);
        }

        private void AddThumbnailToCache(string path, Image image)
        {
            if (string.IsNullOrWhiteSpace(path) || image == null)
            {
                if (image != null)
                {
                    image.Dispose();
                }
                return;
            }

            Image existing;
            if (thumbnailCache.TryGetValue(path, out existing) && !object.ReferenceEquals(existing, image))
            {
                existing.Dispose();
            }

            thumbnailCache[path] = image;
            TouchThumbnailCacheNode(path);
            TrimThumbnailCache();
        }

        private void TrimThumbnailCache()
        {
            while (thumbnailCache.Count > ThumbnailCacheLimit && thumbnailCacheOrder.First != null)
            {
                string path = thumbnailCacheOrder.First.Value;
                thumbnailCacheOrder.RemoveFirst();
                thumbnailCacheNodes.Remove(path);

                Image image;
                if (thumbnailCache.TryGetValue(path, out image))
                {
                    thumbnailCache.Remove(path);
                    if (image != null)
                    {
                        image.Dispose();
                    }
                }
            }
        }

        private void QueueThumbnailLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || pendingThumbnailLoads.Contains(path))
            {
                return;
            }

            Image cached;
            if (TryGetThumbnailFromCache(path, out cached))
            {
                return;
            }

            pendingThumbnailLoads.Add(path);
            StartStaBackground(delegate
            {
                Image image = VideoThumbnailProvider.GetThumbnail(path, new Size(300, 166));
                bool queued = QueueOnUi(delegate
                {
                    pendingThumbnailLoads.Remove(path);
                    if (image != null)
                    {
                        AddThumbnailToCache(path, image);
                    }

                    if (IsCurrentDetailPath(path))
                    {
                        if (image != null)
                        {
                            SetDetailImage(image, false);
                        }
                        else
                        {
                            SetDetailImage(CreatePlaceholderImage("无缩略图"), true);
                        }
                    }
                });

                if (!queued && image != null)
                {
                    image.Dispose();
                }
            });
        }

        private string GetListVideoSummary(string path)
        {
            VideoFileInfo cached;
            if (!string.IsNullOrWhiteSpace(path) && videoInfoCache.TryGetValue(path, out cached))
            {
                return cached.ListSummary;
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return "文件不存在";
            }

            try
            {
                FileInfo file = new FileInfo(path);
                return VideoMetadataReader.FormatBytes(file.Length) + " | " + (Path.GetExtension(path) ?? "").TrimStart('.').ToUpperInvariant();
            }
            catch
            {
                return "";
            }
        }

        private void UpdateSelectedPreviewDetails()
        {
            if (previewList == null || previewList.SelectedItems.Count == 0)
            {
                return;
            }

            RenamePlan entry = previewList.SelectedItems[0].Tag as RenamePlan;
            if (entry == null)
            {
                ShowNoVideoDetails();
                return;
            }

            string context = string.Format("第 {0} 行 / 场号 {1} / 镜号 {2} / {3} / {4}", entry.RowIndex, entry.Scene, entry.Shot, entry.ColumnName, entry.TailSegment);
            ShowVideoDetails(entry.OldPath, entry.NewName, context);
            UpdateCustomTailControls(entry);
        }

        private void UpdateSelectedCellDetails()
        {
            if (rendering || grid == null || grid.CurrentCell == null || thumbnailBox == null)
            {
                return;
            }

            int rowIndex = grid.CurrentCell.RowIndex;
            int columnIndex = grid.CurrentCell.ColumnIndex;
            if (rowIndex < 0 || rowIndex >= rows.Count || (columnIndex != GridMainColumn && columnIndex != GridBackupColumn))
            {
                ShowNoVideoDetails();
                return;
            }

            ShotRow row = rows[rowIndex];
            List<string> files = columnIndex == GridMainColumn ? row.MainFiles : row.BackupFiles;
            if (files.Count == 0)
            {
                ShowNoVideoDetails();
                return;
            }

            bool isMain = columnIndex == GridMainColumn;
            RenamePlan firstPlan = currentPlan.FirstOrDefault(p => p.Row == row && p.IsMain == isMain && p.FileIndex == 0);
            string newName = firstPlan == null ? "" : firstPlan.NewName;
            string context = string.Format("第 {0} 行 / 场号 {1} / 镜号 {2} / {3}，单元格共 {4} 个视频，当前显示第 1 个", rowIndex + 1, GetEffectiveScene(row, GetDefaultScene(), IsRowSceneEnabled()), row.Sequence, isMain ? "主要素材" : "备用素材", files.Count);
            ShowVideoDetails(files[0], newName, context);
            UpdateCustomTailControls(firstPlan);
        }

        private void ShowVideoDetails(string path, string newName, string context)
        {
            if (thumbnailBox == null || detailTitleLabel == null || detailInfoLabel == null || detailPathLabel == null)
            {
                return;
            }

            detailLoadVersion++;
            currentDetailPath = path ?? "";
            currentDetailNewName = newName ?? "";
            currentDetailContext = context ?? "";

            VideoFileInfo info;
            if (string.IsNullOrWhiteSpace(path) || !videoInfoCache.TryGetValue(path, out info))
            {
                info = GetFastVideoInfo(path);
                QueueVideoInfoLoad(path);
            }

            RenderVideoInfo(info, currentDetailNewName, currentDetailContext);
            Image thumbnail;
            if (TryGetThumbnailFromCache(path, out thumbnail))
            {
                SetDetailImage(thumbnail, false);
            }
            else
            {
                SetDetailImage(CreatePlaceholderImage(info.Exists ? "缩略图读取中" : "无缩略图"), true);
                QueueThumbnailLoad(path);
            }
        }

        private bool IsCurrentDetailPath(string path)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(currentDetailPath ?? "", path ?? "");
        }

        private void RenderVideoInfo(VideoFileInfo info, string newName, string context)
        {
            if (info == null)
            {
                info = GetFastVideoInfo("");
            }

            detailTitleLabel.Text = string.IsNullOrWhiteSpace(info.FileName) ? "未选择素材" : info.FileName;
            UpdatePreviewListInfoSummary(info.Path, info);

            List<string> lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(context))
            {
                lines.Add(context);
            }
            if (!string.IsNullOrWhiteSpace(newName))
            {
                lines.Add("目标文件名：" + newName);
            }
            lines.Add("大小：" + (string.IsNullOrWhiteSpace(info.SizeText) ? "未知" : info.SizeText));
            lines.Add("分辨率：" + info.ResolutionText);
            lines.Add("修改时间：" + (string.IsNullOrWhiteSpace(info.ModifiedText) ? "未知" : info.ModifiedText));
            detailInfoLabel.Text = string.Join("\r\n", lines.ToArray());
            detailPathLabel.Text = info.Path ?? "";
        }

        private void UpdateCustomTailControls(RenamePlan entry)
        {
            if (chkCustomTail == null || txtCustomTail == null)
            {
                return;
            }

            bool enabled = entry != null && entry.Row != null;
            chkCustomTail.Enabled = enabled;
            txtCustomTail.Text = enabled && entry.HasCustomTail ? entry.CustomTailText : "";

            UpdateCustomTailInputState();
        }

        private void UpdateCustomTailInputState()
        {
            if (chkCustomTail == null || txtCustomTail == null)
            {
                return;
            }

            bool hasSelection = GetSelectedPreviewEntry() != null;
            chkCustomTail.Enabled = true;
            txtCustomTail.Enabled = hasSelection && chkCustomTail.Checked;
            UiTheme.ApplyControl(chkCustomTail, darkMode);
            UiTheme.ApplyControl(txtCustomTail, darkMode);
        }

        private RenamePlan GetSelectedPreviewEntry()
        {
            if (previewList == null || previewList.SelectedItems.Count == 0)
            {
                return null;
            }

            return previewList.SelectedItems[0].Tag as RenamePlan;
        }

        private void ApplySelectedCustomTail(bool showMessage)
        {
            RenamePlan entry = GetSelectedPreviewEntry();
            if (entry == null || entry.Row == null)
            {
                if (showMessage)
                {
                    MessageBox.Show(this, "请先在底部预览中选中一条素材记录。", "未选择素材", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            if (chkCustomTail != null && !chkCustomTail.Checked)
            {
                if (showMessage)
                {
                    MessageBox.Show(this, "请先勾选自定义，再输入末尾编号或文字。", "未启用自定义", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            string normalized = "";
            string requestedNormalized = "";
            normalized = NormalizeCustomTailText(txtCustomTail == null ? "" : txtCustomTail.Text);
            requestedNormalized = normalized;
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                normalized = GetUniqueCustomTail(entry, normalized, currentPlan, (int)numEpisode.Value, entry.Scene, chkKeepExtension.Checked);
            }

            ShotRow row = entry.Row;
            bool isMain = entry.IsMain;
            int fileIndex = entry.FileIndex;

            SetTailOverride(entry, normalized);

            RefreshPreview();
            SelectPreviewEntry(row, isMain, fileIndex);
            if (statusLabel != null)
            {
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    statusLabel.Text = "已恢复默认 T 编号。";
                }
                else if (!StringComparer.Ordinal.Equals(normalized, requestedNormalized))
                {
                    statusLabel.Text = "已应用自定义末尾：" + normalized + "（自动补号）";
                }
                else
                {
                    statusLabel.Text = "已应用自定义末尾：" + normalized;
                }
            }
        }

        private void SelectPreviewEntry(ShotRow row, bool isMain, int fileIndex)
        {
            if (previewList == null)
            {
                return;
            }

            foreach (ListViewItem item in previewList.Items)
            {
                RenamePlan entry = item.Tag as RenamePlan;
                if (entry != null && entry.Row == row && entry.IsMain == isMain && entry.FileIndex == fileIndex)
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    UpdateSelectedPreviewDetails();
                    return;
                }
            }
        }

        private void UpdatePreviewListInfoSummary(string path, VideoFileInfo info)
        {
            if (previewList == null || string.IsNullOrWhiteSpace(path) || info == null)
            {
                return;
            }

            foreach (ListViewItem item in previewList.Items)
            {
                RenamePlan entry = item.Tag as RenamePlan;
                if (entry != null &&
                    StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, path) &&
                    item.SubItems.Count > 7)
                {
                    item.SubItems[7].Text = info.ListSummary;
                }
            }
        }

        private void ShowNoVideoDetails()
        {
            if (thumbnailBox == null || detailTitleLabel == null || detailInfoLabel == null || detailPathLabel == null)
            {
                return;
            }

            detailLoadVersion++;
            currentDetailPath = "";
            currentDetailNewName = "";
            currentDetailContext = "";
            detailTitleLabel.Text = "未选择素材";
            detailInfoLabel.Text = "选中底部预览记录，或选中 B/C 中已有素材的单元格，可查看缩略图、分辨率和文件大小。";
            detailPathLabel.Text = "";
            UpdateCustomTailControls(null);
            SetDetailImage(CreatePlaceholderImage("视频预览"), true);
        }

        private Image CreatePlaceholderImage(string text)
        {
            Bitmap bitmap = new Bitmap(300, 166);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(UiTheme.ControlBack(darkMode));
                using (Pen border = new Pen(UiTheme.BorderColor(darkMode)))
                {
                    graphics.DrawRectangle(border, 0, 0, bitmap.Width - 1, bitmap.Height - 1);
                }
                using (Brush brush = new SolidBrush(UiTheme.MutedText(darkMode)))
                using (Font font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold))
                {
                    SizeF size = graphics.MeasureString(text, font);
                    graphics.DrawString(text, font, brush, (bitmap.Width - size.Width) / 2, (bitmap.Height - size.Height) / 2);
                }
            }
            return bitmap;
        }

        private void SetDetailImage(Image image, bool ownsImage)
        {
            if (thumbnailBox == null)
            {
                if (ownsImage && image != null)
                {
                    image.Dispose();
                }
                return;
            }

            if (ownedDetailImage != null && !object.ReferenceEquals(ownedDetailImage, image))
            {
                ownedDetailImage.Dispose();
            }

            thumbnailBox.Image = image;
            ownedDetailImage = ownsImage ? image : null;
        }

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
            currentPlan.AddRange(BuildPlan(rows, (int)numEpisode.Value, GetDefaultScene(), chkKeepExtension.Checked, IsExport1080pEnabled(), IsRowSceneEnabled()));

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
                            string.Format("第 {0} 行 / 场号 {1} / 镜号 {2}", entry.RowIndex, entry.Scene, entry.Shot),
                            HorizontalAlignment.Left);
                        previewGroups[entry.RowIndex] = group;
                        previewList.Groups.Add(group);
                    }

                    ListViewItem item = new ListViewItem(firstInGroup ? "▶ " + entry.RowIndex : "  " + entry.RowIndex);
                    item.Tag = entry;
                    item.Group = previewGroups[entry.RowIndex];
                    item.SubItems.Add(entry.Shot.ToString());
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

            if (IsBlockingIssue(entry))
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

            int errors = currentPlan.Count(IsBlockingIssue);
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
                        if (IsFileLocked(entry.TargetPath))
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

            List<RenamePlan> issues = currentPlan.Where(IsBlockingIssue).ToList();
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
