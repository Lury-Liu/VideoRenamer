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
    public static class UpdateManager
    {
        private const string AppId = "VideoMaterialRenamer";

        public static UpdateInfo GetLatestUpdateInfo()
        {
            return FetchUpdateInfo();
        }

        public static bool IsRemoteVersionNewer(UpdateInfo info)
        {
            return info != null && IsNewerVersion(info.Version, AppInfo.Version);
        }

        public static string GetUpdateDisplayVersion(UpdateInfo info)
        {
            return GetDisplayVersion(info);
        }

        public static bool CanAutoInstallUpdate()
        {
            return IsRunningPackagedExecutable();
        }

        public static bool CheckForUpdatesOnStartup(IWin32Window owner)
        {
            if (!IsRunningPackagedExecutable())
            {
                return false;
            }

            try
            {
                UpdateInfo info = FetchUpdateInfo();
                if (info == null || !IsNewerVersion(info.Version, AppInfo.Version))
                {
                    return false;
                }

                string message =
                    "检测到新版本 " + GetDisplayVersion(info) + "，当前版本 " + AppInfo.Version + "。\r\n\r\n" +
                    "是否立即下载并更新？\r\n\r\n" +
                    "选择“否”后，本次不会更新，下次启动仍会提示。";
                if (!string.IsNullOrWhiteSpace(info.Notes))
                {
                    message += "\r\n\r\n更新说明：\r\n" + info.Notes;
                }

                DialogResult result = MessageBox.Show(owner, message, "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result != DialogResult.Yes)
                {
                    return false;
                }

                return DownloadAndRestart(info, owner);
            }
            catch
            {
                return false;
            }
        }

        public static bool CheckForUpdatesManually(IWin32Window owner)
        {
            try
            {
                UpdateInfo info = FetchUpdateInfo();
                if (info == null)
                {
                    MessageBox.Show(owner, "未能从 GitHub 获取有效的版本信息。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }

                if (!IsNewerVersion(info.Version, AppInfo.Version))
                {
                    MessageBox.Show(
                        owner,
                        "当前已是最新版本。\r\n\r\n当前版本：" + AppInfo.Version + "\r\nGitHub 版本：" + GetDisplayVersion(info),
                        "检查更新",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return false;
                }

                string message =
                    "检测到新版本 " + GetDisplayVersion(info) + "，当前版本 " + AppInfo.Version + "。\r\n\r\n" +
                    "是否立即下载并更新？";
                if (!string.IsNullOrWhiteSpace(info.Notes))
                {
                    message += "\r\n\r\n更新说明：\r\n" + info.Notes;
                }

                DialogResult result = MessageBox.Show(owner, message, "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result != DialogResult.Yes)
                {
                    return false;
                }

                if (!IsRunningPackagedExecutable())
                {
                    MessageBox.Show(owner, "当前不是正式 EXE 运行状态，无法自动替换更新。", "无法更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                return DownloadAndRestart(info, owner);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "检查更新失败：\r\n" + ex.Message, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        public static void RunSelfTest()
        {
            string json = "{\"appId\":\"VideoMaterialRenamer\",\"version\":\"1.0.5.99\",\"displayVersion\":\"V1.0.5.99\",\"downloadUrl\":\"https://example.com/app.exe\",\"sha256\":\"ABCDEF\",\"fileName\":\"视频素材镜头表命名工具.exe\",\"notes\":\"测试更新\"}";
            UpdateInfo info = ParseManifest(json);
            if (info == null || info.Version != "1.0.5.99" || info.DownloadUrl != "https://example.com/app.exe" || info.Notes != "测试更新")
            {
                throw new Exception("更新清单解析测试失败。");
            }

            if (!IsNewerVersion("1.0.5.99", "V1.0.5.26") || IsNewerVersion("1.0.5.1", "V1.0.5.26"))
            {
                throw new Exception("更新版本比较测试失败。");
            }

            string apiJson = "{\"assets\":[{\"name\":\"VideoRenamer-v1.0.5.99.exe\",\"url\":\"https://api.github.com/repos/Lury-Liu/VideoRenamer/releases/assets/1\"},{\"name\":\"latest.json\",\"url\":\"https://api.github.com/repos/Lury-Liu/VideoRenamer/releases/assets/2\"}]}";
            string manifestAssetUrl = GetReleaseAssetApiUrl(apiJson, "latest.json");
            if (manifestAssetUrl != "https://api.github.com/repos/Lury-Liu/VideoRenamer/releases/assets/2")
            {
                throw new Exception("GitHub Release 资产解析测试失败：" + manifestAssetUrl);
            }
        }

        private static bool IsRunningPackagedExecutable()
        {
            try
            {
                string exePath = Application.ExecutablePath;
                string name = Path.GetFileNameWithoutExtension(exePath) ?? "";
                return File.Exists(exePath) &&
                    Path.GetExtension(exePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                    name.IndexOf("视频素材镜头表命名工具", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static UpdateInfo FetchUpdateInfo()
        {
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
            Exception directException = null;

            try
            {
                UpdateInfo info = FetchUpdateInfoFromDirectManifest();
                if (info != null)
                {
                    return info;
                }
            }
            catch (Exception ex)
            {
                directException = ex;
            }

            try
            {
                return FetchUpdateInfoFromGitHubApi();
            }
            catch
            {
                if (directException != null)
                {
                    throw directException;
                }
                throw;
            }
        }

        private static UpdateInfo FetchUpdateInfoFromGitHubApi()
        {
            if (string.IsNullOrWhiteSpace(AppInfo.UpdateReleaseApiUrl))
            {
                return null;
            }

            using (TimeoutWebClient client = CreateUpdateWebClient(9000))
            {
                string releaseJson = client.DownloadString(AppInfo.UpdateReleaseApiUrl + "?t=" + DateTime.UtcNow.Ticks.ToString());
                string assetUrl = GetReleaseAssetApiUrl(releaseJson, "latest.json");
                if (string.IsNullOrWhiteSpace(assetUrl))
                {
                    return null;
                }

                client.Headers[HttpRequestHeader.Accept] = "application/octet-stream";
                string manifestJson = client.DownloadString(assetUrl);
                return ParseManifest(manifestJson);
            }
        }

        private static UpdateInfo FetchUpdateInfoFromDirectManifest()
        {
            if (string.IsNullOrWhiteSpace(AppInfo.UpdateManifestUrl))
            {
                return null;
            }

            using (TimeoutWebClient client = new TimeoutWebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "VideoMaterialRenamer/" + AppInfo.Version;
                client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                string json = client.DownloadString(AppInfo.UpdateManifestUrl + "?t=" + DateTime.UtcNow.Ticks.ToString());
                return ParseManifest(json);
            }
        }

        private static TimeoutWebClient CreateUpdateWebClient(int timeoutMilliseconds)
        {
            TimeoutWebClient client = new TimeoutWebClient();
            client.TimeoutMilliseconds = timeoutMilliseconds;
            client.Headers[HttpRequestHeader.UserAgent] = "VideoMaterialRenamer/" + AppInfo.Version;
            client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        private static TimeoutWebClient CreateDownloadWebClient(int timeoutMilliseconds)
        {
            TimeoutWebClient client = new TimeoutWebClient();
            client.TimeoutMilliseconds = timeoutMilliseconds;
            client.Headers[HttpRequestHeader.UserAgent] = "VideoMaterialRenamer/" + AppInfo.Version;
            client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            client.Headers[HttpRequestHeader.Accept] = "application/octet-stream";
            return client;
        }

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

        private static string GetReleaseAssetApiUrl(string releaseJson, string assetName)
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

        private static UpdateInfo ParseManifest(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            string appId = GetJsonString(json, "appId");
            if (!string.IsNullOrWhiteSpace(appId) && !StringComparer.OrdinalIgnoreCase.Equals(appId, AppId))
            {
                return null;
            }

            UpdateInfo info = new UpdateInfo();
            info.Version = GetJsonString(json, "version");
            info.DisplayVersion = GetJsonString(json, "displayVersion");
            info.DownloadUrl = GetJsonString(json, "downloadUrl");
            info.Sha256 = GetJsonString(json, "sha256");
            info.FileName = GetJsonString(json, "fileName");
            info.Notes = GetJsonString(json, "notes");
            return string.IsNullOrWhiteSpace(info.Version) ? null : info;
        }

        private static string GetJsonString(string json, string name)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase);
            return match.Success ? UnescapeJsonString(match.Groups["value"].Value) : "";
        }

        private static string UnescapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            return Regex.Replace(value, @"\\(u[0-9a-fA-F]{4}|[""\\/bfnrt])", delegate(Match match)
            {
                string token = match.Groups[1].Value;
                if (token.Length == 5 && token[0] == 'u')
                {
                    int code = Convert.ToInt32(token.Substring(1), 16);
                    return ((char)code).ToString();
                }

                switch (token)
                {
                    case "\"":
                        return "\"";
                    case "\\":
                        return "\\";
                    case "/":
                        return "/";
                    case "b":
                        return "\b";
                    case "f":
                        return "\f";
                    case "n":
                        return "\n";
                    case "r":
                        return "\r";
                    case "t":
                        return "\t";
                    default:
                        return token;
                }
            });
        }

        private static bool IsNewerVersion(string remoteVersionText, string currentVersionText)
        {
            Version remoteVersion;
            Version currentVersion;
            if (!TryParseVersion(remoteVersionText, out remoteVersion) || !TryParseVersion(currentVersionText, out currentVersion))
            {
                return false;
            }

            return remoteVersion.CompareTo(currentVersion) > 0;
        }

        private static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            string normalized = NormalizeVersionText(text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            try
            {
                version = new Version(normalized);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeVersionText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            Match match = Regex.Match(text, @"\d+(?:\.\d+){1,3}");
            return match.Success ? match.Value : "";
        }

        private static string GetDisplayVersion(UpdateInfo info)
        {
            if (info == null)
            {
                return "";
            }

            return string.IsNullOrWhiteSpace(info.DisplayVersion) ? "V" + info.Version : info.DisplayVersion;
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
