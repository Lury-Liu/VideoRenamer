using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace VideoRenamer
{
    // ffmpeg.exe 的定位与内置资源提取。
    // 冻结契约：候选顺序 baseDir > baseDir\tools > cwd > cwd\tools > PATH > 内置资源；
    // 内置资源名必须是 "VideoRenamer.ffmpeg.exe"（构建脚本 /resource 写入）。
    public static class FfmpegLocator
    {
        private static readonly object SyncRoot = new object();
        private static string cachedPath;

        // 结果记忆化：原实现每次调用都重新提取/遍历候选（导出启动时在 UI 线程上
        // 重新核对约 100MB 内置资源，造成数秒卡顿）。规则：
        //  - 只缓存成功结果，且缓存路径失效（文件被删）时自动重新解析；
        //  - 失败（返回空串）永不缓存——用户按报错提示放置 ffmpeg.exe 后重试即可生效。
        public static string Resolve()
        {
            lock (SyncRoot)
            {
                if (!string.IsNullOrWhiteSpace(cachedPath) && File.Exists(cachedPath))
                {
                    return cachedPath;
                }

                string resolved = FindFfmpegPathCore();
                cachedPath = string.IsNullOrWhiteSpace(resolved) ? null : resolved;
                return resolved;
            }
        }

        private static string FindFfmpegPathCore()
        {
            List<string> candidates = new List<string>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentDir = Environment.CurrentDirectory;
            candidates.Add(Path.Combine(baseDir, "ffmpeg.exe"));
            candidates.Add(Path.Combine(baseDir, "tools", "ffmpeg.exe"));
            candidates.Add(Path.Combine(currentDir, "ffmpeg.exe"));
            candidates.Add(Path.Combine(currentDir, "tools", "ffmpeg.exe"));

            string pathText = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string directory in pathText.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    candidates.Add(Path.Combine(directory.Trim(), "ffmpeg.exe"));
                }
                catch
                {
                }
            }

            string embeddedFfmpeg = ExtractEmbeddedFfmpeg();
            if (!string.IsNullOrWhiteSpace(embeddedFfmpeg))
            {
                candidates.Add(embeddedFfmpeg);
            }

            foreach (string candidate in candidates)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private static string ExtractEmbeddedFfmpeg()
        {
            const string resourceName = "VideoRenamer.ffmpeg.exe";
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream resource = assembly.GetManifestResourceStream(resourceName))
                {
                    if (resource == null)
                    {
                        return "";
                    }

                    string toolsDir = Path.Combine(AppInfo.AppDataDirectory, "tools");
                    Directory.CreateDirectory(toolsDir);
                    string ffmpegPath = Path.Combine(toolsDir, "ffmpeg.exe");
                    long resourceLength = resource.CanSeek ? resource.Length : -1;
                    if (File.Exists(ffmpegPath) && resourceLength > 0)
                    {
                        FileInfo existing = new FileInfo(ffmpegPath);
                        if (existing.Length == resourceLength)
                        {
                            return ffmpegPath;
                        }
                    }

                    string tempPath = ffmpegPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (FileStream output = File.Create(tempPath))
                    {
                        resource.CopyTo(output);
                    }

                    if (File.Exists(ffmpegPath))
                    {
                        File.Delete(ffmpegPath);
                    }
                    File.Move(tempPath, ffmpegPath);
                    return ffmpegPath;
                }
            }
            catch
            {
                return "";
            }
        }
    }
}
