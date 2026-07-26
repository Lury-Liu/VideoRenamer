using System;
using System.Net;
using System.Text.RegularExpressions;

namespace VideoRenamer
{
    public static partial class UpdateManager
    {
        private const string AppId = AppInfo.Name;

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

        // 阶段12a：提示/进度窗/退出进程等 UI 侧编排移至 App/Presenters/
        // UpdatePrompter（含原 CanAutoInstallUpdate 的 EXE 名检查——它读
        // Application.ExecutablePath，属 WinForms）。本类只剩纯逻辑：
        // 清单获取/解析、版本比较、下载/校验、替换脚本生成、临时清扫。
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
                client.Headers[HttpRequestHeader.UserAgent] = AppInfo.Name + "/" + AppInfo.Version;
                client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                string json = client.DownloadString(AppInfo.UpdateManifestUrl + "?t=" + DateTime.UtcNow.Ticks.ToString());
                return ParseManifest(json);
            }
        }

        private static TimeoutWebClient CreateUpdateWebClient(int timeoutMilliseconds)
        {
            TimeoutWebClient client = new TimeoutWebClient();
            client.TimeoutMilliseconds = timeoutMilliseconds;
            client.Headers[HttpRequestHeader.UserAgent] = AppInfo.Name + "/" + AppInfo.Version;
            client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        private static TimeoutWebClient CreateDownloadWebClient(int timeoutMilliseconds)
        {
            TimeoutWebClient client = new TimeoutWebClient();
            client.TimeoutMilliseconds = timeoutMilliseconds;
            client.Headers[HttpRequestHeader.UserAgent] = AppInfo.Name + "/" + AppInfo.Version;
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
            if (!StringComparer.Ordinal.Equals(appId, AppId))
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
            return string.IsNullOrWhiteSpace(info.Version) || !IsValidSha256(info.Sha256) ? null : info;
        }

        internal static bool IsValidSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char ch = value[index];
                bool isHex = (ch >= '0' && ch <= '9')
                    || (ch >= 'a' && ch <= 'f')
                    || (ch >= 'A' && ch <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
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
