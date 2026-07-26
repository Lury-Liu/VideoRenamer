using System;
using System.Runtime.InteropServices;

namespace VideoRenamer
{
    // 快捷方式在安装时固定引用 current.ico；每次更新图标内容后只通知 Shell
    // 该图标文件已变化，无需扫描或改写任何用户/公共快捷方式。
    internal static class IconFileChangeNotifier
    {
        private const uint ShellChangeUpdateItem = 0x00002000;
        private const uint ShellChangePathW = 0x0005;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);

        public static void TryNotifyChanged(string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return;
            }

            try
            {
                SHChangeNotify(ShellChangeUpdateItem, ShellChangePathW, iconPath, IntPtr.Zero);
            }
            catch
            {
            }
        }
    }
}
