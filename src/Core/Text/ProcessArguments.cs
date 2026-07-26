namespace VideoRenamer
{
    // 进程命令行参数引号处理的唯一实现（阶段8c）。
    // 历史上共三份拷贝：两份已在阶段2 并入 FfmpegArguments，
    // 第三份在 UpdateManager.Download——现在统一收拢到这里。
    public static class ProcessArguments
    {
        public static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }
    }
}
