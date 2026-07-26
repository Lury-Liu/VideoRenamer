using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;


namespace VideoMaterialRenamer
{
    public static class VideoMetadataReader
    {
        public static VideoFileInfo Read(string path)
        {
            VideoFileInfo info = new VideoFileInfo();
            info.Path = path ?? "";
            info.FileName = string.IsNullOrWhiteSpace(path) ? "" : System.IO.Path.GetFileName(path);
            info.SizeText = "";
            info.ResolutionText = "未知";
            info.DurationText = "";
            info.ModifiedText = "";
            info.Exists = File.Exists(path);

            if (!info.Exists)
            {
                return info;
            }

            try
            {
                FileInfo file = new FileInfo(path);
                info.SizeText = FormatBytes(file.Length);
                info.ModifiedText = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
            }

            try
            {
                string width;
                string height;
                string duration;
                ReadVideoDetailsViaShell(path, out width, out height, out duration);
                if (!string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height))
                {
                    info.ResolutionText = width + " x " + height;
                }
                info.DurationText = FormatDurationText(duration);
            }
            catch
            {
            }

            return info;
        }

        // Shell 属性列索引缓存：宽/高所在的列号在同一系统上是稳定的。
        // 首个文件做一次 0..339 全列扫描并记住两列索引，之后每个文件只需
        // 约 4 次 late-bound COM 调用（原实现每个文件约 680 次）。
        // 缓存索引读不出值时（区域设置变化等）自动回退全扫描并刷新缓存。
        private static readonly object IndexCacheSync = new object();
        private static int cachedWidthIndex = -1;
        private static int cachedHeightIndex = -1;
        private static int cachedDurationIndex = -1;

        private static readonly string[] WidthDetailNames = { "帧宽度", "宽度", "Frame width", "Width" };
        private static readonly string[] HeightDetailNames = { "帧高度", "高度", "Frame height", "Height" };
        // 时长为尽力而为：列缺失/读不出只影响时长展示，不影响分辨率。
        //（"Length" 的包含匹配可能撞上 "Focal length" 等列，但值必须形如
        // H:MM:SS 才被采纳——见 FormatDurationText 的格式门。）
        private static readonly string[] DurationDetailNames = { "时长", "Length", "Duration" };

        private static void ReadVideoDetailsViaShell(string path, out string width, out string height, out string duration)
        {
            width = "";
            height = "";
            duration = "";

            object shell = null;
            object folder = null;
            object item = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    return;
                }

                shell = Activator.CreateInstance(shellType);
                string directory = System.IO.Path.GetDirectoryName(path);
                string fileName = System.IO.Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
                {
                    return;
                }

