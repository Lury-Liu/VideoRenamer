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

        internal static string BuildFfmpegArguments(string inputPath, string outputPath, bool copyAudio, string watermarkText)
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
    }
}
