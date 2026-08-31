using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace VideoRenamer
{
    // 更新流程的 UI 侧（阶段12a，自 UpdateManager 逐字迁入）：提示对话框、
    // 进度窗、以及"下载→校验→启动替换脚本→退出进程"的编排。
    // 纯逻辑（清单获取/解析/版本比较/下载/校验/脚本生成）留在 Services 的
    // UpdateManager——该类自此不再触碰 WinForms。
    // 启动提示文案为冻结行为，逐字保持。
    public static class UpdatePrompter
    {
        public static bool CanAutoInstallUpdate()
        {
            return IsRunningPackagedExecutable();
        }

        public static bool PromptAndInstallUpdate(UpdateInfo info, IWin32Window owner)
        {
            try
            {
                if (info == null || !UpdateManager.IsNewerVersion(info.Version, AppInfo.Version) || !IsRunningPackagedExecutable())
                {
                    return false;
                }

                string message =
                    "检测到新版本 " + UpdateManager.GetUpdateDisplayVersion(info) + "，当前版本 " + AppInfo.Version + "。\r\n\r\n" +
                    "是否立即下载并更新？\r\n\r\n" +
                    "选择“否”后，本次不会更新，下次启动仍会提示。";
                if (!string.IsNullOrWhiteSpace(info.Notes))
                {
                    message += "\r\n\r\n更新说明：\r\n" + info.Notes;
                }

                DialogResult result = MessageBox.Show(owner, message, "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result != DialogResult.Yes)
                {
                    return false;
                }

                return DownloadAndRestart(info, owner);
            }
            catch (Exception ex)
            {
                AppLog.Write("update", "更新提示/安装流程异常", ex);
                return false;
            }
        }

        public static bool DownloadAndRestartWithProgress(UpdateInfo info, IWin32Window owner)
        {
            return DownloadAndRestart(info, owner);
        }

        private static bool IsRunningPackagedExecutable()
        {
            try
            {
                string exePath = Application.ExecutablePath;
                string name = Path.GetFileNameWithoutExtension(exePath) ?? "";
                return File.Exists(exePath) &&
                    Path.GetExtension(exePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                    name.IndexOf(AppInfo.Name, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool DownloadAndRestart(UpdateInfo info, IWin32Window owner)
        {
            string error = "";
            bool started = false;
            bool userCancelled = false;
            using (UpdateDownloadProgressForm progressForm = new UpdateDownloadProgressForm(UiTheme.DetectWindowsDarkMode()))
            {
                progressForm.Shown += delegate
                {
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        string threadError;
                        bool threadStarted = TryDownloadAndRestart(info, delegate(string status, int percent, long bytesReceived, long totalBytes)
                        {
                            progressForm.UpdateProgress(status, percent, bytesReceived, totalBytes);
                        }, delegate
                        {
                            return progressForm.CancelRequested;
                        }, out threadError);

                        started = threadStarted;
                        if (!threadStarted)
                        {
                            error = threadError;
                            try
                            {
                                progressForm.BeginInvoke((MethodInvoker)delegate
                                {
                                    progressForm.Close();
                                });
                            }
                            catch
                            {
                            }
                        }
                    });
                };

                if (owner == null)
                {
                    progressForm.ShowDialog();
                }
                else
                {
                    progressForm.ShowDialog(owner);
                }

                userCancelled = progressForm.CancelRequested;
            }

            if (!started && !userCancelled)
            {
                MessageBox.Show(owner, "更新失败：\r\n" + error, "无法更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return started;
        }

        private static bool TryDownloadAndRestart(UpdateInfo info, Action<string, int, long, long> progress, Func<bool> cancelRequested, out string error)
        {
            error = "";
            if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl))
            {
                error = "更新清单中没有下载地址。";
                return false;
            }

            string currentExe = Application.ExecutablePath;
            string updateDir = Path.Combine(Path.GetTempPath(), AppInfo.Name + "_Update");
            Directory.CreateDirectory(updateDir);
            string downloadPath = Path.Combine(updateDir, "update_" + Guid.NewGuid().ToString("N") + ".exe");

            try
            {
                UpdateManager.ReportUpdateProgress(progress, "正在准备下载更新...", 0, 0, 0);
                UpdateManager.DownloadUpdateFile(info, downloadPath, progress, cancelRequested);

                if (!File.Exists(downloadPath) || new FileInfo(downloadPath).Length == 0)
                {
                    throw new IOException("下载后的更新文件为空。");
                }

                UpdateManager.ReportUpdateProgress(progress, "正在校验更新文件...", 96, 0, 0);
                if (!UpdateManager.IsValidSha256(info.Sha256))
                {
                    throw new IOException("更新清单缺少有效的 SHA-256，已取消安装。");
                }

                string actualHash = UpdateManager.ComputeSha256(downloadPath);
                if (!StringComparer.OrdinalIgnoreCase.Equals(actualHash, info.Sha256))
                {
                    throw new IOException("更新文件校验失败，已取消安装。");
                }

                UpdateManager.ReportUpdateProgress(progress, "正在准备重启软件...", 100, 0, 0);
                // 阶段12b：目标目录不可写（例如受保护目录）→ 提权启动
                // 替换脚本（一次 UAC）；脚本启动失败（如拒绝提权）绝不退出
                // 进程——报错并回到可用状态。
                bool elevate = !UpdateManager.IsDirectoryWritable(Path.GetDirectoryName(currentExe));
                if (!UpdateManager.StartUpdaterProcess(currentExe, downloadPath, elevate))
                {
                    throw new IOException(elevate
                        ? "更新需要管理员权限，但提权被拒绝或失败；本次未更新。"
                        : "启动更新脚本失败；本次未更新。");
                }
                Application.Exit();
                Environment.Exit(0);
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(downloadPath))
                    {
                        File.Delete(downloadPath);
                    }
                }
                catch
                {
                }

                error = ex.Message;
                return false;
            }
        }
    }
}
