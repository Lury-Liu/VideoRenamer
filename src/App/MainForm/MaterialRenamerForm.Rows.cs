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

namespace VideoRenamer
{
    public partial class MaterialRenamerForm
    {

        private void ImportSelectedCell()
        {
            if (grid.CurrentCell == null || (grid.CurrentCell.ColumnIndex != GridMainColumn && grid.CurrentCell.ColumnIndex != GridBackupColumn))
            {
                MessageBox.Show("请先选中一个 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 单元格。", "未选择素材格", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择视频文件";
                dialog.Multiselect = true;
                dialog.Filter = "视频文件|*.mp4;*.mov;*.m4v;*.avi;*.mkv;*.wmv;*.flv;*.webm;*.mts;*.m2ts;*.3gp;*.mpeg;*.mpg|所有文件|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddFilesToShotCell(grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex, dialog.FileNames);
                }
            }
        }

        private void DeleteSelectedPreviewRecord()
        {
            if (previewList.SelectedItems.Count == 0)
            {
                StatusText = "请先在底部预览中选中一条素材记录。";
                return;
            }

            RenamePlan entry = previewList.SelectedItems[0].Tag as RenamePlan;
            if (entry == null || entry.Row == null)
            {
                StatusText = "选中的预览记录无效。";
                return;
            }

            List<string> files = entry.IsMain ? entry.Row.MainFiles : entry.Row.BackupFiles;
            List<string> tails = entry.IsMain ? entry.Row.MainTailOverrides : entry.Row.BackupTailOverrides;
            RenamePlanBuilder.EnsureTailOverrideSize(entry.Row, entry.IsMain);
            if (entry.FileIndex < 0 || entry.FileIndex >= files.Count)
            {
                StatusText = "选中的素材记录已经变化，请重新选择。";
                RefreshPreview();
                return;
            }

            string removed = Path.GetFileName(files[entry.FileIndex]);
            files.RemoveAt(entry.FileIndex);
            if (entry.FileIndex >= 0 && entry.FileIndex < tails.Count)
            {
                tails.RemoveAt(entry.FileIndex);
            }
            RenderGridRow(entry.RowIndex - 1);
            RefreshPreview();
            StatusText = "已删除单条记录：" + removed;
        }

        private int GetNextShotSequence()
        {
            int max = 0;
            foreach (ShotRow row in rows)
            {
                if (row.Sequence > max)
                {
                    max = row.Sequence;
                }
            }

            return Math.Max(1, max + 1);
        }

        private void AddEmptyRow()
        {
            int currentRowIndex = GetCurrentGridRowIndex();
            int insertIndex = currentRowIndex >= 0 ? currentRowIndex + 1 : rows.Count;
            rows.Insert(insertIndex, new ShotRow { Scene = GetDefaultScene(), Sequence = GetNextShotSequence() });
            InsertGridRowAt(insertIndex);
            RefreshPreview();
            SelectGridCell(insertIndex, GetDefaultGridFocusColumn());
            StatusText = IsRowSceneEnabled()
                ? "已新增一条空记录；场号、镜号可直接改成任意正整数。"
                : "已新增一条空记录；镜号可直接改成任意正整数。";
        }

        private void MoveCurrentRow(int direction)
        {
            int currentRowIndex = GetCurrentGridRowIndex();
            if (currentRowIndex < 0)
            {
                StatusText = "请先选中要移动的行。";
                return;
            }

            int targetIndex = currentRowIndex + direction;
            if (targetIndex < 0 || targetIndex >= rows.Count)
            {
                StatusText = "当前行已经在边界位置。";
                return;
            }

            int columnIndex = grid.CurrentCell != null ? grid.CurrentCell.ColumnIndex : 0;
            ShotRow moving = rows[currentRowIndex];
            rows.RemoveAt(currentRowIndex);
            rows.Insert(targetIndex, moving);
            RepopulateGridRowPair(currentRowIndex, targetIndex);
            RefreshPreview();
            SelectGridCell(targetIndex, columnIndex);
            StatusText = IsRowSceneEnabled()
                ? "已移动当前行；场号、镜号保持不变。"
                : "已移动当前行；镜号保持不变。";
        }

