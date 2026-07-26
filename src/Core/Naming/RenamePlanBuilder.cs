using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;


namespace VideoRenamer
{
    // Pure naming / rename-plan logic, decoupled from the WinForms UI.
    public static class RenamePlanBuilder
    {

        public static int GetEffectiveScene(ShotRow row, int defaultScene, bool useRowScene)
        {
            return useRowScene && row != null && row.Scene > 0 ? row.Scene : Math.Max(1, defaultScene);
        }

        // 旧签名保留（既有调用方与测试沿用）；委托到快照+探测器版本。
        public static List<RenamePlan> BuildPlan(List<ShotRow> sourceRows, int episode, int scene, bool keepExtensionCase, bool export1080p, bool useRowScene = false)
        {
            NamingSettings settings = new NamingSettings
            {
                Episode = episode,
                DefaultScene = scene,
                KeepExtensionCase = keepExtensionCase,
                Export1080p = export1080p,
                UseRowScene = useRowScene
            };
            return BuildPlan(sourceRows, settings, RealFileSystemProbe.Instance);
        }

        public static List<RenamePlan> BuildPlan(List<ShotRow> sourceRows, NamingSettings settings, IFileSystemProbe probe)
        {
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            List<RenamePlan> plan = new List<RenamePlan>();
            int rowIndex = 1;

            foreach (ShotRow row in sourceRows)
            {
                int rowScene = GetEffectiveScene(row, settings.DefaultScene, settings.UseRowScene);
                int shot = Math.Max(1, row.Sequence);
                int take = 1;

                EnsureTailOverrideSize(row, true);
                EnsureTailOverrideSize(row, false);
                AddFilesToPlan(plan, seen, row, rowIndex, "主要素材", true, row.MainFiles, row.MainTailOverrides, settings.Episode, rowScene, shot, ref take, settings.KeepExtensionCase, settings.Export1080p, probe);
                AddFilesToPlan(plan, seen, row, rowIndex, "备用素材", false, row.BackupFiles, row.BackupTailOverrides, settings.Episode, rowScene, shot, ref take, settings.KeepExtensionCase, settings.Export1080p, probe);
                rowIndex++;
            }

            return plan;
        }

        private static void AddFilesToPlan(
            List<RenamePlan> plan,
            Dictionary<string, bool> seen,
            ShotRow row,
            int rowIndex,
            string columnName,
            bool isMain,
            List<string> files,
            List<string> tailOverrides,
            int episode,
            int scene,
            int shot,
            ref int take,
            bool keepExtensionCase,
            bool export1080p,
            IFileSystemProbe probe)
        {
            for (int fileIndex = 0; fileIndex < files.Count; fileIndex++)
            {
                string oldPath = Path.GetFullPath(files[fileIndex]);
                string customTail = tailOverrides != null && fileIndex < tailOverrides.Count ? NormalizeCustomTailText(tailOverrides[fileIndex]) : "";
                string tailSegment = GetTailSegment(take, customTail);
                string newName = GetMaterialFileName(episode, scene, shot, row != null ? row.ShotSuffix : "", tailSegment, oldPath, keepExtensionCase);
                string directory = Path.GetDirectoryName(oldPath);
                string targetPath = Path.GetFullPath(Path.Combine(directory, newName));
                PlanStatus status = PlanStatus.Ready;

                if (!probe.FileExists(oldPath))
                {
                    status = PlanStatus.SourceMissing;
                }
                else if (StringComparer.OrdinalIgnoreCase.Equals(targetPath, oldPath))
                {
                    status = export1080p ? PlanStatus.PendingOverwriteExport : PlanStatus.Unchanged;
                }
                else if (probe.FileExists(targetPath))
                {
                    status = PlanStatus.TargetExists;
                }

                if (export1080p && status == PlanStatus.Ready)
                {
                    status = PlanStatus.PendingOverwriteExport;
                }

                if (seen.ContainsKey(targetPath))
                {
                    status = PlanStatus.DuplicateNewName;
                }

                seen[targetPath] = true;
                plan.Add(new RenamePlan
                {
                    Row = row,
                    RowIndex = rowIndex,
                    ColumnName = columnName,
                    IsMain = isMain,
                    FileIndex = fileIndex,
                    Scene = scene,
                    Shot = shot,
                    ShotLabel = FormatShotLabel(shot, row != null ? row.ShotSuffix : ""),
                    Take = take,
                    TailSegment = tailSegment,
                    CustomTailText = customTail,
                    HasCustomTail = !string.IsNullOrWhiteSpace(customTail),
                    OldPath = oldPath,
                    TargetPath = targetPath,
                    OldName = Path.GetFileName(oldPath),
                    NewName = Path.GetFileName(targetPath),
                    Status = status
                });

                take++;
            }
        }

