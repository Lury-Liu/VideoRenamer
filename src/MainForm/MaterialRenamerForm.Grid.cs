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

        private void ApplyGridNumberColumnStyles()
        {
            if (grid == null || grid.Columns.Count <= GridShotColumn)
            {
                return;
            }

            Color sceneColor = GetSceneColumnTextColor();
            Color shotColor = GetShotColumnTextColor();
            grid.Columns[GridSceneColumn].DefaultCellStyle.ForeColor = sceneColor;
            grid.Columns[GridSceneColumn].DefaultCellStyle.SelectionForeColor = Color.White;
            grid.Columns[GridSceneColumn].HeaderCell.Style.ForeColor = sceneColor;
            grid.Columns[GridShotColumn].DefaultCellStyle.ForeColor = shotColor;
            grid.Columns[GridShotColumn].DefaultCellStyle.SelectionForeColor = Color.White;
            grid.Columns[GridShotColumn].HeaderCell.Style.ForeColor = shotColor;

            foreach (DataGridViewRow row in grid.Rows)
            {
                ApplyGridNumberCellStyles(row);
            }
        }

        private void ApplyGridNumberCellStyles(DataGridViewRow row)
        {
            if (row == null || row.Cells.Count <= GridShotColumn)
            {
                return;
            }

            row.Cells[GridSceneColumn].Style.ForeColor = GetSceneColumnTextColor();
            row.Cells[GridSceneColumn].Style.SelectionForeColor = Color.White;
            row.Cells[GridShotColumn].Style.ForeColor = GetShotColumnTextColor();
            row.Cells[GridShotColumn].Style.SelectionForeColor = Color.White;
        }

        private void ClearDragHighlight()
        {
            if (grid == null || dragHighlightRow < 0 || dragHighlightColumn < 0)
            {
                dragHighlightRow = -1;
                dragHighlightColumn = -1;
                return;
            }

            int row = dragHighlightRow;
            int column = dragHighlightColumn;
            dragHighlightRow = -1;
            dragHighlightColumn = -1;
            ResetGridCellStyle(row, column);
        }

        private void SetDragHighlight(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= rows.Count || (columnIndex != GridMainColumn && columnIndex != GridBackupColumn))
            {
                ClearDragHighlight();
                return;
            }

            if (dragHighlightRow == rowIndex && dragHighlightColumn == columnIndex)
            {
                return;
            }

            ClearDragHighlight();
            dragHighlightRow = rowIndex;
            dragHighlightColumn = columnIndex;
            ReapplyDragHighlight();
        }

        private void ReapplyDragHighlight()
        {
            if (grid == null || dragHighlightRow < 0 || dragHighlightColumn < 0 ||
                dragHighlightRow >= grid.Rows.Count || dragHighlightColumn >= grid.Columns.Count)
            {
                return;
            }

            DataGridViewCell cell = grid.Rows[dragHighlightRow].Cells[dragHighlightColumn];
            cell.Style.BackColor = UiTheme.DropTargetBack(darkMode);
            cell.Style.ForeColor = UiTheme.DropTargetFore(darkMode);
            cell.Style.SelectionBackColor = UiTheme.DropTargetBack(darkMode);
            cell.Style.SelectionForeColor = UiTheme.DropTargetFore(darkMode);
        }

        private void ResetGridCellStyle(int rowIndex, int columnIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            DataGridViewCell cell = grid.Rows[rowIndex].Cells[columnIndex];
            cell.Style.BackColor = Color.Empty;
            cell.Style.ForeColor = Color.Empty;
            cell.Style.SelectionBackColor = Color.Empty;
            cell.Style.SelectionForeColor = Color.Empty;
        }

        private void RenderGrid()
        {
            rendering = true;
            try
            {
                ApplyGridColumnLayout();
                dragHighlightRow = -1;
                dragHighlightColumn = -1;
                grid.Rows.Clear();
                foreach (ShotRow row in rows)
                {
                    int index = grid.Rows.Add();
                    DataGridViewRow gridRow = grid.Rows[index];
                    gridRow.Tag = row;
                    gridRow.Height = grid.RowTemplate.Height;
                    gridRow.Resizable = DataGridViewTriState.False;
                    gridRow.Cells[GridSceneColumn].Value = GetEffectiveScene(row, GetDefaultScene(), true);
                    gridRow.Cells[GridShotColumn].Value = row.Sequence;
                    gridRow.Cells[GridMainColumn].Value = GetCellSummary(row.MainFiles);
                    gridRow.Cells[GridBackupColumn].Value = GetCellSummary(row.BackupFiles);
                    gridRow.Cells[GridProgressColumn].Value = row.ProgressPercent;
                    gridRow.Cells[GridMainColumn].ToolTipText = GetCellSummary(row.MainFiles);
                    gridRow.Cells[GridBackupColumn].ToolTipText = GetCellSummary(row.BackupFiles);
                    ApplyGridNumberCellStyles(gridRow);
                }
                ApplyGridNumberColumnStyles();
            }
            finally
            {
                rendering = false;
            }
        }

        private void RenderGridRow(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= rows.Count || rowIndex >= grid.Rows.Count)
            {
                RenderGrid();
                return;
            }

            rendering = true;
            try
            {
                ShotRow row = rows[rowIndex];
                DataGridViewRow gridRow = grid.Rows[rowIndex];
                gridRow.Tag = row;
                gridRow.Height = grid.RowTemplate.Height;
                gridRow.Resizable = DataGridViewTriState.False;
                gridRow.Cells[GridSceneColumn].Value = GetEffectiveScene(row, GetDefaultScene(), true);
                gridRow.Cells[GridShotColumn].Value = row.Sequence;
                gridRow.Cells[GridMainColumn].Value = GetCellSummary(row.MainFiles);
                gridRow.Cells[GridBackupColumn].Value = GetCellSummary(row.BackupFiles);
                gridRow.Cells[GridProgressColumn].Value = row.ProgressPercent;
                gridRow.Cells[GridMainColumn].ToolTipText = GetCellSummary(row.MainFiles);
                gridRow.Cells[GridBackupColumn].ToolTipText = GetCellSummary(row.BackupFiles);
                ApplyGridNumberCellStyles(gridRow);
            }
            finally
            {
                rendering = false;
            }
        }

        private void RenderGridProgress(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= rows.Count || rowIndex >= grid.Rows.Count || grid.Columns.Count <= GridProgressColumn)
            {
                return;
            }

            grid.Rows[rowIndex].Cells[GridProgressColumn].Value = rows[rowIndex].ProgressPercent;
            grid.InvalidateCell(GridProgressColumn, rowIndex);
        }

        private void SetProgressColumnVisible(bool visible)
        {
            progressColumnVisible = visible;
            ApplyGridColumnLayout();
        }

        private int GetDefaultGridFocusColumn()
        {
            return IsRowSceneEnabled() ? GridSceneColumn : GridShotColumn;
        }

        private void ApplyGridColumnLayout()
        {
            if (grid == null || grid.Columns.Count <= GridProgressColumn)
            {
                return;
            }

            bool rowScene = IsRowSceneEnabled();
            if (grid.CurrentCell != null)
            {
                int currentColumn = grid.CurrentCell.ColumnIndex;
                bool currentColumnWillHide =
                    (!rowScene && currentColumn == GridSceneColumn) ||
                    (!progressColumnVisible && currentColumn == GridProgressColumn);
                if (currentColumnWillHide)
                {
                    SelectGridCell(grid.CurrentCell.RowIndex, GetDefaultGridFocusColumn());
                }
            }

            grid.Columns[GridSceneColumn].Visible = rowScene;
            grid.Columns[GridShotColumn].HeaderText = rowScene ? "B 镜号" : "A 镜号";
            grid.Columns[GridMainColumn].HeaderText = rowScene ? "C 主要素材" : "B 主要素材";
            grid.Columns[GridBackupColumn].HeaderText = rowScene ? "D 备用素材" : "C 备用素材";
            grid.Columns[GridProgressColumn].HeaderText = rowScene ? "E 进度" : "D 进度";
            grid.Columns[GridProgressColumn].Visible = progressColumnVisible;
        }

        private void ResetProgressBars()
        {
            foreach (ShotRow row in rows)
            {
                row.ProgressPercent = 0;
            }

            RenderGrid();
        }

        private void OnGridDragEnterOrOver(object sender, DragEventArgs e)
        {
            try
            {
                if (e != null && e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    int rowIndex;
                    int columnIndex;
                    if (TryGetDropTargetCell(e, out rowIndex, out columnIndex))
                    {
                        e.Effect = DragDropEffects.Copy;
                        SetDragHighlight(rowIndex, columnIndex);
                        statusLabel.Text = string.Format(
                            "松开鼠标后会导入到第 {0} 行 {1}。",
                            rowIndex + 1,
                            columnIndex == GridMainColumn ? GetMainColumnDisplayName() : GetBackupColumnDisplayName());
                    }
                    else
                    {
                        e.Effect = DragDropEffects.None;
                        ClearDragHighlight();
                    }
                }
                else if (e != null)
                {
                    e.Effect = DragDropEffects.None;
                    ClearDragHighlight();
                }
            }
            catch
            {
                if (e != null)
                {
                    e.Effect = DragDropEffects.None;
                }
                ClearDragHighlight();
            }
        }

        private void OnGridDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (e == null || e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    statusLabel.Text = "没有检测到可拖入的文件。";
                    return;
                }

                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null || paths.Length == 0)
                {
                    statusLabel.Text = "没有检测到可拖入的文件。";
                    return;
                }

                int rowIndex = -1;
                int columnIndex = -1;

                if (!TryGetDropTargetCell(e, out rowIndex, out columnIndex))
                {
                    statusLabel.Text = "请拖到 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 单元格。";
                    return;
                }

                AddFilesToShotCell(rowIndex, columnIndex, paths);
            }
            catch (Exception ex)
            {
                statusLabel.Text = "拖入失败：" + ex.Message;
            }
            finally
            {
                ClearDragHighlight();
            }
        }

        private bool TryGetDropTargetCell(DragEventArgs e, out int rowIndex, out int columnIndex)
        {
            rowIndex = -1;
            columnIndex = -1;

            if (grid == null || e == null)
            {
                return false;
            }

            Point point = grid.PointToClient(new Point(e.X, e.Y));
            DataGridView.HitTestInfo hit = grid.HitTest(point.X, point.Y);
            if (hit.RowIndex >= 0 && hit.RowIndex < rows.Count && (hit.ColumnIndex == GridMainColumn || hit.ColumnIndex == GridBackupColumn))
            {
                rowIndex = hit.RowIndex;
                columnIndex = hit.ColumnIndex;
                return true;
            }

            if (grid.CurrentCell != null &&
                grid.CurrentCell.RowIndex >= 0 &&
                grid.CurrentCell.RowIndex < rows.Count &&
                (grid.CurrentCell.ColumnIndex == GridMainColumn || grid.CurrentCell.ColumnIndex == GridBackupColumn))
            {
                rowIndex = grid.CurrentCell.RowIndex;
                columnIndex = grid.CurrentCell.ColumnIndex;
                return true;
            }

            return false;
        }

        private void AddFilesToShotCell(int rowIndex, int columnIndex, string[] paths)
        {
            if (rowIndex < 0 || rowIndex >= rows.Count)
            {
                statusLabel.Text = "目标行无效。";
                return;
            }

            if (columnIndex != GridMainColumn && columnIndex != GridBackupColumn)
            {
                statusLabel.Text = "请把视频拖到 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 列。";
                return;
            }

            List<string> videoPaths = GetVideoFilePaths(paths);
            if (videoPaths.Count == 0)
            {
                statusLabel.Text = "没有发现支持的视频文件。";
                return;
            }

            ShotRow targetRow = rows[rowIndex];
            List<string> targetFiles = columnIndex == GridMainColumn ? targetRow.MainFiles : targetRow.BackupFiles;
            List<string> targetTails = columnIndex == GridMainColumn ? targetRow.MainTailOverrides : targetRow.BackupTailOverrides;
            EnsureTailOverrideSize(targetRow, columnIndex == GridMainColumn);
            HashSet<string> existing = GetAllFileKeys();
            List<string> skippedNames = new List<string>();
            int added = 0;
            int skipped = 0;

            foreach (string path in videoPaths)
            {
                if (existing.Contains(path))
                {
                    skipped++;
                    skippedNames.Add(Path.GetFileName(path));
                    continue;
                }

                targetFiles.Add(path);
                targetTails.Add("");
                existing.Add(path);
                added++;
            }

            RenderGridRow(rowIndex);
            if (rowIndex >= 0 && rowIndex < grid.Rows.Count && columnIndex >= 0 && columnIndex < grid.Columns.Count)
            {
                grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
            }
            RefreshPreview();

            string columnName = columnIndex == GridMainColumn ? "主要素材" : "备用素材";
            statusLabel.Text = string.Format("第 {0} 行 {1} 已加入 {2} 个视频；跳过重复 {3} 个。", rowIndex + 1, columnName, added, skipped);
            if (skippedNames.Count > 0)
            {
                ShowDuplicateFileWarning(skippedNames);
            }
            ShowCurrentPlanIssueWarning();
        }

        private void OnGridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (rendering || e == null || e.RowIndex < 0 || e.RowIndex >= rows.Count)
            {
                return;
            }

            ShotRow row = rows[e.RowIndex];
            if (e.ColumnIndex == GridSceneColumn)
            {
                object value = grid.Rows[e.RowIndex].Cells[GridSceneColumn].Value;
                int parsed;
                if (value != null && int.TryParse(value.ToString(), out parsed) && parsed > 0)
                {
                    row.Scene = parsed;
                }
                else
                {
                    row.Scene = GetEffectiveScene(row, GetDefaultScene(), true);
                    grid.Rows[e.RowIndex].Cells[GridSceneColumn].Value = row.Scene;
                    statusLabel.Text = "A 列场号必须是大于 0 的整数，已保留原场号。";
                }
            }
            else if (e.ColumnIndex == GridShotColumn)
            {
                object value = grid.Rows[e.RowIndex].Cells[GridShotColumn].Value;
                int parsed;
                if (value != null && int.TryParse(value.ToString(), out parsed) && parsed > 0)
                {
                    row.Sequence = parsed;
                }
                else
                {
                    row.Sequence = Math.Max(1, row.Sequence);
                    grid.Rows[e.RowIndex].Cells[GridShotColumn].Value = row.Sequence;
                    statusLabel.Text = (IsRowSceneEnabled() ? "B" : "A") + " 列镜号必须是大于 0 的整数，已保留原镜号。";
                }
            }
            else if (e.ColumnIndex == GridProgressColumn)
            {
                grid.Rows[e.RowIndex].Cells[GridProgressColumn].Value = row.ProgressPercent;
            }

            RenderAll();
        }

        private int GetCurrentGridRowIndex()
        {
            if (grid.CurrentCell != null && grid.CurrentCell.RowIndex >= 0 && grid.CurrentCell.RowIndex < rows.Count)
            {
                return grid.CurrentCell.RowIndex;
            }

            if (grid.SelectedCells.Count > 0)
            {
                int rowIndex = grid.SelectedCells[0].RowIndex;
                if (rowIndex >= 0 && rowIndex < rows.Count)
                {
                    return rowIndex;
                }
            }

            return -1;
        }

        private void SelectGridCell(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return;
            }

            if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                columnIndex = GetDefaultGridFocusColumn();
            }

            if (columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
            {
                columnIndex = GetDefaultGridFocusColumn();
            }

            if (columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
            {
                for (int index = 0; index < grid.Columns.Count; index++)
                {
                    if (grid.Columns[index].Visible)
                    {
                        columnIndex = index;
                        break;
                    }
                }
            }

            if (columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
            {
                return;
            }

            grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
        }
    }
}
