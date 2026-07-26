namespace VideoRenamer
{
    // 计划条目状态机。原实现是八个中文显示串充当的“字符串协议”，散布在
    // 构建器/预览/导出/重命名多处做字符串相等比较——漏掉一处只会静默出错。
    // 枚举化后，所有比较点由编译器检查；显示文本唯一来源是 PlanStatusText。
    public enum PlanStatus
    {
        Ready = 0,              // 就绪
        Unchanged,              // 未变化
        TargetExists,           // 目标已存在
        DuplicateNewName,       // 新文件名重复
        SourceMissing,          // 源文件丢失
        PendingOverwriteExport, // 待覆盖导出1080p
        TargetLocked,           // 目标文件被占用（预览的后台文件锁检测标记）
        SaveAsNewFile           // 另存为新文件（导出计划派生）
    }
}
