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

        internal static string EncodeHistoryValue(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        internal static string DecodeHistoryValue(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
        }

        private void SaveRenameHistory(List<RenameOperation> operations)
        {
            if (operations == null || operations.Count == 0)
            {
                return;
            }

            List<string> lines = new List<string>();
            lines.Add("VideoMaterialRenamerHistoryV1");
            foreach (RenameOperation op in operations)
            {
                lines.Add(string.Join("\t", new string[]
                {
                    op.RowIndex.ToString(),
                    op.IsMain ? "1" : "0",
                    op.FileIndex.ToString(),
                    EncodeHistoryValue(op.OriginalPath),
                    EncodeHistoryValue(op.RenamedPath)
                }));
            }

            File.WriteAllLines(historyPath, lines.ToArray(), Encoding.UTF8);
        }

        private List<RenameOperation> LoadRenameHistory()
        {
            List<RenameOperation> operations = new List<RenameOperation>();
            if (!File.Exists(historyPath))
            {
                return operations;
            }

            string[] lines = File.ReadAllLines(historyPath, Encoding.UTF8);
            if (lines.Length == 0 || lines[0] != "VideoMaterialRenamerHistoryV1")
            {
                return operations;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('\t');
                if (parts.Length != 5)
                {
                    continue;
                }

                int rowIndex;
                int fileIndex;
                if (!int.TryParse(parts[0], out rowIndex) || !int.TryParse(parts[2], out fileIndex))
                {
                    continue;
                }

                operations.Add(new RenameOperation
                {
                    Row = rowIndex >= 1 && rowIndex <= rows.Count ? rows[rowIndex - 1] : null,
                    RowIndex = rowIndex,
                    IsMain = parts[1] == "1",
                    FileIndex = fileIndex,
                    OriginalPath = DecodeHistoryValue(parts[3]),
                    RenamedPath = DecodeHistoryValue(parts[4])
                });
            }

            return operations;
        }

        private void RestoreLastRename()
        {
            bool fromMemory = undoStack.Count > 0;
            List<RenameOperation> operations = fromMemory ? undoStack.Peek() : LoadRenameHistory();
            if (operations.Count == 0)
            {
                MessageBox.Show(this, "没有可还原的重命名记录。", "无法还原", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                this,
                "即将把上次成功重命名的 " + operations.Count + " 个文件还原为原文件名，是否继续？",
                "确认还原",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            List<string> failures = new List<string>();
            for (int i = operations.Count - 1; i >= 0; i--)
            {
                RenameOperation op = operations[i];
                try
                {
                    if (!File.Exists(op.RenamedPath))
                    {
                        failures.Add(Path.GetFileName(op.RenamedPath) + ": 当前文件不存在");
                        continue;
                    }

                    if (File.Exists(op.OriginalPath) && !StringComparer.OrdinalIgnoreCase.Equals(op.OriginalPath, op.RenamedPath))
                    {
                        failures.Add(Path.GetFileName(op.RenamedPath) + ": 原文件名已被占用");
                        continue;
                    }

                    if (!StringComparer.OrdinalIgnoreCase.Equals(op.RenamedPath, op.OriginalPath))
                    {
                        File.Move(op.RenamedPath, op.OriginalPath);
                    }

                    if (op.Row != null)
                    {
                        List<string> files = op.IsMain ? op.Row.MainFiles : op.Row.BackupFiles;
                        if (op.FileIndex >= 0 && op.FileIndex < files.Count && StringComparer.OrdinalIgnoreCase.Equals(files[op.FileIndex], op.RenamedPath))
                        {
                            files[op.FileIndex] = op.OriginalPath;
                        }
                        else
                        {
                            int currentIndex = files.FindIndex(p => StringComparer.OrdinalIgnoreCase.Equals(p, op.RenamedPath));
                            if (currentIndex >= 0)
                            {
                                files[currentIndex] = op.OriginalPath;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(Path.GetFileName(op.RenamedPath) + ": " + ex.Message);
                }
            }

            if (failures.Count == 0)
            {
                if (fromMemory)
                {
                    undoStack.Pop();
                }
                if (File.Exists(historyPath))
                {
                    File.Delete(historyPath);
                }
                RenderAll();
                MessageBox.Show(this, "已取消上次命名。", "取消命名完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            RenderAll();
            MessageBox.Show(this, string.Join("\r\n", failures.Take(8).ToArray()), "部分文件还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SetOperationUiEnabled(bool enabled)
        {
            operationRunning = !enabled;
            UseWaitCursor = !enabled;
            foreach (Control control in Controls)
            {
                if (object.ReferenceEquals(control, statusLabel))
                {
                    continue;
                }
                control.Enabled = enabled;
            }

            if (statusLabel != null)
            {
                statusLabel.Enabled = true;
            }
        }
    }
}
