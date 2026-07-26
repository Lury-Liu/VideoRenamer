using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace VideoRenamer.Tests
{
    // App 层（主题）特征测试：暖纸色系关键色值锁定——调色板漂移会在
    // 自检里立刻现形，而不是等人眼发现"怎么颜色变了"。
    public static class AppTests
    {
        public static List<TestCase> Cases()
        {
            List<TestCase> cases = new List<TestCase>();
            cases.Add(new TestCase("palette_pins_light", PalettePinsLight));
            cases.Add(new TestCase("palette_pins_dark", PalettePinsDark));
            cases.Add(new TestCase("app_identity_is_videorenamer", AppIdentityIsVideoRenamer));
            cases.Add(new TestCase("startup_icon_rotation_cycles", StartupIconRotationCycles));
            cases.Add(new TestCase("startup_icon_previews_decode_largest_png_layers", StartupIconPreviewsDecodeLargestPngLayers));
            cases.Add(new TestCase("installer_shortcuts_use_current_icon_proxy", InstallerShortcutsUseCurrentIconProxy));
            return cases;
        }

        private static void AppIdentityIsVideoRenamer()
        {
            TestAssert.AreEqual("VideoRenamer", AppInfo.Name, "runtime app name");
            TestAssert.AreEqual("VideoRenamer", Path.GetFileName(AppInfo.AppDataDirectory), "AppData directory name");

            string installer = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "installer.iss"));
            TestAssert.IsTrue(installer.Contains("#define AppName \"VideoRenamer\""), "installer app name");
            TestAssert.IsTrue(installer.Contains("#define AppExeName \"VideoRenamer.exe\""), "installer EXE name");

            string project = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "VideoRenamer.csproj"));
            TestAssert.IsTrue(project.Contains("<AssemblyName>VideoRenamer</AssemblyName>"), "assembly name");
        }

        private static void StartupIconRotationCycles()
        {
            const int iconCount = 9;
            TestAssert.AreEqual(0, StartupIconRotation.GetNextIndex(-1, iconCount), "first startup selects the first icon");
            TestAssert.AreEqual(1, StartupIconRotation.GetNextIndex(0, iconCount), "second startup selects the second icon");
            TestAssert.AreEqual(0, StartupIconRotation.GetNextIndex(iconCount - 1, iconCount), "last icon wraps to the first icon");
            TestAssert.AreEqual(0, StartupIconRotation.GetNextIndex(99, iconCount), "corrupt persisted index resets to the first icon");
        }

        private static void StartupIconPreviewsDecodeLargestPngLayers()
        {
            for (int index = 1; index <= 9; index++)
            {
                string name = index.ToString("00");
                string iconPath = Path.Combine(Environment.CurrentDirectory, "assets", "startup-icons", name + ".ico");
                using (Image preview = StartupIconPreview.ExtractLargestPngLayer(File.ReadAllBytes(iconPath)))
                {
                    TestAssert.IsNotNull(preview, "startup icon " + name + " exposes a PNG preview layer");
                    TestAssert.AreEqual(256, preview.Width, "startup preview " + name + " uses the largest PNG width");
                    TestAssert.AreEqual(256, preview.Height, "startup preview " + name + " uses the largest PNG height");
                }
            }
        }

        private static void InstallerShortcutsUseCurrentIconProxy()
        {
            string installerPath = Path.Combine(Environment.CurrentDirectory, "installer.iss");
            string installer = File.ReadAllText(installerPath);
            string currentIconPath = "{commonappdata}\\VideoRenamer\\startup-icons\\current.ico";
            TestAssert.IsTrue(installer.IndexOf("IconFilename: \"" + currentIconPath + "\"", StringComparison.OrdinalIgnoreCase) >= 0,
                "installer shortcuts must use the shared current icon proxy");
            TestAssert.IsTrue(installer.IndexOf("Permissions: users-modify", StringComparison.OrdinalIgnoreCase) >= 0,
                "shared icon proxy directory must be writable by standard users");
        }

        private static void AssertColor(int r, int g, int b, Color actual, string label)
        {
            TestAssert.AreEqual(Color.FromArgb(r, g, b).ToArgb(), actual.ToArgb(), label);
        }

        private static void PalettePinsLight()
        {
            AssertColor(250, 249, 245, UiTheme.WindowBack(false), "light window");
            AssertColor(244, 242, 236, UiTheme.PanelBack(false), "light panel");
            AssertColor(239, 237, 229, UiTheme.HeaderBack(false), "light header");
            AssertColor(61, 58, 52, UiTheme.TextColor(false), "light text");
            AssertColor(120, 116, 106, UiTheme.MutedText(false), "light muted");
            AssertColor(226, 223, 213, UiTheme.BorderColor(false), "light border");
            AssertColor(186, 91, 52, UiTheme.AccentBack(false), "light clay accent");
            AssertColor(255, 255, 255, UiTheme.AccentFore(false), "light accent fore is white");
            AssertColor(247, 237, 216, UiTheme.PreviewWarningBack(false), "light warning tint");
            AssertColor(247, 228, 222, UiTheme.PreviewErrorBack(false), "light error tint");
            AssertColor(58, 118, 62, UiTheme.PreviewOkFore(false), "light status ok green");
            AssertColor(176, 58, 44, UiTheme.PreviewErrorFore(false), "light status error red");
        }

        private static void PalettePinsDark()
        {
            AssertColor(38, 37, 33, UiTheme.WindowBack(true), "dark window");
            AssertColor(45, 43, 39, UiTheme.PanelBack(true), "dark panel");
            AssertColor(237, 234, 227, UiTheme.TextColor(true), "dark text");
            AssertColor(74, 70, 63, UiTheme.BorderColor(true), "dark border");
            AssertColor(217, 119, 87, UiTheme.AccentBack(true), "dark clay accent");
            AssertColor(59, 26, 14, UiTheme.AccentFore(true), "dark accent fore is deep clay");
            AssertColor(137, 183, 128, UiTheme.PreviewOkFore(true), "dark status ok green");
            AssertColor(229, 124, 107, UiTheme.PreviewErrorFore(true), "dark status error red");
        }
    }
}
