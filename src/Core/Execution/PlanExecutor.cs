using System;
using System.Collections.Generic;
using System.IO;

namespace VideoRenamer
{
    // 重命名计划的执行（File.Move）与行模型路径写回。
    // PatchRowFileList 是原先在 Rename/Export 完成/历史还原三处
    // 各写一遍的写回逻辑的唯一实现（采用"索引+路径匹配，失配则
    // FindIndex 兜底"的健壮变体）。
    public static class PlanExecutor
    {
        public sealed class ExecutionResult
        {
            public readonly List<RenameOperation> Successes = new List<RenameOperation>();
            public readonly List<string> Failures = new List<string>();
            // 阶段10d：文件间取消——已完成的移动保持完成（并由调用方照常
            // 写入撤销历史），其余条目不再开始。
            public bool Cancelled;
        }

        // 只做磁盘改名，不动行模型（写回由调用方在 UI 线程用
        // PatchRowFileList 统一执行，避免工作线程并发改行列表）。
        public static ExecutionResult Execute(List<RenamePlan> plan, Action<RenamePlan, int> perFileStarted)
        {
            return Execute(plan, perFileStarted, null);
        }

        public static ExecutionResult Execute(List<RenamePlan> plan, Action<RenamePlan, int> perFileStarted, Func<bool> shouldStop)
        {
            ExecutionResult result = new ExecutionResult();
            if (plan == null)
            {
                return result;
            }

            int index = 0;
            foreach (RenamePlan entry in plan)
            {
                if (shouldStop != null && shouldStop())
                {
                    result.Cancelled = true;
                    break;
                }

                index++;
                if (perFileStarted != null)
                {
                    perFileStarted(entry, index);
                }

                try
                {
                    string originalPath = entry.OldPath;
                    string renamedPath = entry.TargetPath;
                    if (!StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
                    {
                        File.Move(entry.OldPath, entry.TargetPath);
                    }

                    if (!StringComparer.OrdinalIgnoreCase.Equals(originalPath, renamedPath))
                    {
                        result.Successes.Add(new RenameOperation
                        {
                            Row = entry.Row,
                            RowIndex = entry.RowIndex,
                            IsMain = entry.IsMain,
                            FileIndex = entry.FileIndex,
                            OriginalPath = originalPath,
                            RenamedPath = renamedPath
                        });
                    }
                }
                catch (Exception ex)
                {
                    result.Failures.Add(entry.OldName + ": " + ex.Message);
                }
            }

            return result;
        }

        // 行模型路径写回的唯一实现：优先按记录的 FileIndex 且路径匹配写回，
        // 索引失配（例如中途行内容变化）时按路径 FindIndex 兜底。
        public static void PatchRowFileList(ShotRow row, bool isMain, int fileIndex, string fromPath, string toPath)
        {
            if (row == null)
            {
                return;
            }

            List<string> files = isMain ? row.MainFiles : row.BackupFiles;
            if (fileIndex >= 0 && fileIndex < files.Count && StringComparer.OrdinalIgnoreCase.Equals(files[fileIndex], fromPath))
            {
                files[fileIndex] = toPath;
                return;
            }

            int currentIndex = files.FindIndex(delegate(string p)
            {
                return StringComparer.OrdinalIgnoreCase.Equals(p, fromPath);
            });
            if (currentIndex >= 0)
            {
                files[currentIndex] = toPath;
            }
        }

        public static void PatchRowFileList(RenameOperation op)
        {
            if (op == null)
            {
                return;
            }
            PatchRowFileList(op.Row, op.IsMain, op.FileIndex, op.OriginalPath, op.RenamedPath);
        }
    }
}
