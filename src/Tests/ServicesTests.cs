using System;
using System.Collections.Generic;

namespace VideoMaterialRenamer.Tests
{
    // Characterization tests for the auto-update contract: manifest parsing,
    // version comparison, and GitHub release asset discovery. The "real published
    // manifest" golden is a verbatim copy of a latest.json produced by
    // 发布更新到GitHub.ps1 - producer and consumer are pinned to the same schema.
    public static class ServicesTests
    {
        private const string RealPublishedManifestGolden =
            "{\r\n" +
            "    \"appId\":  \"VideoMaterialRenamer\",\r\n" +
            "    \"version\":  \"1.0.6.0\",\r\n" +
            "    \"displayVersion\":  \"V1.0.6.0\",\r\n" +
            "    \"fileName\":  \"VideoRenamer-v1.0.6.0.exe\",\r\n" +
            "    \"downloadUrl\":  \"https://github.com/Lury-Liu/VideoRenamer/releases/download/v1.0.6.0/VideoRenamer-v1.0.6.0.exe\",\r\n" +
            "    \"sha256\":  \"33a6bb20e248c2746e465db1918f9ef20903dc2e6efbcb6d5e885d52d62514ce\",\r\n" +
            "    \"notes\":  \"Improve user experience and fix known issues.\",\r\n" +
            "    \"publishedAt\":  \"2026-07-26T10:34:55Z\"\r\n" +
            "}";

        public static List<TestCase> Cases()
        {
            List<TestCase> cases = new List<TestCase>();
            cases.Add(new TestCase("manifest_parse_sample", ManifestParseSample));
            cases.Add(new TestCase("manifest_parse_real_published_golden", ManifestParseRealPublishedGolden));
            cases.Add(new TestCase("manifest_parse_wrong_app_id_returns_null", ManifestParseWrongAppIdReturnsNull));
            cases.Add(new TestCase("manifest_parse_missing_version_returns_null", ManifestParseMissingVersionReturnsNull));
            cases.Add(new TestCase("version_compare_table", VersionCompareTable));
            cases.Add(new TestCase("release_asset_api_url_pairing", ReleaseAssetApiUrlPairing));
            return cases;
        }

        private static void ManifestParseSample()
        {
            string json = "{\"appId\":\"VideoMaterialRenamer\",\"version\":\"1.0.5.99\",\"displayVersion\":\"V1.0.5.99\",\"downloadUrl\":\"https://example.com/app.exe\",\"sha256\":\"ABCDEF\",\"fileName\":\"视频素材镜头表命名工具.exe\",\"notes\":\"测试更新\"}";
            UpdateInfo info = UpdateManager.ParseManifest(json);
            TestAssert.IsNotNull(info, "sample manifest parses");
            TestAssert.AreEqual("1.0.5.99", info.Version, "sample version");
            TestAssert.AreEqual("https://example.com/app.exe", info.DownloadUrl, "sample downloadUrl");
            TestAssert.AreEqual("测试更新", info.Notes, "sample notes");
        }

        private static void ManifestParseRealPublishedGolden()
        {
            UpdateInfo info = UpdateManager.ParseManifest(RealPublishedManifestGolden);
            TestAssert.IsNotNull(info, "real manifest parses");
            TestAssert.AreEqual("1.0.6.0", info.Version, "real manifest version");
            TestAssert.AreEqual("V1.0.6.0", info.DisplayVersion, "real manifest displayVersion");
            TestAssert.AreEqual("VideoRenamer-v1.0.6.0.exe", info.FileName, "real manifest fileName");
            TestAssert.AreEqual("https://github.com/Lury-Liu/VideoRenamer/releases/download/v1.0.6.0/VideoRenamer-v1.0.6.0.exe",
                info.DownloadUrl, "real manifest downloadUrl");
            TestAssert.AreEqual("33a6bb20e248c2746e465db1918f9ef20903dc2e6efbcb6d5e885d52d62514ce",
                info.Sha256, "real manifest sha256");
            TestAssert.AreEqual("Improve user experience and fix known issues.", info.Notes, "real manifest notes");
        }

        private static void ManifestParseWrongAppIdReturnsNull()
        {
            string json = "{\"appId\":\"SomeOtherApp\",\"version\":\"9.9.9.9\",\"downloadUrl\":\"https://example.com/x.exe\"}";
            TestAssert.IsNull(UpdateManager.ParseManifest(json), "foreign appId rejected");
        }

        private static void ManifestParseMissingVersionReturnsNull()
        {
            string json = "{\"appId\":\"VideoMaterialRenamer\",\"downloadUrl\":\"https://example.com/x.exe\"}";
            TestAssert.IsNull(UpdateManager.ParseManifest(json), "missing version rejected");
            TestAssert.IsNull(UpdateManager.ParseManifest(""), "empty json rejected");
            TestAssert.IsNull(UpdateManager.ParseManifest(null), "null json rejected");
        }

        private static void VersionCompareTable()
        {
            TestAssert.IsTrue(UpdateManager.IsNewerVersion("1.0.5.99", "V1.0.5.26"), "newer wins");
            TestAssert.IsFalse(UpdateManager.IsNewerVersion("1.0.5.1", "V1.0.5.26"), "older loses");
            TestAssert.IsFalse(UpdateManager.IsNewerVersion("1.0.6.0", "1.0.6.0"), "equal is not newer");
            TestAssert.IsTrue(UpdateManager.IsNewerVersion("1.0.6.1", "V1.0.6.0"), "patch bump is newer");
            TestAssert.IsTrue(UpdateManager.IsNewerVersion("2.0", "1.9.9"), "major bump with fewer segments is newer");
            // Pinned .NET Version semantics: 3-segment 1.0.6 has Revision=-1 and
            // therefore compares LOWER than 4-segment 1.0.6.0.
            TestAssert.IsFalse(UpdateManager.IsNewerVersion("1.0.6", "V1.0.6.0"), "3-segment compares below 4-segment");
            TestAssert.IsFalse(UpdateManager.IsNewerVersion("garbage", "1.0.0"), "unparseable remote is not newer");
            TestAssert.IsFalse(UpdateManager.IsNewerVersion("1.0.0", ""), "unparseable current is not newer");
        }

        private static void ReleaseAssetApiUrlPairing()
        {
            string apiJson = "{\"assets\":[{\"name\":\"VideoRenamer-v1.0.5.99.exe\",\"url\":\"https://api.github.com/repos/Lury-Liu/VideoRenamer/releases/assets/1\"},{\"name\":\"latest.json\",\"url\":\"https://api.github.com/repos/Lury-Liu/VideoRenamer/releases/assets/2\"}]}";
            TestAssert.AreEqual("https://api.github.com/repos/Lury-Liu/VideoRenamer/releases/assets/2",
                UpdateManager.GetReleaseAssetApiUrl(apiJson, "latest.json"),
                "manifest asset URL pairing");
        }
    }
}
