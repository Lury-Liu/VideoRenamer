using System;
using System.IO;
using System.Text;

namespace VideoMaterialRenamer
{
    // 轻量文件日志（阶段8d）：唯一目的是让"静默失败"留下可诊断的痕迹。
    // 约定：本类绝不抛异常、绝不弹窗（日志失败不得反过来伤害主流程）；
    // 单文件上限 1MB，超限滚动为 app.old.log（只保留一代）。
    // 位置：%LocalAppData%\VideoMaterialRenamer\logs\app.log
    public static class AppLog
    {
        private static readonly object Sync = new object();
        private const long MaxLogBytes = 1024 * 1024;

        public static string LogDirectory
        {
            get { return Path.Combine(AppInfo.AppDataDirectory, "logs"); }
        }

        public static string LogPath
        {
            get { return Path.Combine(LogDirectory, "app.log"); }
        }

        public static void Write(string category, string message)
        {
            Write(category, message, null);
        }

        public static void Write(string category, string message, Exception error)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    RollIfOversized();
                    StringBuilder line = new StringBuilder();
                    line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    line.Append(" [").Append(string.IsNullOrEmpty(category) ? "app" : category).Append("] ");
                    line.Append(message ?? "");
                    if (error != null)
                    {
                        line.Append(" | ").Append(error.GetType().Name).Append(": ").Append(error.Message);
                    }
                    line.Append("\r\n");
                    File.AppendAllText(LogPath, line.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        private static void RollIfOversized()
        {
            FileInfo info = new FileInfo(LogPath);
            if (info.Exists && info.Length > MaxLogBytes)
            {
                string old = Path.Combine(LogDirectory, "app.old.log");
                if (File.Exists(old))
                {
                    File.Delete(old);
                }
                File.Move(LogPath, old);
            }
        }
    }
}
