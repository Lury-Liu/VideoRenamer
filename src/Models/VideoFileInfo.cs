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
    public class VideoFileInfo
    {
        public string Path;
        public string FileName;
        public string SizeText;
        public string ResolutionText;
        public string ModifiedText;
        public bool Exists;

        public string ListSummary
        {
            get
            {
                if (!Exists)
                {
                    return "文件不存在";
                }

                List<string> parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(ResolutionText) && ResolutionText != "未知")
                {
                    parts.Add(ResolutionText);
                }
                if (!string.IsNullOrWhiteSpace(SizeText))
                {
                    parts.Add(SizeText);
                }

                return parts.Count == 0 ? "已读取" : string.Join(" | ", parts.ToArray());
            }
        }
    }
}
