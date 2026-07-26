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

            // TLS 1.2：全进程启用一次（更新检查/下载共用；替代原先散布在
            // UpdateManager 两处的 (SecurityProtocolType)3072 魔数写法）。
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;

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

            // 更新检查移到后台线程，与固定 4 秒的启动画面并行——原实现在
            // 启动画面之后于 UI 线程同步联网，离线冷启动最长可再挂 ~24 秒
            //（6s 直链 + 2×9s API 串行超时）不见任何窗口。启动画面结束时
            // 若检查尚未返回（慢网），本次启动跳过提示，下次启动再提示。
            object updateSync = new object();
            UpdateInfo[] pendingUpdate = { null };
            bool[] updateCheckDone = { false };
            if (UpdateManager.CanAutoInstallUpdate())
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    UpdateInfo info = null;
                    try
                    {
                        info = UpdateManager.GetLatestUpdateInfo();
                    }
                    catch
                    {
                    }
                    lock (updateSync)
                    {
                        pendingUpdate[0] = info;
                        updateCheckDone[0] = true;
                    }
                });
            }

            using (SplashForm splash = new SplashForm(licenseInfo, darkMode))
            {
                splash.ShowDialog();
            }

            UpdateInfo foundUpdate = null;
            lock (updateSync)
            {
                if (updateCheckDone[0])
                {
                    foundUpdate = pendingUpdate[0];
                }
            }

            // 保留原语义：接受更新时安装脚本已就绪、旧进程立即退出，
            // 决不进入主窗体。
            if (foundUpdate != null && UpdateManager.PromptAndInstallUpdate(foundUpdate, null))
            {
                return;
            }

            Application.Run(new MaterialRenamerForm(licenseInfo));
        }
    }
}
