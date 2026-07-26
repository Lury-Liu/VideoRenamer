using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace VideoMaterialRenamer.Tests
{
    // 授权验证器特征测试（阶段8e）。
    //
    // 关于内置样例密钥的安全性：三把密钥全部在 2024-01-01 过期，且机器码为
    // 任何真机都不会产生的哑值——从 EXE 里提取出来毫无价值。测试通过把
    // nowUtc 参数"回拨"到 2023 来走通有效路径（验证器时钟可注入正是为此）。
    // 私钥只存在于授权方的 生成授权密钥工具.ps1，绝不进入 src/。
    public static class LicensingTests
    {
        private const string DummyMachine = "0123456789ABCDEF0123";
        private const string OtherMachine = "FFFFFFFFFFFFFFFFFFFF";
        private const long PinnedExpiryTicks = 638396640000000000; // 2024-01-01 00:00:00 UTC

        private const string ExpiredKeyForDummyMachine =
            "VMR2.MDEyMzQ1Njc4OUFCQ0RFRjAxMjN8NjM4Mzk2NjQwMDAwMDAwMDAwfHBpbm5lZHRlc3Rub25jZTAwMDAwMDAwMDAwMDAwMDBhfFJTQS1TSEEyNTY.g0aCNjoci3jeEhYJOTKqYbsZhl5bOtcD9r9wtKD173MIQXUhN05GAj1H8El3LsO2iGXOgoHfUkrke4P5E1dbH9-epg7uiSftvWLmCTcQCnCslN7zD3R86_Ew_UG15-5H8A3Gs51yn01vgrJQCNIRrWj_O8eOlWrGXNdX6cYMEAjPFYKBN6O4ZcriE-TqU-wH3NN4Am70XqNXhPJDElATapVYORS7Ap5HswDSE78Ssfoip8eV-3q6azCLk0MULCqL10NxSUXk3nARyEfo-CMniLth47qNB0BHAskFRYCFeFQUAZAl54m6v4MOOISfhypj6tjJxa51CSeLtwklaK-KZA";

        private const string ExpiredKeyForOtherMachine =
            "VMR2.RkZGRkZGRkZGRkZGRkZGRkZGRkZ8NjM4Mzk2NjQwMDAwMDAwMDAwfHBpbm5lZHRlc3Rub25jZTAwMDAwMDAwMDAwMDAwMDBifFJTQS1TSEEyNTY.CxYzVm63yS4ig8m_V9uInXQO1Cnf2CtZ25nUe082D41hqIzWOFlSaUFHFcgOWExp8TKbjwx1aYLYUVRxd2w4iKHe0mASd8ACFrluCgqiouGTkF_TMinYTVnjQe7dk4v4n9D3MTY7kgvHqaox594yXfH6f0aLYChWoCLO2M1hwxzHLdCdBg1lLHNidC88jNUNSVHHDch02Q9NL_iDmKHBcepWYSbW5TNcQUEegDmj3yWDQ6-tWWCAJZ0VfEsPbhlQVNLXJo4tdWIqs8g5RShPx-7wVCRZHHqdZdLkdcUDd9LsKGnz45mBnEtgGAcdjeuQJoOe6QI3Lb2M1qSf4Vko7A";

        // 签名有效但载荷不是 4 段授权格式（"not-a-license-payload"）。
        private const string SignedGarbagePayloadKey =
            "VMR2.bm90LWEtbGljZW5zZS1wYXlsb2Fk.cKVehlwf8toldIxtUoMeBJSvwJ9xgCwtgXzc3A6pUSaYvkWPO5dLrOznM9Y4fO_WHiaHAXO9R3DrwWP_A-hY3j_Qjr7ESUgVH_mbku8TDC0fjIFqqLjybc30dRe1fjlbydRs_eArrvIYs3u0u0l-blpC5fu-3Io5S6CtbBKFxTAOtK_Mns2F3rxymvVx6CFXsARyKEx1kQu6ou-0Pcs1rV5xXfC_HAAo0MrWGy369wpwR_PVCyh6Qu5aWgJLuVqVfavT0iXVZuvzaaWFKtt44SQef_vGTv8S_Cm7Ar4xfgl8Z9cFsdzvvhcNwbD-nSFJ8fZwG1Njo3Mg3CXN74-vag";

        private static readonly DateTime BeforeExpiry = new DateTime(2023, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime AfterExpiry = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static List<TestCase> Cases()
        {
            List<TestCase> cases = new List<TestCase>();
            cases.Add(new TestCase("license_valid_key_via_injected_clock", ValidKeyViaInjectedClock));
            cases.Add(new TestCase("license_expired_key_rejected", ExpiredKeyRejected));
            cases.Add(new TestCase("license_wrong_machine_rejected", WrongMachineRejected));
            cases.Add(new TestCase("license_machine_checked_before_expiry", MachineCheckedBeforeExpiry));
            cases.Add(new TestCase("license_format_error_table", FormatErrorTable));
            cases.Add(new TestCase("license_tampered_signature_rejected", TamperedSignatureRejected));
            cases.Add(new TestCase("license_signed_garbage_payload_rejected", SignedGarbagePayloadRejected));
            cases.Add(new TestCase("license_base64url_roundtrip", Base64UrlRoundtrip));
            cases.Add(new TestCase("dpapi_protected_text_roundtrip", DpapiProtectedTextRoundtrip));
            return cases;
        }

        private static void ValidKeyViaInjectedClock()
        {
            LicenseInfo info;
            string error;
            bool ok = LicenseValidator.Validate(ExpiredKeyForDummyMachine, DummyMachine, BeforeExpiry, out info, out error);
            TestAssert.IsTrue(ok, "key valid before its expiry: " + error);
            TestAssert.IsNotNull(info, "info returned");
            TestAssert.AreEqual(DummyMachine, info.MachineCode, "machine code round-trips");
            TestAssert.AreEqual(PinnedExpiryTicks, info.ExpiresUtc.Ticks, "expiry ticks round-trip");
            TestAssert.AreEqual("pinnedtestnonce0000000000000000a", info.Nonce, "nonce round-trips");
        }

        private static void ExpiredKeyRejected()
        {
            LicenseInfo info;
            string error;
            TestAssert.IsFalse(LicenseValidator.Validate(ExpiredKeyForDummyMachine, DummyMachine, AfterExpiry, out info, out error), "expired rejected");
            TestAssert.AreEqual("密钥已过期。", error, "expiry error text");
            TestAssert.IsNull(info, "no info on failure");
        }

        private static void WrongMachineRejected()
        {
            LicenseInfo info;
            string error;
            TestAssert.IsFalse(LicenseValidator.Validate(ExpiredKeyForDummyMachine, OtherMachine, BeforeExpiry, out info, out error), "wrong machine rejected");
            TestAssert.AreEqual("密钥不属于本机。请把本机机器码发给授权方重新生成密钥。", error, "machine error text");
        }

        private static void MachineCheckedBeforeExpiry()
        {
            // 既有行为锁定：机器码判定先于有效期——错机器+过期时报"不属于本机"。
            LicenseInfo info;
            string error;
            TestAssert.IsFalse(LicenseValidator.Validate(ExpiredKeyForDummyMachine, OtherMachine, AfterExpiry, out info, out error), "rejected");
            TestAssert.AreEqual("密钥不属于本机。请把本机机器码发给授权方重新生成密钥。", error, "machine error wins over expiry");
        }

        private static void FormatErrorTable()
        {
            LicenseInfo info;
            string error;
            LicenseValidator.Validate(null, DummyMachine, BeforeExpiry, out info, out error);
            TestAssert.AreEqual("密钥为空。", error, "null key");
            LicenseValidator.Validate("   ", DummyMachine, BeforeExpiry, out info, out error);
            TestAssert.AreEqual("密钥为空。", error, "blank key");
            LicenseValidator.Validate("hello", DummyMachine, BeforeExpiry, out info, out error);
            TestAssert.AreEqual("密钥格式不正确。", error, "no separators");
            LicenseValidator.Validate("VMR1.aa.bb", DummyMachine, BeforeExpiry, out info, out error);
            TestAssert.AreEqual("密钥格式不正确。", error, "wrong prefix");
            LicenseValidator.Validate("VMR2.@@.##", DummyMachine, BeforeExpiry, out info, out error);
            TestAssert.AreEqual("密钥编码不正确。", error, "invalid base64url");
        }

        private static void TamperedSignatureRejected()
        {
            // 拼接攻击样例：密钥1 的载荷 + 密钥2 的签名 → 签名校验必须失败。
            string[] parts1 = ExpiredKeyForDummyMachine.Split('.');
            string[] parts2 = ExpiredKeyForOtherMachine.Split('.');
            string spliced = parts1[0] + "." + parts1[1] + "." + parts2[2];

            LicenseInfo info;
            string error;
            TestAssert.IsFalse(LicenseValidator.Validate(spliced, DummyMachine, BeforeExpiry, out info, out error), "spliced key rejected");
            TestAssert.AreEqual("密钥签名无效。", error, "signature error text");
        }

        private static void SignedGarbagePayloadRejected()
        {
            LicenseInfo info;
            string error;
            TestAssert.IsFalse(LicenseValidator.Validate(SignedGarbagePayloadKey, DummyMachine, BeforeExpiry, out info, out error), "garbage payload rejected");
            TestAssert.AreEqual("密钥内容不完整。", error, "payload error text");
        }

        private static void Base64UrlRoundtrip()
        {
            byte[] data = new byte[] { 0, 1, 2, 250, 251, 252, 253, 254, 255, 62, 63 };
            TestAssert.AreEqual(BitConverter.ToString(data), BitConverter.ToString(Base64Url.From(Base64Url.To(data))), "bytes roundtrip");
            string encoded = Base64Url.To(data);
            TestAssert.IsTrue(encoded.IndexOf('+') < 0 && encoded.IndexOf('/') < 0 && encoded.IndexOf('=') < 0, "url-safe alphabet");
        }

        private static void DpapiProtectedTextRoundtrip()
        {
            // DPAPI CurrentUser 在本进程内即可往返——证明 8e 重构后存储层
            // （Write/ReadProtectedText + Base64Url 落盘格式）完好。
            MethodInfo write = typeof(LicenseManager).GetMethod("WriteProtectedText", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo read = typeof(LicenseManager).GetMethod("ReadProtectedText", BindingFlags.NonPublic | BindingFlags.Static);
            TestAssert.IsNotNull(write, "WriteProtectedText present");
            TestAssert.IsNotNull(read, "ReadProtectedText present");

            string dir = Path.Combine(Path.GetTempPath(), "VmrTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "roundtrip.dat");
                string secret = "样例文本|self-test|123";
                write.Invoke(null, new object[] { path, secret, "self-test" });
                TestAssert.IsTrue(File.Exists(path), "protected file written");
                string back = (string)read.Invoke(null, new object[] { path, "self-test" });
                TestAssert.AreEqual(secret, back, "protected text roundtrips");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