        public static bool IsBlockingIssue(RenamePlan entry)
        {
            return entry != null &&
                (entry.Status == PlanStatus.TargetExists ||
                 entry.Status == PlanStatus.TargetLocked ||
                 entry.Status == PlanStatus.DuplicateNewName ||
                 entry.Status == PlanStatus.SourceMissing);
        }

        // 实现移至 RealFileSystemProbe；保留原 API 形状。
        public static bool IsFileLocked(string path)
        {
            return RealFileSystemProbe.Instance.IsFileLocked(path);
        }

        // 规则实现集中在 ShotLabelParser（与 TryParse 互为逆运算）；这里保留
        // 原公共 API 形状以免打扰既有调用方。
        public static string NormalizeShotSuffix(string value)
        {
            return ShotLabelParser.NormalizeSuffix(value);
        }

        public static string FormatShotLabel(int shot, string suffix)
        {
            return ShotLabelParser.Format(shot, suffix);
        }

        public static string GetMaterialFileName(int episode, int scene, int shot, string shotSuffix, string tailSegment, string sourcePath, bool keepExtensionCase)
        {
            string extension = Path.GetExtension(sourcePath) ?? "";
            if (!keepExtensionCase)
            {
                extension = extension.ToLowerInvariant();
            }

            string safeTail = string.IsNullOrWhiteSpace(tailSegment) ? "T1" : tailSegment;
            string shotLabel = FormatShotLabel(shot, shotSuffix);
            return string.Format("E{0}-S{1}-{2}-{3}{4}", Math.Max(1, episode), Math.Max(1, scene), shotLabel, safeTail, extension);
        }

        public static string GetTailSegment(int take, string customTail)
        {
            string normalized = NormalizeCustomTailText(customTail);
            return string.IsNullOrWhiteSpace(normalized) ? "T" + Math.Max(1, take) : normalized;
        }

        // 批量生成自定义尾段：基名末尾若带数字则拆出作为起始序号（"替换1"→base="替换",start=1），
        // 否则起始序号 1。序号与基名之间用下划线分隔。空基名返回全空串（=回退默认 T 编号）。
        public static List<string> BuildBatchTails(string baseName, int count)
        {
            List<string> result = new List<string>();
            string normalizedBase = NormalizeCustomTailText(baseName);
            if (string.IsNullOrWhiteSpace(normalizedBase))
            {
                for (int i = 0; i < Math.Max(0, count); i++)
                {
                    result.Add("");
                }
                return result;
            }

            int start = 1;
            Match trailing = Regex.Match(normalizedBase, @"^(?<stem>.*?)(?<num>\d+)$");
            if (trailing.Success)
            {
                string stem = trailing.Groups["stem"].Value;
                int parsed;
                if (int.TryParse(trailing.Groups["num"].Value, out parsed) && parsed > 0)
                {
                    normalizedBase = stem;
                    start = parsed;
                }
            }

            string stemTrimmed = normalizedBase.TrimEnd('_');
            for (int i = 0; i < Math.Max(0, count); i++)
            {
                string tail = string.IsNullOrWhiteSpace(stemTrimmed)
                    ? (start + i).ToString()
                    : stemTrimmed + "_" + (start + i);
                result.Add(NormalizeCustomTailText(tail));
            }
            return result;
        }

        public static string NormalizeCustomTailText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string text = value.Trim();
            HashSet<char> invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            StringBuilder builder = new StringBuilder();
            foreach (char ch in text)
            {
                if (invalid.Contains(ch) || char.IsControl(ch))
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(ch);
                }
            }

            string normalized = builder.ToString().Trim().Trim('.');
            if (normalized.Length > 80)
            {
                normalized = normalized.Substring(0, 80).Trim();
            }

