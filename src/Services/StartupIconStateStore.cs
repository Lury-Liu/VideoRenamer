using System;
using System.IO;
using System.Text;

namespace VideoRenamer
{
    // 只保存上次成功选中的图标序号；轮换计算本身留在 Core，避免把
    // 文件系统细节混入可测试的纯逻辑。
    internal static class StartupIconStateStore
    {
        private static readonly string StatePath = Path.Combine(AppInfo.AppDataDirectory, "startup-icon-index.txt");

        public static int ReadLastIndex()
        {
            try
            {
                if (!File.Exists(StatePath))
                {
                    return -1;
                }

                int index;
                return int.TryParse(File.ReadAllText(StatePath, Encoding.UTF8).Trim(), out index) ? index : -1;
            }
            catch
            {
                return -1;
            }
        }

        public static void TryWriteLastIndex(int index)
        {
            try
            {
                Directory.CreateDirectory(AppInfo.AppDataDirectory);
                File.WriteAllText(StatePath, index.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLog.Write("startup-icon", "保存图标轮换状态失败", ex);
            }
        }
    }
}
