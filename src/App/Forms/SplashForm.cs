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
    public class SplashForm : Form
    {
        private readonly System.Windows.Forms.Timer closeTimer;

        public SplashForm(LicenseInfo licenseInfo, bool darkMode)
        {
            Text = "启动";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(560, 292);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            AppIcon.Apply(this);

            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Padding = new Padding(30, 26, 30, 22);
            Controls.Add(panel);

            PictureBox mark = new PictureBox();
            mark.Location = new Point(32, 34);
            mark.Size = new Size(64, 64);
            mark.SizeMode = PictureBoxSizeMode.Zoom;
            Icon icon = AppIcon.Get();
            if (icon != null)
            {
                mark.Image = icon.ToBitmap();
            }
            panel.Controls.Add(mark);

            Label title = new Label();
            title.Text = "视频素材镜头表命名工具";
            title.Font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold);
            title.AutoSize = false;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Location = new Point(116, 30);
            title.Size = new Size(398, 42);
            panel.Controls.Add(title);

            Label version = new Label();
            version.Text = "版本 " + AppInfo.Version;
            version.Tag = "Muted";
            version.AutoSize = false;
            version.TextAlign = ContentAlignment.MiddleLeft;
            version.Location = new Point(118, 76);
            version.Size = new Size(396, 24);
            panel.Controls.Add(version);

            Panel divider = new Panel();
            divider.Location = new Point(32, 122);
            divider.Size = new Size(482, 1);
            panel.Controls.Add(divider);

            Label author = new Label();
            author.Text = "制作人：" + AppInfo.Author;
            author.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            author.AutoSize = false;
            author.TextAlign = ContentAlignment.MiddleLeft;
            author.Location = new Point(34, 142);
            author.Size = new Size(480, 30);
            panel.Controls.Add(author);

            Label days = new Label();
            days.Text = "剩余激活天数：" + LicenseManager.GetRemainingDays(licenseInfo) + " 天";
            days.AutoSize = false;
            days.TextAlign = ContentAlignment.MiddleLeft;
            days.Location = new Point(34, 176);
            days.Size = new Size(480, 28);
            panel.Controls.Add(days);

            Label hint = new Label();
            hint.Text = "正在启动，窗口将在 4 秒后自动关闭";
            hint.Tag = "Muted";
            hint.AutoSize = false;
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Location = new Point(34, 210);
            hint.Size = new Size(480, 28);
            panel.Controls.Add(hint);

            Panel line = new Panel();
            line.Dock = DockStyle.Bottom;
            line.Height = 4;
            line.BackColor = UiTheme.SelectionBack(darkMode);
            panel.Controls.Add(line);

            closeTimer = new System.Windows.Forms.Timer();
            closeTimer.Interval = 4000;
            closeTimer.Tick += delegate
            {
                closeTimer.Stop();
                Close();
            };
            Shown += delegate { closeTimer.Start(); };
            Click += delegate { Close(); };
            panel.Click += delegate { Close(); };

            UiTheme.ApplyForm(this, darkMode);
            divider.BackColor = UiTheme.BorderColor(darkMode);
            line.BackColor = UiTheme.SelectionBack(darkMode);
            mark.BackColor = UiTheme.PanelBack(darkMode);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closeTimer.Stop();
            closeTimer.Dispose();
            base.OnFormClosed(e);
        }
    }
}
