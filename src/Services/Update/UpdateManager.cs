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

        // 启动更新提示：清单已由后台线程取回（与启动画面并行，见 Program.Run），
        // 这里只做提示与安装。提示文案与原 CheckForUpdatesOnStartup 逐字一致。
        // 原同步的 CheckForUpdatesOnStartup 与从未被调用的 CheckForUpdatesManually
        //（约 50 行死代码，与 AboutForm 内自有流程重复）一并移除。
        public static bool PromptAndInstallUpdate(UpdateInfo info, IWin32Window owner)
        {
            try
            {
                if (info == null || !IsNewerVersion(info.Version, AppInfo.Version) || !IsRunningPackagedExecutable())
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
            catch (Exception ex)
            {
                AppLog.Write("update", "更新提示/安装流程异常", ex);
                return false;
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
            // TLS 1.2 已在 Program.Run 启动时全进程启用一次（原先此处与
            // 下载分部各自用 (SecurityProtocolType)3072 魔数改全局状态）。
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

        internal static UpdateInfo ParseManifest(string json)
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

        internal static bool IsNewerVersion(string remoteVersionText, string currentVersionText)
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
    }
}
