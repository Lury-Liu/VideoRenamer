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

namespace VideoMaterialRenamer
{
    public class UpdateDownloadProgressForm : Form
    {
        private readonly bool darkMode;
        private readonly Label statusLabel;
        private readonly Label detailLabel;
        private readonly ProgressBar progressBar;

        public UpdateDownloadProgressForm(bool darkMode)
        {
            this.darkMode = darkMode;
            Text = "正在更新";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 150);
            Font = new Font("Microsoft YaHei UI", 9f);
            AppIcon.Apply(this);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.Padding = new Padding(18, 16, 18, 14);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.Dock = DockStyle.Top;
            statusLabel.Height = 30;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Text = "正在准备下载更新...";
            layout.Controls.Add(statusLabel, 0, 0);

            progressBar = new ProgressBar();
            progressBar.Dock = DockStyle.Top;
            progressBar.Height = 24;
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Value = 0;
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Margin = new Padding(0, 8, 0, 8);
            layout.Controls.Add(progressBar, 0, 1);

            detailLabel = new Label();
            detailLabel.AutoSize = false;
            detailLabel.Dock = DockStyle.Top;
            detailLabel.Height = 34;
            detailLabel.Tag = "Muted";
            detailLabel.TextAlign = ContentAlignment.MiddleLeft;
            detailLabel.Text = "请不要关闭软件。";
            layout.Controls.Add(detailLabel, 0, 2);

            UiTheme.ApplyForm(this, darkMode);
        }

        public void UpdateProgress(string status, int percent, long bytesReceived, long totalBytes)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        UpdateProgress(status, percent, bytesReceived, totalBytes);
                    });
                }
                catch
                {
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                statusLabel.Text = status;
            }

            if (percent >= 0)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = Math.Max(progressBar.Minimum, Math.Min(progressBar.Maximum, percent));
            }
            else
            {
                progressBar.Style = ProgressBarStyle.Marquee;
            }

            detailLabel.Text = BuildDetailText(percent, bytesReceived, totalBytes);
            UiTheme.ApplyControl(statusLabel, darkMode);
            UiTheme.ApplyControl(detailLabel, darkMode);
        }

        private static string BuildDetailText(int percent, long bytesReceived, long totalBytes)
        {
            if (totalBytes > 0)
            {
                return percent.ToString() + "%  " + FormatBytes(bytesReceived) + " / " + FormatBytes(totalBytes);
            }

            if (bytesReceived > 0)
            {
                return "已下载 " + FormatBytes(bytesReceived);
            }

            return percent >= 0 ? percent.ToString() + "%" : "正在连接，请稍候...";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return (bytes / 1024d / 1024d / 1024d).ToString("0.00") + " GB";
            }

            if (bytes >= 1024L * 1024L)
            {
                return (bytes / 1024d / 1024d).ToString("0.0") + " MB";
            }

            if (bytes >= 1024L)
            {
                return (bytes / 1024d).ToString("0.0") + " KB";
            }

            return bytes.ToString() + " B";
        }
    }
}
