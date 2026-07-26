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
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Run();
        }

        public static void Run()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 后台清扫上次异常退出遗留的抽帧临时目录（不阻塞启动）。
            ThreadPool.QueueUserWorkItem(delegate
            {
                VideoFrameStripProvider.SweepOrphanedStripDirs();
            });

            bool darkMode = UiTheme.DetectWindowsDarkMode();
            if (!DisclaimerManager.EnsureAccepted(null, darkMode))
            {
                return;
            }

            LicenseInfo licenseInfo;
            if (!LicenseManager.EnsureLicensed(null, out licenseInfo))
            {
                return;
            }

            using (SplashForm splash = new SplashForm(licenseInfo, darkMode))
            {
                splash.ShowDialog();
            }

            if (UpdateManager.CheckForUpdatesOnStartup(null))
            {
                return;
            }

            Application.Run(new MaterialRenamerForm(licenseInfo));
        }
    }
}
