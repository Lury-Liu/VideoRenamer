using System;

namespace VideoRenamer
{
    // UI 线程投递接口（QueueOnUi 的契约化）。
    // 冻结契约：窗体已释放或句柄未创建时返回 false 且不执行——
    // 多处调用方依赖该 false 返回值来释放本应展示的 Image 资源，
    // 任何替换实现（如 SynchronizationContext.Post）都必须保留这一语义，
    // 否则会泄漏 GDI 位图。
    public interface IUiDispatcher
    {
        bool Post(Action action);
    }
}
