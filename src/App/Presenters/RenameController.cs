using System;
using System.Collections.Generic;
using System.Threading;

namespace VideoMaterialRenamer
{
    // 重命名执行编排：把 File.Move 循环放到工作线程（原实现在 UI 线程里
    // 同步执行，大批量/网络盘会整窗冻结），逐文件进度经 IStatusSink 报告，
    // 完成后回投 UI 线程交由窗体做行模型写回、撤销入栈与结果对话框。
    public sealed class RenameController
    {
        private readonly IUiDispatcher dispatcher;
        private readonly IStatusSink status;

        public RenameController(IUiDispatcher dispatcher, IStatusSink status)
        {
            this.dispatcher = dispatcher;
            this.status = status;
        }

        public void ExecuteAsync(List<RenamePlan> plan, Action<PlanExecutor.ExecutionResult> completed)
        {
            int total = plan == null ? 0 : plan.Count;
            ThreadPool.QueueUserWorkItem(delegate
            {
                PlanExecutor.ExecutionResult result = PlanExecutor.Execute(plan, delegate(RenamePlan entry, int index)
                {
                    RenamePlan currentEntry = entry;
                    int currentIndex = index;
                    dispatcher.Post(delegate
                    {
                        status.SetStatus(string.Format("正在重命名 {0}/{1}：{2}", currentIndex, total, currentEntry.OldName));
                    });
                });

                dispatcher.Post(delegate
                {
                    completed(result);
                });
            });
        }
    }
}
