using System;
using System.Collections.Generic;
using System.Threading;

namespace VideoRenamer
{
    // 媒体加载调度器：用两条常驻 STA 工作线程取代原先“每次选中就新建
    // 最多 3 条 STA 线程”的模式（快速翻页曾堆出数十条线程 + 并发 ffmpeg）。
    //  - 快车道：元数据 / 缩略图（Shell COM，毫秒级）
    //  - 慢车道：ffmpeg 抽帧条（秒级）——分道避免慢任务阻塞详情面板首绘
    // 两道均为“最新优先”（LIFO）：快速翻页时最后选中的项最先加载。
    // 冻结契约：工作线程必须 STA——VideoThumbnailProvider/VideoMetadataReader
    // 走 Shell COM，MTA 下会静默降级。时效性过滤仍由调用方在 UI 回投时
    // 用版本号/IsCurrentDetailPath 判定（本类不做去重）。
    public sealed class MediaLoadScheduler : IDisposable
    {
        private sealed class StaWorkQueue : IDisposable
        {
            private readonly object sync = new object();
            private readonly List<Action> queue = new List<Action>();
            private readonly Thread worker;
            private bool disposed;

            public StaWorkQueue(string name)
            {
                worker = new Thread(WorkLoop);
                worker.Name = name;
                worker.IsBackground = true;
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
            }

            public void Enqueue(Action work)
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
                        // 后台加载失败静默吞掉（与原 StartStaBackground 行为一致：
                        // 线程内异常不得杀死进程；结果缺失由 UI 层兜底显示）。
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

        private readonly StaWorkQueue fastLane = new StaWorkQueue("VMR-MediaFast");
        private readonly StaWorkQueue slowLane = new StaWorkQueue("VMR-MediaSlow");

        // 元数据/缩略图。
        public void QueueFast(Action work)
        {
            fastLane.Enqueue(work);
        }

        // ffmpeg 抽帧条等重活。
        public void QueueSlow(Action work)
        {
            slowLane.Enqueue(work);
        }

        public void Dispose()
        {
            fastLane.Dispose();
            slowLane.Dispose();
        }
    }
}
