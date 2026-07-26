using System;
using System.Collections.Generic;
using System.Drawing;

namespace VideoMaterialRenamer.Tests
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
            return cases;
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
