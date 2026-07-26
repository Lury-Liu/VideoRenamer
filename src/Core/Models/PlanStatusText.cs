namespace VideoMaterialRenamer
{
    // PlanStatus 的用户可见中文文本。
    // 规则（由构建门禁 grep 强制）：状态中文字面量只允许出现在本文件与
    // src/Tests/（测试作为文本预期的锚点）。任何其他文件出现即构建失败。
    public static class PlanStatusText
    {
        public static string For(PlanStatus status)
        {
            switch (status)
            {
                case PlanStatus.Ready:
                    return "就绪";
                case PlanStatus.Unchanged:
                    return "未变化";
                case PlanStatus.TargetExists:
                    return "目标已存在";
                case PlanStatus.DuplicateNewName:
                    return "新文件名重复";
                case PlanStatus.SourceMissing:
                    return "源文件丢失";
                case PlanStatus.PendingOverwriteExport:
                    return "待覆盖导出1080p";
                case PlanStatus.TargetLocked:
                    return "目标文件被占用";
                case PlanStatus.SaveAsNewFile:
                    return "另存为新文件";
                default:
                    return status.ToString();
            }
        }
    }
}
