param(
    [switch]$SelfTest,
    [switch]$SmokeTest
)

$ErrorActionPreference = "Stop"
if ($PSScriptRoot) {
    Set-Location -LiteralPath $PSScriptRoot
}

$source = @"
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

[assembly: AssemblyTitle("视频素材镜头表命名工具")]
[assembly: AssemblyProduct("视频素材镜头表命名工具")]
[assembly: AssemblyCompany("@寒松")]
[assembly: AssemblyVersion("1.0.5.34")]
[assembly: AssemblyFileVersion("1.0.5.34")]

namespace VideoMaterialRenamer
{
    public static class AppInfo
    {
        public const string Version = "V1.0.5.34";
        public const string Author = "@寒松";
        public const int DefaultRowCount = 1;
        public const string UpdateManifestUrl = "https://github.com/Lury-Liu/VideoRenamer/releases/latest/download/latest.json";
        public const string UpdateReleaseApiUrl = "https://api.github.com/repos/Lury-Liu/VideoRenamer/releases/latest";

        public static string AppDataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VideoMaterialRenamer");
            }
        }
    }

    public class ShotRow
    {
        public int Scene;
        public int Sequence;
        public List<string> MainFiles = new List<string>();
        public List<string> BackupFiles = new List<string>();
        public List<string> MainTailOverrides = new List<string>();
        public List<string> BackupTailOverrides = new List<string>();
        public int ProgressPercent;
    }

    public class RenamePlan
    {
        public ShotRow Row;
        public int RowIndex;
        public string ColumnName;
        public bool IsMain;
        public int FileIndex;
        public int Scene;
        public int Shot;
        public int Take;
        public string TailSegment;
        public string CustomTailText;
        public bool HasCustomTail;
        public string OldPath;
        public string TargetPath;
        public string OldName;
        public string NewName;
        public string Status;
    }

    public class RenameOperation
    {
        public ShotRow Row;
        public int RowIndex;
        public bool IsMain;
        public int FileIndex;
        public string OriginalPath;
        public string RenamedPath;
    }

    public enum ExportOutputMode
    {
        OverwriteOriginal,
        SaveAsNewFile
    }

    public class VideoFileInfo
    {
        public string Path;
        public string FileName;
        public string SizeText;
        public string ResolutionText;
        public string ModifiedText;
        public bool Exists;

        public string ListSummary
        {
            get
            {
                if (!Exists)
                {
                    return "文件不存在";
                }

                List<string> parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(ResolutionText) && ResolutionText != "未知")
                {
                    parts.Add(ResolutionText);
                }
                if (!string.IsNullOrWhiteSpace(SizeText))
                {
                    parts.Add(SizeText);
                }

                return parts.Count == 0 ? "已读取" : string.Join(" | ", parts.ToArray());
            }
        }
    }

    public class NaturalPathComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            return StringComparer.CurrentCultureIgnoreCase.Compare(ToNaturalKey(Path.GetFileName(x)), ToNaturalKey(Path.GetFileName(y)));
        }

        private static string ToNaturalKey(string value)
        {
            if (value == null)
            {
                return "";
            }

            return Regex.Replace(value, "\\d+", delegate(Match match)
            {
                return match.Value.PadLeft(12, '0');
            });
        }
    }

    public class DataGridViewProgressColumn : DataGridViewColumn
    {
        public DataGridViewProgressColumn()
            : base(new DataGridViewProgressCell())
        {
            ReadOnly = true;
        }
    }

    public class DataGridViewProgressCell : DataGridViewTextBoxCell
    {
        protected override void Paint(
            Graphics graphics,
            Rectangle clipBounds,
            Rectangle cellBounds,
            int rowIndex,
            DataGridViewElementStates cellState,
            object value,
            object formattedValue,
            string errorText,
            DataGridViewCellStyle cellStyle,
            DataGridViewAdvancedBorderStyle advancedBorderStyle,
            DataGridViewPaintParts paintParts)
        {
            int progress = 0;
            if (value != null)
            {
                int.TryParse(value.ToString(), out progress);
            }
            progress = Math.Max(0, Math.Min(100, progress));

            base.Paint(
                graphics,
                clipBounds,
                cellBounds,
                rowIndex,
                cellState,
                value,
                formattedValue,
                errorText,
                cellStyle,
                advancedBorderStyle,
                paintParts & ~DataGridViewPaintParts.ContentForeground);

            bool selected = (cellState & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected;
            Color textColor = selected ? cellStyle.SelectionForeColor : cellStyle.ForeColor;
            Color trackColor = ControlPaint.Light(selected ? cellStyle.SelectionBackColor : cellStyle.BackColor);
            Color fillColor = progress >= 100 ? Color.FromArgb(43, 150, 92) : Color.FromArgb(35, 120, 210);

            Rectangle bar = new Rectangle(cellBounds.X + 8, cellBounds.Y + 12, Math.Max(4, cellBounds.Width - 16), Math.Max(8, cellBounds.Height - 24));
            using (Brush track = new SolidBrush(trackColor))
            {
                graphics.FillRectangle(track, bar);
            }

            int fillWidth = (int)Math.Round(bar.Width * (progress / 100.0));
            if (fillWidth > 0)
            {
                using (Brush fill = new SolidBrush(fillColor))
                {
                    graphics.FillRectangle(fill, new Rectangle(bar.X, bar.Y, fillWidth, bar.Height));
                }
            }

            using (Pen border = new Pen(ControlPaint.Dark(trackColor)))
            {
                graphics.DrawRectangle(border, bar);
            }

            string text = progress + "%";
            TextRenderer.DrawText(
                graphics,
                text,
                cellStyle.Font,
                cellBounds,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    public static class VideoMetadataReader
    {
        public static VideoFileInfo Read(string path)
        {
            VideoFileInfo info = new VideoFileInfo();
            info.Path = path ?? "";
            info.FileName = string.IsNullOrWhiteSpace(path) ? "" : System.IO.Path.GetFileName(path);
            info.SizeText = "";
            info.ResolutionText = "未知";
            info.ModifiedText = "";
            info.Exists = File.Exists(path);

            if (!info.Exists)
            {
                return info;
            }

            try
            {
                FileInfo file = new FileInfo(path);
                info.SizeText = FormatBytes(file.Length);
                info.ModifiedText = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
            }

            try
            {
                Dictionary<string, string> details = ReadShellDetails(path);
                string width = NormalizeDimension(FindDetail(details, new string[] { "帧宽度", "宽度", "Frame width", "Width" }));
                string height = NormalizeDimension(FindDetail(details, new string[] { "帧高度", "高度", "Frame height", "Height" }));
                if (!string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height))
                {
                    info.ResolutionText = width + " x " + height;
                }
            }
            catch
            {
            }

            return info;
        }

        public static string FormatBytes(long bytes)
        {
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? value.ToString("0") + " " + units[unit] : value.ToString("0.##") + " " + units[unit];
        }

        private static Dictionary<string, string> ReadShellDetails(string path)
        {
            Dictionary<string, string> details = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
            object shell = null;
            object folder = null;
            object item = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    return details;
                }

                shell = Activator.CreateInstance(shellType);
                string directory = System.IO.Path.GetDirectoryName(path);
                string fileName = System.IO.Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
                {
                    return details;
                }

                folder = shellType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { directory });
                if (folder == null)
                {
                    return details;
                }

                item = folder.GetType().InvokeMember("ParseName", BindingFlags.InvokeMethod, null, folder, new object[] { fileName });
                if (item == null)
                {
                    return details;
                }

                for (int i = 0; i < 340; i++)
                {
                    string key = CleanShellText(Convert.ToString(folder.GetType().InvokeMember("GetDetailsOf", BindingFlags.InvokeMethod, null, folder, new object[] { null, i })));
                    string value = CleanShellText(Convert.ToString(folder.GetType().InvokeMember("GetDetailsOf", BindingFlags.InvokeMethod, null, folder, new object[] { item, i })));
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value) && !details.ContainsKey(key))
                    {
                        details[key] = value;
                    }
                }
            }
            finally
            {
                ReleaseComObject(item);
                ReleaseComObject(folder);
                ReleaseComObject(shell);
            }

            return details;
        }

        private static string FindDetail(Dictionary<string, string> details, string[] names)
        {
            if (details == null || names == null)
            {
                return "";
            }

            foreach (string name in names)
            {
                foreach (KeyValuePair<string, string> pair in details)
                {
                    if (pair.Key.IndexOf(name, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    {
                        return pair.Value;
                    }
                }
            }

            return "";
        }

        private static string NormalizeDimension(string value)
        {
            value = CleanShellText(value);
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            Match match = Regex.Match(value.Replace(",", ""), "\\d+");
            return match.Success ? match.Value : value;
        }

        private static string CleanShellText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return value
                .Replace("\u200e", "")
                .Replace("\u200f", "")
                .Replace("\u202a", "")
                .Replace("\u202c", "")
                .Trim();
        }

        private static void ReleaseComObject(object value)
        {
            try
            {
                if (value != null && Marshal.IsComObject(value))
                {
                    Marshal.FinalReleaseComObject(value);
                }
            }
            catch
            {
            }
        }
    }

    public static class VideoThumbnailProvider
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [Flags]
        private enum SIIGBF
        {
            ResizeToFit = 0x00000000,
            BiggerSizeOk = 0x00000001,
            MemoryOnly = 0x00000002,
            IconOnly = 0x00000004,
            ThumbnailOnly = 0x00000008,
            InCacheOnly = 0x00000010,
            CropToSquare = 0x00000020,
            WideThumbnails = 0x00000040,
            IconBackground = 0x00000080,
            ScaleUp = 0x00000100
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public static Image GetThumbnail(string path, Size size)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            Image shellImage = TryGetShellThumbnail(path, size);
            if (shellImage != null)
            {
                return shellImage;
            }

            return TryGetAssociatedIcon(path, size);
        }

        private static Image TryGetShellThumbnail(string path, Size size)
        {
            IShellItemImageFactory factory = null;
            IntPtr bitmapHandle = IntPtr.Zero;
            try
            {
                Guid factoryId = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref factoryId, out factory);
                SIZE shellSize = new SIZE { cx = Math.Max(64, size.Width), cy = Math.Max(64, size.Height) };
                int result = factory.GetImage(shellSize, SIIGBF.ThumbnailOnly | SIIGBF.BiggerSizeOk | SIIGBF.ResizeToFit, out bitmapHandle);
                if (result != 0 || bitmapHandle == IntPtr.Zero)
                {
                    return null;
                }

                using (Bitmap bitmap = Image.FromHbitmap(bitmapHandle))
                {
                    return new Bitmap(bitmap);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (bitmapHandle != IntPtr.Zero)
                {
                    DeleteObject(bitmapHandle);
                }
                if (factory != null && Marshal.IsComObject(factory))
                {
                    Marshal.FinalReleaseComObject(factory);
                }
            }
        }

        private static Image TryGetAssociatedIcon(string path, Size size)
        {
            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null)
                    {
                        return null;
                    }

                    Bitmap bitmap = new Bitmap(Math.Max(64, size.Width), Math.Max(64, size.Height));
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.Transparent);
                        int iconSize = Math.Min(64, Math.Min(bitmap.Width, bitmap.Height) - 16);
                        Rectangle target = new Rectangle((bitmap.Width - iconSize) / 2, (bitmap.Height - iconSize) / 2, iconSize, iconSize);
                        graphics.DrawIcon(icon, target);
                    }
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }
    }

    public class LicenseInfo
    {
        public string MachineCode;
        public DateTime ExpiresUtc;
        public string Nonce;
    }

    public static class LicenseManager
    {
        private const string KeyPrefix = "VMR2";
        private const string PayloadVersion = "RSA-SHA256";
        private const string StateVersion = "LicenseStateV2";
        private const string PublicKeyXml = @"<RSAKeyValue><Modulus>wulgLKdZu8gG3znaPiWEoPD6VoMAyW7yMM3BqEStw/ajSwba89/IlUK+aTiILfzvwnCTCz5lnA9OzBGFpjwvUjl5GquNxKE44ff2a+0eu+FPbu04JzM/ArbM8Amk+KcYRUTXUY7H8dGkHKbJOrPsu3qFGksOd6cy6qpREl6tkL8P7d1YvA01ptz3dK2Ya3ch5qxqaiSXbCL5OllFH/P3GXOJzUixPWd2ulEHJZZO5kJSt8SkS8BG8XMmVbFj28VeU6xWKOJS8F9ZLmi0nS5VDptwihGIqWLDSuLzglXs8Lt6Jdbji6pkmm7Dr5NAelWiF8ibelOenEX0OEJ7xlsl2Q==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
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
                error = "授权已到期，请获取新的 14 天密钥。";
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

        private static bool ValidateLicenseKey(string key, out LicenseInfo info, out string error)
        {
            info = null;
            error = "";
            if (string.IsNullOrWhiteSpace(key))
            {
                error = "密钥为空。";
                return false;
            }

            string[] parts = key.Trim().Split('.');
            if (parts.Length != 3 || parts[0] != KeyPrefix)
            {
                error = "密钥格式不正确。";
                return false;
            }

            byte[] payloadBytes;
            byte[] signatureBytes;
            try
            {
                payloadBytes = FromBase64Url(parts[1]);
                signatureBytes = FromBase64Url(parts[2]);
            }
            catch
            {
                error = "密钥编码不正确。";
                return false;
            }

            if (!VerifySignature(payloadBytes, signatureBytes))
            {
                error = "密钥签名无效。";
                return false;
            }

            string payload = Encoding.UTF8.GetString(payloadBytes);
            string[] fields = payload.Split('|');
            if (fields.Length != 4 || fields[3] != PayloadVersion)
            {
                error = "密钥内容不完整。";
                return false;
            }

            long ticks;
            if (!long.TryParse(fields[1], out ticks))
            {
                error = "密钥日期无效。";
                return false;
            }

            DateTime expiresUtc = new DateTime(ticks, DateTimeKind.Utc);
            string currentMachine = GetMachineCode();
            if (!StringComparer.OrdinalIgnoreCase.Equals(fields[0], currentMachine))
            {
                error = "密钥不属于本机。请把本机机器码发给授权方重新生成密钥。";
                return false;
            }

            if (DateTime.UtcNow > expiresUtc)
            {
                error = "密钥已过期。";
                return false;
            }

            info = new LicenseInfo
            {
                MachineCode = fields[0],
                ExpiresUtc = expiresUtc,
                Nonce = fields[2]
            };
            return true;
        }

        private static void SaveState(string key, DateTime lastSeenUtc)
        {
            Directory.CreateDirectory(LicenseDirectory);
            string keyHash = ToBase64Url(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(key)));
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

                string keyHash = ToBase64Url(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(key)));
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

        private static bool VerifySignature(byte[] payloadBytes, byte[] signatureBytes)
        {
            using (RSACryptoServiceProvider rsa = CreateRsaProvider())
            {
                rsa.FromXmlString(PublicKeyXml);
                return rsa.VerifyData(payloadBytes, CryptoConfig.MapNameToOID("SHA256"), signatureBytes);
            }
        }

        private static RSACryptoServiceProvider CreateRsaProvider()
        {
            CspParameters parameters = new CspParameters();
            parameters.ProviderType = 24;
            parameters.ProviderName = "Microsoft Enhanced RSA and AES Cryptographic Provider";
            return new RSACryptoServiceProvider(parameters);
        }

        private static void WriteProtectedText(string path, string text, string purpose)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            byte[] clearBytes = Encoding.UTF8.GetBytes(text ?? "");
            byte[] protectedBytes = ProtectedData.Protect(clearBytes, GetDpapiEntropy(purpose), DataProtectionScope.CurrentUser);
            File.WriteAllText(path, ToBase64Url(protectedBytes), Encoding.UTF8);
        }

        private static string ReadProtectedText(string path, string purpose)
        {
            byte[] protectedBytes = FromBase64Url(File.ReadAllText(path, Encoding.UTF8).Trim());
            byte[] clearBytes = ProtectedData.Unprotect(protectedBytes, GetDpapiEntropy(purpose), DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clearBytes);
        }

        private static byte[] GetDpapiEntropy(string purpose)
        {
            return SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes("VideoMaterialRenamer|" + purpose + "|" + GetMachineCode()));
        }

        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] FromBase64Url(string value)
        {
            string base64 = value.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
            }
            return Convert.FromBase64String(base64);
        }
    }

    public static class UiTheme
    {
        public static bool DetectWindowsDarkMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value != null)
                    {
                        return Convert.ToInt32(value) == 0;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public static Color WindowBack(bool dark)
        {
            return dark ? Color.FromArgb(24, 26, 31) : Color.White;
        }

        public static Color PanelBack(bool dark)
        {
            return dark ? Color.FromArgb(32, 35, 42) : Color.FromArgb(246, 247, 249);
        }

        public static Color ControlBack(bool dark)
        {
            return dark ? Color.FromArgb(40, 44, 52) : Color.White;
        }

        public static Color HeaderBack(bool dark)
        {
            return dark ? Color.FromArgb(48, 53, 63) : Color.FromArgb(235, 239, 245);
        }

        public static Color TextColor(bool dark)
        {
            return dark ? Color.FromArgb(232, 236, 244) : Color.FromArgb(24, 31, 42);
        }

        public static Color MutedText(bool dark)
        {
            return dark ? Color.FromArgb(166, 174, 188) : Color.FromArgb(95, 105, 120);
        }

        public static Color BorderColor(bool dark)
        {
            return dark ? Color.FromArgb(78, 86, 100) : Color.FromArgb(206, 214, 224);
        }

        public static Color SelectionBack(bool dark)
        {
            return dark ? Color.FromArgb(60, 116, 220) : Color.FromArgb(35, 94, 190);
        }

        public static Color DropTargetBack(bool dark)
        {
            return dark ? Color.FromArgb(86, 72, 30) : Color.FromArgb(255, 242, 157);
        }

        public static Color DropTargetFore(bool dark)
        {
            return dark ? Color.White : Color.FromArgb(38, 32, 14);
        }

        public static Color PreviewAltBack(bool dark)
        {
            return dark ? Color.FromArgb(30, 40, 54) : Color.FromArgb(246, 250, 255);
        }

        public static Color PreviewNeutralBack(bool dark)
        {
            return dark ? Color.FromArgb(45, 48, 55) : Color.FromArgb(245, 245, 245);
        }

        public static Color PreviewWarningBack(bool dark)
        {
            return dark ? Color.FromArgb(92, 75, 28) : Color.FromArgb(255, 249, 196);
        }

        public static Color PreviewErrorBack(bool dark)
        {
            return dark ? Color.FromArgb(92, 42, 50) : Color.FromArgb(255, 235, 238);
        }

        public static Color ErrorText(bool dark)
        {
            return dark ? Color.FromArgb(255, 154, 154) : Color.FromArgb(150, 60, 50);
        }

        public static void ApplyForm(Form form, bool dark)
        {
            if (form == null)
            {
                return;
            }

            form.BackColor = WindowBack(dark);
            form.ForeColor = TextColor(dark);
            foreach (Control control in form.Controls)
            {
                ApplyControl(control, dark);
            }
        }

        public static void ApplyControl(Control control, bool dark)
        {
            if (control == null)
            {
                return;
            }

            string role = control.Tag as string;
            bool muted = StringComparer.OrdinalIgnoreCase.Equals(role, "Muted");
            bool error = StringComparer.OrdinalIgnoreCase.Equals(role, "Error");
            bool primary = StringComparer.OrdinalIgnoreCase.Equals(role, "Primary");

            if (control is ToolStrip)
            {
                ApplyToolStrip((ToolStrip)control, dark);
            }
            else if (control is Button)
            {
                Button button = (Button)control;
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = primary ? SelectionBack(dark) : BorderColor(dark);
                button.BackColor = primary ? SelectionBack(dark) : ControlBack(dark);
                button.ForeColor = primary ? Color.White : TextColor(dark);
            }
            else if (control is TextBoxBase || control is NumericUpDown)
            {
                control.BackColor = ControlBack(dark);
                control.ForeColor = TextColor(dark);
            }
            else if (control is CheckBox)
            {
                ApplyCheckBox((CheckBox)control, dark);
            }
            else if (control is Label)
            {
                control.BackColor = ParentBack(control, dark);
                control.ForeColor = error ? ErrorText(dark) : (muted ? MutedText(dark) : TextColor(dark));
            }
            else if (control is DataGridView)
            {
                ApplyGrid((DataGridView)control, dark);
            }
            else if (control is ListView)
            {
                ListView listView = (ListView)control;
                listView.BackColor = ControlBack(dark);
                listView.ForeColor = TextColor(dark);
            }
            else if (control is Panel || control is FlowLayoutPanel || control is SplitterPanel || control is SplitContainer)
            {
                control.BackColor = PanelBack(dark);
                control.ForeColor = TextColor(dark);
            }
            else
            {
                control.BackColor = ParentBack(control, dark);
                control.ForeColor = TextColor(dark);
            }

            foreach (Control child in control.Controls)
            {
                ApplyControl(child, dark);
            }
        }

        private static void ApplyToolStrip(ToolStrip toolStrip, bool dark)
        {
            toolStrip.BackColor = PanelBack(dark);
            toolStrip.ForeColor = TextColor(dark);
            foreach (ToolStripItem item in toolStrip.Items)
            {
                ApplyToolStripItem(item, dark);
            }
        }

        private static void ApplyToolStripItem(ToolStripItem item, bool dark)
        {
            if (item == null)
            {
                return;
            }

            item.BackColor = PanelBack(dark);
            item.ForeColor = TextColor(dark);
            ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
            if (dropDown == null)
            {
                return;
            }

            dropDown.DropDown.BackColor = PanelBack(dark);
            dropDown.DropDown.ForeColor = TextColor(dark);
            foreach (ToolStripItem child in dropDown.DropDownItems)
            {
                ApplyToolStripItem(child, dark);
            }
        }

        private static void ApplyCheckBox(CheckBox checkBox, bool dark)
        {
            checkBox.UseVisualStyleBackColor = false;
            checkBox.FlatStyle = FlatStyle.Flat;
            checkBox.FlatAppearance.BorderColor = BorderColor(dark);
            checkBox.FlatAppearance.CheckedBackColor = dark ? Color.FromArgb(58, 88, 144) : Color.FromArgb(222, 235, 255);
            checkBox.FlatAppearance.MouseOverBackColor = dark ? Color.FromArgb(48, 53, 63) : Color.FromArgb(238, 243, 250);
            checkBox.FlatAppearance.MouseDownBackColor = dark ? Color.FromArgb(58, 64, 76) : Color.FromArgb(226, 235, 247);
            checkBox.BackColor = ParentBack(checkBox, dark);
            checkBox.ForeColor = TextColor(dark);
        }

        public static void ApplyGrid(DataGridView grid, bool dark)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.BackgroundColor = WindowBack(dark);
            grid.GridColor = BorderColor(dark);
            grid.DefaultCellStyle.BackColor = ControlBack(dark);
            grid.DefaultCellStyle.ForeColor = TextColor(dark);
            grid.DefaultCellStyle.SelectionBackColor = SelectionBack(dark);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.AlternatingRowsDefaultCellStyle.BackColor = dark ? Color.FromArgb(36, 41, 50) : Color.FromArgb(250, 252, 255);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = TextColor(dark);
            grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderBack(dark);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextColor(dark);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderBack(dark);
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextColor(dark);
            grid.RowHeadersDefaultCellStyle.BackColor = HeaderBack(dark);
            grid.RowHeadersDefaultCellStyle.ForeColor = MutedText(dark);
            grid.RowHeadersDefaultCellStyle.SelectionBackColor = HeaderBack(dark);
            grid.RowHeadersDefaultCellStyle.SelectionForeColor = TextColor(dark);
            grid.RowsDefaultCellStyle.BackColor = ControlBack(dark);
            grid.RowsDefaultCellStyle.ForeColor = TextColor(dark);
        }

        private static Color ParentBack(Control control, bool dark)
        {
            return control.Parent == null ? WindowBack(dark) : control.Parent.BackColor;
        }
    }

    public static class AppIcon
    {
        private static Icon cachedIcon;

        public static Icon Get()
        {
            if (cachedIcon != null)
            {
                return cachedIcon;
            }

            foreach (string path in GetCandidatePaths())
            {
                try
                {
                    if (File.Exists(path))
                    {
                        cachedIcon = new Icon(path);
                        return cachedIcon;
                    }
                }
                catch
                {
                }
            }

            try
            {
                string executablePath = Application.ExecutablePath;
                if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
                {
                    cachedIcon = Icon.ExtractAssociatedIcon(executablePath);
                }
            }
            catch
            {
            }

            return cachedIcon;
        }

        public static void Apply(Form form)
        {
            Icon icon = Get();
            if (form != null && icon != null)
            {
                form.Icon = icon;
            }
        }

        private static IEnumerable<string> GetCandidatePaths()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentDir = Environment.CurrentDirectory;
            yield return Path.Combine(baseDir, "assets", "app.ico");
            yield return Path.Combine(baseDir, "app.ico");
            yield return Path.Combine(currentDir, "assets", "app.ico");
            yield return Path.Combine(currentDir, "app.ico");
        }
    }

    public class SplashForm : Form
    {
        private readonly System.Windows.Forms.Timer closeTimer;

        public SplashForm(LicenseInfo licenseInfo, bool darkMode)
        {
            Text = "启动";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(560, 292);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9f);
            AppIcon.Apply(this);

            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Padding = new Padding(30, 26, 30, 22);
            Controls.Add(panel);

            PictureBox mark = new PictureBox();
            mark.Location = new Point(32, 34);
            mark.Size = new Size(64, 64);
            mark.SizeMode = PictureBoxSizeMode.Zoom;
            Icon icon = AppIcon.Get();
            if (icon != null)
            {
                mark.Image = icon.ToBitmap();
            }
            panel.Controls.Add(mark);

            Label title = new Label();
            title.Text = "视频素材镜头表命名工具";
            title.Font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold);
            title.AutoSize = false;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Location = new Point(116, 30);
            title.Size = new Size(398, 42);
            panel.Controls.Add(title);

            Label version = new Label();
            version.Text = "版本 " + AppInfo.Version;
            version.Tag = "Muted";
            version.AutoSize = false;
            version.TextAlign = ContentAlignment.MiddleLeft;
            version.Location = new Point(118, 76);
            version.Size = new Size(396, 24);
            panel.Controls.Add(version);

            Panel divider = new Panel();
            divider.Location = new Point(32, 122);
            divider.Size = new Size(482, 1);
            panel.Controls.Add(divider);

            Label author = new Label();
            author.Text = "制作人：" + AppInfo.Author;
            author.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            author.AutoSize = false;
            author.TextAlign = ContentAlignment.MiddleLeft;
            author.Location = new Point(34, 142);
            author.Size = new Size(480, 30);
            panel.Controls.Add(author);

            Label days = new Label();
            days.Text = "剩余激活天数：" + LicenseManager.GetRemainingDays(licenseInfo) + " 天";
            days.AutoSize = false;
            days.TextAlign = ContentAlignment.MiddleLeft;
            days.Location = new Point(34, 176);
            days.Size = new Size(480, 28);
            panel.Controls.Add(days);

            Label hint = new Label();
            hint.Text = "正在启动，窗口将在 4 秒后自动关闭";
            hint.Tag = "Muted";
            hint.AutoSize = false;
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Location = new Point(34, 210);
            hint.Size = new Size(480, 28);
            panel.Controls.Add(hint);

            Panel line = new Panel();
            line.Dock = DockStyle.Bottom;
            line.Height = 4;
            line.BackColor = UiTheme.SelectionBack(darkMode);
            panel.Controls.Add(line);

            closeTimer = new System.Windows.Forms.Timer();
            closeTimer.Interval = 4000;
            closeTimer.Tick += delegate
            {
                closeTimer.Stop();
                Close();
            };
            Shown += delegate { closeTimer.Start(); };
            Click += delegate { Close(); };
            panel.Click += delegate { Close(); };

            UiTheme.ApplyForm(this, darkMode);
            divider.BackColor = UiTheme.BorderColor(darkMode);
            line.BackColor = UiTheme.SelectionBack(darkMode);
            mark.BackColor = UiTheme.PanelBack(darkMode);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closeTimer.Stop();
            closeTimer.Dispose();
            base.OnFormClosed(e);
        }
    }

    public static class DisclaimerManager
    {
        private const string AcceptanceVersion = "DisclaimerAcceptedV1";
        private static readonly string AcceptancePath = Path.Combine(AppInfo.AppDataDirectory, "disclaimer.accepted");

        public static bool EnsureAccepted(IWin32Window owner, bool darkMode)
        {
            if (IsAccepted())
            {
                return true;
            }

            using (DisclaimerDialog dialog = new DisclaimerDialog(darkMode))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK)
                {
                    return false;
                }
            }

            try
            {
                Directory.CreateDirectory(AppInfo.AppDataDirectory);
                string text = AcceptanceVersion + "|" + DateTime.UtcNow.Ticks.ToString() + "|" + AppInfo.Version;
                File.WriteAllText(AcceptancePath, text, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "无法保存免责协议确认记录，本次仍会继续运行。\r\n\r\n" + ex.Message, "保存确认记录失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return true;
        }

        private static bool IsAccepted()
        {
            try
            {
                if (!File.Exists(AcceptancePath))
                {
                    return false;
                }

                string text = File.ReadAllText(AcceptancePath, Encoding.UTF8).Trim();
                return text.StartsWith(AcceptanceVersion + "|", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }

    public class DisclaimerDialog : Form
    {
        private readonly CheckBox agreeCheck;
        private readonly CheckBox disagreeCheck;
        private readonly Button acceptButton;

        public DisclaimerDialog(bool darkMode)
        {
            Text = "免责协议";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(720, 560);
            MinimumSize = new Size(640, 500);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9f);
            AppIcon.Apply(this);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.Padding = new Padding(18, 16, 18, 16);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(layout);

            Label title = new Label();
            title.Text = "使用前请阅读并确认";
            title.Dock = DockStyle.Fill;
            title.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            layout.Controls.Add(title, 0, 0);

            TextBox body = new TextBox();
            body.Multiline = true;
            body.ReadOnly = true;
            body.ScrollBars = ScrollBars.Vertical;
            body.Dock = DockStyle.Fill;
            body.Text =
                "【重要提示】\r\n\r\n" +
                "本软件为个人兴趣开发的辅助工具，非商业软件，仅供您自愿试用。\r\n\r\n" +
                "使用前请您务必知悉：\r\n" +
                "1. 本软件涉及对文件的批量重命名操作，极大可能因代码缺陷、系统环境差异、个人操作不当等原因，导致文件重命名错误、文件丢失或数据损坏。\r\n" +
                "2. 您在使用本软件前，【必须自行备份所有重要文件】。因使用本软件造成的任何数据丢失或损坏，开发者不承担赔偿责任。\r\n" +
                "3. 建议您首先在【测试文件副本】上试用，确认功能正常后再用于正式素材。\r\n\r\n" +
                "【我已阅读并理解上述风险，自愿使用本软件】";
            layout.Controls.Add(body, 0, 1);

            FlowLayoutPanel choices = new FlowLayoutPanel();
            choices.Dock = DockStyle.Fill;
            choices.FlowDirection = FlowDirection.TopDown;
            choices.WrapContents = false;
            choices.Padding = new Padding(0, 8, 0, 0);
            layout.Controls.Add(choices, 0, 2);

            agreeCheck = new CheckBox();
            agreeCheck.Text = "我同意";
            agreeCheck.AutoSize = true;
            agreeCheck.Margin = new Padding(0, 0, 0, 8);
            agreeCheck.CheckedChanged += delegate
            {
                if (agreeCheck.Checked)
                {
                    disagreeCheck.Checked = false;
                }
                acceptButton.Enabled = agreeCheck.Checked;
            };
            choices.Controls.Add(agreeCheck);

            disagreeCheck = new CheckBox();
            disagreeCheck.Text = "不同意并退出";
            disagreeCheck.AutoSize = true;
            disagreeCheck.CheckedChanged += delegate
            {
                if (disagreeCheck.Checked)
                {
                    agreeCheck.Checked = false;
                    acceptButton.Enabled = false;
                }
            };
            choices.Controls.Add(disagreeCheck);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            layout.Controls.Add(buttons, 0, 3);

            Button exitButton = new Button();
            exitButton.Text = "不同意并退出";
            exitButton.Width = 120;
            exitButton.Height = 30;
            exitButton.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            buttons.Controls.Add(exitButton);

            acceptButton = new Button();
            acceptButton.Text = "接受并继续";
            acceptButton.Tag = "Primary";
            acceptButton.Width = 110;
            acceptButton.Height = 30;
            acceptButton.Enabled = false;
            acceptButton.Click += delegate
            {
                if (!agreeCheck.Checked)
                {
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            };
            buttons.Controls.Add(acceptButton);

            UiTheme.ApplyForm(this, darkMode);
        }
    }

    public class LicenseDialog : Form
    {
        private readonly TextBox keyBox;
        private readonly Label messageLabel;

        public LicenseDialog(string machineCode, string reason)
        {
            Text = "输入授权密钥";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(560, 330);
            MinimumSize = new Size(520, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9f);
            AppIcon.Apply(this);

            Label title = new Label();
            title.Text = "需要授权后才能使用";
            title.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(18, 16);
            Controls.Add(title);

            messageLabel = new Label();
            messageLabel.Text = string.IsNullOrWhiteSpace(reason) ? "请输入授权密钥。" : reason;
            messageLabel.Tag = "Error";
            messageLabel.AutoSize = false;
            messageLabel.Size = new Size(500, 36);
            messageLabel.Location = new Point(20, 48);
            Controls.Add(messageLabel);

            Label machineLabel = new Label();
            machineLabel.Text = "本机机器码";
            machineLabel.AutoSize = true;
            machineLabel.Location = new Point(20, 94);
            Controls.Add(machineLabel);

            TextBox machineBox = new TextBox();
            machineBox.Text = machineCode;
            machineBox.ReadOnly = true;
            machineBox.Location = new Point(110, 90);
            machineBox.Width = 290;
            Controls.Add(machineBox);

            Button copyButton = new Button();
            copyButton.Text = "复制机器码";
            copyButton.Location = new Point(412, 88);
            copyButton.Width = 100;
            copyButton.Click += delegate
            {
                Clipboard.SetText(machineCode);
                messageLabel.Text = "机器码已复制。";
            };
            Controls.Add(copyButton);

            Label keyLabel = new Label();
            keyLabel.Text = "授权密钥";
            keyLabel.AutoSize = true;
            keyLabel.Location = new Point(20, 136);
            Controls.Add(keyLabel);

            keyBox = new TextBox();
            keyBox.Multiline = true;
            keyBox.ScrollBars = ScrollBars.Vertical;
            keyBox.Location = new Point(110, 132);
            keyBox.Size = new Size(402, 82);
            Controls.Add(keyBox);

            Button unlockButton = new Button();
            unlockButton.Text = "解锁";
            unlockButton.Location = new Point(330, 232);
            unlockButton.Width = 86;
            unlockButton.Click += delegate
            {
                string message;
                if (LicenseManager.TryActivate(keyBox.Text, out message))
                {
                    MessageBox.Show(this, message, "授权成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    messageLabel.Text = message;
                }
            };
            Controls.Add(unlockButton);

            Button exitButton = new Button();
            exitButton.Text = "退出";
            exitButton.Location = new Point(426, 232);
            exitButton.Width = 86;
            exitButton.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(exitButton);

            UiTheme.ApplyForm(this, UiTheme.DetectWindowsDarkMode());
        }
    }

    public class MaterialRenamerForm : Form
    {
        private const int ThumbnailCacheLimit = 200;
        private const int PlanStatusCheckBatchSize = 50;
        private const int GridSceneColumn = 0;
        private const int GridShotColumn = 1;
        private const int GridMainColumn = 2;
        private const int GridBackupColumn = 3;
        private const int GridProgressColumn = 4;

        private static readonly HashSet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".wmv", ".flv",
            ".webm", ".mts", ".m2ts", ".3gp", ".mpeg", ".mpg"
        };

        private readonly List<ShotRow> rows = new List<ShotRow>();
        private readonly List<RenamePlan> currentPlan = new List<RenamePlan>();
        private readonly Stack<List<RenameOperation>> undoStack = new Stack<List<RenameOperation>>();
        private readonly Dictionary<string, VideoFileInfo> videoInfoCache = new Dictionary<string, VideoFileInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Image> thumbnailCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> thumbnailCacheOrder = new LinkedList<string>();
        private readonly Dictionary<string, LinkedListNode<string>> thumbnailCacheNodes = new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pendingVideoInfoLoads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pendingThumbnailLoads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly LicenseInfo activeLicenseInfo;
        private readonly string historyPath = Path.Combine(Environment.CurrentDirectory, "rename_history.tsv");

        private DataGridView grid;
        private ListView previewList;
        private PictureBox thumbnailBox;
        private Label detailTitleLabel;
        private Label detailInfoLabel;
        private Label detailPathLabel;
        private NumericUpDown numEpisode;
        private NumericUpDown numScene;
        private CheckBox chkKeepExtension;
        private CheckBox chkRowScene;
        private CheckBox chkExport1080p;
        private CheckBox chkExportWatermark;
        private CheckBox chkCustomTail;
        private TextBox txtCustomTail;
        private Label statusLabel;
        private Button btnRename;
        private Button btnTheme;
        private Button btnAbout;
        private System.Windows.Forms.Timer previewColumnResizeTimer;
        private Image ownedDetailImage;
        private int dragHighlightRow = -1;
        private int dragHighlightColumn = -1;
        private int detailLoadVersion;
        private int planCheckVersion;
        private string currentDetailPath = "";
        private string currentDetailNewName = "";
        private string currentDetailContext = "";
        private bool darkMode;
        private bool operationRunning;
        private bool rendering;
        private bool progressColumnVisible;
        private bool rowSceneModeInitialized;

        public MaterialRenamerForm()
            : this(null)
        {
        }

        public MaterialRenamerForm(LicenseInfo licenseInfo)
        {
            activeLicenseInfo = licenseInfo;
            darkMode = UiTheme.DetectWindowsDarkMode();
            for (int i = 1; i <= AppInfo.DefaultRowCount; i++)
            {
                rows.Add(new ShotRow { Scene = 1, Sequence = i });
            }

            BuildUi();
            ApplyTheme();
            RenderAll();
        }

        public static string RunSelfTest()
        {
            ShotRow row = new ShotRow { Sequence = 5 };
            row.MainFiles.Add(@"C:\Temp\main1.mp4");
            row.MainFiles.Add(@"C:\Temp\main2.mp4");
            row.MainFiles.Add(@"C:\Temp\main3.mp4");
            row.BackupFiles.Add(@"C:\Temp\backup1.mp4");
            row.BackupFiles.Add(@"C:\Temp\backup2.mp4");
            row.BackupFiles.Add(@"C:\Temp\backup3.mp4");

            List<RenamePlan> plan = BuildPlan(new List<ShotRow> { row }, 5, 1, true, false);
            string[] expected = new string[]
            {
                "E5-S1-5-T1.mp4",
                "E5-S1-5-T2.mp4",
                "E5-S1-5-T3.mp4",
                "E5-S1-5-T4.mp4",
                "E5-S1-5-T5.mp4",
                "E5-S1-5-T6.mp4"
            };

            string actual = string.Join("|", plan.Select(p => p.NewName).ToArray());
            string want = string.Join("|", expected);
            if (actual != want)
            {
                throw new Exception("同一行 B/C 连续编号测试失败：" + actual);
            }

            string watermarkedArgs = BuildFfmpegArguments(@"C:\Temp\input.mp4", @"C:\Temp\output.mp4", true, "E5-S1-1-T1.mp4");
            if (!watermarkedArgs.Contains("-vf") || !watermarkedArgs.Contains("drawtext=") || watermarkedArgs.Contains("-filter_complex") || watermarkedArgs.Contains("-loop"))
            {
                throw new Exception("文件名水印导出参数测试失败：" + watermarkedArgs);
            }

            string noWatermarkArgs = BuildFfmpegArguments(@"C:\Temp\input.mp4", @"C:\Temp\output.mp4", true, "");
            if (noWatermarkArgs.Contains("drawtext=") || noWatermarkArgs.Contains("未命名视频"))
            {
                throw new Exception("关闭文件名水印测试失败：" + noWatermarkArgs);
            }

            UpdateManager.RunSelfTest();

            ShotRow customRow = new ShotRow { Sequence = 17 };
            customRow.MainFiles.Add(@"C:\Temp\custom.mp4");
            List<RenamePlan> customPlan = BuildPlan(new List<ShotRow> { customRow }, 5, 1, true, false);
            if (customPlan.Count != 1 || customPlan[0].NewName != "E5-S1-17-T1.mp4")
            {
                throw new Exception("自定义镜号测试失败：" + (customPlan.Count == 0 ? "无预览" : customPlan[0].NewName));
            }

            ShotRow customSceneRow = new ShotRow { Scene = 3, Sequence = 7 };
            customSceneRow.MainFiles.Add(@"C:\Temp\custom_scene.mp4");
            List<RenamePlan> customScenePlan = BuildPlan(new List<ShotRow> { customSceneRow }, 5, 1, true, false, true);
            if (customScenePlan.Count != 1 || customScenePlan[0].NewName != "E5-S3-7-T1.mp4" || customScenePlan[0].Scene != 3)
            {
                throw new Exception("自定义场号测试失败：" + (customScenePlan.Count == 0 ? "无预览" : customScenePlan[0].NewName));
            }

            List<RenamePlan> defaultScenePlan = BuildPlan(new List<ShotRow> { customSceneRow }, 5, 1, true, false, false);
            if (defaultScenePlan.Count != 1 || defaultScenePlan[0].NewName != "E5-S1-7-T1.mp4")
            {
                throw new Exception("默认场号测试失败：" + (defaultScenePlan.Count == 0 ? "无预览" : defaultScenePlan[0].NewName));
            }

            ShotRow customTailRow = new ShotRow { Sequence = 1 };
            customTailRow.MainFiles.Add(@"C:\Temp\custom_tail.mp4");
            customTailRow.MainTailOverrides.Add("补+文字");
            List<RenamePlan> customTailPlan = BuildPlan(new List<ShotRow> { customTailRow }, 5, 1, true, false);
            if (customTailPlan.Count != 1 || customTailPlan[0].NewName != "E5-S1-1-补+文字.mp4" || customTailPlan[0].TailSegment != "补+文字")
            {
                throw new Exception("自定义末尾编号测试失败：" + (customTailPlan.Count == 0 ? "无预览" : customTailPlan[0].NewName));
            }

            ShotRow duplicateTailRow = new ShotRow { Sequence = 5 };
            duplicateTailRow.MainFiles.Add(@"C:\Temp\dup1.mp4");
            duplicateTailRow.MainFiles.Add(@"C:\Temp\dup2.mp4");
            duplicateTailRow.MainTailOverrides.Add("补手机");
            duplicateTailRow.MainTailOverrides.Add("");
            List<RenamePlan> duplicateTailPlan = BuildPlan(new List<ShotRow> { duplicateTailRow }, 5, 6, true, false);
            string uniqueTail = GetUniqueCustomTail(duplicateTailPlan[1], "补手机", duplicateTailPlan, 5, 6, true);
            if (uniqueTail != "补手机2")
            {
                throw new Exception("自定义末尾自动补号测试失败：" + uniqueTail);
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "VideoMaterialRenamerSelfTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string alreadyNamed = Path.Combine(tempDir, "E5-S1-17-T1.mp4");
                File.WriteAllText(alreadyNamed, "test");
                ShotRow exportRow = new ShotRow { Sequence = 17 };
                exportRow.MainFiles.Add(alreadyNamed);
                List<RenamePlan> exportPlan = BuildPlan(new List<ShotRow> { exportRow }, 5, 1, true, true);
                if (exportPlan.Count != 1 || exportPlan[0].Status != "待覆盖导出1080p")
                {
                    throw new Exception("覆盖导出预览测试失败：" + (exportPlan.Count == 0 ? "无预览" : exportPlan[0].Status));
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                }
            }

            return "SelfTest OK";
        }

        public static string RunSmokeTest()
        {
            using (MaterialRenamerForm form = new MaterialRenamerForm())
            {
                form.RenderAll();
                if (form.btnAbout == null || form.btnAbout.Text != "关于")
                {
                    throw new Exception("关于按钮初始化失败。");
                }
                if (form.chkExportWatermark == null || form.chkExportWatermark.Checked)
                {
                    throw new Exception("文件名水印默认状态测试失败。");
                }
                if (form.grid == null ||
                    form.grid.Columns[GridSceneColumn].DefaultCellStyle.ForeColor != form.GetSceneColumnTextColor() ||
                    form.grid.Columns[GridShotColumn].DefaultCellStyle.ForeColor != form.GetShotColumnTextColor())
                {
                    throw new Exception("场号/镜号列颜色测试失败。");
                }
                form.darkMode = true;
                form.ApplyTheme();
                if (form.chkExportWatermark.ForeColor != UiTheme.TextColor(true) ||
                    form.chkCustomTail.ForeColor != UiTheme.TextColor(true))
                {
                    throw new Exception("护眼模式默认复选框颜色测试失败。");
                }
                form.chkExport1080p.Checked = true;
                form.UpdateWatermarkOptionState();
                if (form.chkExportWatermark.ForeColor != UiTheme.TextColor(true))
                {
                    throw new Exception("护眼模式水印复选框颜色测试失败。");
                }
            }

            return "SmokeTest OK";
        }

        private void BuildUi()
        {
            Text = "视频素材镜头表命名工具";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1240, 820);
            MinimumSize = new Size(820, 680);
            Font = new Font("Microsoft YaHei UI", 9f);
            AppIcon.Apply(this);

            Panel headerHost = new Panel();
            headerHost.Dock = DockStyle.Top;
            headerHost.Height = 100;
            Controls.Add(headerHost);

            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Fill;
            topPanel.Padding = new Padding(14, 10, 14, 8);
            topPanel.BackColor = Color.FromArgb(246, 247, 249);
            headerHost.Controls.Add(topPanel);

            FlowLayoutPanel settingsPanel = new FlowLayoutPanel();
            settingsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            settingsPanel.FlowDirection = FlowDirection.LeftToRight;
            settingsPanel.WrapContents = false;
            settingsPanel.BackColor = Color.FromArgb(246, 247, 249);
            settingsPanel.Location = new Point(14, 10);
            settingsPanel.Size = new Size(1196, 34);
            topPanel.Controls.Add(settingsPanel);

            Label labelEpisode = new Label();
            labelEpisode.Text = "集数 E";
            labelEpisode.AutoSize = true;
            labelEpisode.Font = new Font(Font, FontStyle.Bold);
            labelEpisode.Margin = new Padding(4, 8, 4, 0);
            settingsPanel.Controls.Add(labelEpisode);

            numEpisode = new NumericUpDown();
            numEpisode.Minimum = 1;
            numEpisode.Maximum = 9999;
            numEpisode.Value = 5;
            numEpisode.Width = 78;
            numEpisode.Margin = new Padding(0, 4, 14, 0);
            numEpisode.ValueChanged += delegate { RefreshPreview(); };
            settingsPanel.Controls.Add(numEpisode);

            Label labelScene = new Label();
            labelScene.Text = "场号 S";
            labelScene.AutoSize = true;
            labelScene.Font = new Font(Font, FontStyle.Bold);
            labelScene.Margin = new Padding(4, 8, 4, 0);
            settingsPanel.Controls.Add(labelScene);

            numScene = new NumericUpDown();
            numScene.Minimum = 1;
            numScene.Maximum = 9999;
            numScene.Value = 1;
            numScene.Width = 78;
            numScene.Margin = new Padding(0, 4, 14, 0);
            numScene.ValueChanged += delegate { RefreshPreview(); };
            settingsPanel.Controls.Add(numScene);

            chkRowScene = new CheckBox();
            chkRowScene.Text = "逐行场号";
            chkRowScene.AutoSize = true;
            chkRowScene.Margin = new Padding(0, 7, 14, 0);
            chkRowScene.CheckedChanged += delegate
            {
                if (chkRowScene.Checked)
                {
                    InitializeRowScenesFromDefaultIfNeeded();
                }

                RenderGrid();
                RefreshPreview();
                UpdateSelectedCellDetails();
            };
            settingsPanel.Controls.Add(chkRowScene);

            chkKeepExtension = new CheckBox();
            chkKeepExtension.Text = "保留扩展名大小写";
            chkKeepExtension.Checked = true;
            chkKeepExtension.AutoSize = true;
            chkKeepExtension.Margin = new Padding(0, 7, 14, 0);
            chkKeepExtension.CheckedChanged += delegate { RefreshPreview(); };
            settingsPanel.Controls.Add(chkKeepExtension);

            chkExport1080p = new CheckBox();
            chkExport1080p.Text = "导出1080x1920";
            chkExport1080p.AutoSize = true;
            chkExport1080p.Margin = new Padding(0, 7, 14, 0);
            chkExport1080p.CheckedChanged += delegate
            {
                RefreshPreview();
                UpdateRenameButtonText();
                UpdateWatermarkOptionState();
            };
            settingsPanel.Controls.Add(chkExport1080p);

            chkExportWatermark = new CheckBox();
            chkExportWatermark.Text = "文件名水印";
            chkExportWatermark.Checked = false;
            chkExportWatermark.AutoSize = true;
            chkExportWatermark.Margin = new Padding(0, 7, 14, 0);
            chkExportWatermark.CheckedChanged += delegate
            {
                UpdateWatermarkOptionState();
                RefreshPreview();
            };
            settingsPanel.Controls.Add(chkExportWatermark);
            UpdateWatermarkOptionState();

            btnTheme = NewButton("", 112);
            btnTheme.Click += delegate { ToggleTheme(); };
            btnTheme.Margin = new Padding(0, 2, 6, 2);
            settingsPanel.Controls.Add(btnTheme);

            btnAbout = NewButton("关于", 58);
            btnAbout.Click += delegate { ShowAboutInfo(); };
            btnAbout.Margin = new Padding(0, 2, 0, 2);
            settingsPanel.Controls.Add(btnAbout);

            FlowLayoutPanel actionBar = new FlowLayoutPanel();
            actionBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            actionBar.BackColor = Color.FromArgb(246, 247, 249);
            actionBar.FlowDirection = FlowDirection.LeftToRight;
            actionBar.WrapContents = false;
            actionBar.Location = new Point(14, 48);
            actionBar.Size = new Size(1196, 36);
            actionBar.Padding = new Padding(0);
            actionBar.Margin = new Padding(0);
            topPanel.Controls.Add(actionBar);

            Button btnAddRow = NewButton("新增行", 72);
            btnAddRow.Click += delegate { AddEmptyRow(); };
            actionBar.Controls.Add(btnAddRow);

            Button btnMoveRowUp = NewButton("上移", 58);
            btnMoveRowUp.Click += delegate { MoveCurrentRow(-1); };
            actionBar.Controls.Add(btnMoveRowUp);

            Button btnMoveRowDown = NewButton("下移", 58);
            btnMoveRowDown.Click += delegate { MoveCurrentRow(1); };
            actionBar.Controls.Add(btnMoveRowDown);

            Button btnDeleteRow = NewButton("删除行", 72);
            btnDeleteRow.Click += delegate { DeleteCurrentRow(); };
            actionBar.Controls.Add(btnDeleteRow);
            actionBar.Controls.Add(NewActionSeparator());

            Button btnImportCell = NewButton("导入素材", 82);
            btnImportCell.Click += delegate { ImportSelectedCell(); };
            actionBar.Controls.Add(btnImportCell);

            Button btnDeleteRecord = NewButton("删除记录", 82);
            btnDeleteRecord.Click += delegate { DeleteSelectedPreviewRecord(); };
            actionBar.Controls.Add(btnDeleteRecord);

            Button btnClearCell = NewButton("清空格", 72);
            btnClearCell.Click += delegate { ClearSelectedCellFiles(); };
            actionBar.Controls.Add(btnClearCell);

            Button btnRemoveTail = NewButton("删除空尾行", 100);
            btnRemoveTail.Click += delegate { RemoveEmptyTailRows(); };
            actionBar.Controls.Add(btnRemoveTail);

            Button btnClearAll = NewButton("全局清空", 88);
            btnClearAll.Click += delegate { ClearAllMaterials(); };
            actionBar.Controls.Add(btnClearAll);
            actionBar.Controls.Add(NewActionSeparator());

            Button btnUndo = NewButton("取消命名", 85);
            btnUndo.Click += delegate { RestoreLastRename(); };
            actionBar.Controls.Add(btnUndo);

            btnRename = NewButton("执行重命名",100);
            btnRename.Tag = "Primary";
            btnRename.Click += delegate { RenameFiles(); };
            actionBar.Controls.Add(btnRename);

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.Height = 28;
            statusLabel.Padding = new Padding(12, 6, 12, 0);
            statusLabel.Tag = "Muted";
            statusLabel.Text = "把视频拖到表格 B「主要素材」或 C「备用素材」单元格。";
            Controls.Add(statusLabel);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            Controls.Add(split);
            split.BringToFront();

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowDrop = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.ColumnHeadersHeight = 34;
            grid.RowHeadersVisible = true;
            grid.RowTemplate.Height = 38;
            grid.RowTemplate.Resizable = DataGridViewTriState.False;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.MultiSelect = false;
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.DefaultCellStyle.Padding = new Padding(4);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            grid.CellEndEdit += OnGridCellEndEdit;
            grid.SelectionChanged += delegate { UpdateSelectedCellDetails(); };
            grid.DragEnter += OnGridDragEnterOrOver;
            grid.DragOver += OnGridDragEnterOrOver;
            grid.DragLeave += delegate { ClearDragHighlight(); };
            grid.DragDrop += OnGridDragDrop;

            DataGridViewTextBoxColumn colScene = new DataGridViewTextBoxColumn();
            colScene.HeaderText = "A 场号";
            colScene.Width = 72;
            colScene.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn colSeq = new DataGridViewTextBoxColumn();
            colSeq.HeaderText = "B 镜号";
            colSeq.Width = 82;
            colSeq.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn colMain = new DataGridViewTextBoxColumn();
            colMain.HeaderText = "C 主要素材";
            colMain.Width = 310;
            colMain.ReadOnly = true;
            colMain.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn colBackup = new DataGridViewTextBoxColumn();
            colBackup.HeaderText = "D 备用素材";
            colBackup.Width = 310;
            colBackup.ReadOnly = true;
            colBackup.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewProgressColumn colProgress = new DataGridViewProgressColumn();
            colProgress.HeaderText = "E 进度";
            colProgress.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colProgress.MinimumWidth = 130;
            colProgress.SortMode = DataGridViewColumnSortMode.NotSortable;
            colProgress.Visible = false;

            grid.Columns.Add(colScene);
            grid.Columns.Add(colSeq);
            grid.Columns.Add(colMain);
            grid.Columns.Add(colBackup);
            grid.Columns.Add(colProgress);
            ApplyGridColumnLayout();
            split.Panel1.Controls.Add(grid);

            TableLayoutPanel previewShell = new TableLayoutPanel();
            previewShell.Dock = DockStyle.Fill;
            previewShell.ColumnCount = 1;
            previewShell.RowCount = 3;
            previewShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            previewShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            previewShell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            split.Panel2.Controls.Add(previewShell);

            Label previewTitle = new Label();
            previewTitle.Text = "重命名预览";
            previewTitle.Dock = DockStyle.Fill;
            previewTitle.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            previewTitle.Padding = new Padding(4, 6, 0, 0);
            previewShell.Controls.Add(previewTitle, 0, 0);

            previewShell.Controls.Add(BuildCustomTailPanel(), 0, 1);

            Panel previewBody = new Panel();
            previewBody.Dock = DockStyle.Fill;
            previewShell.Controls.Add(previewBody, 0, 2);

            Panel detailHost = new Panel();
            detailHost.Dock = DockStyle.Right;
            detailHost.Width = 320;
            detailHost.MinimumSize = new Size(280, 0);

            previewList = new ListView();
            previewList.Dock = DockStyle.Fill;
            previewList.View = View.Details;
            previewList.FullRowSelect = true;
            previewList.GridLines = true;
            previewList.HideSelection = false;
            previewList.MultiSelect = false;
            previewList.ShowGroups = true;
            previewList.Columns.Add("行", 56);
            previewList.Columns.Add("镜号", 58);
            previewList.Columns.Add("末尾", 68);
            previewList.Columns.Add("列", 90);
            previewList.Columns.Add("原文件名", 144);
            previewList.Columns.Add("新文件名", 260);
            previewList.Columns.Add("状态", 110);
            previewList.Columns.Add("信息", 160);
            previewList.SelectedIndexChanged += delegate { UpdateSelectedPreviewDetails(); };
            previewList.SizeChanged += delegate { SchedulePreviewColumnResize(); };
            previewList.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete)
                {
                    DeleteSelectedPreviewRecord();
                    e.Handled = true;
                }
            };
            previewBody.Controls.Add(previewList);
            previewBody.Controls.Add(detailHost);
            detailHost.BringToFront();
            detailHost.Controls.Add(BuildVideoDetailsPanel());
        }

        private Control BuildVideoDetailsPanel()
        {
            TableLayoutPanel panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 1;
            panel.RowCount = 5;
            panel.Padding = new Padding(10, 8, 10, 10);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Label title = new Label();
            title.Text = "素材信息预览";
            title.Dock = DockStyle.Fill;
            title.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            title.Padding = new Padding(0, 4, 0, 0);
            panel.Controls.Add(title, 0, 0);

            thumbnailBox = new PictureBox();
            thumbnailBox.Dock = DockStyle.Fill;
            thumbnailBox.SizeMode = PictureBoxSizeMode.Zoom;
            thumbnailBox.BorderStyle = BorderStyle.FixedSingle;
            panel.Controls.Add(thumbnailBox, 0, 1);

            detailTitleLabel = new Label();
            detailTitleLabel.Dock = DockStyle.Fill;
            detailTitleLabel.AutoEllipsis = true;
            detailTitleLabel.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            detailTitleLabel.Padding = new Padding(0, 8, 0, 0);
            panel.Controls.Add(detailTitleLabel, 0, 2);

            detailInfoLabel = new Label();
            detailInfoLabel.Dock = DockStyle.Fill;
            detailInfoLabel.AutoEllipsis = false;
            detailInfoLabel.Padding = new Padding(0, 4, 0, 0);
            panel.Controls.Add(detailInfoLabel, 0, 3);

            detailPathLabel = new Label();
            detailPathLabel.Dock = DockStyle.Fill;
            detailPathLabel.Tag = "Muted";
            detailPathLabel.AutoEllipsis = true;
            detailPathLabel.Padding = new Padding(0, 4, 0, 0);
            panel.Controls.Add(detailPathLabel, 0, 4);

            ShowNoVideoDetails();
            return panel;
        }

        private Control BuildCustomTailPanel()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.FlowDirection = FlowDirection.LeftToRight;
            panel.WrapContents = false;
            panel.Padding = new Padding(6, 6, 6, 4);
            panel.Margin = new Padding(0);

            Label label = new Label();
            label.Text = "新文件名末尾";
            label.Tag = "Muted";
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Size = new Size(92, 26);
            label.Margin = new Padding(0, 0, 8, 0);
            panel.Controls.Add(label);

            chkCustomTail = new CheckBox();
            chkCustomTail.Text = "自定义";
            chkCustomTail.AutoSize = true;
            chkCustomTail.Margin = new Padding(0, 5, 8, 0);
            chkCustomTail.CheckedChanged += delegate { UpdateCustomTailInputState(); };
            panel.Controls.Add(chkCustomTail);

            txtCustomTail = new TextBox();
            txtCustomTail.Width = 230;
            txtCustomTail.Margin = new Padding(0, 2, 8, 0);
            txtCustomTail.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    ApplySelectedCustomTail(true);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            panel.Controls.Add(txtCustomTail);

            return panel;
        }

        private static Button NewButton(string text, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = Math.Max(44, (int)Math.Round(width * 0.8));
            button.Height = 30;
            button.Margin = new Padding(1, 2, 2, 2);
            button.AutoEllipsis = true;
            return button;
        }

        private static Label NewActionSeparator()
        {
            Label separator = new Label();
            separator.Text = "|";
            separator.Tag = "Muted";
            separator.AutoSize = false;
            separator.Width = 10;
            // 分割|的宽度设置
            separator.Height = 30;
            separator.TextAlign = ContentAlignment.MiddleCenter;
            separator.Margin = new Padding(6, 2, 6, 2);
            return separator;
        }

        private void ToggleTheme()
        {
            darkMode = !darkMode;
            ApplyTheme();
            RefreshPreview();
        }

        private bool IsExport1080pEnabled()
        {
            return chkExport1080p != null && chkExport1080p.Checked;
        }

        private bool IsExportWatermarkEnabled()
        {
            return IsExport1080pEnabled() && chkExportWatermark != null && chkExportWatermark.Checked;
        }

        private void UpdateWatermarkOptionState()
        {
            if (chkExportWatermark != null)
            {
                if (!IsExport1080pEnabled() && chkExportWatermark.Checked)
                {
                    chkExportWatermark.Checked = false;
                }
                chkExportWatermark.Enabled = true;
                UiTheme.ApplyControl(chkExportWatermark, darkMode);
            }
        }

        private void UpdateRenameButtonText()
        {
            if (btnRename != null)
            {
                btnRename.Text = IsExport1080pEnabled() ? "导出1080p" : "执行重命名";
            }
        }

        private void ShowAboutInfo()
        {
            using (AboutForm dialog = new AboutForm(activeLicenseInfo, darkMode))
            {
                dialog.ShowDialog(this);
            }
        }

        private void ApplyTheme()
        {
            UpdateRenameButtonText();
            UpdateWatermarkOptionState();
            if (btnTheme != null)
            {
                btnTheme.Text = "护眼模式";
            }

            UiTheme.ApplyForm(this, darkMode);
            ApplyGridNumberColumnStyles();
            if (thumbnailBox != null)
            {
                thumbnailBox.BackColor = UiTheme.ControlBack(darkMode);
            }
            if (detailTitleLabel != null && (string.IsNullOrWhiteSpace(detailTitleLabel.Text) || detailTitleLabel.Text == "未选择素材"))
            {
                ShowNoVideoDetails();
            }
            ReapplyDragHighlight();
        }

        private Color GetSceneColumnTextColor()
        {
            return darkMode ? Color.FromArgb(255, 128, 128) : Color.FromArgb(190, 35, 35);
        }

        private Color GetShotColumnTextColor()
        {
            return darkMode ? UiTheme.TextColor(darkMode) : Color.Black;
        }

        private void ApplyGridNumberColumnStyles()
        {
            if (grid == null || grid.Columns.Count <= GridShotColumn)
            {
                return;
            }

            Color sceneColor = GetSceneColumnTextColor();
            Color shotColor = GetShotColumnTextColor();
            grid.Columns[GridSceneColumn].DefaultCellStyle.ForeColor = sceneColor;
            grid.Columns[GridSceneColumn].DefaultCellStyle.SelectionForeColor = Color.White;
            grid.Columns[GridSceneColumn].HeaderCell.Style.ForeColor = sceneColor;
            grid.Columns[GridShotColumn].DefaultCellStyle.ForeColor = shotColor;
            grid.Columns[GridShotColumn].DefaultCellStyle.SelectionForeColor = Color.White;
            grid.Columns[GridShotColumn].HeaderCell.Style.ForeColor = shotColor;

            foreach (DataGridViewRow row in grid.Rows)
            {
                ApplyGridNumberCellStyles(row);
            }
        }

        private void ApplyGridNumberCellStyles(DataGridViewRow row)
        {
            if (row == null || row.Cells.Count <= GridShotColumn)
            {
                return;
            }

            row.Cells[GridSceneColumn].Style.ForeColor = GetSceneColumnTextColor();
            row.Cells[GridSceneColumn].Style.SelectionForeColor = Color.White;
            row.Cells[GridShotColumn].Style.ForeColor = GetShotColumnTextColor();
            row.Cells[GridShotColumn].Style.SelectionForeColor = Color.White;
        }

        private void ClearDragHighlight()
        {
            if (grid == null || dragHighlightRow < 0 || dragHighlightColumn < 0)
            {
                dragHighlightRow = -1;
                dragHighlightColumn = -1;
                return;
            }

            int row = dragHighlightRow;
            int column = dragHighlightColumn;
            dragHighlightRow = -1;
            dragHighlightColumn = -1;
            ResetGridCellStyle(row, column);
        }

        private void SetDragHighlight(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= rows.Count || (columnIndex != GridMainColumn && columnIndex != GridBackupColumn))
            {
                ClearDragHighlight();
                return;
            }

            if (dragHighlightRow == rowIndex && dragHighlightColumn == columnIndex)
            {
                return;
            }

            ClearDragHighlight();
            dragHighlightRow = rowIndex;
            dragHighlightColumn = columnIndex;
            ReapplyDragHighlight();
        }

        private void ReapplyDragHighlight()
        {
            if (grid == null || dragHighlightRow < 0 || dragHighlightColumn < 0 ||
                dragHighlightRow >= grid.Rows.Count || dragHighlightColumn >= grid.Columns.Count)
            {
                return;
            }

            DataGridViewCell cell = grid.Rows[dragHighlightRow].Cells[dragHighlightColumn];
            cell.Style.BackColor = UiTheme.DropTargetBack(darkMode);
            cell.Style.ForeColor = UiTheme.DropTargetFore(darkMode);
            cell.Style.SelectionBackColor = UiTheme.DropTargetBack(darkMode);
            cell.Style.SelectionForeColor = UiTheme.DropTargetFore(darkMode);
        }

        private void ResetGridCellStyle(int rowIndex, int columnIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            DataGridViewCell cell = grid.Rows[rowIndex].Cells[columnIndex];
            cell.Style.BackColor = Color.Empty;
            cell.Style.ForeColor = Color.Empty;
            cell.Style.SelectionBackColor = Color.Empty;
            cell.Style.SelectionForeColor = Color.Empty;
        }

        private bool QueueOnUi(MethodInvoker action)
        {
            if (action == null || IsDisposed || !IsHandleCreated)
            {
                return false;
            }

            try
            {
                BeginInvoke(action);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void StartStaBackground(ThreadStart action)
        {
            if (action == null)
            {
                return;
            }

            Thread thread = new Thread(action);
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private VideoFileInfo GetFastVideoInfo(string path)
        {
            VideoFileInfo info = new VideoFileInfo();
            info.Path = path ?? "";
            info.FileName = string.IsNullOrWhiteSpace(path) ? "" : Path.GetFileName(path);
            info.SizeText = "";
            info.ResolutionText = "读取中";
            info.ModifiedText = "";
            info.Exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);

            if (!info.Exists)
            {
                info.ResolutionText = "未知";
                return info;
            }

            try
            {
                FileInfo file = new FileInfo(path);
                info.SizeText = VideoMetadataReader.FormatBytes(file.Length);
                info.ModifiedText = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
            }

            return info;
        }

        private void QueueVideoInfoLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || videoInfoCache.ContainsKey(path) || pendingVideoInfoLoads.Contains(path))
            {
                return;
            }

            pendingVideoInfoLoads.Add(path);
            StartStaBackground(delegate
            {
                VideoFileInfo info = VideoMetadataReader.Read(path);
                QueueOnUi(delegate
                {
                    pendingVideoInfoLoads.Remove(path);
                    videoInfoCache[path] = info;
                    UpdatePreviewListInfoSummary(path, info);
                    if (IsCurrentDetailPath(path))
                    {
                        RenderVideoInfo(info, currentDetailNewName, currentDetailContext);
                    }
                });
            });
        }

        private bool TryGetThumbnailFromCache(string path, out Image image)
        {
            image = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (!thumbnailCache.TryGetValue(path, out image))
            {
                return false;
            }

            TouchThumbnailCacheNode(path);
            return image != null;
        }

        private void TouchThumbnailCacheNode(string path)
        {
            LinkedListNode<string> node;
            if (!thumbnailCacheNodes.TryGetValue(path, out node))
            {
                node = thumbnailCacheOrder.AddLast(path);
                thumbnailCacheNodes[path] = node;
                return;
            }

            thumbnailCacheOrder.Remove(node);
            thumbnailCacheOrder.AddLast(node);
        }

        private void AddThumbnailToCache(string path, Image image)
        {
            if (string.IsNullOrWhiteSpace(path) || image == null)
            {
                if (image != null)
                {
                    image.Dispose();
                }
                return;
            }

            Image existing;
            if (thumbnailCache.TryGetValue(path, out existing) && !object.ReferenceEquals(existing, image))
            {
                existing.Dispose();
            }

            thumbnailCache[path] = image;
            TouchThumbnailCacheNode(path);
            TrimThumbnailCache();
        }

        private void TrimThumbnailCache()
        {
            while (thumbnailCache.Count > ThumbnailCacheLimit && thumbnailCacheOrder.First != null)
            {
                string path = thumbnailCacheOrder.First.Value;
                thumbnailCacheOrder.RemoveFirst();
                thumbnailCacheNodes.Remove(path);

                Image image;
                if (thumbnailCache.TryGetValue(path, out image))
                {
                    thumbnailCache.Remove(path);
                    if (image != null)
                    {
                        image.Dispose();
                    }
                }
            }
        }

        private void QueueThumbnailLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || pendingThumbnailLoads.Contains(path))
            {
                return;
            }

            Image cached;
            if (TryGetThumbnailFromCache(path, out cached))
            {
                return;
            }

            pendingThumbnailLoads.Add(path);
            StartStaBackground(delegate
            {
                Image image = VideoThumbnailProvider.GetThumbnail(path, new Size(300, 166));
                bool queued = QueueOnUi(delegate
                {
                    pendingThumbnailLoads.Remove(path);
                    if (image != null)
                    {
                        AddThumbnailToCache(path, image);
                    }

                    if (IsCurrentDetailPath(path))
                    {
                        if (image != null)
                        {
                            SetDetailImage(image, false);
                        }
                        else
                        {
                            SetDetailImage(CreatePlaceholderImage("无缩略图"), true);
                        }
                    }
                });

                if (!queued && image != null)
                {
                    image.Dispose();
                }
            });
        }

        private string GetListVideoSummary(string path)
        {
            VideoFileInfo cached;
            if (!string.IsNullOrWhiteSpace(path) && videoInfoCache.TryGetValue(path, out cached))
            {
                return cached.ListSummary;
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return "文件不存在";
            }

            try
            {
                FileInfo file = new FileInfo(path);
                return VideoMetadataReader.FormatBytes(file.Length) + " | " + (Path.GetExtension(path) ?? "").TrimStart('.').ToUpperInvariant();
            }
            catch
            {
                return "";
            }
        }

        private void UpdateSelectedPreviewDetails()
        {
            if (previewList == null || previewList.SelectedItems.Count == 0)
            {
                return;
            }

            RenamePlan entry = previewList.SelectedItems[0].Tag as RenamePlan;
            if (entry == null)
            {
                ShowNoVideoDetails();
                return;
            }

            string context = string.Format("第 {0} 行 / 场号 {1} / 镜号 {2} / {3} / {4}", entry.RowIndex, entry.Scene, entry.Shot, entry.ColumnName, entry.TailSegment);
            ShowVideoDetails(entry.OldPath, entry.NewName, context);
            UpdateCustomTailControls(entry);
        }

        private void UpdateSelectedCellDetails()
        {
            if (rendering || grid == null || grid.CurrentCell == null || thumbnailBox == null)
            {
                return;
            }

            int rowIndex = grid.CurrentCell.RowIndex;
            int columnIndex = grid.CurrentCell.ColumnIndex;
            if (rowIndex < 0 || rowIndex >= rows.Count || (columnIndex != GridMainColumn && columnIndex != GridBackupColumn))
            {
                ShowNoVideoDetails();
                return;
            }

            ShotRow row = rows[rowIndex];
            List<string> files = columnIndex == GridMainColumn ? row.MainFiles : row.BackupFiles;
            if (files.Count == 0)
            {
                ShowNoVideoDetails();
                return;
            }

            bool isMain = columnIndex == GridMainColumn;
            RenamePlan firstPlan = currentPlan.FirstOrDefault(p => p.Row == row && p.IsMain == isMain && p.FileIndex == 0);
            string newName = firstPlan == null ? "" : firstPlan.NewName;
            string context = string.Format("第 {0} 行 / 场号 {1} / 镜号 {2} / {3}，单元格共 {4} 个视频，当前显示第 1 个", rowIndex + 1, GetEffectiveScene(row, GetDefaultScene(), IsRowSceneEnabled()), row.Sequence, isMain ? "主要素材" : "备用素材", files.Count);
            ShowVideoDetails(files[0], newName, context);
            UpdateCustomTailControls(firstPlan);
        }

        private void ShowVideoDetails(string path, string newName, string context)
        {
            if (thumbnailBox == null || detailTitleLabel == null || detailInfoLabel == null || detailPathLabel == null)
            {
                return;
            }

            detailLoadVersion++;
            currentDetailPath = path ?? "";
            currentDetailNewName = newName ?? "";
            currentDetailContext = context ?? "";

            VideoFileInfo info;
            if (string.IsNullOrWhiteSpace(path) || !videoInfoCache.TryGetValue(path, out info))
            {
                info = GetFastVideoInfo(path);
                QueueVideoInfoLoad(path);
            }

            RenderVideoInfo(info, currentDetailNewName, currentDetailContext);
            Image thumbnail;
            if (TryGetThumbnailFromCache(path, out thumbnail))
            {
                SetDetailImage(thumbnail, false);
            }
            else
            {
                SetDetailImage(CreatePlaceholderImage(info.Exists ? "缩略图读取中" : "无缩略图"), true);
                QueueThumbnailLoad(path);
            }
        }

        private bool IsCurrentDetailPath(string path)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(currentDetailPath ?? "", path ?? "");
        }

        private void RenderVideoInfo(VideoFileInfo info, string newName, string context)
        {
            if (info == null)
            {
                info = GetFastVideoInfo("");
            }

            detailTitleLabel.Text = string.IsNullOrWhiteSpace(info.FileName) ? "未选择素材" : info.FileName;
            UpdatePreviewListInfoSummary(info.Path, info);

            List<string> lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(context))
            {
                lines.Add(context);
            }
            if (!string.IsNullOrWhiteSpace(newName))
            {
                lines.Add("目标文件名：" + newName);
            }
            lines.Add("大小：" + (string.IsNullOrWhiteSpace(info.SizeText) ? "未知" : info.SizeText));
            lines.Add("分辨率：" + info.ResolutionText);
            lines.Add("修改时间：" + (string.IsNullOrWhiteSpace(info.ModifiedText) ? "未知" : info.ModifiedText));
            detailInfoLabel.Text = string.Join("\r\n", lines.ToArray());
            detailPathLabel.Text = info.Path ?? "";
        }

        private void UpdateCustomTailControls(RenamePlan entry)
        {
            if (chkCustomTail == null || txtCustomTail == null)
            {
                return;
            }

            bool enabled = entry != null && entry.Row != null;
            chkCustomTail.Enabled = enabled;
            txtCustomTail.Text = enabled && entry.HasCustomTail ? entry.CustomTailText : "";

            UpdateCustomTailInputState();
        }

        private void UpdateCustomTailInputState()
        {
            if (chkCustomTail == null || txtCustomTail == null)
            {
                return;
            }

            bool hasSelection = GetSelectedPreviewEntry() != null;
            chkCustomTail.Enabled = true;
            txtCustomTail.Enabled = hasSelection && chkCustomTail.Checked;
            UiTheme.ApplyControl(chkCustomTail, darkMode);
            UiTheme.ApplyControl(txtCustomTail, darkMode);
        }

        private RenamePlan GetSelectedPreviewEntry()
        {
            if (previewList == null || previewList.SelectedItems.Count == 0)
            {
                return null;
            }

            return previewList.SelectedItems[0].Tag as RenamePlan;
        }

        private void ApplySelectedCustomTail(bool showMessage)
        {
            RenamePlan entry = GetSelectedPreviewEntry();
            if (entry == null || entry.Row == null)
            {
                if (showMessage)
                {
                    MessageBox.Show(this, "请先在底部预览中选中一条素材记录。", "未选择素材", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            if (chkCustomTail != null && !chkCustomTail.Checked)
            {
                if (showMessage)
                {
                    MessageBox.Show(this, "请先勾选自定义，再输入末尾编号或文字。", "未启用自定义", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            string normalized = "";
            string requestedNormalized = "";
            normalized = NormalizeCustomTailText(txtCustomTail == null ? "" : txtCustomTail.Text);
            requestedNormalized = normalized;
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                normalized = GetUniqueCustomTail(entry, normalized, currentPlan, (int)numEpisode.Value, entry.Scene, chkKeepExtension.Checked);
            }

            ShotRow row = entry.Row;
            bool isMain = entry.IsMain;
            int fileIndex = entry.FileIndex;

            SetTailOverride(entry, normalized);

            RefreshPreview();
            SelectPreviewEntry(row, isMain, fileIndex);
            if (statusLabel != null)
            {
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    statusLabel.Text = "已恢复默认 T 编号。";
                }
                else if (!StringComparer.Ordinal.Equals(normalized, requestedNormalized))
                {
                    statusLabel.Text = "已应用自定义末尾：" + normalized + "（自动补号）";
                }
                else
                {
                    statusLabel.Text = "已应用自定义末尾：" + normalized;
                }
            }
        }

        private void SelectPreviewEntry(ShotRow row, bool isMain, int fileIndex)
        {
            if (previewList == null)
            {
                return;
            }

            foreach (ListViewItem item in previewList.Items)
            {
                RenamePlan entry = item.Tag as RenamePlan;
                if (entry != null && entry.Row == row && entry.IsMain == isMain && entry.FileIndex == fileIndex)
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    UpdateSelectedPreviewDetails();
                    return;
                }
            }
        }

        private void UpdatePreviewListInfoSummary(string path, VideoFileInfo info)
        {
            if (previewList == null || string.IsNullOrWhiteSpace(path) || info == null)
            {
                return;
            }

            foreach (ListViewItem item in previewList.Items)
            {
                RenamePlan entry = item.Tag as RenamePlan;
                if (entry != null &&
                    StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, path) &&
                    item.SubItems.Count > 7)
                {
                    item.SubItems[7].Text = info.ListSummary;
                }
            }
        }

        private void ShowNoVideoDetails()
        {
            if (thumbnailBox == null || detailTitleLabel == null || detailInfoLabel == null || detailPathLabel == null)
            {
                return;
            }

            detailLoadVersion++;
            currentDetailPath = "";
            currentDetailNewName = "";
            currentDetailContext = "";
            detailTitleLabel.Text = "未选择素材";
            detailInfoLabel.Text = "选中底部预览记录，或选中 B/C 中已有素材的单元格，可查看缩略图、分辨率和文件大小。";
            detailPathLabel.Text = "";
            UpdateCustomTailControls(null);
            SetDetailImage(CreatePlaceholderImage("视频预览"), true);
        }

        private Image CreatePlaceholderImage(string text)
        {
            Bitmap bitmap = new Bitmap(300, 166);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(UiTheme.ControlBack(darkMode));
                using (Pen border = new Pen(UiTheme.BorderColor(darkMode)))
                {
                    graphics.DrawRectangle(border, 0, 0, bitmap.Width - 1, bitmap.Height - 1);
                }
                using (Brush brush = new SolidBrush(UiTheme.MutedText(darkMode)))
                using (Font font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold))
                {
                    SizeF size = graphics.MeasureString(text, font);
                    graphics.DrawString(text, font, brush, (bitmap.Width - size.Width) / 2, (bitmap.Height - size.Height) / 2);
                }
            }
            return bitmap;
        }

        private void SetDetailImage(Image image, bool ownsImage)
        {
            if (thumbnailBox == null)
            {
                if (ownsImage && image != null)
                {
                    image.Dispose();
                }
                return;
            }

            if (ownedDetailImage != null && !object.ReferenceEquals(ownedDetailImage, image))
            {
                ownedDetailImage.Dispose();
            }

            thumbnailBox.Image = image;
            ownedDetailImage = ownsImage ? image : null;
        }

        private void RenderAll()
        {
            RenderGrid();
            RefreshPreview();
        }

        private void RenderGrid()
        {
            rendering = true;
            try
            {
                ApplyGridColumnLayout();
                dragHighlightRow = -1;
                dragHighlightColumn = -1;
                grid.Rows.Clear();
                foreach (ShotRow row in rows)
                {
                    int index = grid.Rows.Add();
                    DataGridViewRow gridRow = grid.Rows[index];
                    gridRow.Tag = row;
                    gridRow.Height = grid.RowTemplate.Height;
                    gridRow.Resizable = DataGridViewTriState.False;
                    gridRow.Cells[GridSceneColumn].Value = GetEffectiveScene(row, GetDefaultScene(), true);
                    gridRow.Cells[GridShotColumn].Value = row.Sequence;
                    gridRow.Cells[GridMainColumn].Value = GetCellSummary(row.MainFiles);
                    gridRow.Cells[GridBackupColumn].Value = GetCellSummary(row.BackupFiles);
                    gridRow.Cells[GridProgressColumn].Value = row.ProgressPercent;
                    gridRow.Cells[GridMainColumn].ToolTipText = GetCellSummary(row.MainFiles);
                    gridRow.Cells[GridBackupColumn].ToolTipText = GetCellSummary(row.BackupFiles);
                    ApplyGridNumberCellStyles(gridRow);
                }
                ApplyGridNumberColumnStyles();
            }
            finally
            {
                rendering = false;
            }
        }

        private void RenderGridRow(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= rows.Count || rowIndex >= grid.Rows.Count)
            {
                RenderGrid();
                return;
            }

            rendering = true;
            try
            {
                ShotRow row = rows[rowIndex];
                DataGridViewRow gridRow = grid.Rows[rowIndex];
                gridRow.Tag = row;
                gridRow.Height = grid.RowTemplate.Height;
                gridRow.Resizable = DataGridViewTriState.False;
                gridRow.Cells[GridSceneColumn].Value = GetEffectiveScene(row, GetDefaultScene(), true);
                gridRow.Cells[GridShotColumn].Value = row.Sequence;
                gridRow.Cells[GridMainColumn].Value = GetCellSummary(row.MainFiles);
                gridRow.Cells[GridBackupColumn].Value = GetCellSummary(row.BackupFiles);
                gridRow.Cells[GridProgressColumn].Value = row.ProgressPercent;
                gridRow.Cells[GridMainColumn].ToolTipText = GetCellSummary(row.MainFiles);
                gridRow.Cells[GridBackupColumn].ToolTipText = GetCellSummary(row.BackupFiles);
                ApplyGridNumberCellStyles(gridRow);
            }
            finally
            {
                rendering = false;
            }
        }

        private void RenderGridProgress(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= rows.Count || rowIndex >= grid.Rows.Count || grid.Columns.Count <= GridProgressColumn)
            {
                return;
            }

            grid.Rows[rowIndex].Cells[GridProgressColumn].Value = rows[rowIndex].ProgressPercent;
            grid.InvalidateCell(GridProgressColumn, rowIndex);
        }

        private int GetDefaultScene()
        {
            return numScene == null ? 1 : Math.Max(1, (int)numScene.Value);
        }

        private bool IsRowSceneEnabled()
        {
            return chkRowScene != null && chkRowScene.Checked;
        }

        private void InitializeRowScenesFromDefaultIfNeeded()
        {
            if (rowSceneModeInitialized)
            {
                return;
            }

            int defaultScene = GetDefaultScene();
            foreach (ShotRow row in rows)
            {
                row.Scene = defaultScene;
            }

            rowSceneModeInitialized = true;
        }

        private static int GetEffectiveScene(ShotRow row, int defaultScene, bool useRowScene)
        {
            return useRowScene && row != null && row.Scene > 0 ? row.Scene : Math.Max(1, defaultScene);
        }

        private void SetProgressColumnVisible(bool visible)
        {
            progressColumnVisible = visible;
            ApplyGridColumnLayout();
        }

        private int GetDefaultGridFocusColumn()
        {
            return IsRowSceneEnabled() ? GridSceneColumn : GridShotColumn;
        }

        private void ApplyGridColumnLayout()
        {
            if (grid == null || grid.Columns.Count <= GridProgressColumn)
            {
                return;
            }

            bool rowScene = IsRowSceneEnabled();
            if (grid.CurrentCell != null)
            {
                int currentColumn = grid.CurrentCell.ColumnIndex;
                bool currentColumnWillHide =
                    (!rowScene && currentColumn == GridSceneColumn) ||
                    (!progressColumnVisible && currentColumn == GridProgressColumn);
                if (currentColumnWillHide)
                {
                    SelectGridCell(grid.CurrentCell.RowIndex, GetDefaultGridFocusColumn());
                }
            }

            grid.Columns[GridSceneColumn].Visible = rowScene;
            grid.Columns[GridShotColumn].HeaderText = rowScene ? "B 镜号" : "A 镜号";
            grid.Columns[GridMainColumn].HeaderText = rowScene ? "C 主要素材" : "B 主要素材";
            grid.Columns[GridBackupColumn].HeaderText = rowScene ? "D 备用素材" : "C 备用素材";
            grid.Columns[GridProgressColumn].HeaderText = rowScene ? "E 进度" : "D 进度";
            grid.Columns[GridProgressColumn].Visible = progressColumnVisible;
        }

        private string GetMainColumnDisplayName()
        {
            return IsRowSceneEnabled() ? "C「主要素材」" : "B「主要素材」";
        }

        private string GetBackupColumnDisplayName()
        {
            return IsRowSceneEnabled() ? "D「备用素材」" : "C「备用素材」";
        }

        private string GetMainColumnLetter()
        {
            return IsRowSceneEnabled() ? "C" : "B";
        }

        private string GetBackupColumnLetter()
        {
            return IsRowSceneEnabled() ? "D" : "C";
        }

        private void ResetProgressBars()
        {
            foreach (ShotRow row in rows)
            {
                row.ProgressPercent = 0;
            }

            RenderGrid();
        }

        private void RefreshPreview()
        {
            if (previewList == null)
            {
                return;
            }

            currentPlan.Clear();
            currentPlan.AddRange(BuildPlan(rows, (int)numEpisode.Value, GetDefaultScene(), chkKeepExtension.Checked, IsExport1080pEnabled(), IsRowSceneEnabled()));

            previewList.BeginUpdate();
            try
            {
                previewList.Items.Clear();
                previewList.Groups.Clear();
                Dictionary<int, ListViewGroup> previewGroups = new Dictionary<int, ListViewGroup>();
                foreach (RenamePlan entry in currentPlan)
                {
                    bool firstInGroup = !previewGroups.ContainsKey(entry.RowIndex);
                    if (firstInGroup)
                    {
                        ListViewGroup group = new ListViewGroup(
                            string.Format("第 {0} 行 / 场号 {1} / 镜号 {2}", entry.RowIndex, entry.Scene, entry.Shot),
                            HorizontalAlignment.Left);
                        previewGroups[entry.RowIndex] = group;
                        previewList.Groups.Add(group);
                    }

                    ListViewItem item = new ListViewItem(firstInGroup ? "▶ " + entry.RowIndex : "  " + entry.RowIndex);
                    item.Tag = entry;
                    item.Group = previewGroups[entry.RowIndex];
                    item.SubItems.Add(entry.Shot.ToString());
                    item.SubItems.Add(entry.TailSegment);
                    item.SubItems.Add(entry.ColumnName);
                    item.SubItems.Add(entry.OldName);
                    item.SubItems.Add(entry.NewName);
                    item.SubItems.Add(entry.Status);
                    item.SubItems.Add(GetListVideoSummary(entry.OldPath));
                    if (firstInGroup)
                    {
                        item.Font = new Font(previewList.Font, FontStyle.Bold);
                    }
                    ApplyPreviewItemStatusStyle(item, entry);

                    previewList.Items.Add(item);
                }

                UpdatePreviewStatusSummary();
            }
            finally
            {
                previewList.EndUpdate();
            }

            SchedulePreviewColumnResize();
            SchedulePlanStatusChecks();
            if (previewList.SelectedItems.Count == 0)
            {
                UpdateSelectedCellDetails();
            }
        }

        private void SchedulePreviewColumnResize()
        {
            if (previewList == null)
            {
                return;
            }

            if (previewColumnResizeTimer == null)
            {
                previewColumnResizeTimer = new System.Windows.Forms.Timer();
                previewColumnResizeTimer.Interval = 120;
                previewColumnResizeTimer.Tick += delegate
                {
                    previewColumnResizeTimer.Stop();
                    AutoResizePreviewColumns();
                };
            }

            previewColumnResizeTimer.Stop();
            previewColumnResizeTimer.Start();
        }

        private void AutoResizePreviewColumns()
        {
            if (previewList == null || previewList.Columns.Count < 8)
            {
                return;
            }

            const int originalNameColumn = 4;
            previewList.Columns[originalNameColumn].Width = 144;
            for (int index = 0; index < previewList.Columns.Count; index++)
            {
                if (index == originalNameColumn)
                {
                    continue;
                }

                previewList.Columns[index].Width = GetPreviewColumnWidth(index);
            }
        }

        private int GetPreviewColumnWidth(int columnIndex)
        {
            int width = MeasurePreviewText(previewList.Columns[columnIndex].Text, previewList.Font) + 20;
            int measuredRows = 0;
            foreach (ListViewItem item in previewList.Items)
            {
                if (columnIndex < item.SubItems.Count)
                {
                    width = Math.Max(width, MeasurePreviewText(item.SubItems[columnIndex].Text, item.Font) + 20);
                }

                measuredRows++;
                if (measuredRows >= 120)
                {
                    break;
                }
            }

            return Math.Min(GetPreviewColumnMaximumWidth(columnIndex), Math.Max(GetPreviewColumnMinimumWidth(columnIndex), width));
        }

        private static int MeasurePreviewText(string text, Font font)
        {
            return TextRenderer.MeasureText(string.IsNullOrEmpty(text) ? " " : text, font).Width;
        }

        private static int GetPreviewColumnMinimumWidth(int columnIndex)
        {
            switch (columnIndex)
            {
                case 0:
                    return 48;
                case 1:
                    return 54;
                case 2:
                    return 60;
                case 3:
                    return 76;
                case 5:
                    return 160;
                case 6:
                    return 86;
                case 7:
                    return 120;
                default:
                    return 72;
            }
        }

        private static int GetPreviewColumnMaximumWidth(int columnIndex)
        {
            switch (columnIndex)
            {
                case 0:
                    return 84;
                case 1:
                    return 92;
                case 2:
                    return 180;
                case 3:
                    return 120;
                case 5:
                    return 420;
                case 6:
                    return 160;
                case 7:
                    return 220;
                default:
                    return 240;
            }
        }

        private void ApplyPreviewItemStatusStyle(ListViewItem item, RenamePlan entry)
        {
            if (item == null || entry == null)
            {
                return;
            }

            item.BackColor = entry.RowIndex % 2 == 0 ? UiTheme.PreviewAltBack(darkMode) : UiTheme.ControlBack(darkMode);
            item.ForeColor = UiTheme.TextColor(darkMode);

            if (IsBlockingIssue(entry))
            {
                item.BackColor = UiTheme.PreviewErrorBack(darkMode);
            }
            else if (entry.Status == "未变化")
            {
                item.BackColor = UiTheme.PreviewNeutralBack(darkMode);
            }
        }

        private void UpdatePreviewStatusSummary()
        {
            if (statusLabel == null)
            {
                return;
            }

            int errors = currentPlan.Count(IsBlockingIssue);
            if (currentPlan.Count == 0)
            {
                statusLabel.Text = "把视频拖到表格 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 单元格。";
            }
            else if (errors > 0)
            {
                statusLabel.Text = string.Format("共 {0} 个视频，发现 {1} 个命名问题；请处理后再执行。", currentPlan.Count, errors);
            }
            else if (IsExport1080pEnabled())
            {
                statusLabel.Text = IsExportWatermarkEnabled()
                    ? string.Format("共 {0} 个视频，预览无冲突；导出时会在左上角加入新文件名水印。", currentPlan.Count)
                    : string.Format("共 {0} 个视频，预览无冲突；执行时可选择覆盖原文件或另存为新文件。", currentPlan.Count);
            }
            else
            {
                statusLabel.Text = string.Format("共 {0} 个视频，预览无冲突。{1} 列先编号，{2} 列接着编号。", currentPlan.Count, GetMainColumnLetter(), GetBackupColumnLetter());
            }
        }

        private void RefreshPreviewStatusOnly()
        {
            if (previewList == null)
            {
                return;
            }

            previewList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in previewList.Items)
                {
                    RenamePlan entry = item.Tag as RenamePlan;
                    if (entry == null)
                    {
                        continue;
                    }

                    if (item.SubItems.Count > 6)
                    {
                        item.SubItems[6].Text = entry.Status;
                    }
                    ApplyPreviewItemStatusStyle(item, entry);
                }
            }
            finally
            {
                previewList.EndUpdate();
            }

            UpdatePreviewStatusSummary();
        }

        private void SchedulePlanStatusChecks()
        {
            int version = ++planCheckVersion;
            List<RenamePlan> targets = currentPlan
                .Where(p => p.Status == "目标已存在" && !string.IsNullOrWhiteSpace(p.TargetPath))
                .ToList();

            if (targets.Count == 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int offset = 0; offset < targets.Count; offset += PlanStatusCheckBatchSize)
                {
                    List<RenamePlan> batch = targets.Skip(offset).Take(PlanStatusCheckBatchSize).ToList();
                    HashSet<string> lockedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (RenamePlan entry in batch)
                    {
                        if (IsFileLocked(entry.TargetPath))
                        {
                            lockedPaths.Add(entry.TargetPath);
                        }
                    }

                    if (lockedPaths.Count > 0)
                    {
                        QueueOnUi(delegate
                        {
                            if (version != planCheckVersion)
                            {
                                return;
                            }

                            foreach (RenamePlan entry in currentPlan)
                            {
                                if (entry.Status == "目标已存在" && lockedPaths.Contains(entry.TargetPath))
                                {
                                    entry.Status = "目标文件被占用";
                                }
                            }

                            RefreshPreviewStatusOnly();
                        });
                    }

                    Thread.Sleep(20);
                }
            });
        }

        private static List<RenamePlan> BuildPlan(List<ShotRow> sourceRows, int episode, int scene, bool keepExtensionCase, bool export1080p, bool useRowScene = false)
        {
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            List<RenamePlan> plan = new List<RenamePlan>();
            int rowIndex = 1;

            foreach (ShotRow row in sourceRows)
            {
                int rowScene = GetEffectiveScene(row, scene, useRowScene);
                int shot = Math.Max(1, row.Sequence);
                int take = 1;

                EnsureTailOverrideSize(row, true);
                EnsureTailOverrideSize(row, false);
                AddFilesToPlan(plan, seen, row, rowIndex, "主要素材", true, row.MainFiles, row.MainTailOverrides, episode, rowScene, shot, ref take, keepExtensionCase, export1080p);
                AddFilesToPlan(plan, seen, row, rowIndex, "备用素材", false, row.BackupFiles, row.BackupTailOverrides, episode, rowScene, shot, ref take, keepExtensionCase, export1080p);
                rowIndex++;
            }

            return plan;
        }

        private static void AddFilesToPlan(
            List<RenamePlan> plan,
            Dictionary<string, bool> seen,
            ShotRow row,
            int rowIndex,
            string columnName,
            bool isMain,
            List<string> files,
            List<string> tailOverrides,
            int episode,
            int scene,
            int shot,
            ref int take,
            bool keepExtensionCase,
            bool export1080p)
        {
            for (int fileIndex = 0; fileIndex < files.Count; fileIndex++)
            {
                string oldPath = Path.GetFullPath(files[fileIndex]);
                string customTail = tailOverrides != null && fileIndex < tailOverrides.Count ? NormalizeCustomTailText(tailOverrides[fileIndex]) : "";
                string tailSegment = GetTailSegment(take, customTail);
                string newName = GetMaterialFileName(episode, scene, shot, tailSegment, oldPath, keepExtensionCase);
                string directory = Path.GetDirectoryName(oldPath);
                string targetPath = Path.GetFullPath(Path.Combine(directory, newName));
                string status = "就绪";

                if (!File.Exists(oldPath))
                {
                    status = "源文件丢失";
                }
                else if (StringComparer.OrdinalIgnoreCase.Equals(targetPath, oldPath))
                {
                    status = export1080p ? "待覆盖导出1080p" : "未变化";
                }
                else if (File.Exists(targetPath))
                {
                    status = "目标已存在";
                }

                if (export1080p && status == "就绪")
                {
                    status = "待覆盖导出1080p";
                }

                if (seen.ContainsKey(targetPath))
                {
                    status = "新文件名重复";
                }

                seen[targetPath] = true;
                plan.Add(new RenamePlan
                {
                    Row = row,
                    RowIndex = rowIndex,
                    ColumnName = columnName,
                    IsMain = isMain,
                    FileIndex = fileIndex,
                    Scene = scene,
                    Shot = shot,
                    Take = take,
                    TailSegment = tailSegment,
                    CustomTailText = customTail,
                    HasCustomTail = !string.IsNullOrWhiteSpace(customTail),
                    OldPath = oldPath,
                    TargetPath = targetPath,
                    OldName = Path.GetFileName(oldPath),
                    NewName = Path.GetFileName(targetPath),
                    Status = status
                });

                take++;
            }
        }

        private static bool IsBlockingIssue(RenamePlan entry)
        {
            return entry != null &&
                (entry.Status == "目标已存在" ||
                 entry.Status == "目标文件被占用" ||
                 entry.Status == "新文件名重复" ||
                 entry.Status == "源文件丢失");
        }

        private static bool IsFileLocked(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        private static string GetMaterialFileName(int episode, int scene, int shot, string tailSegment, string sourcePath, bool keepExtensionCase)
        {
            string extension = Path.GetExtension(sourcePath) ?? "";
            if (!keepExtensionCase)
            {
                extension = extension.ToLowerInvariant();
            }

            string safeTail = string.IsNullOrWhiteSpace(tailSegment) ? "T1" : tailSegment;
            return string.Format("E{0}-S{1}-{2}-{3}{4}", Math.Max(1, episode), Math.Max(1, scene), Math.Max(1, shot), safeTail, extension);
        }

        private static string GetTailSegment(int take, string customTail)
        {
            string normalized = NormalizeCustomTailText(customTail);
            return string.IsNullOrWhiteSpace(normalized) ? "T" + Math.Max(1, take) : normalized;
        }

        private static string NormalizeCustomTailText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string text = value.Trim();
            HashSet<char> invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            StringBuilder builder = new StringBuilder();
            foreach (char ch in text)
            {
                if (invalid.Contains(ch) || char.IsControl(ch))
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(ch);
                }
            }

            string normalized = builder.ToString().Trim().Trim('.');
            if (normalized.Length > 80)
            {
                normalized = normalized.Substring(0, 80).Trim();
            }

            return normalized;
        }

        private static List<string> GetTailOverrideList(ShotRow row, bool isMain)
        {
            return isMain ? row.MainTailOverrides : row.BackupTailOverrides;
        }

        private static List<string> GetFileList(ShotRow row, bool isMain)
        {
            return isMain ? row.MainFiles : row.BackupFiles;
        }

        private static void EnsureTailOverrideSize(ShotRow row, bool isMain)
        {
            if (row == null)
            {
                return;
            }

            List<string> files = GetFileList(row, isMain);
            List<string> tails = GetTailOverrideList(row, isMain);
            while (tails.Count < files.Count)
            {
                tails.Add("");
            }
            while (tails.Count > files.Count)
            {
                tails.RemoveAt(tails.Count - 1);
            }
        }

        private static string SetTailOverride(RenamePlan entry, string value)
        {
            if (entry == null || entry.Row == null)
            {
                return "";
            }

            EnsureTailOverrideSize(entry.Row, entry.IsMain);
            List<string> tails = GetTailOverrideList(entry.Row, entry.IsMain);
            if (entry.FileIndex < 0 || entry.FileIndex >= tails.Count)
            {
                return "";
            }

            string normalized = NormalizeCustomTailText(value);
            tails[entry.FileIndex] = normalized;
            return normalized;
        }

        private static string GetUniqueCustomTail(RenamePlan selectedEntry, string requestedTail, IEnumerable<RenamePlan> existingPlan, int episode, int scene, bool keepExtensionCase)
        {
            string baseTail = NormalizeCustomTailText(requestedTail);
            if (selectedEntry == null || string.IsNullOrWhiteSpace(baseTail))
            {
                return baseTail;
            }

            for (int counter = 1; counter < 10000; counter++)
            {
                string candidateTail = counter == 1 ? baseTail : AppendCustomTailCounter(baseTail, counter);
                string candidatePath = BuildTargetPathForTail(selectedEntry, candidateTail, episode, scene, keepExtensionCase);
                bool duplicate = existingPlan != null && existingPlan.Any(delegate(RenamePlan entry)
                {
                    return entry != null &&
                        !IsSamePlanEntry(entry, selectedEntry) &&
                        StringComparer.OrdinalIgnoreCase.Equals(entry.TargetPath, candidatePath);
                });

                if (!duplicate)
                {
                    return candidateTail;
                }
            }

            return AppendCustomTailCounter(baseTail, Environment.TickCount & 0x7fffffff);
        }

        private static string AppendCustomTailCounter(string baseTail, int counter)
        {
            string suffix = Math.Max(2, counter).ToString();
            int maxBaseLength = Math.Max(1, 80 - suffix.Length);
            string trimmedBase = baseTail.Length > maxBaseLength ? baseTail.Substring(0, maxBaseLength).Trim() : baseTail;
            return NormalizeCustomTailText(trimmedBase + suffix);
        }

        private static string BuildTargetPathForTail(RenamePlan entry, string tailSegment, int episode, int scene, bool keepExtensionCase)
        {
            string directory = Path.GetDirectoryName(entry.OldPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Environment.CurrentDirectory;
            }

            string newName = GetMaterialFileName(episode, scene, entry.Shot, tailSegment, entry.OldPath, keepExtensionCase);
            return Path.GetFullPath(Path.Combine(directory, newName));
        }

        private static bool IsSamePlanEntry(RenamePlan left, RenamePlan right)
        {
            return left != null &&
                right != null &&
                left.Row == right.Row &&
                left.IsMain == right.IsMain &&
                left.FileIndex == right.FileIndex;
        }

        private static string GetUniquePathWithSuffix(string path, string suffix)
        {
            string directory = Path.GetDirectoryName(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            string safeSuffix = string.IsNullOrWhiteSpace(suffix) ? "_副本" : suffix;
            string first = Path.Combine(directory, stem + safeSuffix + extension);
            if (!File.Exists(first))
            {
                return first;
            }

            int counter = 2;
            while (true)
            {
                string candidate = Path.Combine(directory, string.Format("{0}{1}{2}{3}", stem, safeSuffix, counter, extension));
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
                counter++;
            }
        }

        private static string GetCellSummary(List<string> files)
        {
            if (files == null || files.Count == 0)
            {
                return "";
            }

            string[] names = files.Take(2).Select(Path.GetFileName).ToArray();
            if (files.Count > 2)
            {
                return string.Format("{0}条：{1} ...", files.Count, string.Join("；", names));
            }

            return string.Format("{0}条：{1}", files.Count, string.Join("；", names));
        }

        private void OnGridDragEnterOrOver(object sender, DragEventArgs e)
        {
            try
            {
                if (e != null && e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    int rowIndex;
                    int columnIndex;
                    if (TryGetDropTargetCell(e, out rowIndex, out columnIndex))
                    {
                        e.Effect = DragDropEffects.Copy;
                        SetDragHighlight(rowIndex, columnIndex);
                        statusLabel.Text = string.Format(
                            "松开鼠标后会导入到第 {0} 行 {1}。",
                            rowIndex + 1,
                            columnIndex == GridMainColumn ? GetMainColumnDisplayName() : GetBackupColumnDisplayName());
                    }
                    else
                    {
                        e.Effect = DragDropEffects.None;
                        ClearDragHighlight();
                    }
                }
                else if (e != null)
                {
                    e.Effect = DragDropEffects.None;
                    ClearDragHighlight();
                }
            }
            catch
            {
                if (e != null)
                {
                    e.Effect = DragDropEffects.None;
                }
                ClearDragHighlight();
            }
        }

        private void OnGridDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (e == null || e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    statusLabel.Text = "没有检测到可拖入的文件。";
                    return;
                }

                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null || paths.Length == 0)
                {
                    statusLabel.Text = "没有检测到可拖入的文件。";
                    return;
                }

                int rowIndex = -1;
                int columnIndex = -1;

                if (!TryGetDropTargetCell(e, out rowIndex, out columnIndex))
                {
                    statusLabel.Text = "请拖到 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 单元格。";
                    return;
                }

                AddFilesToShotCell(rowIndex, columnIndex, paths);
            }
            catch (Exception ex)
            {
                statusLabel.Text = "拖入失败：" + ex.Message;
            }
            finally
            {
                ClearDragHighlight();
            }
        }

        private bool TryGetDropTargetCell(DragEventArgs e, out int rowIndex, out int columnIndex)
        {
            rowIndex = -1;
            columnIndex = -1;

            if (grid == null || e == null)
            {
                return false;
            }

            Point point = grid.PointToClient(new Point(e.X, e.Y));
            DataGridView.HitTestInfo hit = grid.HitTest(point.X, point.Y);
            if (hit.RowIndex >= 0 && hit.RowIndex < rows.Count && (hit.ColumnIndex == GridMainColumn || hit.ColumnIndex == GridBackupColumn))
            {
                rowIndex = hit.RowIndex;
                columnIndex = hit.ColumnIndex;
                return true;
            }

            if (grid.CurrentCell != null &&
                grid.CurrentCell.RowIndex >= 0 &&
                grid.CurrentCell.RowIndex < rows.Count &&
                (grid.CurrentCell.ColumnIndex == GridMainColumn || grid.CurrentCell.ColumnIndex == GridBackupColumn))
            {
                rowIndex = grid.CurrentCell.RowIndex;
                columnIndex = grid.CurrentCell.ColumnIndex;
                return true;
            }

            return false;
        }

        private void AddFilesToShotCell(int rowIndex, int columnIndex, string[] paths)
        {
            if (rowIndex < 0 || rowIndex >= rows.Count)
            {
                statusLabel.Text = "目标行无效。";
                return;
            }

            if (columnIndex != GridMainColumn && columnIndex != GridBackupColumn)
            {
                statusLabel.Text = "请把视频拖到 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 列。";
                return;
            }

            List<string> videoPaths = GetVideoFilePaths(paths);
            if (videoPaths.Count == 0)
            {
                statusLabel.Text = "没有发现支持的视频文件。";
                return;
            }

            ShotRow targetRow = rows[rowIndex];
            List<string> targetFiles = columnIndex == GridMainColumn ? targetRow.MainFiles : targetRow.BackupFiles;
            List<string> targetTails = columnIndex == GridMainColumn ? targetRow.MainTailOverrides : targetRow.BackupTailOverrides;
            EnsureTailOverrideSize(targetRow, columnIndex == GridMainColumn);
            HashSet<string> existing = GetAllFileKeys();
            List<string> skippedNames = new List<string>();
            int added = 0;
            int skipped = 0;

            foreach (string path in videoPaths)
            {
                if (existing.Contains(path))
                {
                    skipped++;
                    skippedNames.Add(Path.GetFileName(path));
                    continue;
                }

                targetFiles.Add(path);
                targetTails.Add("");
                existing.Add(path);
                added++;
            }

            RenderGridRow(rowIndex);
            if (rowIndex >= 0 && rowIndex < grid.Rows.Count && columnIndex >= 0 && columnIndex < grid.Columns.Count)
            {
                grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
            }
            RefreshPreview();

            string columnName = columnIndex == GridMainColumn ? "主要素材" : "备用素材";
            statusLabel.Text = string.Format("第 {0} 行 {1} 已加入 {2} 个视频；跳过重复 {3} 个。", rowIndex + 1, columnName, added, skipped);
            if (skippedNames.Count > 0)
            {
                ShowDuplicateFileWarning(skippedNames);
            }
            ShowCurrentPlanIssueWarning();
        }

        private void ShowDuplicateFileWarning(List<string> duplicateNames)
        {
            if (duplicateNames == null || duplicateNames.Count == 0)
            {
                return;
            }

            List<string> lines = duplicateNames.Take(10).Select(name => " - " + name).ToList();
            if (duplicateNames.Count > 10)
            {
                lines.Add("... 另有 " + (duplicateNames.Count - 10) + " 个重复文件");
            }

            MessageBox.Show(
                this,
                "检测到重复文件，已自动跳过，不会重复加入表格。\r\n\r\n" + string.Join("\r\n", lines.ToArray()),
                "重复文件已跳过",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private void ShowCurrentPlanIssueWarning()
        {
            if (currentPlan == null || currentPlan.Count == 0)
            {
                return;
            }

            List<RenamePlan> issues = currentPlan.Where(IsBlockingIssue).ToList();
            if (issues.Count == 0)
            {
                return;
            }

            MessageBox.Show(
                this,
                BuildIssueMessage(issues),
                "检测到文件问题",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private static string BuildIssueMessage(List<RenamePlan> issues)
        {
            List<string> lines = new List<string>();
            lines.Add("检测到以下问题，请处理后再执行重命名：");
            lines.Add("");

            foreach (RenamePlan issue in issues.Take(10))
            {
                lines.Add(string.Format(
                    "第 {0} 行 {1} {2}：{3}",
                    issue.RowIndex,
                    issue.ColumnName,
                    issue.TailSegment,
                    issue.Status));
                lines.Add("  原文件：" + issue.OldName);
                lines.Add("  目标名：" + issue.NewName);
            }

            if (issues.Count > 10)
            {
                lines.Add("");
                lines.Add("... 另有 " + (issues.Count - 10) + " 个问题");
            }

            return string.Join("\r\n", lines.ToArray());
        }

        private static List<string> GetVideoFilePaths(string[] paths)
        {
            List<string> files = new List<string>();
            if (paths == null)
            {
                return files;
            }

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (File.Exists(path))
                {
                    string extension = Path.GetExtension(path);
                    if (extension != null && VideoExtensions.Contains(extension))
                    {
                        files.Add(Path.GetFullPath(path));
                    }
                    continue;
                }

                if (Directory.Exists(path))
                {
                    foreach (string file in Directory.GetFiles(path))
                    {
                        string extension = Path.GetExtension(file);
                        if (extension != null && VideoExtensions.Contains(extension))
                        {
                            files.Add(Path.GetFullPath(file));
                        }
                    }
                }
            }

            files.Sort(new NaturalPathComparer());
            return files;
        }

        private HashSet<string> GetAllFileKeys()
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ShotRow row in rows)
            {
                foreach (string file in row.MainFiles)
                {
                    keys.Add(file);
                }
                foreach (string file in row.BackupFiles)
                {
                    keys.Add(file);
                }
            }
            return keys;
        }

        private void OnGridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (rendering || e == null || e.RowIndex < 0 || e.RowIndex >= rows.Count)
            {
                return;
            }

            ShotRow row = rows[e.RowIndex];
            if (e.ColumnIndex == GridSceneColumn)
            {
                object value = grid.Rows[e.RowIndex].Cells[GridSceneColumn].Value;
                int parsed;
                if (value != null && int.TryParse(value.ToString(), out parsed) && parsed > 0)
                {
                    row.Scene = parsed;
                }
                else
                {
                    row.Scene = GetEffectiveScene(row, GetDefaultScene(), true);
                    grid.Rows[e.RowIndex].Cells[GridSceneColumn].Value = row.Scene;
                    statusLabel.Text = "A 列场号必须是大于 0 的整数，已保留原场号。";
                }
            }
            else if (e.ColumnIndex == GridShotColumn)
            {
                object value = grid.Rows[e.RowIndex].Cells[GridShotColumn].Value;
                int parsed;
                if (value != null && int.TryParse(value.ToString(), out parsed) && parsed > 0)
                {
                    row.Sequence = parsed;
                }
                else
                {
                    row.Sequence = Math.Max(1, row.Sequence);
                    grid.Rows[e.RowIndex].Cells[GridShotColumn].Value = row.Sequence;
                    statusLabel.Text = (IsRowSceneEnabled() ? "B" : "A") + " 列镜号必须是大于 0 的整数，已保留原镜号。";
                }
            }
            else if (e.ColumnIndex == GridProgressColumn)
            {
                grid.Rows[e.RowIndex].Cells[GridProgressColumn].Value = row.ProgressPercent;
            }

            RenderAll();
        }

        private void ImportSelectedCell()
        {
            if (grid.CurrentCell == null || (grid.CurrentCell.ColumnIndex != GridMainColumn && grid.CurrentCell.ColumnIndex != GridBackupColumn))
            {
                MessageBox.Show("请先选中一个 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 单元格。", "未选择素材格", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择视频文件";
                dialog.Multiselect = true;
                dialog.Filter = "视频文件|*.mp4;*.mov;*.m4v;*.avi;*.mkv;*.wmv;*.flv;*.webm;*.mts;*.m2ts;*.3gp;*.mpeg;*.mpg|所有文件|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddFilesToShotCell(grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex, dialog.FileNames);
                }
            }
        }

        private void DeleteSelectedPreviewRecord()
        {
            if (previewList.SelectedItems.Count == 0)
            {
                statusLabel.Text = "请先在底部预览中选中一条素材记录。";
                return;
            }

            RenamePlan entry = previewList.SelectedItems[0].Tag as RenamePlan;
            if (entry == null || entry.Row == null)
            {
                statusLabel.Text = "选中的预览记录无效。";
                return;
            }

            List<string> files = entry.IsMain ? entry.Row.MainFiles : entry.Row.BackupFiles;
            List<string> tails = entry.IsMain ? entry.Row.MainTailOverrides : entry.Row.BackupTailOverrides;
            EnsureTailOverrideSize(entry.Row, entry.IsMain);
            if (entry.FileIndex < 0 || entry.FileIndex >= files.Count)
            {
                statusLabel.Text = "选中的素材记录已经变化，请重新选择。";
                RefreshPreview();
                return;
            }

            string removed = Path.GetFileName(files[entry.FileIndex]);
            files.RemoveAt(entry.FileIndex);
            if (entry.FileIndex >= 0 && entry.FileIndex < tails.Count)
            {
                tails.RemoveAt(entry.FileIndex);
            }
            RenderGridRow(entry.RowIndex - 1);
            RefreshPreview();
            statusLabel.Text = "已删除单条记录：" + removed;
        }

        private int GetCurrentGridRowIndex()
        {
            if (grid.CurrentCell != null && grid.CurrentCell.RowIndex >= 0 && grid.CurrentCell.RowIndex < rows.Count)
            {
                return grid.CurrentCell.RowIndex;
            }

            if (grid.SelectedCells.Count > 0)
            {
                int rowIndex = grid.SelectedCells[0].RowIndex;
                if (rowIndex >= 0 && rowIndex < rows.Count)
                {
                    return rowIndex;
                }
            }

            return -1;
        }

        private int GetNextShotSequence()
        {
            int max = 0;
            foreach (ShotRow row in rows)
            {
                if (row.Sequence > max)
                {
                    max = row.Sequence;
                }
            }

            return Math.Max(1, max + 1);
        }

        private void SelectGridCell(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return;
            }

            if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                columnIndex = GetDefaultGridFocusColumn();
            }

            if (columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
            {
                columnIndex = GetDefaultGridFocusColumn();
            }

            if (columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
            {
                for (int index = 0; index < grid.Columns.Count; index++)
                {
                    if (grid.Columns[index].Visible)
                    {
                        columnIndex = index;
                        break;
                    }
                }
            }

            if (columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
            {
                return;
            }

            grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
        }

        private void AddEmptyRow()
        {
            int currentRowIndex = GetCurrentGridRowIndex();
            int insertIndex = currentRowIndex >= 0 ? currentRowIndex + 1 : rows.Count;
            rows.Insert(insertIndex, new ShotRow { Scene = GetDefaultScene(), Sequence = GetNextShotSequence() });
            RenderAll();
            SelectGridCell(insertIndex, GetDefaultGridFocusColumn());
            statusLabel.Text = IsRowSceneEnabled()
                ? "已新增一条空记录；A 列场号、B 列镜号可直接改成任意正整数。"
                : "已新增一条空记录；A 列镜号可直接改成任意正整数。";
        }

        private void MoveCurrentRow(int direction)
        {
            int currentRowIndex = GetCurrentGridRowIndex();
            if (currentRowIndex < 0)
            {
                statusLabel.Text = "请先选中要移动的行。";
                return;
            }

            int targetIndex = currentRowIndex + direction;
            if (targetIndex < 0 || targetIndex >= rows.Count)
            {
                statusLabel.Text = "当前行已经在边界位置。";
                return;
            }

            int columnIndex = grid.CurrentCell != null ? grid.CurrentCell.ColumnIndex : 0;
            ShotRow moving = rows[currentRowIndex];
            rows.RemoveAt(currentRowIndex);
            rows.Insert(targetIndex, moving);
            RenderAll();
            SelectGridCell(targetIndex, columnIndex);
            statusLabel.Text = IsRowSceneEnabled()
                ? "已移动当前行；A 列场号、B 列镜号保持不变。"
                : "已移动当前行；A 列镜号保持不变。";
        }

        private void DeleteCurrentRow()
        {
            int currentRowIndex = GetCurrentGridRowIndex();
            if (currentRowIndex < 0)
            {
                statusLabel.Text = "请先选中要删除的行。";
                return;
            }

            ShotRow row = rows[currentRowIndex];
            bool hasContent = row.MainFiles.Count > 0 || row.BackupFiles.Count > 0;
            if (hasContent)
            {
                DialogResult confirm = MessageBox.Show(
                    this,
                    "是否删除第 " + (currentRowIndex + 1) + " 行及其中所有素材记录？\r\n\r\n该操作只会从表格中移除记录，不会删除磁盘上的视频文件。",
                    "确认删除当前行",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    return;
                }
            }

            int columnIndex = grid.CurrentCell != null ? grid.CurrentCell.ColumnIndex : 0;
            rows.RemoveAt(currentRowIndex);
            if (rows.Count == 0)
            {
                rows.Add(new ShotRow { Scene = GetDefaultScene(), Sequence = 1 });
            }

            RenderAll();
            int nextRowIndex = Math.Min(currentRowIndex, rows.Count - 1);
            SelectGridCell(nextRowIndex, columnIndex);
            statusLabel.Text = IsRowSceneEnabled()
                ? "已删除当前行，下方行已上移；A 列场号、B 列镜号保持不变。"
                : "已删除当前行，下方行已上移；A 列镜号保持不变。";
        }

        private void ClearSelectedCellFiles()
        {
            if (grid.CurrentCell == null)
            {
                return;
            }

            int rowIndex = grid.CurrentCell.RowIndex;
            int columnIndex = grid.CurrentCell.ColumnIndex;
            if (rowIndex < 0 || rowIndex >= rows.Count)
            {
                return;
            }

            if (columnIndex == GridMainColumn)
            {
                rows[rowIndex].MainFiles.Clear();
                rows[rowIndex].MainTailOverrides.Clear();
            }
            else if (columnIndex == GridBackupColumn)
            {
                rows[rowIndex].BackupFiles.Clear();
                rows[rowIndex].BackupTailOverrides.Clear();
            }
            else
            {
                statusLabel.Text = "当前单元格不是素材列。";
                return;
            }

            RenderGridRow(rowIndex);
            RefreshPreview();
            grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
        }

        private void RemoveEmptyTailRows()
        {
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                ShotRow row = rows[i];
                if (row.MainFiles.Count == 0 && row.BackupFiles.Count == 0)
                {
                    rows.RemoveAt(i);
                }
                else
                {
                    break;
                }
            }

            if (rows.Count == 0)
            {
                for (int i = 1; i <= AppInfo.DefaultRowCount; i++)
                {
                    rows.Add(new ShotRow { Scene = GetDefaultScene(), Sequence = i });
                }
            }

            RenderAll();
            statusLabel.Text = IsRowSceneEnabled()
                ? "已删除尾部空白行；A 列场号、B 列镜号保持不变。"
                : "已删除尾部空白行；A 列镜号保持不变。";
        }

        private void ClearAllMaterials()
        {
            DialogResult confirm = MessageBox.Show(this, "是否清空表格中所有素材？场号和镜号会保留，进度会清零。", "确认清空", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            foreach (ShotRow row in rows)
            {
                row.MainFiles.Clear();
                row.BackupFiles.Clear();
                row.MainTailOverrides.Clear();
                row.BackupTailOverrides.Clear();
                row.ProgressPercent = 0;
            }
            RenderAll();
        }

        private static string EncodeHistoryValue(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string DecodeHistoryValue(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
        }

        private void SaveRenameHistory(List<RenameOperation> operations)
        {
            if (operations == null || operations.Count == 0)
            {
                return;
            }

            List<string> lines = new List<string>();
            lines.Add("VideoMaterialRenamerHistoryV1");
            foreach (RenameOperation op in operations)
            {
                lines.Add(string.Join("\t", new string[]
                {
                    op.RowIndex.ToString(),
                    op.IsMain ? "1" : "0",
                    op.FileIndex.ToString(),
                    EncodeHistoryValue(op.OriginalPath),
                    EncodeHistoryValue(op.RenamedPath)
                }));
            }

            File.WriteAllLines(historyPath, lines.ToArray(), Encoding.UTF8);
        }

        private List<RenameOperation> LoadRenameHistory()
        {
            List<RenameOperation> operations = new List<RenameOperation>();
            if (!File.Exists(historyPath))
            {
                return operations;
            }

            string[] lines = File.ReadAllLines(historyPath, Encoding.UTF8);
            if (lines.Length == 0 || lines[0] != "VideoMaterialRenamerHistoryV1")
            {
                return operations;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('\t');
                if (parts.Length != 5)
                {
                    continue;
                }

                int rowIndex;
                int fileIndex;
                if (!int.TryParse(parts[0], out rowIndex) || !int.TryParse(parts[2], out fileIndex))
                {
                    continue;
                }

                operations.Add(new RenameOperation
                {
                    Row = rowIndex >= 1 && rowIndex <= rows.Count ? rows[rowIndex - 1] : null,
                    RowIndex = rowIndex,
                    IsMain = parts[1] == "1",
                    FileIndex = fileIndex,
                    OriginalPath = DecodeHistoryValue(parts[3]),
                    RenamedPath = DecodeHistoryValue(parts[4])
                });
            }

            return operations;
        }

        private void RestoreLastRename()
        {
            bool fromMemory = undoStack.Count > 0;
            List<RenameOperation> operations = fromMemory ? undoStack.Peek() : LoadRenameHistory();
            if (operations.Count == 0)
            {
                MessageBox.Show(this, "没有可还原的重命名记录。", "无法还原", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                this,
                "即将把上次成功重命名的 " + operations.Count + " 个文件还原为原文件名，是否继续？",
                "确认还原",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            List<string> failures = new List<string>();
            for (int i = operations.Count - 1; i >= 0; i--)
            {
                RenameOperation op = operations[i];
                try
                {
                    if (!File.Exists(op.RenamedPath))
                    {
                        failures.Add(Path.GetFileName(op.RenamedPath) + ": 当前文件不存在");
                        continue;
                    }

                    if (File.Exists(op.OriginalPath) && !StringComparer.OrdinalIgnoreCase.Equals(op.OriginalPath, op.RenamedPath))
                    {
                        failures.Add(Path.GetFileName(op.RenamedPath) + ": 原文件名已被占用");
                        continue;
                    }

                    if (!StringComparer.OrdinalIgnoreCase.Equals(op.RenamedPath, op.OriginalPath))
                    {
                        File.Move(op.RenamedPath, op.OriginalPath);
                    }

                    if (op.Row != null)
                    {
                        List<string> files = op.IsMain ? op.Row.MainFiles : op.Row.BackupFiles;
                        if (op.FileIndex >= 0 && op.FileIndex < files.Count && StringComparer.OrdinalIgnoreCase.Equals(files[op.FileIndex], op.RenamedPath))
                        {
                            files[op.FileIndex] = op.OriginalPath;
                        }
                        else
                        {
                            int currentIndex = files.FindIndex(p => StringComparer.OrdinalIgnoreCase.Equals(p, op.RenamedPath));
                            if (currentIndex >= 0)
                            {
                                files[currentIndex] = op.OriginalPath;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(Path.GetFileName(op.RenamedPath) + ": " + ex.Message);
                }
            }

            if (failures.Count == 0)
            {
                if (fromMemory)
                {
                    undoStack.Pop();
                }
                if (File.Exists(historyPath))
                {
                    File.Delete(historyPath);
                }
                RenderAll();
                MessageBox.Show(this, "已取消上次命名。", "取消命名完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            RenderAll();
            MessageBox.Show(this, string.Join("\r\n", failures.Take(8).ToArray()), "部分文件还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SetOperationUiEnabled(bool enabled)
        {
            operationRunning = !enabled;
            UseWaitCursor = !enabled;
            foreach (Control control in Controls)
            {
                if (object.ReferenceEquals(control, statusLabel))
                {
                    continue;
                }
                control.Enabled = enabled;
            }

            if (statusLabel != null)
            {
                statusLabel.Enabled = true;
            }
        }

        private static string FindFfmpegPath()
        {
            List<string> candidates = new List<string>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentDir = Environment.CurrentDirectory;
            candidates.Add(Path.Combine(baseDir, "ffmpeg.exe"));
            candidates.Add(Path.Combine(baseDir, "tools", "ffmpeg.exe"));
            candidates.Add(Path.Combine(currentDir, "ffmpeg.exe"));
            candidates.Add(Path.Combine(currentDir, "tools", "ffmpeg.exe"));

            string pathText = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string directory in pathText.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    candidates.Add(Path.Combine(directory.Trim(), "ffmpeg.exe"));
                }
                catch
                {
                }
            }

            string embeddedFfmpeg = ExtractEmbeddedFfmpeg();
            if (!string.IsNullOrWhiteSpace(embeddedFfmpeg))
            {
                candidates.Add(embeddedFfmpeg);
            }

            foreach (string candidate in candidates)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private static string ExtractEmbeddedFfmpeg()
        {
            const string resourceName = "VideoMaterialRenamer.ffmpeg.exe";
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream resource = assembly.GetManifestResourceStream(resourceName))
                {
                    if (resource == null)
                    {
                        return "";
                    }

                    string toolsDir = Path.Combine(AppInfo.AppDataDirectory, "tools");
                    Directory.CreateDirectory(toolsDir);
                    string ffmpegPath = Path.Combine(toolsDir, "ffmpeg.exe");
                    long resourceLength = resource.CanSeek ? resource.Length : -1;
                    if (File.Exists(ffmpegPath) && resourceLength > 0)
                    {
                        FileInfo existing = new FileInfo(ffmpegPath);
                        if (existing.Length == resourceLength)
                        {
                            return ffmpegPath;
                        }
                    }

                    string tempPath = ffmpegPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (FileStream output = File.Create(tempPath))
                    {
                        resource.CopyTo(output);
                    }

                    if (File.Exists(ffmpegPath))
                    {
                        File.Delete(ffmpegPath);
                    }
                    File.Move(tempPath, ffmpegPath);
                    return ffmpegPath;
                }
            }
            catch
            {
                return "";
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static string TrimProcessLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "未知错误";
            }

            text = text.Trim();
            if (text.Length <= 500)
            {
                return text;
            }

            return text.Substring(text.Length - 500);
        }

        private static double ParseClockSeconds(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return -1;
            }

            Match match = Regex.Match(value.Trim(), @"(?<h>\d+):(?<m>\d+):(?<s>\d+(?:\.\d+)?)");
            if (!match.Success)
            {
                return -1;
            }

            double seconds;
            if (!double.TryParse(match.Groups["s"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds))
            {
                return -1;
            }

            return int.Parse(match.Groups["h"].Value) * 3600 + int.Parse(match.Groups["m"].Value) * 60 + seconds;
        }

        private static double ParseProgressSeconds(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return -1;
            }

            string[] parts = line.Split(new char[] { '=' }, 2);
            if (parts.Length != 2)
            {
                return -1;
            }

            string key = parts[0].Trim();
            string value = parts[1].Trim();
            if (key == "out_time" || key == "out_time_str")
            {
                return ParseClockSeconds(value);
            }

            if (key == "out_time_ms" || key == "out_time_us")
            {
                double raw;
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out raw))
                {
                    return raw / 1000000.0;
                }
            }

            return -1;
        }

        private static double ParseDurationSeconds(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return -1;
            }

            Match match = Regex.Match(line, @"Duration:\s*(?<time>\d+:\d+:\d+(?:\.\d+)?)");
            if (!match.Success)
            {
                return -1;
            }

            return ParseClockSeconds(match.Groups["time"].Value);
        }

        private static RenamePlan CloneRenamePlan(RenamePlan entry)
        {
            if (entry == null)
            {
                return null;
            }

            return new RenamePlan
            {
                Row = entry.Row,
                RowIndex = entry.RowIndex,
                ColumnName = entry.ColumnName,
                IsMain = entry.IsMain,
                FileIndex = entry.FileIndex,
                Scene = entry.Scene,
                Shot = entry.Shot,
                Take = entry.Take,
                TailSegment = entry.TailSegment,
                CustomTailText = entry.CustomTailText,
                HasCustomTail = entry.HasCustomTail,
                OldPath = entry.OldPath,
                TargetPath = entry.TargetPath,
                OldName = entry.OldName,
                NewName = entry.NewName,
                Status = entry.Status
            };
        }

        private static List<RenamePlan> PrepareExportPlan(List<RenamePlan> sourcePlan, ExportOutputMode outputMode)
        {
            List<RenamePlan> prepared = new List<RenamePlan>();
            Dictionary<string, bool> targets = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (RenamePlan source in sourcePlan)
            {
                RenamePlan entry = CloneRenamePlan(source);
                if (entry == null)
                {
                    continue;
                }

                if (outputMode == ExportOutputMode.SaveAsNewFile && StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
                {
                    entry.TargetPath = GetUniquePathWithSuffix(entry.TargetPath, "_1080p");
                    entry.NewName = Path.GetFileName(entry.TargetPath);
                    entry.Status = "另存为新文件";
                }

                if (outputMode == ExportOutputMode.SaveAsNewFile &&
                    File.Exists(entry.TargetPath) &&
                    !StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
                {
                    throw new IOException("目标文件已存在：" + entry.NewName);
                }

                if (targets.ContainsKey(entry.TargetPath))
                {
                    throw new IOException("新文件名重复：" + entry.NewName);
                }

                targets[entry.TargetPath] = true;
                prepared.Add(entry);
            }

            return prepared;
        }

        private static string NormalizeWatermarkText(string text)
        {
            string value = Path.GetFileName(text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsControl(chars[i]))
                {
                    chars[i] = ' ';
                }
            }

            return new string(chars).Trim();
        }

        private static string EscapeFfmpegFilterValue(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace(":", "\\:")
                .Replace(",", "\\,")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Replace(";", "\\;");
        }

        private static string GetWatermarkFontFile()
        {
            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] candidates = new string[]
            {
                Path.Combine(windowsDir, "Fonts", "msyh.ttc"),
                Path.Combine(windowsDir, "Fonts", "msyhbd.ttc"),
                Path.Combine(windowsDir, "Fonts", "simhei.ttf"),
                Path.Combine(windowsDir, "Fonts", "arial.ttf")
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        return candidate.Replace('\\', '/');
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private static string BuildVideoFilter(string watermarkText)
        {
            string baseFilter = "scale=1080:1920:flags=bicubic,setsar=1";
            string normalized = NormalizeWatermarkText(watermarkText);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return baseFilter;
            }

            string fontFile = GetWatermarkFontFile();
            string fontPart = string.IsNullOrWhiteSpace(fontFile) ? "" : "fontfile='" + EscapeFfmpegFilterValue(fontFile) + "':";
            string text = EscapeFfmpegFilterValue(normalized);
            return baseFilter +
                ",drawtext=" + fontPart +
                "text='" + text + "':" +
                "x=24:y=24:fontsize=24:" +
                "fontcolor=white@0.92:" +
                "box=1:boxcolor=black@0.55:boxborderw=10:" +
                "expansion=none";
        }

        private static string BuildFfmpegArguments(string inputPath, string outputPath, bool copyAudio, string watermarkText)
        {
            List<string> args = new List<string>();
            args.Add("-hide_banner");
            args.Add("-nostdin");
            args.Add("-nostats");
            args.Add("-y");
            args.Add("-i");
            args.Add(QuoteArgument(inputPath));
            args.Add("-vf");
            args.Add(QuoteArgument(BuildVideoFilter(watermarkText)));
            args.Add("-c:v");
            args.Add("libx264");
            args.Add("-preset");
            args.Add("veryfast");
            args.Add("-crf");
            args.Add("20");
            args.Add("-pix_fmt");
            args.Add("yuv420p");
            args.Add("-threads");
            args.Add("0");
            args.Add("-progress");
            args.Add("pipe:1");

            if (copyAudio)
            {
                args.Add("-c:a");
                args.Add("copy");
            }
            else
            {
                args.Add("-c:a");
                args.Add("aac");
                args.Add("-b:a");
                args.Add("160k");
            }

            args.Add(QuoteArgument(outputPath));
            return string.Join(" ", args.ToArray());
        }

        private static void RunFfmpegExport(string ffmpegPath, string inputPath, string outputPath, bool copyAudio, string watermarkText, Action<int> progressCallback)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = ffmpegPath;
            startInfo.Arguments = BuildFfmpegArguments(inputPath, outputPath, copyAudio, watermarkText);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;

            using (Process process = Process.Start(startInfo))
            {
                if (progressCallback != null)
                {
                    progressCallback(5);
                }

                StringBuilder error = new StringBuilder();
                double durationSeconds = -1;
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        error.AppendLine(e.Data);
                        double parsedDuration = ParseDurationSeconds(e.Data);
                        if (parsedDuration > 0)
                        {
                            durationSeconds = parsedDuration;
                        }
                    }
                };
                process.BeginErrorReadLine();

                string line;
                bool sawProgress = false;
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    if (progressCallback != null)
                    {
                        if (!sawProgress && (line.StartsWith("out_time=", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("out_time_us=", StringComparison.OrdinalIgnoreCase)))
                        {
                            sawProgress = true;
                        }

                        double outputSeconds = ParseProgressSeconds(line);
                        if (outputSeconds >= 0)
                        {
                            if (durationSeconds > 0)
                            {
                                int percent = 5 + (int)Math.Round(Math.Min(1.0, outputSeconds / durationSeconds) * 90.0);
                                progressCallback(Math.Max(5, Math.Min(95, percent)));
                            }
                            else
                            {
                                progressCallback(sawProgress ? 50 : 15);
                            }
                        }
                        else if (line.Trim() == "progress=end")
                        {
                            progressCallback(95);
                        }
                    }
                }

                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new Exception(TrimProcessLog(error.ToString()));
                }
            }
        }

        private static void ReplaceOriginalWithExport(string tempPath, RenamePlan entry)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
            {
                File.Replace(tempPath, entry.OldPath, null);
                return;
            }

            if (File.Exists(entry.TargetPath))
            {
                throw new IOException("目标文件已存在。");
            }

            File.Move(tempPath, entry.TargetPath);
            try
            {
                File.Delete(entry.OldPath);
            }
            catch (Exception ex)
            {
                throw new IOException("已生成新文件，但原文件删除失败：" + ex.Message);
            }
        }

        private static void ExportOneVideoTo1080p(string ffmpegPath, RenamePlan entry, ExportOutputMode outputMode, bool watermarkEnabled, Action<int> progressCallback)
        {
            if (entry == null)
            {
                throw new InvalidOperationException("导出记录无效。");
            }

            if (!File.Exists(entry.OldPath))
            {
                throw new FileNotFoundException("源文件不存在。", entry.OldPath);
            }

            if (outputMode == ExportOutputMode.SaveAsNewFile &&
                File.Exists(entry.TargetPath) &&
                !StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
            {
                throw new IOException("目标文件已存在。");
            }

            string directory = outputMode == ExportOutputMode.OverwriteOriginal ? Path.GetDirectoryName(entry.OldPath) : Path.GetDirectoryName(entry.TargetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string outputPath = entry.TargetPath;
            string tempPath = "";
            string watermarkText = watermarkEnabled ? entry.NewName : "";
            if (outputMode == ExportOutputMode.OverwriteOriginal)
            {
                tempPath = Path.Combine(directory, ".vmr_" + Guid.NewGuid().ToString("N") + Path.GetExtension(entry.TargetPath));
                outputPath = tempPath;
            }

            try
            {
                try
                {
                    RunFfmpegExport(ffmpegPath, entry.OldPath, outputPath, true, watermarkText, progressCallback);
                }
                catch
                {
                    if (File.Exists(outputPath))
                    {
                        File.Delete(outputPath);
                    }
                    if (progressCallback != null)
                    {
                        progressCallback(0);
                    }
                    RunFfmpegExport(ffmpegPath, entry.OldPath, outputPath, false, watermarkText, progressCallback);
                }

                if (outputMode == ExportOutputMode.OverwriteOriginal)
                {
                    ReplaceOriginalWithExport(tempPath, entry);
                    tempPath = "";
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void StartExport1080p(List<RenamePlan> plan, string ffmpegPath, ExportOutputMode outputMode, bool watermarkEnabled)
        {
            SetProgressColumnVisible(true);
            ResetProgressBars();
            SetOperationUiEnabled(false);
            statusLabel.Text = outputMode == ExportOutputMode.OverwriteOriginal
                ? (watermarkEnabled ? "正在导出并添加文件名水印，请等待..." : "正在导出并覆盖原文件，请等待...")
                : (watermarkEnabled ? "正在导出 1080x1920 新文件并添加文件名水印，请等待..." : "正在导出 1080x1920 新文件，请等待...");

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> failures = new List<string>();
                List<RenameOperation> successfulOperations = new List<RenameOperation>();
                Dictionary<ShotRow, int> rowTotals = new Dictionary<ShotRow, int>();
                Dictionary<ShotRow, int> rowCompleted = new Dictionary<ShotRow, int>();
                foreach (RenamePlan item in plan)
                {
                    if (item.Row == null)
                    {
                        continue;
                    }
                    if (!rowTotals.ContainsKey(item.Row))
                    {
                        rowTotals[item.Row] = 0;
                        rowCompleted[item.Row] = 0;
                    }
                    rowTotals[item.Row]++;
                }

                int total = plan.Count;
                int index = 0;

                foreach (RenamePlan entry in plan)
                {
                    index++;
                    int currentIndex = index;
                    QueueOnUi(delegate
                    {
                        statusLabel.Text = string.Format("正在导出 {0}/{1}：{2}", currentIndex, total, entry.NewName);
                    });

                    try
                    {
                        Action<int> progressCallback = delegate(int percent)
                        {
                            if (entry.Row == null)
                            {
                                return;
                            }

                            int completed = rowCompleted.ContainsKey(entry.Row) ? rowCompleted[entry.Row] : 0;
                            int rowTotal = rowTotals.ContainsKey(entry.Row) ? Math.Max(1, rowTotals[entry.Row]) : 1;
                            int safePercent = Math.Max(0, Math.Min(100, percent));
                            int rowPercent = (int)Math.Max(0, Math.Min(100, Math.Round((completed + safePercent / 100.0) * 100.0 / rowTotal)));
                            QueueOnUi(delegate
                            {
                                entry.Row.ProgressPercent = rowPercent;
                                RenderGridProgress(entry.RowIndex - 1);
                                statusLabel.Text = string.Format("正在导出 {0}/{1}：{2}（{3}%）", currentIndex, total, entry.NewName, safePercent);
                            });
                        };

                        ExportOneVideoTo1080p(ffmpegPath, entry, outputMode, watermarkEnabled, progressCallback);
                        if (entry.Row != null && rowCompleted.ContainsKey(entry.Row))
                        {
                            rowCompleted[entry.Row]++;
                            progressCallback(100);
                        }
                        successfulOperations.Add(new RenameOperation
                        {
                            Row = entry.Row,
                            RowIndex = entry.RowIndex,
                            IsMain = entry.IsMain,
                            FileIndex = entry.FileIndex,
                            OriginalPath = entry.OldPath,
                            RenamedPath = entry.TargetPath
                        });
                    }
                    catch (Exception ex)
                    {
                        failures.Add(entry.OldName + ": " + ex.Message);
                        if (entry.Row != null && rowCompleted.ContainsKey(entry.Row))
                        {
                            rowCompleted[entry.Row]++;
                        }
                    }
                }

                QueueOnUi(delegate
                {
                    foreach (RenameOperation op in successfulOperations)
                    {
                        if (op.Row == null)
                        {
                            continue;
                        }

                        List<string> files = op.IsMain ? op.Row.MainFiles : op.Row.BackupFiles;
                        if (op.FileIndex >= 0 && op.FileIndex < files.Count && StringComparer.OrdinalIgnoreCase.Equals(files[op.FileIndex], op.OriginalPath))
                        {
                            files[op.FileIndex] = op.RenamedPath;
                        }
                        else
                        {
                            int currentIndex = files.FindIndex(p => StringComparer.OrdinalIgnoreCase.Equals(p, op.OriginalPath));
                            if (currentIndex >= 0)
                            {
                                files[currentIndex] = op.RenamedPath;
                            }
                        }
                    }

                    RenderAll();
                    SetProgressColumnVisible(false);
                    SetOperationUiEnabled(true);

                    if (failures.Count > 0)
                    {
                        MessageBox.Show(this, string.Join("\r\n", failures.Take(8).ToArray()), "部分视频导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        string finishMessage = outputMode == ExportOutputMode.OverwriteOriginal
                            ? "已处理 " + successfulOperations.Count + " 个视频，并覆盖原文件。"
                            : "已导出 " + successfulOperations.Count + " 个 1080x1920 新文件，原始素材已保留。";
                        MessageBox.Show(this, finishMessage, "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                });
            });
        }

        private bool TryChooseExportOutputMode(out ExportOutputMode outputMode)
        {
            outputMode = ExportOutputMode.OverwriteOriginal;
            DialogResult result = MessageBox.Show(
                this,
                "请选择 1080x1920 导出后的保存方式：\r\n\r\n是：覆盖原文件（默认，原文件会被替换）\r\n否：另存为新文件（保留原文件）\r\n取消：不执行",
                "选择导出保存方式",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (result == DialogResult.Cancel)
            {
                return false;
            }

            outputMode = result == DialogResult.Yes ? ExportOutputMode.OverwriteOriginal : ExportOutputMode.SaveAsNewFile;
            return true;
        }

        private void RenameFiles()
        {
            if (operationRunning)
            {
                statusLabel.Text = "当前正在处理视频，请等待完成。";
                return;
            }

            RefreshPreview();
            if (currentPlan.Count == 0)
            {
                MessageBox.Show(this, "请先把视频拖到 " + GetMainColumnLetter() + " 或 " + GetBackupColumnLetter() + " 列。", "没有素材", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<RenamePlan> badRows = currentPlan.Where(IsBlockingIssue).ToList();
            if (badRows.Count > 0)
            {
                MessageBox.Show(this, BuildIssueMessage(badRows), "存在文件问题", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<string> preview = currentPlan.Take(8).Select(p => p.OldName + "  ->  " + p.NewName).ToList();
            if (currentPlan.Count > 8)
            {
                preview.Add("... 另有 " + (currentPlan.Count - 8) + " 个文件");
            }

            bool export1080p = IsExport1080pEnabled();
            if (export1080p)
            {
                ExportOutputMode outputMode;
                if (!TryChooseExportOutputMode(out outputMode))
                {
                    return;
                }

                string ffmpegPath = FindFfmpegPath();
                if (string.IsNullOrWhiteSpace(ffmpegPath))
                {
                    MessageBox.Show(
                        this,
                        "没有找到 ffmpeg.exe，无法导出 1080x1920 视频。\r\n\r\n请把 ffmpeg.exe 放到软件 EXE 同目录，或放到软件目录下的 tools 文件夹中，然后重新运行。",
                        "缺少 FFmpeg",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                List<RenamePlan> exportPlan;
                try
                {
                    exportPlan = PrepareExportPlan(currentPlan.ToList(), outputMode);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "导出目标异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                List<string> exportPreview = exportPlan.Take(8).Select(p => p.OldName + "  ->  " + p.NewName).ToList();
                if (exportPlan.Count > 8)
                {
                    exportPreview.Add("... 另有 " + (exportPlan.Count - 8) + " 个文件");
                }

                string modeText = outputMode == ExportOutputMode.OverwriteOriginal
                    ? "即将导出 1080x1920 并覆盖原文件。该操作不会保留原始 720p 文件，是否继续？"
                    : "即将导出 1080x1920 新视频文件，原始素材会保留。是否继续？";
                bool watermarkEnabled = IsExportWatermarkEnabled();
                if (watermarkEnabled)
                {
                    modeText += "\r\n\r\n导出画面左上角会加入新文件名水印。";
                }
                string exportMessage = modeText + "\r\n\r\n" + string.Join("\r\n", exportPreview.ToArray());
                DialogResult exportConfirm = MessageBox.Show(this, exportMessage, "确认导出1080p", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (exportConfirm != DialogResult.Yes)
                {
                    return;
                }

                StartExport1080p(exportPlan, ffmpegPath, outputMode, watermarkEnabled);
                return;
            }

            string message = "即将直接修改原视频文件名，是否继续？\r\n\r\n" + string.Join("\r\n", preview.ToArray());
            DialogResult confirm = MessageBox.Show(this, message, "确认重命名", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            List<string> failures = new List<string>();
            List<RenameOperation> successfulOperations = new List<RenameOperation>();
            foreach (RenamePlan entry in currentPlan)
            {
                try
                {
                    string originalPath = entry.OldPath;
                    string renamedPath = entry.TargetPath;
                    if (!StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
                    {
                        File.Move(entry.OldPath, entry.TargetPath);
                    }

                    if (entry.IsMain)
                    {
                        entry.Row.MainFiles[entry.FileIndex] = entry.TargetPath;
                    }
                    else
                    {
                        entry.Row.BackupFiles[entry.FileIndex] = entry.TargetPath;
                    }

                    if (!StringComparer.OrdinalIgnoreCase.Equals(originalPath, renamedPath))
                    {
                        successfulOperations.Add(new RenameOperation
                        {
                            Row = entry.Row,
                            RowIndex = entry.RowIndex,
                            IsMain = entry.IsMain,
                            FileIndex = entry.FileIndex,
                            OriginalPath = originalPath,
                            RenamedPath = renamedPath
                        });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(entry.OldName + ": " + ex.Message);
                }
            }

            if (successfulOperations.Count > 0)
            {
                undoStack.Push(successfulOperations);
                SaveRenameHistory(successfulOperations);
            }

            RenderAll();

            if (failures.Count > 0)
            {
                MessageBox.Show(this, string.Join("\r\n", failures.Take(8).ToArray()), "部分文件重命名失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                MessageBox.Show(this, "已处理 " + currentPlan.Count + " 个视频文件。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (previewColumnResizeTimer != null)
            {
                previewColumnResizeTimer.Stop();
                previewColumnResizeTimer.Dispose();
                previewColumnResizeTimer = null;
            }

            if (ownedDetailImage != null)
            {
                ownedDetailImage.Dispose();
                ownedDetailImage = null;
            }

            foreach (Image image in thumbnailCache.Values)
            {
                if (image != null)
                {
                    image.Dispose();
                }
            }
            thumbnailCache.Clear();
            thumbnailCacheOrder.Clear();
            thumbnailCacheNodes.Clear();

            base.OnFormClosed(e);
        }
    }

    public class AboutForm : Form
    {
        private readonly bool darkMode;
        private readonly Label updateStatusLabel;
        private readonly Button checkUpdateButton;

        public AboutForm(LicenseInfo licenseInfo, bool darkMode)
        {
            this.darkMode = darkMode;
            Text = "关于";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 270);
            Font = new Font("Microsoft YaHei UI", 9f);
            AppIcon.Apply(this);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.Padding = new Padding(18, 16, 18, 14);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            Label title = new Label();
            title.Text = "视频素材镜头表命名工具";
            title.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
            title.AutoSize = false;
            title.Height = 30;
            title.Dock = DockStyle.Top;
            title.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(title, 0, 0);

            Label infoLabel = new Label();
            infoLabel.AutoSize = false;
            infoLabel.Dock = DockStyle.Fill;
            infoLabel.TextAlign = ContentAlignment.TopLeft;
            infoLabel.Text = BuildInfoText(licenseInfo);
            layout.Controls.Add(infoLabel, 0, 1);

            updateStatusLabel = new Label();
            updateStatusLabel.AutoSize = false;
            updateStatusLabel.Height = 28;
            updateStatusLabel.Dock = DockStyle.Top;
            updateStatusLabel.Tag = "Muted";
            updateStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            updateStatusLabel.Text = "可从 GitHub 检查是否有新版本。";
            layout.Controls.Add(updateStatusLabel, 0, 2);

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel();
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.WrapContents = false;
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.Height = 38;
            buttonPanel.Margin = new Padding(0, 6, 0, 0);
            layout.Controls.Add(buttonPanel, 0, 3);

            Button closeButton = new Button();
            closeButton.Text = "关闭";
            closeButton.Width = 76;
            closeButton.Height = 30;
            closeButton.Click += delegate { Close(); };
            buttonPanel.Controls.Add(closeButton);

            checkUpdateButton = new Button();
            checkUpdateButton.Text = "检查更新";
            checkUpdateButton.Width = 94;
            checkUpdateButton.Height = 30;
            checkUpdateButton.Margin = new Padding(0, 2, 8, 2);
            checkUpdateButton.Click += delegate { CheckForUpdatesFromAbout(); };
            buttonPanel.Controls.Add(checkUpdateButton);

            UiTheme.ApplyForm(this, darkMode);
        }

        private string BuildInfoText(LicenseInfo licenseInfo)
        {
            List<string> lines = new List<string>();
            lines.Add("当前版本：" + AppInfo.Version);
            lines.Add("制作人：" + AppInfo.Author);
            lines.Add("");

            if (licenseInfo != null)
            {
                lines.Add("授权剩余时间：" + LicenseManager.GetRemainingDays(licenseInfo) + " 天");
                lines.Add("授权到期时间：" + licenseInfo.ExpiresUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            }
            else
            {
                lines.Add("授权剩余时间：未知");
            }

            return string.Join("\r\n", lines.ToArray());
        }

        private void CheckForUpdatesFromAbout()
        {
            SetUpdateBusy(true, "正在后台从 GitHub 拉取版本信息...");

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = null;
                Exception failure = null;
                try
                {
                    info = UpdateManager.GetLatestUpdateInfo();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                RunOnUi(delegate
                {
                    HandleUpdateCheckResult(info, failure);
                });
            });
        }

        private void HandleUpdateCheckResult(UpdateInfo info, Exception failure)
        {
            if (failure != null)
            {
                SetUpdateBusy(false, "检查失败。");
                MessageBox.Show(this, "检查更新失败：\r\n" + failure.Message, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (info == null)
            {
                SetUpdateBusy(false, "未获取到有效版本信息。");
                MessageBox.Show(this, "未能从 GitHub 获取有效的版本信息。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!UpdateManager.IsRemoteVersionNewer(info))
            {
                SetUpdateBusy(false, "当前已是最新版本。");
                MessageBox.Show(
                    this,
                    "当前已是最新版本。\r\n\r\n当前版本：" + AppInfo.Version + "\r\nGitHub 版本：" + UpdateManager.GetUpdateDisplayVersion(info),
                    "检查更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string message =
                "检测到新版本 " + UpdateManager.GetUpdateDisplayVersion(info) + "，当前版本 " + AppInfo.Version + "。\r\n\r\n" +
                "是否立即下载并更新？";
            if (!string.IsNullOrWhiteSpace(info.Notes))
            {
                message += "\r\n\r\n更新说明：\r\n" + info.Notes;
            }

            DialogResult result = MessageBox.Show(this, message, "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (result != DialogResult.Yes)
            {
                SetUpdateBusy(false, "已取消更新。");
                return;
            }

            if (!UpdateManager.CanAutoInstallUpdate())
            {
                SetUpdateBusy(false, "当前不是正式 EXE 运行状态。");
                MessageBox.Show(this, "当前不是正式 EXE 运行状态，无法自动替换更新。", "无法更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StartBackgroundUpdateDownload(info);
        }

        private void StartBackgroundUpdateDownload(UpdateInfo info)
        {
            SetUpdateBusy(true, "正在下载更新...");
            bool started = UpdateManager.DownloadAndRestartWithProgress(info, this);
            if (!started)
            {
                SetUpdateBusy(false, "更新未完成。");
            }
        }

        private void SetUpdateBusy(bool busy, string status)
        {
            if (checkUpdateButton != null)
            {
                checkUpdateButton.Enabled = !busy;
                UiTheme.ApplyControl(checkUpdateButton, darkMode);
            }
            if (updateStatusLabel != null)
            {
                updateStatusLabel.Text = status;
                UiTheme.ApplyControl(updateStatusLabel, darkMode);
            }
        }

        private void RunOnUi(MethodInvoker action)
        {
            if (action == null || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    public class UpdateDownloadProgressForm : Form
    {
        private readonly bool darkMode;
        private readonly Label statusLabel;
        private readonly Label detailLabel;
        private readonly ProgressBar progressBar;

        public UpdateDownloadProgressForm(bool darkMode)
        {
            this.darkMode = darkMode;
            Text = "正在更新";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 150);
            Font = new Font("Microsoft YaHei UI", 9f);
            AppIcon.Apply(this);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.Padding = new Padding(18, 16, 18, 14);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.Dock = DockStyle.Top;
            statusLabel.Height = 30;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Text = "正在准备下载更新...";
            layout.Controls.Add(statusLabel, 0, 0);

            progressBar = new ProgressBar();
            progressBar.Dock = DockStyle.Top;
            progressBar.Height = 24;
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Value = 0;
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Margin = new Padding(0, 8, 0, 8);
            layout.Controls.Add(progressBar, 0, 1);

            detailLabel = new Label();
            detailLabel.AutoSize = false;
            detailLabel.Dock = DockStyle.Top;
            detailLabel.Height = 34;
            detailLabel.Tag = "Muted";
            detailLabel.TextAlign = ContentAlignment.MiddleLeft;
            detailLabel.Text = "请不要关闭软件。";
            layout.Controls.Add(detailLabel, 0, 2);

            UiTheme.ApplyForm(this, darkMode);
        }

        public void UpdateProgress(string status, int percent, long bytesReceived, long totalBytes)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        UpdateProgress(status, percent, bytesReceived, totalBytes);
                    });
                }
                catch
                {
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                statusLabel.Text = status;
            }

            if (percent >= 0)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = Math.Max(progressBar.Minimum, Math.Min(progressBar.Maximum, percent));
            }
            else
            {
                progressBar.Style = ProgressBarStyle.Marquee;
            }

            detailLabel.Text = BuildDetailText(percent, bytesReceived, totalBytes);
            UiTheme.ApplyControl(statusLabel, darkMode);
            UiTheme.ApplyControl(detailLabel, darkMode);
        }

        private static string BuildDetailText(int percent, long bytesReceived, long totalBytes)
        {
            if (totalBytes > 0)
            {
                return percent.ToString() + "%  " + FormatBytes(bytesReceived) + " / " + FormatBytes(totalBytes);
            }

            if (bytesReceived > 0)
            {
                return "已下载 " + FormatBytes(bytesReceived);
            }

            return percent >= 0 ? percent.ToString() + "%" : "正在连接，请稍候...";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return (bytes / 1024d / 1024d / 1024d).ToString("0.00") + " GB";
            }

            if (bytes >= 1024L * 1024L)
            {
                return (bytes / 1024d / 1024d).ToString("0.0") + " MB";
            }

            if (bytes >= 1024L)
            {
                return (bytes / 1024d).ToString("0.0") + " KB";
            }

            return bytes.ToString() + " B";
        }
    }

    public class UpdateInfo
    {
        public string Version;
        public string DisplayVersion;
        public string DownloadUrl;
        public string Sha256;
        public string FileName;
        public string Notes;
    }

    public class TimeoutWebClient : WebClient
    {
        public int TimeoutMilliseconds = 6000;

        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest request = base.GetWebRequest(address);
            if (request != null)
            {
                request.Timeout = TimeoutMilliseconds;
                HttpWebRequest httpRequest = request as HttpWebRequest;
                if (httpRequest != null)
                {
                    httpRequest.ReadWriteTimeout = TimeoutMilliseconds;
                    httpRequest.AllowAutoRedirect = true;
                }
            }

            return request;
        }
    }

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
"@

Add-Type -TypeDefinition $source -ReferencedAssemblies @("System.Windows.Forms.dll", "System.Drawing.dll", "System.Core.dll", "System.Security.dll")

if ($SelfTest) {
    [VideoMaterialRenamer.MaterialRenamerForm]::RunSelfTest()
    return
}

if ($SmokeTest) {
    [VideoMaterialRenamer.MaterialRenamerForm]::RunSmokeTest()
    return
}

[VideoMaterialRenamer.Program]::Run()
