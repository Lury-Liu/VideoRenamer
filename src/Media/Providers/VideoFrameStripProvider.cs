using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;

namespace VideoRenamer
{
    // 用 ffmpeg 一次性抽取若干均匀分布的关键帧，供预览区“鼠标划过看不同时间点画面”。
    public static class VideoFrameStripProvider
    {
        // 启动时清扫上次异常退出遗留的抽帧临时目录（仅清理 1 小时以前的，
        // 避免误删并行实例正在使用的目录）。
        public static void SweepOrphanedStripDirs()
        {
            try
            {
                DateTime cutoff = DateTime.Now.AddHours(-1);
                foreach (string dir in Directory.GetDirectories(Path.GetTempPath(), "VMR_Strip_*"))
                {
                    try
                    {
                        if (Directory.GetLastWriteTime(dir) < cutoff)
                        {
                            Directory.Delete(dir, true);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        public static string BuildStripArguments(string inputPath, string outputPattern, int frameCount)
        {
            int count = Math.Max(1, frameCount);
            List<string> args = new List<string>();
            args.Add("-hide_banner");
            args.Add("-nostdin");
            args.Add("-nostats");
            args.Add("-y");
            args.Add("-i");
            args.Add(FfmpegArguments.QuoteArgument(inputPath));
            // thumbnail 过滤器挑选有代表性的帧；scale 限制宽度加速。
            args.Add("-vf");
            args.Add(FfmpegArguments.QuoteArgument("thumbnail=n=" + count + ",scale=320:-1"));
            args.Add("-frames:v");
            args.Add(count.ToString());
            args.Add("-vsync");
            args.Add("vfr");
            args.Add(FfmpegArguments.QuoteArgument(outputPattern));
            return string.Join(" ", args.ToArray());
        }

        public static List<Image> Extract(string ffmpegPath, string inputPath, int frameCount)
        {
            List<Image> frames = new List<Image>();
            if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath) ||
                string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
            {
                return frames;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "VMR_Strip_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                string pattern = Path.Combine(tempDir, "f_%03d.png");

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = ffmpegPath;
                startInfo.Arguments = BuildStripArguments(inputPath, pattern, frameCount);
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardError = true;
                startInfo.RedirectStandardOutput = true;

                using (Process process = Process.Start(startInfo))
                {
                    process.StandardError.ReadToEnd();
                    process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                }

                string[] files = Directory.GetFiles(tempDir, "f_*.png");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    try
                    {
                        using (Image loaded = Image.FromFile(file))
                        {
                            frames.Add(new Bitmap(loaded));
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                }
            }
            return frames;
        }
    }
}
