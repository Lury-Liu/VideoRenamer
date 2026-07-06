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
    public class LicenseDialog : Form
    {
        private readonly TextBox keyBox;
        private readonly Label messageLabel;

        public LicenseDialog(string machineCode, string reason)
        {
            Text = "输入授权密钥";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(560, 330);
            MinimumSize = new Size(520, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9f);
            AppIcon.Apply(this);

            Label title = new Label();
            title.Text = "需要授权后才能使用";
            title.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(18, 16);
            Controls.Add(title);

            messageLabel = new Label();
            messageLabel.Text = string.IsNullOrWhiteSpace(reason) ? "请输入授权密钥。" : reason;
            messageLabel.Tag = "Error";
            messageLabel.AutoSize = false;
            messageLabel.Size = new Size(500, 36);
            messageLabel.Location = new Point(20, 48);
            Controls.Add(messageLabel);

            Label machineLabel = new Label();
            machineLabel.Text = "本机机器码";
            machineLabel.AutoSize = true;
            machineLabel.Location = new Point(20, 94);
            Controls.Add(machineLabel);

            TextBox machineBox = new TextBox();
            machineBox.Text = machineCode;
            machineBox.ReadOnly = true;
            machineBox.Location = new Point(110, 90);
            machineBox.Width = 290;
            Controls.Add(machineBox);

            Button copyButton = new Button();
            copyButton.Text = "复制机器码";
            copyButton.Location = new Point(412, 88);
            copyButton.Width = 100;
            copyButton.Click += delegate
            {
                Clipboard.SetText(machineCode);
                messageLabel.Text = "机器码已复制。";
            };
            Controls.Add(copyButton);

            Label keyLabel = new Label();
            keyLabel.Text = "授权密钥";
            keyLabel.AutoSize = true;
            keyLabel.Location = new Point(20, 136);
            Controls.Add(keyLabel);

            keyBox = new TextBox();
            keyBox.Multiline = true;
            keyBox.ScrollBars = ScrollBars.Vertical;
            keyBox.Location = new Point(110, 132);
            keyBox.Size = new Size(402, 82);
            Controls.Add(keyBox);

            Button unlockButton = new Button();
            unlockButton.Text = "解锁";
            unlockButton.Location = new Point(330, 232);
            unlockButton.Width = 86;
            unlockButton.Click += delegate
            {
                string message;
                if (LicenseManager.TryActivate(keyBox.Text, out message))
                {
                    MessageBox.Show(this, message, "授权成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    messageLabel.Text = message;
                }
            };
            Controls.Add(unlockButton);

            Button exitButton = new Button();
            exitButton.Text = "退出";
            exitButton.Location = new Point(426, 232);
            exitButton.Width = 86;
            exitButton.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(exitButton);

            UiTheme.ApplyForm(this, UiTheme.DetectWindowsDarkMode());
        }
    }
}
