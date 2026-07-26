using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace VideoRenamer
{
    public static partial class UpdateManager
    {

        // 阶段12a：下载编排的 UI 壳（进度窗/退出进程）在 UpdatePrompter；
        // 这里保留可复用的纯下载/校验/脚本机件（internal 供同程序集调用）。
        internal static void DownloadUpdateFile(UpdateInfo info, string downloadPath, Action<string, int, long, long> progress, Func<bool> cancelRequested)
        {
            Exception directException = null;
            try
            {
                DownloadFileWithProgress(info.DownloadUrl, downloadPath, 30000, false, progress, "正在下载更新文件...", cancelRequested);
                return;
            }
            catch (Exception ex)
            {
                directException = ex;
                try
                {
                    if (File.Exists(downloadPath))
                    {
                        File.Delete(downloadPath);
                    }
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(info.FileName))
            {
                throw directException;
            }

            try
            {
                ReportUpdateProgress(progress, "直链下载失败，正在切换备用下载方式...", -1, 0, 0);
                DownloadReleaseAssetByName(info.FileName, downloadPath, 30000, progress, cancelRequested);
            }
            catch (Exception ex)
            {
                throw new IOException("直链下载失败，GitHub API 下载也失败：" + ex.Message, ex);
            }
        }

        private static void DownloadReleaseAssetByName(string assetName, string outputPath, int timeoutMilliseconds, Action<string, int, long, long> progress, Func<bool> cancelRequested)
        {
            if (string.IsNullOrWhiteSpace(AppInfo.UpdateReleaseApiUrl))
            {
                throw new IOException("没有配置 GitHub Release API 地址。");
            }

            string assetUrl;
            using (TimeoutWebClient client = CreateUpdateWebClient(timeoutMilliseconds))
            {
                ReportUpdateProgress(progress, "正在获取备用下载地址...", -1, 0, 0);
                string releaseJson = client.DownloadString(AppInfo.UpdateReleaseApiUrl + "?t=" + DateTime.UtcNow.Ticks.ToString());
                assetUrl = GetReleaseAssetApiUrl(releaseJson, assetName);
                if (string.IsNullOrWhiteSpace(assetUrl))
                {
                    throw new IOException("找不到更新文件资产：" + assetName);
                }
            }

            DownloadFileWithProgress(
                assetUrl,
                outputPath,
                timeoutMilliseconds,
                true,
                progress,
                "正在通过备用方式下载更新文件...",
                cancelRequested);
        }

        // 下载停滞判定阈值：超过该时长无任何进度事件即视为连接停滞并中止。
        // （HttpWebRequest.Timeout 对 DownloadFileAsync 无效——原实现
        // waitHandle.WaitOne() 无限期阻塞，停滞的连接把用户永远困在
        // ControlBox=false 的模态框里。）
        private const int DownloadStallTimeoutMilliseconds = 60000;

        private static void DownloadFileWithProgress(
            string url,
            string outputPath,
            int timeoutMilliseconds,
            bool githubAssetApi,
            Action<string, int, long, long> progress,
            string status,
            Func<bool> cancelRequested)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new IOException("更新下载地址为空。");
            }

            using (TimeoutWebClient client = githubAssetApi ? CreateUpdateWebClient(timeoutMilliseconds) : CreateDownloadWebClient(timeoutMilliseconds))
            using (ManualResetEvent waitHandle = new ManualResetEvent(false))
            {
                if (githubAssetApi)
                {
                    client.Headers[HttpRequestHeader.Accept] = "application/octet-stream";
                }

                Exception failure = null;
                bool cancelled = false;
                long[] lastActivityTicks = { DateTime.UtcNow.Ticks };
                int[] lastReportedPercent = { -2 };
                long[] lastReportTicks = { 0 };

                client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
                {
                    lastActivityTicks[0] = DateTime.UtcNow.Ticks;
                    int percent = e.TotalBytesToReceive > 0 ? e.ProgressPercentage : -1;

                    // 节流：百分比变化或距上次上报超过 100ms 才上报
                    //（WebClient 对 100MB 文件每秒可触发数百次进度事件，
                    // 每次都跨线程投递会淹没 UI 线程）。
                    long nowTicks = DateTime.UtcNow.Ticks;
                    if (percent == lastReportedPercent[0] && (nowTicks - lastReportTicks[0]) < TimeSpan.TicksPerMillisecond * 100)
                    {
                        return;
                    }
                    lastReportedPercent[0] = percent;
                    lastReportTicks[0] = nowTicks;
                    ReportUpdateProgress(progress, status, percent, e.BytesReceived, e.TotalBytesToReceive);
                };
                client.DownloadFileCompleted += delegate(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
                {
                    cancelled = e.Cancelled;
                    failure = e.Error;
                    waitHandle.Set();
                };

                ReportUpdateProgress(progress, status, -1, 0, 0);
                client.DownloadFileAsync(new Uri(url), outputPath);

                bool userCancelled = false;
                bool stalled = false;
                while (!waitHandle.WaitOne(500))
                {
                    if (cancelRequested != null && cancelRequested())
                    {
                        userCancelled = true;
                        client.CancelAsync();
                        waitHandle.WaitOne(10000);
                        break;
                    }

                    long idleTicks = DateTime.UtcNow.Ticks - lastActivityTicks[0];
                    if (idleTicks > TimeSpan.TicksPerMillisecond * DownloadStallTimeoutMilliseconds)
                    {
                        stalled = true;
                        client.CancelAsync();
                        waitHandle.WaitOne(10000);
                        break;
                    }
                }

                if (stalled)
                {
                    throw new IOException("更新下载超时（60 秒无数据），请稍后重试。");
                }

                if (userCancelled || cancelled)
                {
                    throw new IOException("更新下载已取消。");
                }

                if (failure != null)
                {
                    throw failure;
                }
            }
        }

        internal static void ReportUpdateProgress(Action<string, int, long, long> progress, string status, int percent, long bytesReceived, long totalBytes)
        {
            if (progress == null)
            {
                return;
            }

            try
            {
                progress(status, percent, bytesReceived, totalBytes);
            }
            catch
            {
            }
        }

        // 阶段11d：清扫更新临时目录的孤儿文件（下载失败/辅助脚本半途而废
        // 时会留下 ~100MB 的 update_*.exe，此前永不清理）。1 小时年龄门槛
        // 避免误删正在进行的下载。
        public static void SweepOrphanedUpdateTemps()
        {
            try
            {
                string updateDir = Path.Combine(Path.GetTempPath(), AppInfo.Name + "_Update");
                if (!Directory.Exists(updateDir))
                {
                    return;
                }

                DateTime cutoffUtc = DateTime.UtcNow.AddHours(-1);
                foreach (string file in Directory.GetFiles(updateDir))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("update", "清扫更新临时目录失败", ex);
            }
        }

        // 阶段12b（标注：行为加固）：替换脚本从"一路 Stop 直行"改为
        // try/catch/finally——Copy-Item 失败（典型：装在 Program Files 且
        // 未提权）不再让用户"窗口关了、什么都没发生"：写失败标记（下次
        // 启动提示原因）、重启旧版本，下载文件必清。脚本文本可测。
        public static string UpdateFailureMarkerPath
        {
            get { return Path.Combine(AppInfo.AppDataDirectory, "update-failed.txt"); }
        }

        internal static string BuildUpdaterScript(string currentExe, string downloadedExe, int processId, string failureMarkerPath)
        {
            return
                "$ErrorActionPreference = 'Stop'\r\n" +
                "$pidToWait = " + processId.ToString() + "\r\n" +
                "$source = " + QuotePowerShellString(downloadedExe) + "\r\n" +
                "$target = " + QuotePowerShellString(currentExe) + "\r\n" +
                "$marker = " + QuotePowerShellString(failureMarkerPath) + "\r\n" +
                "for ($i = 0; $i -lt 120; $i++) {\r\n" +
                "    if (-not (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)) { break }\r\n" +
                "    Start-Sleep -Milliseconds 500\r\n" +
                "}\r\n" +
                "try {\r\n" +
                "    Copy-Item -LiteralPath $source -Destination $target -Force\r\n" +
                "    Start-Process -FilePath $target\r\n" +
                "}\r\n" +
                "catch {\r\n" +
                "    try { Set-Content -LiteralPath $marker -Value $_.Exception.Message -Encoding UTF8 } catch {}\r\n" +
                "    try { Start-Process -FilePath $target } catch {}\r\n" +
                "}\r\n" +
                "finally {\r\n" +
                "    Remove-Item -LiteralPath $source -Force -ErrorAction SilentlyContinue\r\n" +
                "    Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue\r\n" +
                "}\r\n";
        }

        // 目录可写性探测：决定替换脚本是否需要提权（Program Files 场景）。
        internal static bool IsDirectoryWritable(string directory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return false;
                }

                string probe = Path.Combine(directory, ".vmr_write_probe_" + Guid.NewGuid().ToString("N"));
                using (FileStream stream = File.Create(probe))
                {
                }
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // 返回 false = 脚本未能启动（典型：用户在 UAC 拒绝提权）——调用方
        // 决不能在此情况下退出进程。
        internal static bool StartUpdaterProcess(string currentExe, string downloadedExe, bool elevated)
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), AppInfo.Name + "_Update", "apply_update_" + Guid.NewGuid().ToString("N") + ".ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));
            string script = BuildUpdaterScript(currentExe, downloadedExe, Process.GetCurrentProcess().Id, UpdateFailureMarkerPath);
            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powershell))
            {
                powershell = "powershell.exe";
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = powershell;
            startInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + ProcessArguments.Quote(scriptPath);
            if (elevated)
            {
                // 触发 UAC 必须 UseShellExecute + runas。
                startInfo.UseShellExecute = true;
                startInfo.Verb = "runas";
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            }
            else
            {
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
            }

            try
            {
                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Write("update", "启动更新脚本失败" + (elevated ? "（提权被拒绝？）" : ""), ex);
                try
                {
                    File.Delete(scriptPath);
                }
                catch
                {
                }
                return false;
            }
        }

        private static string QuotePowerShellString(string value)
        {
            return "'" + (value ?? "").Replace("'", "''") + "'";
        }

        internal static string GetReleaseAssetApiUrl(string releaseJson, string assetName)
        {
            if (string.IsNullOrWhiteSpace(releaseJson) || string.IsNullOrWhiteSpace(assetName))
            {
                return "";
            }

            MatchCollection matches = Regex.Matches(
                releaseJson,
                "\"url\"\\s*:\\s*\"(?<url>(?:\\\\.|[^\"])*)\"(?:(?!\\}\\s*,?\\s*\\{).)*?\"name\"\\s*:\\s*\"(?<name>(?:\\\\.|[^\"])*)\"|\"name\"\\s*:\\s*\"(?<name2>(?:\\\\.|[^\"])*)\"(?:(?!\\}\\s*,?\\s*\\{).)*?\"url\"\\s*:\\s*\"(?<url2>(?:\\\\.|[^\"])*)\"",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                string name = match.Groups["name"].Success ? match.Groups["name"].Value : match.Groups["name2"].Value;
                string url = match.Groups["url"].Success ? match.Groups["url"].Value : match.Groups["url2"].Value;
                name = UnescapeJsonString(name);
                url = UnescapeJsonString(url);
                if (StringComparer.OrdinalIgnoreCase.Equals(name, assetName) &&
                    url.IndexOf("/releases/assets/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return url;
                }
            }

            return "";
        }

        internal static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
