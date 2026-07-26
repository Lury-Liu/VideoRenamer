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
    public static class VideoMetadataReader
    {
        public static VideoFileInfo Read(string path)
        {
            VideoFileInfo info = new VideoFileInfo();
            info.Path = path ?? "";
            info.FileName = string.IsNullOrWhiteSpace(path) ? "" : System.IO.Path.GetFileName(path);
            info.SizeText = "";
            info.ResolutionText = "未知";
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
                Dictionary<string, string> details = ReadShellDetails(path);
                string width = NormalizeDimension(FindDetail(details, new string[] { "帧宽度", "宽度", "Frame width", "Width" }));
                string height = NormalizeDimension(FindDetail(details, new string[] { "帧高度", "高度", "Frame height", "Height" }));
                if (!string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height))
                {
                    info.ResolutionText = width + " x " + height;
                }
            }
            catch
            {
            }

            return info;
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

        private static Dictionary<string, string> ReadShellDetails(string path)
        {
            Dictionary<string, string> details = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
            object shell = null;
            object folder = null;
            object item = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    return details;
                }

                shell = Activator.CreateInstance(shellType);
                string directory = System.IO.Path.GetDirectoryName(path);
                string fileName = System.IO.Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
                {
                    return details;
                }

                folder = shellType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { directory });
                if (folder == null)
                {
                    return details;
                }

                item = folder.GetType().InvokeMember("ParseName", BindingFlags.InvokeMethod, null, folder, new object[] { fileName });
                if (item == null)
                {
                    return details;
                }

                for (int i = 0; i < 340; i++)
                {
                    string key = CleanShellText(Convert.ToString(folder.GetType().InvokeMember("GetDetailsOf", BindingFlags.InvokeMethod, null, folder, new object[] { null, i })));
                    string value = CleanShellText(Convert.ToString(folder.GetType().InvokeMember("GetDetailsOf", BindingFlags.InvokeMethod, null, folder, new object[] { item, i })));
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value) && !details.ContainsKey(key))
                    {
                        details[key] = value;
                    }
                }
            }
            finally
            {
                ReleaseComObject(item);
                ReleaseComObject(folder);
                ReleaseComObject(shell);
            }

            return details;
        }

        private static string FindDetail(Dictionary<string, string> details, string[] names)
        {
            if (details == null || names == null)
            {
                return "";
            }

            foreach (string name in names)
            {
                foreach (KeyValuePair<string, string> pair in details)
                {
                    if (pair.Key.IndexOf(name, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    {
                        return pair.Value;
                    }
                }
            }

            return "";
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
