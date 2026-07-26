using System;
using System.Collections.Generic;
using System.IO;

namespace VideoMaterialRenamer
{
    // ffmpeg 命令行参数构造（纯函数，无进程/UI 依赖）。
    // 导出参数串由测试黄金值逐字节锁定——任何改动都会改变导出画质或破坏导出。
    public static class FfmpegArguments
    {
        internal static string QuoteArgument(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
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

        internal static string EscapeFfmpegFilterValue(string value)
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

        public static string BuildExportArguments(string inputPath, string outputPath, bool copyAudio, string watermarkText)
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
    }
}
