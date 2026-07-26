using System;
using System.Collections.Generic;

namespace VideoMaterialRenamer
{
    // 单次刷新周期内的 FileExists 记忆化（阶段11b）：一次 BuildPlan 里同一
    // 路径可能被探测多次（目标存在性 + 源存在性），把一次刷新视为文件系统
    // 的一致快照——重复路径不再重复打磁盘。IsFileLocked 决不缓存（锁状态
    // 随时变化）。每次刷新新建实例，缓存生命周期即一次刷新。
    public sealed class CachedFileSystemProbe : IFileSystemProbe
    {
        private readonly IFileSystemProbe inner;
        private readonly Dictionary<string, bool> fileExists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public CachedFileSystemProbe(IFileSystemProbe inner)
        {
            this.inner = inner;
        }

        public bool FileExists(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return inner.FileExists(path);
            }

            bool exists;
            if (!fileExists.TryGetValue(path, out exists))
            {
                exists = inner.FileExists(path);
                fileExists[path] = exists;
            }
            return exists;
        }

        public bool IsFileLocked(string path)
        {
            return inner.IsFileLocked(path);
        }
    }
}
