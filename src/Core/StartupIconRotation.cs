using System;

namespace VideoRenamer
{
    // 启动图标的顺序规则保持纯逻辑：资源加载、磁盘持久化和快捷方式更新
    // 分别由上层模块负责，使轮换规则可独立测试。
    public static class StartupIconRotation
    {
        public static int GetNextIndex(int previousIndex, int iconCount)
        {
            if (iconCount <= 0)
            {
                throw new ArgumentOutOfRangeException("iconCount");
            }

            if (previousIndex < 0 || previousIndex >= iconCount)
            {
                return 0;
            }

            return (previousIndex + 1) % iconCount;
        }
    }
}
