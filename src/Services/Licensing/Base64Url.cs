using System;

namespace VideoMaterialRenamer
{
    // Base64Url 编解码（授权子系统共用；阶段8e 自 LicenseManager 抽出，
    // 逻辑逐字不变——密钥/DPAPI 文件的磁盘格式依赖它）。
    public static class Base64Url
    {
        public static string To(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static byte[] From(string value)
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
}