                folder = shellType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { directory });
                if (folder == null)
                {
                    return;
                }

                item = folder.GetType().InvokeMember("ParseName", BindingFlags.InvokeMethod, null, folder, new object[] { fileName });
                if (item == null)
                {
                    return;
                }

                int widthIndex;
                int heightIndex;
                int durationIndex;
                lock (IndexCacheSync)
                {
                    widthIndex = cachedWidthIndex;
                    heightIndex = cachedHeightIndex;
                    durationIndex = cachedDurationIndex;
                }

                if (widthIndex >= 0 && heightIndex >= 0)
                {
                    width = NormalizeDimension(GetDetailValue(folder, item, widthIndex));
                    height = NormalizeDimension(GetDetailValue(folder, item, heightIndex));
                    if (!string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height))
                    {
                        if (durationIndex >= 0)
                        {
                            duration = NormalizeDurationValue(GetDetailValue(folder, item, durationIndex));
                        }
                        return;
                    }
                    width = "";
                    height = "";
                    duration = "";
                }

                // 全扫描：逐列取列名，命中目标列才取值（并记录索引）。
                for (int i = 0; i < 340; i++)
                {
                    string key = CleanShellText(Convert.ToString(folder.GetType().InvokeMember("GetDetailsOf", BindingFlags.InvokeMethod, null, folder, new object[] { null, i })));
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(width) && MatchesAnyName(key, WidthDetailNames))
                    {
                        string value = NormalizeDimension(GetDetailValue(folder, item, i));
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            width = value;
                            widthIndex = i;
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(height) && MatchesAnyName(key, HeightDetailNames))
                    {
                        string value = NormalizeDimension(GetDetailValue(folder, item, i));
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            height = value;
                            heightIndex = i;
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(duration) && MatchesAnyName(key, DurationDetailNames))
                    {
                        string value = NormalizeDurationValue(GetDetailValue(folder, item, i));
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            duration = value;
                            durationIndex = i;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height) && !string.IsNullOrWhiteSpace(duration))
                    {
                        break;
                    }
                }

                if (widthIndex >= 0 && heightIndex >= 0)
                {
                    lock (IndexCacheSync)
                    {
                        cachedWidthIndex = widthIndex;
                        cachedHeightIndex = heightIndex;
                        if (durationIndex >= 0)
                        {
                            cachedDurationIndex = durationIndex;
                        }
                    }
                }
            }
            finally
            {
                ReleaseComObject(item);
                ReleaseComObject(folder);
                ReleaseComObject(shell);
            }
        }

        private static bool MatchesAnyName(string key, string[] names)
        {
            foreach (string name in names)
            {
                if (key.IndexOf(name, StringComparison.CurrentCultureIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static string GetDetailValue(object folder, object item, int index)
        {
            return CleanShellText(Convert.ToString(folder.GetType().InvokeMember("GetDetailsOf", BindingFlags.InvokeMethod, null, folder, new object[] { item, index })));
        }

        public static string FormatBytes(long bytes)
        {
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? value.ToString("0") + " " + units[unit] : value.ToString("0.##") + " " + units[unit];
        }

        // 只接受 H:MM:SS / M:SS 形态的列值——这是"该列确实是时长"的
        // 格式门（防止 "Length" 包含匹配误缓存 "Focal length" 等列）。
        private static string NormalizeDurationValue(string value)
        {
            value = CleanShellText(value);
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return Regex.IsMatch(value, @"^\d{1,3}:\d{1,2}(:\d{1,2})?(\.\d+)?$") ? value : "";
        }

        // Shell 时长列是整秒（如 00:00:08）——紧凑显示：8s / 2:05 / 1:02:03。
        private static string FormatDurationText(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "";
            }

            string[] segments = normalized.Split(':');
            int hours = 0;
            int minutes = 0;
            double seconds = 0;
            bool parsed;
            if (segments.Length == 3)
            {
                parsed = int.TryParse(segments[0], out hours) && int.TryParse(segments[1], out minutes) && double.TryParse(segments[2], out seconds);
            }
            else if (segments.Length == 2)
            {
                parsed = int.TryParse(segments[0], out minutes) && double.TryParse(segments[1], out seconds);
            }
            else
            {
                parsed = false;
            }

            if (!parsed)
            {
                return "";
            }

            double total = hours * 3600 + minutes * 60 + seconds;
            if (total <= 0)
            {
                return "";
            }
            if (total < 60)
            {
                return (total % 1 == 0 ? ((int)total).ToString() : total.ToString("0.#")) + "s";
            }

            int wholeSeconds = (int)Math.Round(total);
            int h = wholeSeconds / 3600;
            int m = (wholeSeconds % 3600) / 60;
            int s = wholeSeconds % 60;
            return h > 0
                ? string.Format("{0}:{1:00}:{2:00}", h, m, s)
                : string.Format("{0}:{1:00}", m, s);
        }

        private static string NormalizeDimension(string value)
        {
            value = CleanShellText(value);
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            Match match = Regex.Match(value.Replace(",", ""), "\\d+");
            return match.Success ? match.Value : value;
        }

        private static string CleanShellText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return value
                .Replace("\u200e", "")
                .Replace("\u200f", "")
                .Replace("\u202a", "")
                .Replace("\u202c", "")
                .Trim();
        }

        private static void ReleaseComObject(object value)
        {
            try
            {
                if (value != null && Marshal.IsComObject(value))
                {
                    Marshal.FinalReleaseComObject(value);
                }
            }
            catch
            {
            }
        }
    }
}
