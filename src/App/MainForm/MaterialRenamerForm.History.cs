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

        private void SaveRenameHistory(List<RenameOperation> operations)
        {
            RenameHistoryStore.Save(historyPath, operations);
        }

        private List<RenameOperation> LoadRenameHistory()
        {
            List<RenameOperation> operations = RenameHistoryStore.Load(historyPath);
            foreach (RenameOperation op in operations)
            {
                op.Row = op.RowIndex >= 1 && op.RowIndex <= rows.Count ? rows[op.RowIndex - 1] : null;
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

                    // 还原方向的写回：RenamedPath → OriginalPath（统一实现）。
                    PlanExecutor.PatchRowFileList(op.Row, op.IsMain, op.FileIndex, op.RenamedPath, op.OriginalPath);
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

    }
}
