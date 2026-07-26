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
    public static class LicenseManager
    {
        private const string StateVersion = "LicenseStateV2";
        private const int RenewalReminderDays = 3;
        private const int ClockRollbackGraceMinutes = 10;
        private const string ClockRollbackMessage = "检测到 Windows 时间倒退。请重新获取新的激活码后再输入，原密钥不能继续解锁。";

        private static readonly string LicenseDirectory = AppInfo.AppDataDirectory;
        private static readonly string LicensePath = Path.Combine(LicenseDirectory, "license.v2.dat");
        private static readonly string StatePath = Path.Combine(LicenseDirectory, "license.state.v2.dat");

        public static bool EnsureLicensed(IWin32Window owner)
        {
            LicenseInfo ignored;
            return EnsureLicensed(owner, out ignored);
        }

        public static bool EnsureLicensed(IWin32Window owner, out LicenseInfo activeInfo)
        {
            activeInfo = null;
            LicenseInfo info;
            string error;
            if (ValidateStoredLicense(out info, out error))
            {
                activeInfo = info;
                ShowRenewalReminder(owner, info);
                return true;
            }

            using (LicenseDialog dialog = new LicenseDialog(GetMachineCode(), error))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK)
                {
                    return false;
                }

                if (ValidateStoredLicense(out activeInfo, out error))
                {
                    ShowRenewalReminder(owner, activeInfo);
                    return true;
                }

                MessageBox.Show(owner, error, "授权状态异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        public static int GetRemainingDays(LicenseInfo info)
        {
            if (info == null)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Ceiling((info.ExpiresUtc - DateTime.UtcNow).TotalDays));
        }

        private static void ShowRenewalReminder(IWin32Window owner, LicenseInfo info)
        {
            double daysLeftExact = (info.ExpiresUtc - DateTime.UtcNow).TotalDays;
            if (daysLeftExact <= RenewalReminderDays)
            {
                int daysLeft = GetRemainingDays(info);
                MessageBox.Show(
                    owner,
                    "授权将在 " + daysLeft + " 天后到期，请及时获取新的密钥续期。",
                    "授权即将到期",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        public static string GetMachineCode()
        {
            string raw = "";
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    object value = key == null ? null : key.GetValue("MachineGuid");
                    if (value != null)
                    {
                        raw = value.ToString();
                    }
                }
            }
            catch
            {
                raw = "";
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = Environment.MachineName + "|" + Environment.UserDomainName;
            }

            byte[] hash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(raw));
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < 10; i++)
            {
                builder.Append(hash[i].ToString("X2"));
            }
            return builder.ToString();
        }

        public static bool TryActivate(string key, out string message)
        {
            LicenseInfo info;
            if (!ValidateLicenseKey(key, out info, out message))
            {
                return false;
            }

            try
            {
                string trimmedKey = key.Trim();
                if (IsClockRollbackForKey(trimmedKey))
                {
                    message = ClockRollbackMessage;
                    return false;
                }

                Directory.CreateDirectory(LicenseDirectory);
                WriteProtectedText(LicensePath, trimmedKey, "license-key");
                SaveState(trimmedKey, DateTime.UtcNow);
                message = "授权成功，有效期至 " + info.ExpiresUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "。";
                return true;
            }
            catch (Exception ex)
            {
                message = "保存授权失败：" + ex.Message;
                return false;
            }
        }

        private static bool ValidateStoredLicense(out LicenseInfo info, out string error)
        {
            info = null;
            error = "";
            if (!File.Exists(LicensePath))
            {
                error = "未检测到授权密钥。";
                return false;
            }

            string key;
            try
            {
                key = ReadProtectedText(LicensePath, "license-key").Trim();
            }
            catch (Exception ex)
            {
                error = "读取授权失败：" + ex.Message;
                return false;
            }

            if (!ValidateLicenseKey(key, out info, out error))
            {
                return false;
            }

            DateTime now = DateTime.UtcNow;
            DateTime lastSeenUtc;
            if (TryLoadState(key, out lastSeenUtc))
            {
                if (now.AddMinutes(ClockRollbackGraceMinutes) < lastSeenUtc)
                {
                    error = ClockRollbackMessage;
                    return false;
                }
            }

            if (now > info.ExpiresUtc)
            {
                // 有效期由密钥自带（签发端 -Days 决定，默认 30 天）——文案
                // 不再写死天数，避免签发策略变化时文案失真。
                error = "授权已到期，请获取新的激活密钥续期。";
                return false;
            }

            SaveState(key, now > lastSeenUtc ? now : lastSeenUtc);
            return true;
        }

        private static bool IsClockRollbackForKey(string key)
        {
            DateTime lastSeenUtc;
            return TryLoadState(key, out lastSeenUtc) &&
                DateTime.UtcNow.AddMinutes(ClockRollbackGraceMinutes) < lastSeenUtc;
        }

        // 验证逻辑已抽为纯函数 LicenseValidator（阶段8e）：本机机器码与
        // 真实时钟仅在这里注入。
        private static bool ValidateLicenseKey(string key, out LicenseInfo info, out string error)
        {
            return LicenseValidator.Validate(key, GetMachineCode(), DateTime.UtcNow, out info, out error);
        }

        private static void SaveState(string key, DateTime lastSeenUtc)
        {
            Directory.CreateDirectory(LicenseDirectory);
            string keyHash = Base64Url.To(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(key)));
            string payload = StateVersion + "|" + GetMachineCode() + "|" + keyHash + "|" + lastSeenUtc.Ticks.ToString();
            WriteProtectedText(StatePath, payload, "license-state");
        }

        private static bool TryLoadState(string key, out DateTime lastSeenUtc)
        {
            lastSeenUtc = DateTime.MinValue;
            if (!File.Exists(StatePath))
            {
                return false;
            }

            try
            {
                string text = ReadProtectedText(StatePath, "license-state").Trim();
                string[] parts = text.Split('|');
                if (parts.Length != 4)
                {
                    return false;
                }

                string keyHash = Base64Url.To(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(key)));
                if (!StringComparer.Ordinal.Equals(parts[0], StateVersion) ||
                    !StringComparer.Ordinal.Equals(parts[1], GetMachineCode()) ||
                    !StringComparer.Ordinal.Equals(parts[2], keyHash))
                {
                    return false;
                }

                long ticks;
                if (!long.TryParse(parts[3], out ticks))
                {
                    return false;
                }

                lastSeenUtc = new DateTime(ticks, DateTimeKind.Utc);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteProtectedText(string path, string text, string purpose)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            byte[] clearBytes = Encoding.UTF8.GetBytes(text ?? "");
            byte[] protectedBytes = ProtectedData.Protect(clearBytes, GetDpapiEntropy(purpose), DataProtectionScope.CurrentUser);
            File.WriteAllText(path, Base64Url.To(protectedBytes), Encoding.UTF8);
        }

        private static string ReadProtectedText(string path, string purpose)
        {
            byte[] protectedBytes = Base64Url.From(File.ReadAllText(path, Encoding.UTF8).Trim());
            byte[] clearBytes = ProtectedData.Unprotect(protectedBytes, GetDpapiEntropy(purpose), DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clearBytes);
        }

        private static byte[] GetDpapiEntropy(string purpose)
        {
            return SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes("VideoMaterialRenamer|" + purpose + "|" + GetMachineCode()));
        }
    }
}
