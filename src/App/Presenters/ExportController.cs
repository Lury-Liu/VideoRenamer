using System;
using System.Collections.Generic;
using System.Threading;

namespace VideoRenamer
{
    // 1080p 批量导出编排：工作线程调度、逐文件执行、逐行进度聚合、
    // 状态栏文本。UI 专属处置（行模型写回、完成对话框、进度列显隐）
    // 由窗体经 completed 回调承担。
    // 冻结契约：进度按 ShotRow 引用身份分组（rowTotals/rowCompleted 字典
    // 以活引用为键）——计划条目的 Row 决不能被克隆或快照。
    public sealed class ExportController
    {
        public sealed class ExportOutcome
        {
            public readonly List<RenameOperation> Successes = new List<RenameOperation>();
            public readonly List<string> Failures = new List<string>();
            // 阶段10d：批次被取消（已完成的文件保持完成，其余未开始）。
            public bool Cancelled;
        }

        private readonly IUiDispatcher dispatcher;
        private readonly IStatusSink status;
        private volatile FfmpegCancellation activeCancellation;

        public ExportController(IUiDispatcher dispatcher, IStatusSink status)
        {
            this.dispatcher = dispatcher;
            this.status = status;
        }

        // 中止当前批次：立即杀掉活动 ffmpeg 进程，剩余条目不再开始。
        // 已完成的文件保持完成状态；进行中文件的 .vmr_ 临时文件由
        // ExportOne 的清理逻辑删除。
        public void CancelActive()
        {
            FfmpegCancellation cancellation = activeCancellation;
            if (cancellation != null)
            {
                cancellation.Cancel();
            }
        }

        public void Start(
            List<RenamePlan> plan,
            string ffmpegPath,
            ExportOutputMode outputMode,
            bool watermarkEnabled,
            Action<RenamePlan, int> applyRowProgress,
            Action<int> applyOverallProgress,
            Action<ExportOutcome> completed)
        {
            FfmpegCancellation cancellation = new FfmpegCancellation();
            activeCancellation = cancellation;

            ThreadPool.QueueUserWorkItem(delegate
            {
                // 开工前清扫覆盖模式可能遗留的 .vmr_ 孤儿临时文件。
                HashSet<string> targetDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (RenamePlan item in plan)
                {
                    string dir = System.IO.Path.GetDirectoryName(item.OldPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        targetDirectories.Add(dir);
                    }
                }
                VideoExportService.SweepOrphanedExportTemps(targetDirectories);

                ExportOutcome outcome = new ExportOutcome();
                Dictionary<ShotRow, int> rowTotals = new Dictionary<ShotRow, int>();
                Dictionary<ShotRow, int> rowCompleted = new Dictionary<ShotRow, int>();
                foreach (RenamePlan item in plan)
                {
                    if (item.Row == null)
                    {
                        continue;
                    }
                    if (!rowTotals.ContainsKey(item.Row))
                    {
                        rowTotals[item.Row] = 0;
                        rowCompleted[item.Row] = 0;
                    }
                    rowTotals[item.Row]++;
                }

                int total = plan.Count;
                int index = 0;

                foreach (RenamePlan entry in plan)
                {
                    if (cancellation.IsCancelled)
                    {
                        outcome.Cancelled = true;
                        break;
                    }

                    index++;
                    int currentIndex = index;
                    RenamePlan currentEntry = entry;
                    dispatcher.Post(delegate
                    {
                        status.SetStatus(string.Format("正在导出 {0}/{1}：{2}", currentIndex, total, currentEntry.NewName));
                        if (applyOverallProgress != null)
                        {
                            applyOverallProgress((currentIndex - 1) * 100 / Math.Max(1, total));
                        }
                    });

                    try
                    {
                        Action<int> progressCallback = delegate(int percent)
                        {
                            if (currentEntry.Row == null)
                            {
                                return;
                            }

                            int completedCount = rowCompleted.ContainsKey(currentEntry.Row) ? rowCompleted[currentEntry.Row] : 0;
                            int rowTotal = rowTotals.ContainsKey(currentEntry.Row) ? Math.Max(1, rowTotals[currentEntry.Row]) : 1;
                            int safePercent = Math.Max(0, Math.Min(100, percent));
                            int rowPercent = (int)Math.Max(0, Math.Min(100, Math.Round((completedCount + safePercent / 100.0) * 100.0 / rowTotal)));
                            dispatcher.Post(delegate
                            {
                                applyRowProgress(currentEntry, rowPercent);
                                status.SetStatus(string.Format("正在导出 {0}/{1}：{2}（{3}%）", currentIndex, total, currentEntry.NewName, safePercent));
                                if (applyOverallProgress != null)
                                {
                                    applyOverallProgress((int)Math.Max(0, Math.Min(100, Math.Round(((currentIndex - 1) + safePercent / 100.0) * 100.0 / Math.Max(1, total)))));
                                }
                            });
                        };

                        VideoExportService.ExportOne(ffmpegPath, currentEntry, outputMode, watermarkEnabled, progressCallback, cancellation);
                        if (currentEntry.Row != null && rowCompleted.ContainsKey(currentEntry.Row))
                        {
                            rowCompleted[currentEntry.Row]++;
                            progressCallback(100);
                        }
                        outcome.Successes.Add(new RenameOperation
                        {
                            Row = currentEntry.Row,
                            RowIndex = currentEntry.RowIndex,
                            IsMain = currentEntry.IsMain,
                            FileIndex = currentEntry.FileIndex,
                            OriginalPath = currentEntry.OldPath,
                            RenamedPath = currentEntry.TargetPath
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        outcome.Cancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        outcome.Failures.Add(currentEntry.OldName + ": " + ex.Message);
                        if (currentEntry.Row != null && rowCompleted.ContainsKey(currentEntry.Row))
                        {
                            rowCompleted[currentEntry.Row]++;
                        }
                    }
                }

                activeCancellation = null;
                dispatcher.Post(delegate
                {
                    completed(outcome);
                });
            });
        }
    }
}
