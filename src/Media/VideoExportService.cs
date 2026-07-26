using System;
using System.IO;

namespace VideoMaterialRenamer
{
    // 单个视频的 1080p 导出执行（纯 I/O + ffmpeg，无 UI）。
    // 冻结契约：覆盖模式先写 .vmr_ 临时文件 → File.Replace 原子替换 →
    // 仅在成功后交换路径；音频先 copy、失败整体回退重编码。
    public static class VideoExportService
    {
        public static void ReplaceOriginalWithExport(string tempPath, RenamePlan entry)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
            {
                File.Replace(tempPath, entry.OldPath, null);
                return;
            }

            if (File.Exists(entry.TargetPath))
            {
                throw new IOException("目标文件已存在。");
            }

            File.Move(tempPath, entry.TargetPath);
            try
            {
                File.Delete(entry.OldPath);
            }
            catch (Exception ex)
            {
                throw new IOException("已生成新文件，但原文件删除失败：" + ex.Message);
            }
        }

        public static void ExportOne(string ffmpegPath, RenamePlan entry, ExportOutputMode outputMode, bool watermarkEnabled, Action<int> progressCallback)
        {
            if (entry == null)
            {
                throw new InvalidOperationException("导出记录无效。");
            }

            if (!File.Exists(entry.OldPath))
            {
                throw new FileNotFoundException("源文件不存在。", entry.OldPath);
            }

            if (outputMode == ExportOutputMode.SaveAsNewFile &&
                File.Exists(entry.TargetPath) &&
                !StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
            {
                throw new IOException("目标文件已存在。");
            }

            string directory = outputMode == ExportOutputMode.OverwriteOriginal ? Path.GetDirectoryName(entry.OldPath) : Path.GetDirectoryName(entry.TargetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string outputPath = entry.TargetPath;
            string tempPath = "";
            string watermarkText = watermarkEnabled ? entry.NewName : "";
            if (outputMode == ExportOutputMode.OverwriteOriginal)
            {
                tempPath = Path.Combine(directory, ".vmr_" + Guid.NewGuid().ToString("N") + Path.GetExtension(entry.TargetPath));
                outputPath = tempPath;
            }

            try
            {
                try
                {
                    FfmpegRunner.RunExport(ffmpegPath, entry.OldPath, outputPath, true, watermarkText, progressCallback);
                }
                catch
                {
                    if (File.Exists(outputPath))
                    {
                        File.Delete(outputPath);
                    }
                    if (progressCallback != null)
                    {
                        progressCallback(0);
                    }
                    FfmpegRunner.RunExport(ffmpegPath, entry.OldPath, outputPath, false, watermarkText, progressCallback);
                }

                if (outputMode == ExportOutputMode.OverwriteOriginal)
                {
                    ReplaceOriginalWithExport(tempPath, entry);
                    tempPath = "";
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
