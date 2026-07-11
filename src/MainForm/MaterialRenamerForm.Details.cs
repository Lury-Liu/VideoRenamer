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
            string context = string.Format("第 {0} 行 / 场号 {1} / 镜号 {2} / {3}，单元格共 {4} 个视频，当前显示第 1 个", rowIndex + 1, RenamePlanBuilder.GetEffectiveScene(row, GetDefaultScene(), IsRowSceneEnabled()), row.Sequence, isMain ? "主要素材" : "备用素材", files.Count);
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
            normalized = RenamePlanBuilder.NormalizeCustomTailText(txtCustomTail == null ? "" : txtCustomTail.Text);
            requestedNormalized = normalized;
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                normalized = RenamePlanBuilder.GetUniqueCustomTail(entry, normalized, currentPlan, (int)numEpisode.Value, entry.Scene, chkKeepExtension.Checked);
            }

            ShotRow row = entry.Row;
            bool isMain = entry.IsMain;
            int fileIndex = entry.FileIndex;

            RenamePlanBuilder.SetTailOverride(entry, normalized);

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

        private void ApplyBatchCustomTail()
        {
            if (previewList == null || previewList.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "请先在预览中选中一条或多条素材记录（可按住 Ctrl / Shift 多选）。", "未选择素材", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string baseName = txtCustomTail == null ? "" : txtCustomTail.Text;
            List<RenamePlan> selected = new List<RenamePlan>();
            foreach (ListViewItem item in previewList.SelectedItems)
            {
                RenamePlan entry = item.Tag as RenamePlan;
                if (entry != null && entry.Row != null)
                {
                    selected.Add(entry);
                }
            }
            if (selected.Count == 0)
            {
                return;
            }

            List<string> tails = RenamePlanBuilder.BuildBatchTails(baseName, selected.Count);
            for (int i = 0; i < selected.Count; i++)
            {
                RenamePlanBuilder.SetTailOverride(selected[i], tails[i]);
            }

            RefreshPreview();
            if (statusLabel != null)
            {
                statusLabel.Text = string.IsNullOrWhiteSpace(RenamePlanBuilder.NormalizeCustomTailText(baseName))
                    ? "已恢复所选 " + selected.Count + " 条为默认 T 编号。"
                    : "已批量应用自定义末尾到 " + selected.Count + " 条。";
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
    }
}
