using System;
using System.IO;

namespace VideoMaterialRenamer
{
    // 计划构建期间的文件系统探测接口：让 BuildPlan 的状态判定可以在测试中
    // 用假实现驱动（不再必须铺设真实临时文件），也为阶段7 的
    // “每轮刷新内 File.Exists 结果记忆化”预留了挂点。
    public interface IFileSystemProbe
    {
        bool FileExists(string path);
        bool IsFileLocked(string path);
    }

    public sealed class RealFileSystemProbe : IFileSystemProbe
    {
        public static readonly RealFileSystemProbe Instance = new RealFileSystemProbe();

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        // 排它打开探测：能独占打开=未被占用。IOException/无权限视为被占用。
        public bool IsFileLocked(string path)
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
    }
}