        private void DeleteCurrentRow()
        {
            int currentRowIndex = GetCurrentGridRowIndex();
            if (currentRowIndex < 0)
            {
                StatusText = "请先选中要删除的行。";
                return;
            }

            ShotRow row = rows[currentRowIndex];
            bool hasContent = row.MainFiles.Count > 0 || row.BackupFiles.Count > 0;
            if (hasContent)
            {
                DialogResult confirm = MessageBox.Show(
                    this,
                    "是否删除第 " + (currentRowIndex + 1) + " 行及其中所有素材记录？\r\n\r\n该操作只会从表格中移除记录，不会删除磁盘上的视频文件。",
                    "确认删除当前行",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    return;
                }
            }

            int columnIndex = grid.CurrentCell != null ? grid.CurrentCell.ColumnIndex : 0;
            rows.RemoveAt(currentRowIndex);
            bool resetToSingleEmptyRow = rows.Count == 0;
            if (resetToSingleEmptyRow)
            {
                rows.Add(new ShotRow { Scene = GetDefaultScene(), Sequence = 1 });
            }

            if (resetToSingleEmptyRow)
            {
                RenderGrid();
            }
            else
            {
                RemoveGridRowAt(currentRowIndex);
            }
            RefreshPreview();
            int nextRowIndex = Math.Min(currentRowIndex, rows.Count - 1);
            SelectGridCell(nextRowIndex, columnIndex);
            StatusText = IsRowSceneEnabled()
                ? "已删除当前行，下方行已上移；场号、镜号保持不变。"
                : "已删除当前行，下方行已上移；镜号保持不变。";
        }

        private void ClearSelectedCellFiles()
        {
            if (grid.CurrentCell == null)
            {
                return;
            }

            int rowIndex = grid.CurrentCell.RowIndex;
            int columnIndex = grid.CurrentCell.ColumnIndex;
            if (rowIndex < 0 || rowIndex >= rows.Count)
            {
                return;
            }

            if (columnIndex == GridMainColumn)
            {
                rows[rowIndex].MainFiles.Clear();
                rows[rowIndex].MainTailOverrides.Clear();
            }
            else if (columnIndex == GridBackupColumn)
            {
                rows[rowIndex].BackupFiles.Clear();
                rows[rowIndex].BackupTailOverrides.Clear();
            }
            else
            {
                StatusText = "当前单元格不是素材列。";
                return;
            }

            RenderGridRow(rowIndex);
            RefreshPreview();
            grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
        }

        private void RemoveEmptyTailRows()
        {
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                ShotRow row = rows[i];
                if (row.MainFiles.Count == 0 && row.BackupFiles.Count == 0)
                {
                    rows.RemoveAt(i);
                }
                else
                {
                    break;
                }
            }

            if (rows.Count == 0)
            {
                for (int i = 1; i <= AppInfo.DefaultRowCount; i++)
                {
                    rows.Add(new ShotRow { Scene = GetDefaultScene(), Sequence = i });
                }
            }

            RenderAll();
            StatusText = IsRowSceneEnabled()
                ? "已删除尾部空白行；场号、镜号保持不变。"
                : "已删除尾部空白行；镜号保持不变。";
        }

        private void ClearAllMaterials()
        {
            DialogResult confirm = MessageBox.Show(this, "是否清空表格中所有素材？场号和镜号会保留。", "确认清空", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            foreach (ShotRow row in rows)
            {
                row.MainFiles.Clear();
                row.BackupFiles.Clear();
                row.MainTailOverrides.Clear();
                row.BackupTailOverrides.Clear();
            }
            RenderAll();
            ShowNoVideoDetails();
        }
    }
}
