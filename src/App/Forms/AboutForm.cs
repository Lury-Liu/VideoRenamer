using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VideoRenamer
{
    public class AboutForm : Form
    {
        private readonly bool darkMode;
        private readonly Label updateStatusLabel;
        private readonly Button checkUpdateButton;

        public AboutForm(LicenseInfo licenseInfo, bool darkMode)
        {
            this.darkMode = darkMode;
            Text = "关于";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 270);
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            AppIcon.Apply(this);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.Padding = new Padding(18, 16, 18, 14);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            Label title = new Label();
            title.Text = AppInfo.Name;
            title.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
            title.AutoSize = false;
            title.Height = 30;
            title.Dock = DockStyle.Top;
            title.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(title, 0, 0);

            Label infoLabel = new Label();
            infoLabel.AutoSize = false;
            infoLabel.Dock = DockStyle.Fill;
            infoLabel.TextAlign = ContentAlignment.TopLeft;
            infoLabel.Text = BuildInfoText(licenseInfo);
            layout.Controls.Add(infoLabel, 0, 1);

            updateStatusLabel = new Label();
            updateStatusLabel.AutoSize = false;
            updateStatusLabel.Height = 28;
            updateStatusLabel.Dock = DockStyle.Top;
            updateStatusLabel.Tag = "Muted";
            updateStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            updateStatusLabel.Text = "可从 GitHub 检查是否有新版本。";
            layout.Controls.Add(updateStatusLabel, 0, 2);

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel();
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.WrapContents = false;
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.Height = 38;
            buttonPanel.Margin = new Padding(0, 6, 0, 0);
            layout.Controls.Add(buttonPanel, 0, 3);

            Button closeButton = new Button();
            closeButton.Text = "关闭";
            closeButton.Width = 76;
            closeButton.Height = 30;
            closeButton.Click += delegate { Close(); };
            buttonPanel.Controls.Add(closeButton);

            checkUpdateButton = new Button();
            checkUpdateButton.Text = "检查更新";
            checkUpdateButton.Width = 94;
            checkUpdateButton.Height = 30;
            checkUpdateButton.Margin = new Padding(0, 2, 8, 2);
            checkUpdateButton.Click += delegate { CheckForUpdatesFromAbout(); };
            buttonPanel.Controls.Add(checkUpdateButton);

            UiTheme.ApplyForm(this, darkMode);
        }

        private string BuildInfoText(LicenseInfo licenseInfo)
        {
            List<string> lines = new List<string>();
            lines.Add("当前版本：" + AppInfo.Version);
            lines.Add("制作人：" + AppInfo.Author);
            lines.Add("");

            if (licenseInfo != null)
            {
                lines.Add("授权剩余时间：" + LicenseManager.GetRemainingDays(licenseInfo) + " 天");
                lines.Add("授权到期时间：" + licenseInfo.ExpiresUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            }
            else
            {
                lines.Add("授权剩余时间：未知");
            }

            return string.Join("\r\n", lines.ToArray());
        }

        private void CheckForUpdatesFromAbout()
        {
            SetUpdateBusy(true, "正在后台从 GitHub 拉取版本信息...");

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = null;
                Exception failure = null;
                try
                {
                    info = UpdateManager.GetLatestUpdateInfo();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                RunOnUi(delegate
                {
                    HandleUpdateCheckResult(info, failure);
                });
            });
        }

        private void HandleUpdateCheckResult(UpdateInfo info, Exception failure)
        {
            if (failure != null)
            {
                SetUpdateBusy(false, "检查失败。");
                MessageBox.Show(this, "检查更新失败：\r\n" + failure.Message, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (info == null)
            {
                SetUpdateBusy(false, "未获取到有效版本信息。");
                MessageBox.Show(this, "未能从 GitHub 获取有效的版本信息。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!UpdateManager.IsRemoteVersionNewer(info))
            {
                SetUpdateBusy(false, "当前已是最新版本。");
                MessageBox.Show(
                    this,
                    "当前已是最新版本。\r\n\r\n当前版本：" + AppInfo.Version + "\r\nGitHub 版本：" + UpdateManager.GetUpdateDisplayVersion(info),
                    "检查更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string message =
                "检测到新版本 " + UpdateManager.GetUpdateDisplayVersion(info) + "，当前版本 " + AppInfo.Version + "。\r\n\r\n" +
                "是否立即下载并更新？";
            if (!string.IsNullOrWhiteSpace(info.Notes))
            {
                message += "\r\n\r\n更新说明：\r\n" + info.Notes;
            }

            DialogResult result = MessageBox.Show(this, message, "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (result != DialogResult.Yes)
            {
                SetUpdateBusy(false, "已取消更新。");
                return;
            }

            if (!UpdatePrompter.CanAutoInstallUpdate())
            {
                SetUpdateBusy(false, "当前不是正式 EXE 运行状态。");
                MessageBox.Show(this, "当前不是正式 EXE 运行状态，无法自动替换更新。", "无法更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StartBackgroundUpdateDownload(info);
        }

        private void StartBackgroundUpdateDownload(UpdateInfo info)
        {
            SetUpdateBusy(true, "正在下载更新...");
            bool started = UpdatePrompter.DownloadAndRestartWithProgress(info, this);
            if (!started)
            {
                SetUpdateBusy(false, "更新未完成。");
            }
        }

        private void SetUpdateBusy(bool busy, string status)
        {
            if (checkUpdateButton != null)
            {
                checkUpdateButton.Enabled = !busy;
                UiTheme.ApplyControl(checkUpdateButton, darkMode);
            }
            if (updateStatusLabel != null)
            {
                updateStatusLabel.Text = status;
                UiTheme.ApplyControl(updateStatusLabel, darkMode);
            }
        }

        private void RunOnUi(MethodInvoker action)
        {
            if (action == null || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
