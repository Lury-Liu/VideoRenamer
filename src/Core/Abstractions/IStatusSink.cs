namespace VideoRenamer
{
    // 状态栏文本的唯一投递口。原先 8 个分部文件直接写 statusLabel.Text，
    // 使所有模块都耦合到一个具体 Label 控件；收拢为接口后，阶段5 的
    // 各 Presenter 只依赖本接口，不再认识控件。
    public interface IStatusSink
    {
        void SetStatus(string text);
    }
}
