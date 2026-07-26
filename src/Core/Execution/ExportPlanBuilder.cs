using System;
using System.Collections.Generic;
using System.IO;

namespace VideoRenamer
{
    // 1080p 导出前的计划派生：克隆条目、另存为模式改名、目标冲突校验。
    public static class ExportPlanBuilder
    {
        public static List<RenamePlan> Prepare(List<RenamePlan> sourcePlan, ExportOutputMode outputMode)
        {
            List<RenamePlan> prepared = new List<RenamePlan>();
            Dictionary<string, bool> targets = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (RenamePlan source in sourcePlan)
            {
                if (source == null)
                {
                    continue;
                }

                RenamePlan entry = source.Clone();

                if (outputMode == ExportOutputMode.SaveAsNewFile && StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
                {
                    entry.TargetPath = RenamePlanBuilder.GetUniquePathWithSuffix(entry.TargetPath, "_1080p");
                    entry.NewName = Path.GetFileName(entry.TargetPath);
                    entry.Status = PlanStatus.SaveAsNewFile;
                }

                if (outputMode == ExportOutputMode.SaveAsNewFile &&
                    File.Exists(entry.TargetPath) &&
                    !StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
                {
                    throw new IOException("目标文件已存在：" + entry.NewName);
                }

                if (targets.ContainsKey(entry.TargetPath))
                {
                    throw new IOException(PlanStatusText.For(PlanStatus.DuplicateNewName) + "：" + entry.NewName);
                }

                targets[entry.TargetPath] = true;
                prepared.Add(entry);
            }

            return prepared;
        }
    }
}
