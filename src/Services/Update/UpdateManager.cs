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
