using System;
using System.Security.Cryptography;
using System.Text;

namespace VideoMaterialRenamer
{
    // 纯授权密钥验证（阶段8e）：机器码与时钟均由参数注入，因此可以在任意
    // 机器上用"已过期的样例密钥 + 回拨的 nowUtc"测遍所有分支；DPAPI 存储
    // 与状态文件不在本类（留在 LicenseManager，磁盘格式冻结）。
    // 错误文案与判定顺序为既有行为，被测试逐字锁定（机器码先于有效期）。
    public static class LicenseValidator
    {
        private const string KeyPrefix = "VMR2";
        private const string PayloadVersion = "RSA-SHA256";
        private const string PublicKeyXml = @"<RSAKeyValue><Modulus>wulgLKdZu8gG3znaPiWEoPD6VoMAyW7yMM3BqEStw/ajSwba89/IlUK+aTiILfzvwnCTCz5lnA9OzBGFpjwvUjl5GquNxKE44ff2a+0eu+FPbu04JzM/ArbM8Amk+KcYRUTXUY7H8dGkHKbJOrPsu3qFGksOd6cy6qpREl6tkL8P7d1YvA01ptz3dK2Ya3ch5qxqaiSXbCL5OllFH/P3GXOJzUixPWd2ulEHJZZO5kJSt8SkS8BG8XMmVbFj28VeU6xWKOJS8F9ZLmi0nS5VDptwihGIqWLDSuLzglXs8Lt6Jdbji6pkmm7Dr5NAelWiF8ibelOenEX0OEJ7xlsl2Q==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        public static bool Validate(string key, string machineCode, DateTime nowUtc, out LicenseInfo info, out string error)
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
                payloadBytes = Base64Url.From(parts[1]);
                signatureBytes = Base64Url.From(parts[2]);
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
            if (!StringComparer.OrdinalIgnoreCase.Equals(fields[0], machineCode))
            {
                error = "密钥不属于本机。请把本机机器码发给授权方重新生成密钥。";
                return false;
            }

            if (nowUtc > expiresUtc)
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
    }
}
