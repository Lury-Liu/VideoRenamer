using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace VideoRenamer
{
    // 1080p 导出前的计划派生：克隆条目、另存为模式改名、目标冲突校验。
    public static class ExportPlanBuilder
    {
        public static List<RenamePlan> Prepare(List<RenamePlan> sourcePlan, ExportOutputMode outputMode)
        {
            List<RenamePlan> prepared = new List<RenamePlan>();
            Dictionary<string, bool> targets = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            if (sourcePlan == null)
            {
                return prepared;
            }

            foreach (RenamePlan source in sourcePlan)
            {
                if (source == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(source.OldPath) ||
                    string.IsNullOrWhiteSpace(source.TargetPath))
                {
                    throw new IOException("导出记录缺少源路径或目标路径。");
                }

                RenamePlan entry = source.Clone();
                entry.OldPath = Path.GetFullPath(entry.OldPath);
                entry.TargetPath = Path.GetFullPath(entry.TargetPath);

                if (outputMode == ExportOutputMode.SaveAsNewFile &&
                    StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
                {
                    entry.TargetPath = GetUniquePathWithSuffixRespectingReserved(
                        entry.TargetPath, "_1080p", targets);
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
                    throw new IOException(
                        PlanStatusText.For(PlanStatus.DuplicateNewName) + "：" + entry.NewName);
                }

                targets[entry.TargetPath] = true;
                prepared.Add(entry);
            }

            return prepared;
        }

        // Compatibility overload
        public static List<RenamePlan> PrepareExportOnly(
            List<RenamePlan> sourcePlan,
            ExportOutputMode outputMode)
        {
            return PrepareExportOnly(sourcePlan, outputMode, "");
        }

        public static List<RenamePlan> PrepareExportOnly(
            List<RenamePlan> sourcePlan,
            ExportOutputMode outputMode,
            string outputDirectory)
        {
            List<RenamePlan> keepName = new List<RenamePlan>();
            Dictionary<string, bool> reserved =
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            string directory = NormalizeOutputDirectory(outputDirectory);
            bool hasOutputDirectory = !string.IsNullOrWhiteSpace(directory);

            if (sourcePlan == null)
            {
                return keepName;
            }

            foreach (RenamePlan source in sourcePlan)
            {
                if (source == null || source.Status == PlanStatus.SourceMissing)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(source.OldPath))
                {
                    throw new IOException("导出记录缺少源路径。");
                }

                RenamePlan entry = source.Clone();
                string sourcePath = Path.GetFullPath(entry.OldPath);
                entry.OldPath = sourcePath;

                // 只取文件名，避免历史数据带路径时逃逸输出目录。
                string oldName = Path.GetFileName(entry.OldName);
                if (string.IsNullOrWhiteSpace(oldName))
                {
                    oldName = Path.GetFileName(sourcePath);
                }

                if (string.IsNullOrWhiteSpace(oldName))
                {
                    throw new IOException("导出记录缺少文件名。");
                }

                string targetPath = hasOutputDirectory
                    ? Path.GetFullPath(Path.Combine(directory, oldName))
                    : sourcePath;

                targetPath = FindAvailableExportPath(
                    targetPath,
                    sourcePath,
                    reserved);

                entry.TargetPath = targetPath;
                entry.NewName = Path.GetFileName(targetPath);
                entry.Status = PlanStatus.Ready;
                reserved[targetPath] = true;
                keepName.Add(entry);
            }

            return Prepare(keepName, outputMode);
        }

        private static string NormalizeOutputDirectory(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return "";
            }

            return Path.GetFullPath(outputDirectory.Trim());
        }

        private static string FindAvailableExportPath(
            string requestedPath,
            string sourcePath,
            Dictionary<string, bool> reserved)
        {
            string candidate = Path.GetFullPath(requestedPath);

            if (!IsOccupied(candidate, sourcePath, reserved))
            {
                return candidate;
            }

            string fileName = Path.GetFileName(candidate);
            string extension = Path.GetExtension(fileName);
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string directory = Path.GetDirectoryName(candidate);

            Match material = Regex.Match(
                stem,
                "^(E[0-9]+-S[0-9]+-[^-]+-)T([0-9]+)$",
                RegexOptions.IgnoreCase);

            if (material.Success)
            {
                int number;
                if (int.TryParse(material.Groups[2].Value, out number) &&
                    number > 0)
                {
                    for (int attempt = 1; attempt < 10000; attempt++)
                    {
                        if (number > int.MaxValue - attempt)
                        {
                            break;
                        }

                        string materialName =
                            material.Groups[1].Value + "T" +
                            (number + attempt) + extension;
                        string materialPath = Path.Combine(directory, materialName);

                        if (!IsOccupied(materialPath, sourcePath, reserved))
                        {
                            return Path.GetFullPath(materialPath);
                        }
                    }
                }
            }

            for (int counter = 2; counter < 10000; counter++)
            {
                string genericPath = Path.Combine(
                    directory,
                    stem + "_" + counter + extension);

                if (!IsOccupied(genericPath, sourcePath, reserved))
                {
                    return Path.GetFullPath(genericPath);
                }
            }

            throw new IOException(
                "无法为导出文件生成不重复的目标名称：" + fileName);
        }

        private static string GetUniquePathWithSuffixRespectingReserved(
            string path,
            string suffix,
            Dictionary<string, bool> reserved)
        {
            string candidate = RenamePlanBuilder.GetUniquePathWithSuffix(path, suffix);
            if (!reserved.ContainsKey(candidate))
            {
                return candidate;
            }

            string extension = Path.GetExtension(candidate);
            string stem = Path.GetFileNameWithoutExtension(candidate);
            string directory = Path.GetDirectoryName(candidate);
            for (int counter = 2; counter < 10000; counter++)
            {
                string next = Path.Combine(
                    directory,
                    stem + "_" + counter + extension);
                if (!reserved.ContainsKey(next) && !File.Exists(next))
                {
                    return Path.GetFullPath(next);
                }
            }

            throw new IOException("无法为导出文件生成不重复的目标名称：" + Path.GetFileName(path));
        }

        private static bool IsOccupied(
            string path,
            string sourcePath,
            Dictionary<string, bool> reserved)
        {
            string normalizedPath = Path.GetFullPath(path);
            string normalizedSource = Path.GetFullPath(sourcePath);

            if (reserved.ContainsKey(normalizedPath))
            {
                return true;
            }

            if (File.Exists(normalizedPath) &&
                !StringComparer.OrdinalIgnoreCase.Equals(normalizedPath, normalizedSource))
            {
                return true;
            }

            return false;
        }
    }
}