            return normalized;
        }

        public static List<string> GetTailOverrideList(ShotRow row, bool isMain)
        {
            return isMain ? row.MainTailOverrides : row.BackupTailOverrides;
        }

        public static List<string> GetFileList(ShotRow row, bool isMain)
        {
            return isMain ? row.MainFiles : row.BackupFiles;
        }

        public static void EnsureTailOverrideSize(ShotRow row, bool isMain)
        {
            if (row == null)
            {
                return;
            }

            List<string> files = GetFileList(row, isMain);
            List<string> tails = GetTailOverrideList(row, isMain);
            while (tails.Count < files.Count)
            {
                tails.Add("");
            }
            while (tails.Count > files.Count)
            {
                tails.RemoveAt(tails.Count - 1);
            }
        }

        public static string SetTailOverride(RenamePlan entry, string value)
        {
            if (entry == null || entry.Row == null)
            {
                return "";
            }

            EnsureTailOverrideSize(entry.Row, entry.IsMain);
            List<string> tails = GetTailOverrideList(entry.Row, entry.IsMain);
            if (entry.FileIndex < 0 || entry.FileIndex >= tails.Count)
            {
                return "";
            }

            string normalized = NormalizeCustomTailText(value);
            tails[entry.FileIndex] = normalized;
            return normalized;
        }

        public static string GetUniqueCustomTail(RenamePlan selectedEntry, string requestedTail, IEnumerable<RenamePlan> existingPlan, int episode, int scene, bool keepExtensionCase)
        {
            string baseTail = NormalizeCustomTailText(requestedTail);
            if (selectedEntry == null || string.IsNullOrWhiteSpace(baseTail))
            {
                return baseTail;
            }

            // 目标路径集合预先建成 HashSet：原实现每个候选序号都对整个计划
            // 做一次线性 Any 扫描（最坏 10000×n）。
            HashSet<string> existingTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingPlan != null)
            {
                foreach (RenamePlan entry in existingPlan)
                {
                    if (entry != null && !IsSamePlanEntry(entry, selectedEntry) && entry.TargetPath != null)
                    {
                        existingTargets.Add(entry.TargetPath);
                    }
                }
            }

            for (int counter = 1; counter < 10000; counter++)
            {
                string candidateTail = counter == 1 ? baseTail : AppendCustomTailCounter(baseTail, counter);
                string candidatePath = BuildTargetPathForTail(selectedEntry, candidateTail, episode, scene, keepExtensionCase);
                if (!existingTargets.Contains(candidatePath))
                {
                    return candidateTail;
                }
            }

            return AppendCustomTailCounter(baseTail, Environment.TickCount & 0x7fffffff);
        }

        public static string AppendCustomTailCounter(string baseTail, int counter)
        {
            string suffix = Math.Max(2, counter).ToString();
            // 序号前加下划线分隔，避免 "TT1"+"1" 粘连成 "TT11"（视觉误读为 T111）。
            string joiner = "_";
            int maxBaseLength = Math.Max(1, 80 - suffix.Length - joiner.Length);
            string trimmedBase = baseTail.Length > maxBaseLength ? baseTail.Substring(0, maxBaseLength).Trim() : baseTail;
            return NormalizeCustomTailText(trimmedBase + joiner + suffix);
        }

        public static string BuildTargetPathForTail(RenamePlan entry, string tailSegment, int episode, int scene, bool keepExtensionCase)
        {
            string directory = Path.GetDirectoryName(entry.OldPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Environment.CurrentDirectory;
            }

            string newName = GetMaterialFileName(episode, scene, entry.Shot, entry.Row != null ? entry.Row.ShotSuffix : "", tailSegment, entry.OldPath, keepExtensionCase);
            return Path.GetFullPath(Path.Combine(directory, newName));
        }

        public static bool IsSamePlanEntry(RenamePlan left, RenamePlan right)
        {
            return left != null &&
                right != null &&
                left.Row == right.Row &&
                left.IsMain == right.IsMain &&
                left.FileIndex == right.FileIndex;
        }

        public static string GetUniquePathWithSuffix(string path, string suffix)
        {
            string directory = Path.GetDirectoryName(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            string safeSuffix = string.IsNullOrWhiteSpace(suffix) ? "_副本" : suffix;
            string first = Path.Combine(directory, stem + safeSuffix + extension);
            if (!File.Exists(first))
            {
                return first;
            }

            int counter = 2;
            while (true)
            {
                string candidate = Path.Combine(directory, string.Format("{0}{1}{2}{3}", stem, safeSuffix, counter, extension));
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
                counter++;
            }
        }

        public static string GetCellSummary(List<string> files)
        {
            if (files == null || files.Count == 0)
            {
                return "";
            }

            string[] names = files.Take(2).Select(Path.GetFileName).ToArray();
            if (files.Count > 2)
            {
                return string.Format("{0}条：{1} ...", files.Count, string.Join("；", names));
            }

            return string.Format("{0}条：{1}", files.Count, string.Join("；", names));
        }
    }
}
