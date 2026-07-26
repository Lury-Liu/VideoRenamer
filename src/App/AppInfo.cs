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

namespace VideoRenamer
{
    public static class AppInfo
    {
        public const string Name = "VideoRenamer";
        public const string Version = "V1.0.8.0";
        public const string Author = "@寒松";
        public const int DefaultRowCount = 1;
        public const string UpdateManifestUrl = "https://github.com/Lury-Liu/VideoRenamer/releases/latest/download/latest.json";
        public const string UpdateReleaseApiUrl = "https://api.github.com/repos/Lury-Liu/VideoRenamer/releases/latest";

        public static string AppDataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Name);
            }
        }
    }
}
