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

            // 最后防线日志（阶段8d）：仅观察不吞并——默认崩溃行为不变，
            // 但任何未处理异常都会在 app.log 留下记录。
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                AppLog.Write("crash", "未处理异常", e.ExceptionObject as Exception);
            };
            AppLog.Write("app", "启动 " + AppInfo.Version);
            AppIcon.InitializeForApplication();

            // TLS 1.2：全进程启用一次（更新检查/下载共用；替代原先散布在
            // UpdateManager 两处的 (SecurityProtocolType)3072 魔数写法）。
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;

            // 后台清扫上次异常退出遗留的抽帧临时目录与更新临时文件（不阻塞启动）。
            ThreadPool.QueueUserWorkItem(delegate
            {
                VideoFrameStripProvider.SweepOrphanedStripDirs();
                UpdateManager.SweepOrphanedUpdateTemps();
            });

            bool darkMode = UiTheme.DetectWindowsDarkMode();
            if (!DisclaimerGate.EnsureAccepted(null, darkMode))
            {
                return;
            }

            LicenseInfo licenseInfo;
            if (!LicenseGate.EnsureLicensed(null, out licenseInfo))
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
            if (UpdatePrompter.CanAutoInstallUpdate())
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    UpdateInfo info = null;
                    try
                    {
                        info = UpdateManager.GetLatestUpdateInfo();
                    }
                    catch (Exception ex)
                    {
                        // 静默容忍（离线属正常场景），但留痕以便诊断"永远收不到更新"。
                        AppLog.Write("update", "启动更新检查失败", ex);
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
            if (foundUpdate != null && UpdatePrompter.PromptAndInstallUpdate(foundUpdate, null))
            {
                return;
            }

            // 阶段12b：上次自动更新失败的标记（替换脚本在 Copy 失败时写入）
            // → 提示一次原因并删除，用户不再面对"接受了更新却什么都没发生"。
            try
            {
                string marker = UpdateManager.UpdateFailureMarkerPath;
                if (File.Exists(marker))
                {
                    string reason = "";
                    try
                    {
                        reason = File.ReadAllText(marker).Trim();
                    }
                    catch
                    {
                    }
                    try
                    {
                        File.Delete(marker);
                    }
                    catch
                    {
                    }
                    AppLog.Write("update", "检测到上次更新失败标记：" + reason);
                    MessageBox.Show(
                        "上次自动更新未能完成：\r\n" + reason + "\r\n\r\n如软件安装在 Program Files，请以管理员身份运行一次后重试更新，或重新下载安装。",
                        "更新未完成",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch
            {
            }

            Application.Run(new MaterialRenamerForm(licenseInfo));
        }
    }
}
