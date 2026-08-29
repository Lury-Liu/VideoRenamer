using System;
using System.Collections.Generic;
using System.Threading;

namespace VideoRenamer
{
    // 媒体加载调度器：一条常驻 STA 工作线程，串行处理元数据加载（Shell COM）。
    // 最新优先（LIFO）：快速翻页时最后选中的项最先加载。
    // 冻结契约：工作线程必须 STA——VideoMetadataReader 走 Shell COM，MTA 下
    // 会静默降级。时效性过滤由调用方在 UI 回投时用 IsCurrentDetailPath 判定。
    public sealed class MediaLoadScheduler : IDisposable
    {
        private readonly object sync = new object();
        private readonly List<Action> queue = new List<Action>();
        private readonly Thread worker;
        private bool disposed;

        public MediaLoadScheduler()
        {
            worker = new Thread(WorkLoop);
            worker.Name = "VMR-MediaLoad";
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        public void Queue(Action work)
        {
            if (work == null)
            {
                return;
            }

            lock (sync)
            {
                if (disposed)
                {
                    return;
                }
                queue.Add(work);
                Monitor.Pulse(sync);
            }
        }

        private void WorkLoop()
        {
            while (true)
            {
                Action work;
                lock (sync)
                {
                    while (queue.Count == 0 && !disposed)
                    {
                        Monitor.Wait(sync);
                    }

                    if (disposed)
                    {
                        return;
                    }

                    // 最新优先：取队尾。
                    work = queue[queue.Count - 1];
                    queue.RemoveAt(queue.Count - 1);
                }

                try
                {
                    work();
                }
                catch
                {
                    // 后台加载失败静默吞掉：线程内异常不得杀死进程；
                    // 结果缺失由 UI 层兜底显示。
                }
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                disposed = true;
                queue.Clear();
                Monitor.Pulse(sync);
            }
        }
    }
}
