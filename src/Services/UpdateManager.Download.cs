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
    public static partial class UpdateManager
    {

        public static bool DownloadAndRestartWithProgress(UpdateInfo info, IWin32Window owner)
        {
            return DownloadAndRestart(info, owner);
        }

        private static bool DownloadAndRestart(UpdateInfo info, IWin32Window owner)
        {
            string error = "";
            bool started = false;
            using (UpdateDownloadProgressForm progressForm = new UpdateDownloadProgressForm(UiTheme.DetectWindowsDarkMode()))
            {
                progressForm.Shown += delegate
                {
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        string threadError;
                        bool threadStarted = TryDownloadAndRestart(info, delegate(string status, int percent, long bytesReceived, long totalBytes)
                        {
                            progressForm.UpdateProgress(status, percent, bytesReceived, totalBytes);
                        }, out threadError);

                        started = threadStarted;
                        if (!threadStarted)
                        {
                            error = threadError;
                            try
                            {
                                progressForm.BeginInvoke((MethodInvoker)delegate
                                {
                                    progressForm.Close();
                                });
                            }
                            catch
                            {
                            }
                        }
                    });
                };

                if (owner == null)
                {
                    progressForm.ShowDialog();
                }
                else
                {
                    progressForm.ShowDialog(owner);
                }
            }

            if (!started)
            {
                MessageBox.Show(owner, "更新失败：\r\n" + error, "无法更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return started;
        }

        public static bool TryDownloadAndRestart(UpdateInfo info, out string error)
        {
            return TryDownloadAndRestart(info, null, out error);
        }

        public static bool TryDownloadAndRestart(UpdateInfo info, Action<string, int, long, long> progress, out string error)
        {
            error = "";
            if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl))
            {
                error = "更新清单中没有下载地址。";
                return false;
            }

            string currentExe = Application.ExecutablePath;
            string updateDir = Path.Combine(Path.GetTempPath(), "VideoMaterialRenamer_Update");
            Directory.CreateDirectory(updateDir);
            string downloadPath = Path.Combine(updateDir, "update_" + Guid.NewGuid().ToString("N") + ".exe");

            try
            {
                ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
                ReportUpdateProgress(progress, "正在准备下载更新...", 0, 0, 0);
                DownloadUpdateFile(info, downloadPath, progress);

                if (!File.Exists(downloadPath) || new FileInfo(downloadPath).Length == 0)
                {
                    throw new IOException("下载后的更新文件为空。");
                }

                ReportUpdateProgress(progress, "正在校验更新文件...", 96, 0, 0);
                if (!string.IsNullOrWhiteSpace(info.Sha256))
                {
                    string actualHash = ComputeSha256(downloadPath);
                    if (!StringComparer.OrdinalIgnoreCase.Equals(actualHash, info.Sha256.Trim()))
                    {
                        throw new IOException("更新文件校验失败，已取消安装。");
                    }
                }

                ReportUpdateProgress(progress, "正在准备重启软件...", 100, 0, 0);
                StartUpdaterProcess(currentExe, downloadPath);
                Application.Exit();
                Environment.Exit(0);
                return true;
            }
            catch (Exception ex)
            {
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

                error = ex.Message;
                return false;
            }
        }

        private static void DownloadUpdateFile(UpdateInfo info, string downloadPath, Action<string, int, long, long> progress)
        {
            Exception directException = null;
            try
            {
                DownloadFileWithProgress(info.DownloadUrl, downloadPath, 30000, false, progress, "正在下载更新文件...");
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
                DownloadReleaseAssetByName(info.FileName, downloadPath, 30000, progress);
            }
            catch (Exception ex)
            {
                throw new IOException("直链下载失败，GitHub API 下载也失败：" + ex.Message, ex);
            }
        }

        private static void DownloadReleaseAssetByName(string assetName, string outputPath, int timeoutMilliseconds, Action<string, int, long, long> progress)
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
                "正在通过备用方式下载更新文件...");
        }

        private static void DownloadFileWithProgress(
            string url,
            string outputPath,
            int timeoutMilliseconds,
            bool githubAssetApi,
            Action<string, int, long, long> progress,
            string status)
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
                client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
                {
                    int percent = e.TotalBytesToReceive > 0 ? e.ProgressPercentage : -1;
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
                waitHandle.WaitOne();

                if (cancelled)
                {
                    throw new IOException("更新下载已取消。");
                }

                if (failure != null)
                {
                    throw failure;
                }
            }
        }

        private static void ReportUpdateProgress(Action<string, int, long, long> progress, string status, int percent, long bytesReceived, long totalBytes)
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

        private static void StartUpdaterProcess(string currentExe, string downloadedExe)
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), "VideoMaterialRenamer_Update", "apply_update_" + Guid.NewGuid().ToString("N") + ".ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));
            string script =
                "$ErrorActionPreference = 'Stop'\r\n" +
                "$pidToWait = " + Process.GetCurrentProcess().Id.ToString() + "\r\n" +
                "$source = " + QuotePowerShellString(downloadedExe) + "\r\n" +
                "$target = " + QuotePowerShellString(currentExe) + "\r\n" +
                "for ($i = 0; $i -lt 120; $i++) {\r\n" +
                "    if (-not (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)) { break }\r\n" +
                "    Start-Sleep -Milliseconds 500\r\n" +
                "}\r\n" +
                "Copy-Item -LiteralPath $source -Destination $target -Force\r\n" +
                "Start-Process -FilePath $target\r\n" +
                "Remove-Item -LiteralPath $source -Force -ErrorAction SilentlyContinue\r\n" +
                "Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue\r\n";
            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powershell))
            {
                powershell = "powershell.exe";
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = powershell;
            startInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + QuoteArgument(scriptPath);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            Process.Start(startInfo);
        }

        private static string QuotePowerShellString(string value)
        {
            return "'" + (value ?? "").Replace("'", "''") + "'";
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
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

        private static string ComputeSha256(string path)
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
