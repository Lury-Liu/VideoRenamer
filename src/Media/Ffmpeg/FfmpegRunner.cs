using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace VideoRenamer
{
    // 跨线程取消令牌：Cancel() 立即杀掉当前活动的 ffmpeg 进程（若有），
    // 之后再启动的进程也会被立刻杀掉。取消后 RunExport 抛
    // OperationCanceledException（调用方据此与真实失败区分）。
    public sealed class FfmpegCancellation
    {
        private readonly object sync = new object();
        private System.Diagnostics.Process active;
        private bool cancelled;

        public bool IsCancelled
        {
            get
            {
                lock (sync)
                {
                    return cancelled;
                }
            }
        }

        public void Cancel()
        {
            lock (sync)
            {
                cancelled = true;
                if (active != null)
                {
                    try
                    {
                        active.Kill();
                    }
                    catch
                    {
                    }
                }
            }
        }

        internal void SetActive(System.Diagnostics.Process process)
        {
            lock (sync)
            {
                active = process;
                if (cancelled && process != null)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }
                }
            }
        }

        internal void ClearActive()
        {
            lock (sync)
            {
                active = null;
            }
        }
    }

    // ffmpeg 导出进程的启动与进度解析。
    public static class FfmpegRunner
    {
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

        public static void RunExport(string ffmpegPath, string inputPath, string outputPath, bool copyAudio, string watermarkText, Action<int> progressCallback)
        {
            RunExport(ffmpegPath, inputPath, outputPath, copyAudio, watermarkText, progressCallback, null);
        }

        public static void RunExport(string ffmpegPath, string inputPath, string outputPath, bool copyAudio, string watermarkText, Action<int> progressCallback, FfmpegCancellation cancellation)
        {
            if (cancellation != null && cancellation.IsCancelled)
            {
                throw new OperationCanceledException("导出已取消。");
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = ffmpegPath;
            startInfo.Arguments = FfmpegArguments.BuildExportArguments(inputPath, outputPath, copyAudio, watermarkText);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;

            using (Process process = Process.Start(startInfo))
            {
                if (cancellation != null)
                {
                    cancellation.SetActive(process);
                }
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
                if (cancellation != null)
                {
                    cancellation.ClearActive();
                    if (cancellation.IsCancelled)
                    {
                        throw new OperationCanceledException("导出已取消。");
                    }
                }
                if (process.ExitCode != 0)
                {
                    throw new Exception(TrimProcessLog(error.ToString()));
                }
            }
        }
    }
}
