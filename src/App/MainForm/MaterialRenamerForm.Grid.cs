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

        private void ApplyGridNumberColumnStyles()
        {
            if (grid == null || grid.Columns.Count <= GridShotColumn)
            {
                return;
            }

            Color sceneColor = GetSceneColumnTextColor();
            Color shotColor = GetShotColumnTextColor();
            Color selectionFore = UiTheme.SelectionFore(darkMode);
            grid.Columns[GridSceneColumn].DefaultCellStyle.ForeColor = sceneColor;
            grid.Columns[GridSceneColumn].DefaultCellStyle.SelectionForeColor = selectionFore;
            grid.Columns[GridSceneColumn].HeaderCell.Style.ForeColor = sceneColor;
            grid.Columns[GridShotColumn].DefaultCellStyle.ForeColor = shotColor;
            grid.Columns[GridShotColumn].DefaultCellStyle.SelectionForeColor = selectionFore;
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
            row.Cells[GridSceneColumn].Style.SelectionForeColor = UiTheme.SelectionFore(darkMode);
            row.Cells[GridShotColumn].Style.ForeColor = GetShotColumnTextColor();
            row.Cells[GridShotColumn].Style.SelectionForeColor = UiTheme.SelectionFore(darkMode);
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

        // 单元格填充的唯一实现（原 RenderGrid 循环体与 RenderGridRow 各写
        // 一遍 12 行赋值，改列时必须改两处）。摘要每格只计算一次，
        // Value 与 ToolTip 共用。
        private void PopulateGridRow(DataGridViewRow gridRow, ShotRow row)
        {
            gridRow.Tag = row;
            gridRow.Height = grid.RowTemplate.Height;
            gridRow.Resizable = DataGridViewTriState.False;
            // 场号列常显（阶段10h）：显示有效场号——逐行场号关闭时即全局值。
            gridRow.Cells[GridSceneColumn].Value = RenamePlanBuilder.GetEffectiveScene(row, GetDefaultScene(), IsRowSceneEnabled());
            gridRow.Cells[GridShotColumn].Value = RenamePlanBuilder.FormatShotLabel(row.Sequence, row.ShotSuffix);
            string mainSummary = RenamePlanBuilder.GetCellSummary(row.MainFiles);
            string backupSummary = RenamePlanBuilder.GetCellSummary(row.BackupFiles);
            gridRow.Cells[GridMainColumn].Value = mainSummary;
            gridRow.Cells[GridBackupColumn].Value = backupSummary;
            gridRow.Cells[GridProgressColumn].Value = row.ProgressPercent;
            gridRow.Cells[GridMainColumn].ToolTipText = mainSummary;
            gridRow.Cells[GridBackupColumn].ToolTipText = backupSummary;
            ApplyGridNumberCellStyles(gridRow);
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
                    PopulateGridRow(grid.Rows[index], row);
                }
                ApplyGridNumberColumnStyles();
            }
            finally
            {
                rendering = false;
            }
        }

        // 阶段11c：行级增量操作——增/移/删只动受影响的网格行，不再整表
        // 重建（预览仍整表刷新：行号与分组标题会整体变化）。任何计数
        // 前置条件不满足都回退 RenderGrid()；等价性由冒烟断言锁定。
        private void InsertGridRowAt(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex > grid.Rows.Count || grid.Rows.Count != rows.Count - 1)
            {
                RenderGrid();
                return;
            }

            rendering = true;
            try
            {
                grid.Rows.Insert(rowIndex, 1);
                PopulateGridRow(grid.Rows[rowIndex], rows[rowIndex]);
            }
            finally
            {
                rendering = false;
            }
        }

        private void RemoveGridRowAt(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || grid.Rows.Count != rows.Count + 1)
            {
                RenderGrid();
                return;
            }

            rendering = true;
            try
            {
                grid.Rows.RemoveAt(rowIndex);
            }
            finally
            {
                rendering = false;
            }
        }

        // 相邻两行互换后按行模型重填两行内容（仅 ±1 移动会调用）。
        private void RepopulateGridRowPair(int indexA, int indexB)
        {
            if (grid == null || indexA < 0 || indexB < 0 ||
                indexA >= grid.Rows.Count || indexB >= grid.Rows.Count ||
                indexA >= rows.Count || indexB >= rows.Count ||
                grid.Rows.Count != rows.Count)
            {
                RenderGrid();
                return;
            }

            rendering = true;
            try
            {
                PopulateGridRow(grid.Rows[indexA], rows[indexA]);
                PopulateGridRow(grid.Rows[indexB], rows[indexB]);
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
                PopulateGridRow(grid.Rows[rowIndex], rows[rowIndex]);
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

        private int GetDefaultGridFocusColumn()
        {
            return IsRowSceneEnabled() ? GridSceneColumn : GridShotColumn;
        }

        // 阶段10h（与设计稿一致）：场号/进度列常显、列头无字母前缀后，这里
        // 只剩场号列的可编辑性——逐行场号关闭时列显示全局场号，就地编辑无
        // 意义，设为只读。
        private void ApplyGridColumnLayout()
        {
            if (grid == null || grid.Columns.Count <= GridProgressColumn)
            {
                return;
            }

            grid.Columns[GridSceneColumn].ReadOnly = !IsRowSceneEnabled();
        }

        // 全局场号变化时就地刷新常显的场号列（逐行场号开启时列由行模型
        // 驱动并整表重建，不在此处理）。
        private void RefreshGridSceneCells()
        {
            if (grid == null || IsRowSceneEnabled())
            {
                return;
            }

            int scene = GetDefaultScene();
            rendering = true;
            try
            {
                foreach (DataGridViewRow gridRow in grid.Rows)
                {
                    gridRow.Cells[GridSceneColumn].Value = scene;
                }
            }
            finally
            {
                rendering = false;
            }
        }

        // 空素材格占位提示（阶段10h，与设计稿一致）：仅替换显示文本并转为
        // 弱化色，不动底层 Value（冒烟的网格等价性转储读 Value，不受影响）。
        private void OnGridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e == null || e.RowIndex < 0 || (e.ColumnIndex != GridMainColumn && e.ColumnIndex != GridBackupColumn))
            {
                return;
            }

            if (e.RowIndex == dragHighlightRow && e.ColumnIndex == dragHighlightColumn)
            {
                return;
            }

            string text = e.Value == null ? "" : e.Value.ToString();
            if (text.Length == 0)
            {
                e.Value = "拖入视频...";
                e.CellStyle.ForeColor = UiTheme.MutedText(darkMode);
                e.CellStyle.SelectionForeColor = UiTheme.MutedText(darkMode);
                e.FormattingApplied = true;
            }
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
                        StatusText = string.Format(
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
                    StatusText = "没有检测到可拖入的文件。";
                    return;
                }

                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null || paths.Length == 0)
                {
                    StatusText = "没有检测到可拖入的文件。";
                    return;
                }

                int rowIndex = -1;
                int columnIndex = -1;

                if (!TryGetDropTargetCell(e, out rowIndex, out columnIndex))
                {
                    StatusText = "请拖到 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 单元格。";
                    return;
                }

                AddFilesToShotCell(rowIndex, columnIndex, paths);
            }
            catch (Exception ex)
            {
                StatusText = "拖入失败：" + ex.Message;
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
                StatusText = "目标行无效。";
                return;
            }

            if (columnIndex != GridMainColumn && columnIndex != GridBackupColumn)
            {
                StatusText = "请把视频拖到 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 列。";
                return;
            }

            List<string> videoPaths = GetVideoFilePaths(paths);
            if (videoPaths.Count == 0)
            {
                StatusText = "没有发现支持的视频文件。";
                return;
            }

            ShotRow targetRow = rows[rowIndex];
            List<string> targetFiles = columnIndex == GridMainColumn ? targetRow.MainFiles : targetRow.BackupFiles;
            List<string> targetTails = columnIndex == GridMainColumn ? targetRow.MainTailOverrides : targetRow.BackupTailOverrides;
            RenamePlanBuilder.EnsureTailOverrideSize(targetRow, columnIndex == GridMainColumn);
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
            StatusText = string.Format("第 {0} 行 {1} 已加入 {2} 个视频；跳过重复 {3} 个。", rowIndex + 1, columnName, added, skipped);
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
                    row.Scene = RenamePlanBuilder.GetEffectiveScene(row, GetDefaultScene(), true);
                    grid.Rows[e.RowIndex].Cells[GridSceneColumn].Value = row.Scene;
                    StatusText = "场号必须是大于 0 的整数，已保留原场号。";
                }
            }
            else if (e.ColumnIndex == GridShotColumn)
            {
                object value = grid.Rows[e.RowIndex].Cells[GridShotColumn].Value;
                string text = value == null ? "" : value.ToString().Trim();
                int parsedShot;
                string parsedSuffix;
                if (ShotLabelParser.TryParse(text, out parsedShot, out parsedSuffix))
                {
                    row.Sequence = parsedShot;
                    row.ShotSuffix = parsedSuffix;
                }
                else
                {
                    row.Sequence = Math.Max(1, row.Sequence);
                    grid.Rows[e.RowIndex].Cells[GridShotColumn].Value = RenamePlanBuilder.FormatShotLabel(row.Sequence, row.ShotSuffix);
                    StatusText = "镜号可填正整数，或整数+字母（如 28A，用于两镜之间补插衔接镜）。";
                }
            }
            else if (e.ColumnIndex == GridProgressColumn)
            {
                grid.Rows[e.RowIndex].Cells[GridProgressColumn].Value = row.ProgressPercent;
            }

            RenderGridRow(e.RowIndex);
            RefreshPreviewNamesOnly();
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
