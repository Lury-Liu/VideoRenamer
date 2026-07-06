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

        private static string FindFfmpegPath()
        {
            List<string> candidates = new List<string>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentDir = Environment.CurrentDirectory;
            candidates.Add(Path.Combine(baseDir, "ffmpeg.exe"));
            candidates.Add(Path.Combine(baseDir, "tools", "ffmpeg.exe"));
            candidates.Add(Path.Combine(currentDir, "ffmpeg.exe"));
            candidates.Add(Path.Combine(currentDir, "tools", "ffmpeg.exe"));

            string pathText = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string directory in pathText.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    candidates.Add(Path.Combine(directory.Trim(), "ffmpeg.exe"));
                }
                catch
                {
                }
            }

            string embeddedFfmpeg = ExtractEmbeddedFfmpeg();
            if (!string.IsNullOrWhiteSpace(embeddedFfmpeg))
            {
                candidates.Add(embeddedFfmpeg);
            }

            foreach (string candidate in candidates)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private static string ExtractEmbeddedFfmpeg()
        {
            const string resourceName = "VideoMaterialRenamer.ffmpeg.exe";
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream resource = assembly.GetManifestResourceStream(resourceName))
                {
                    if (resource == null)
                    {
                        return "";
                    }

                    string toolsDir = Path.Combine(AppInfo.AppDataDirectory, "tools");
                    Directory.CreateDirectory(toolsDir);
                    string ffmpegPath = Path.Combine(toolsDir, "ffmpeg.exe");
                    long resourceLength = resource.CanSeek ? resource.Length : -1;
                    if (File.Exists(ffmpegPath) && resourceLength > 0)
                    {
                        FileInfo existing = new FileInfo(ffmpegPath);
                        if (existing.Length == resourceLength)
                        {
                            return ffmpegPath;
                        }
                    }

                    string tempPath = ffmpegPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (FileStream output = File.Create(tempPath))
                    {
                        resource.CopyTo(output);
                    }

                    if (File.Exists(ffmpegPath))
                    {
                        File.Delete(ffmpegPath);
                    }
                    File.Move(tempPath, ffmpegPath);
                    return ffmpegPath;
                }
            }
            catch
            {
                return "";
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static string TrimProcessLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "未知错误";
            }

            text = text.Trim();
            if (text.Length <= 500)
            {
                return text;
            }

            return text.Substring(text.Length - 500);
        }

        private static double ParseClockSeconds(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return -1;
            }

            Match match = Regex.Match(value.Trim(), @"(?<h>\d+):(?<m>\d+):(?<s>\d+(?:\.\d+)?)");
            if (!match.Success)
            {
                return -1;
            }

            double seconds;
            if (!double.TryParse(match.Groups["s"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds))
            {
                return -1;
            }

            return int.Parse(match.Groups["h"].Value) * 3600 + int.Parse(match.Groups["m"].Value) * 60 + seconds;
        }

        private static double ParseProgressSeconds(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return -1;
            }

            string[] parts = line.Split(new char[] { '=' }, 2);
            if (parts.Length != 2)
            {
                return -1;
            }

            string key = parts[0].Trim();
            string value = parts[1].Trim();
            if (key == "out_time" || key == "out_time_str")
            {
                return ParseClockSeconds(value);
            }

            if (key == "out_time_ms" || key == "out_time_us")
            {
                double raw;
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out raw))
                {
                    return raw / 1000000.0;
                }
            }

            return -1;
        }

        private static double ParseDurationSeconds(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return -1;
            }

            Match match = Regex.Match(line, @"Duration:\s*(?<time>\d+:\d+:\d+(?:\.\d+)?)");
            if (!match.Success)
            {
                return -1;
            }

            return ParseClockSeconds(match.Groups["time"].Value);
        }

        private static RenamePlan CloneRenamePlan(RenamePlan entry)
        {
            if (entry == null)
            {
                return null;
            }

            return new RenamePlan
            {
                Row = entry.Row,
                RowIndex = entry.RowIndex,
                ColumnName = entry.ColumnName,
                IsMain = entry.IsMain,
                FileIndex = entry.FileIndex,
                Scene = entry.Scene,
                Shot = entry.Shot,
                Take = entry.Take,
                TailSegment = entry.TailSegment,
                CustomTailText = entry.CustomTailText,
                HasCustomTail = entry.HasCustomTail,
                OldPath = entry.OldPath,
                TargetPath = entry.TargetPath,
                OldName = entry.OldName,
                NewName = entry.NewName,
                Status = entry.Status
            };
        }

        private static List<RenamePlan> PrepareExportPlan(List<RenamePlan> sourcePlan, ExportOutputMode outputMode)
        {
            List<RenamePlan> prepared = new List<RenamePlan>();
            Dictionary<string, bool> targets = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (RenamePlan source in sourcePlan)
            {
                RenamePlan entry = CloneRenamePlan(source);
                if (entry == null)
                {
                    continue;
                }

                if (outputMode == ExportOutputMode.SaveAsNewFile && StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
                {
                    entry.TargetPath = GetUniquePathWithSuffix(entry.TargetPath, "_1080p");
                    entry.NewName = Path.GetFileName(entry.TargetPath);
                    entry.Status = "另存为新文件";
                }

                if (outputMode == ExportOutputMode.SaveAsNewFile &&
                    File.Exists(entry.TargetPath) &&
                    !StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
                {
                    throw new IOException("目标文件已存在：" + entry.NewName);
                }

                if (targets.ContainsKey(entry.TargetPath))
                {
                    throw new IOException("新文件名重复：" + entry.NewName);
                }

                targets[entry.TargetPath] = true;
                prepared.Add(entry);
            }

            return prepared;
        }

        private static string NormalizeWatermarkText(string text)
        {
            string value = Path.GetFileName(text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsControl(chars[i]))
                {
                    chars[i] = ' ';
                }
            }

            return new string(chars).Trim();
        }

        private static string EscapeFfmpegFilterValue(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace(":", "\\:")
                .Replace(",", "\\,")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Replace(";", "\\;");
        }

        private static string GetWatermarkFontFile()
        {
            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] candidates = new string[]
            {
                Path.Combine(windowsDir, "Fonts", "msyh.ttc"),
                Path.Combine(windowsDir, "Fonts", "msyhbd.ttc"),
                Path.Combine(windowsDir, "Fonts", "simhei.ttf"),
                Path.Combine(windowsDir, "Fonts", "arial.ttf")
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        return candidate.Replace('\\', '/');
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private static string BuildVideoFilter(string watermarkText)
        {
            string baseFilter = "scale=1080:1920:flags=bicubic,setsar=1";
            string normalized = NormalizeWatermarkText(watermarkText);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return baseFilter;
            }

            string fontFile = GetWatermarkFontFile();
            string fontPart = string.IsNullOrWhiteSpace(fontFile) ? "" : "fontfile='" + EscapeFfmpegFilterValue(fontFile) + "':";
            string text = EscapeFfmpegFilterValue(normalized);
            return baseFilter +
                ",drawtext=" + fontPart +
                "text='" + text + "':" +
                "x=24:y=24:fontsize=24:" +
                "fontcolor=white@0.92:" +
                "box=1:boxcolor=black@0.55:boxborderw=10:" +
                "expansion=none";
        }

        private static string BuildFfmpegArguments(string inputPath, string outputPath, bool copyAudio, string watermarkText)
        {
            List<string> args = new List<string>();
            args.Add("-hide_banner");
            args.Add("-nostdin");
            args.Add("-nostats");
            args.Add("-y");
            args.Add("-i");
            args.Add(QuoteArgument(inputPath));
            args.Add("-vf");
            args.Add(QuoteArgument(BuildVideoFilter(watermarkText)));
            args.Add("-c:v");
            args.Add("libx264");
            args.Add("-preset");
            args.Add("veryfast");
            args.Add("-crf");
            args.Add("20");
            args.Add("-pix_fmt");
            args.Add("yuv420p");
            args.Add("-threads");
            args.Add("0");
            args.Add("-progress");
            args.Add("pipe:1");

            if (copyAudio)
            {
                args.Add("-c:a");
                args.Add("copy");
            }
            else
            {
                args.Add("-c:a");
                args.Add("aac");
                args.Add("-b:a");
                args.Add("160k");
            }

            args.Add(QuoteArgument(outputPath));
            return string.Join(" ", args.ToArray());
        }

        private static void RunFfmpegExport(string ffmpegPath, string inputPath, string outputPath, bool copyAudio, string watermarkText, Action<int> progressCallback)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = ffmpegPath;
            startInfo.Arguments = BuildFfmpegArguments(inputPath, outputPath, copyAudio, watermarkText);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;

            using (Process process = Process.Start(startInfo))
            {
                if (progressCallback != null)
                {
                    progressCallback(5);
                }

                StringBuilder error = new StringBuilder();
                double durationSeconds = -1;
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        error.AppendLine(e.Data);
                        double parsedDuration = ParseDurationSeconds(e.Data);
                        if (parsedDuration > 0)
                        {
                            durationSeconds = parsedDuration;
                        }
                    }
                };
                process.BeginErrorReadLine();

                string line;
                bool sawProgress = false;
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    if (progressCallback != null)
                    {
                        if (!sawProgress && (line.StartsWith("out_time=", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("out_time_us=", StringComparison.OrdinalIgnoreCase)))
                        {
                            sawProgress = true;
                        }

                        double outputSeconds = ParseProgressSeconds(line);
                        if (outputSeconds >= 0)
                        {
                            if (durationSeconds > 0)
                            {
                                int percent = 5 + (int)Math.Round(Math.Min(1.0, outputSeconds / durationSeconds) * 90.0);
                                progressCallback(Math.Max(5, Math.Min(95, percent)));
                            }
                            else
                            {
                                progressCallback(sawProgress ? 50 : 15);
                            }
                        }
                        else if (line.Trim() == "progress=end")
                        {
                            progressCallback(95);
                        }
                    }
                }

                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new Exception(TrimProcessLog(error.ToString()));
                }
            }
        }

        private static void ReplaceOriginalWithExport(string tempPath, RenamePlan entry)
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

        private static void ExportOneVideoTo1080p(string ffmpegPath, RenamePlan entry, ExportOutputMode outputMode, bool watermarkEnabled, Action<int> progressCallback)
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
                    RunFfmpegExport(ffmpegPath, entry.OldPath, outputPath, true, watermarkText, progressCallback);
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
                    RunFfmpegExport(ffmpegPath, entry.OldPath, outputPath, false, watermarkText, progressCallback);
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

        private void StartExport1080p(List<RenamePlan> plan, string ffmpegPath, ExportOutputMode outputMode, bool watermarkEnabled)
        {
            SetProgressColumnVisible(true);
            ResetProgressBars();
            SetOperationUiEnabled(false);
            statusLabel.Text = outputMode == ExportOutputMode.OverwriteOriginal
                ? (watermarkEnabled ? "正在导出并添加文件名水印，请等待..." : "正在导出并覆盖原文件，请等待...")
                : (watermarkEnabled ? "正在导出 1080x1920 新文件并添加文件名水印，请等待..." : "正在导出 1080x1920 新文件，请等待...");

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> failures = new List<string>();
                List<RenameOperation> successfulOperations = new List<RenameOperation>();
                Dictionary<ShotRow, int> rowTotals = new Dictionary<ShotRow, int>();
                Dictionary<ShotRow, int> rowCompleted = new Dictionary<ShotRow, int>();
                foreach (RenamePlan item in plan)
                {
                    if (item.Row == null)
                    {
                        continue;
                    }
                    if (!rowTotals.ContainsKey(item.Row))
                    {
                        rowTotals[item.Row] = 0;
                        rowCompleted[item.Row] = 0;
                    }
                    rowTotals[item.Row]++;
                }

                int total = plan.Count;
                int index = 0;

                foreach (RenamePlan entry in plan)
                {
                    index++;
                    int currentIndex = index;
                    QueueOnUi(delegate
                    {
                        statusLabel.Text = string.Format("正在导出 {0}/{1}：{2}", currentIndex, total, entry.NewName);
                    });

                    try
                    {
                        Action<int> progressCallback = delegate(int percent)
                        {
                            if (entry.Row == null)
                            {
                                return;
                            }

                            int completed = rowCompleted.ContainsKey(entry.Row) ? rowCompleted[entry.Row] : 0;
                            int rowTotal = rowTotals.ContainsKey(entry.Row) ? Math.Max(1, rowTotals[entry.Row]) : 1;
                            int safePercent = Math.Max(0, Math.Min(100, percent));
                            int rowPercent = (int)Math.Max(0, Math.Min(100, Math.Round((completed + safePercent / 100.0) * 100.0 / rowTotal)));
                            QueueOnUi(delegate
                            {
                                entry.Row.ProgressPercent = rowPercent;
                                RenderGridProgress(entry.RowIndex - 1);
                                statusLabel.Text = string.Format("正在导出 {0}/{1}：{2}（{3}%）", currentIndex, total, entry.NewName, safePercent);
                            });
                        };

                        ExportOneVideoTo1080p(ffmpegPath, entry, outputMode, watermarkEnabled, progressCallback);
                        if (entry.Row != null && rowCompleted.ContainsKey(entry.Row))
                        {
                            rowCompleted[entry.Row]++;
                            progressCallback(100);
                        }
                        successfulOperations.Add(new RenameOperation
                        {
                            Row = entry.Row,
                            RowIndex = entry.RowIndex,
                            IsMain = entry.IsMain,
                            FileIndex = entry.FileIndex,
                            OriginalPath = entry.OldPath,
                            RenamedPath = entry.TargetPath
                        });
                    }
                    catch (Exception ex)
                    {
                        failures.Add(entry.OldName + ": " + ex.Message);
                        if (entry.Row != null && rowCompleted.ContainsKey(entry.Row))
                        {
                            rowCompleted[entry.Row]++;
                        }
                    }
                }

                QueueOnUi(delegate
                {
                    foreach (RenameOperation op in successfulOperations)
                    {
                        if (op.Row == null)
                        {
                            continue;
                        }

                        List<string> files = op.IsMain ? op.Row.MainFiles : op.Row.BackupFiles;
                        if (op.FileIndex >= 0 && op.FileIndex < files.Count && StringComparer.OrdinalIgnoreCase.Equals(files[op.FileIndex], op.OriginalPath))
                        {
                            files[op.FileIndex] = op.RenamedPath;
                        }
                        else
                        {
                            int currentIndex = files.FindIndex(p => StringComparer.OrdinalIgnoreCase.Equals(p, op.OriginalPath));
                            if (currentIndex >= 0)
                            {
                                files[currentIndex] = op.RenamedPath;
                            }
                        }
                    }

                    RenderAll();
                    SetProgressColumnVisible(false);
                    SetOperationUiEnabled(true);

                    if (failures.Count > 0)
                    {
                        MessageBox.Show(this, string.Join("\r\n", failures.Take(8).ToArray()), "部分视频导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        string finishMessage = outputMode == ExportOutputMode.OverwriteOriginal
                            ? "已处理 " + successfulOperations.Count + " 个视频，并覆盖原文件。"
                            : "已导出 " + successfulOperations.Count + " 个 1080x1920 新文件，原始素材已保留。";
                        MessageBox.Show(this, finishMessage, "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                });
            });
        }

        private bool TryChooseExportOutputMode(out ExportOutputMode outputMode)
        {
            outputMode = ExportOutputMode.OverwriteOriginal;
            DialogResult result = MessageBox.Show(
                this,
                "请选择 1080x1920 导出后的保存方式：\r\n\r\n是：覆盖原文件（默认，原文件会被替换）\r\n否：另存为新文件（保留原文件）\r\n取消：不执行",
                "选择导出保存方式",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (result == DialogResult.Cancel)
            {
                return false;
            }

            outputMode = result == DialogResult.Yes ? ExportOutputMode.OverwriteOriginal : ExportOutputMode.SaveAsNewFile;
            return true;
        }
    }
}
