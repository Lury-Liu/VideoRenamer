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
    public class DisclaimerDialog : Form
    {
        private readonly CheckBox agreeCheck;
        private readonly CheckBox disagreeCheck;
        private readonly Button acceptButton;

        public DisclaimerDialog(bool darkMode)
        {
            Text = "免责协议";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(720, 560);
            MinimumSize = new Size(640, 500);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9f);
            AppIcon.Apply(this);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.Padding = new Padding(18, 16, 18, 16);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(layout);

            Label title = new Label();
            title.Text = "使用前请阅读并确认";
            title.Dock = DockStyle.Fill;
            title.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            layout.Controls.Add(title, 0, 0);

            TextBox body = new TextBox();
            body.Multiline = true;
            body.ReadOnly = true;
            body.ScrollBars = ScrollBars.Vertical;
            body.Dock = DockStyle.Fill;
            body.Text =
                "【重要提示】\r\n\r\n" +
                "本软件为个人兴趣开发的辅助工具，非商业软件，仅供您自愿试用。\r\n\r\n" +
                "使用前请您务必知悉：\r\n" +
                "1. 本软件涉及对文件的批量重命名操作，极大可能因代码缺陷、系统环境差异、个人操作不当等原因，导致文件重命名错误、文件丢失或数据损坏。\r\n" +
                "2. 您在使用本软件前，【必须自行备份所有重要文件】。因使用本软件造成的任何数据丢失或损坏，开发者不承担赔偿责任。\r\n" +
                "3. 建议您首先在【测试文件副本】上试用，确认功能正常后再用于正式素材。\r\n\r\n" +
                "【我已阅读并理解上述风险，自愿使用本软件】";
            layout.Controls.Add(body, 0, 1);

            FlowLayoutPanel choices = new FlowLayoutPanel();
            choices.Dock = DockStyle.Fill;
            choices.FlowDirection = FlowDirection.TopDown;
            choices.WrapContents = false;
            choices.Padding = new Padding(0, 8, 0, 0);
            layout.Controls.Add(choices, 0, 2);

            agreeCheck = new CheckBox();
            agreeCheck.Text = "我同意";
            agreeCheck.AutoSize = true;
            agreeCheck.Margin = new Padding(0, 0, 0, 8);
            agreeCheck.CheckedChanged += delegate
            {
                if (agreeCheck.Checked)
                {
                    disagreeCheck.Checked = false;
                }
                acceptButton.Enabled = agreeCheck.Checked;
            };
            choices.Controls.Add(agreeCheck);

            disagreeCheck = new CheckBox();
            disagreeCheck.Text = "不同意并退出";
            disagreeCheck.AutoSize = true;
            disagreeCheck.CheckedChanged += delegate
            {
                if (disagreeCheck.Checked)
                {
                    agreeCheck.Checked = false;
                    acceptButton.Enabled = false;
                }
            };
            choices.Controls.Add(disagreeCheck);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            layout.Controls.Add(buttons, 0, 3);

            Button exitButton = new Button();
            exitButton.Text = "不同意并退出";
            exitButton.Width = 120;
            exitButton.Height = 30;
            exitButton.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            buttons.Controls.Add(exitButton);

            acceptButton = new Button();
            acceptButton.Text = "接受并继续";
            acceptButton.Tag = "Primary";
            acceptButton.Width = 110;
            acceptButton.Height = 30;
            acceptButton.Enabled = false;
            acceptButton.Click += delegate
            {
                if (!agreeCheck.Checked)
                {
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            };
            buttons.Controls.Add(acceptButton);

            UiTheme.ApplyForm(this, darkMode);
        }
    }
}
