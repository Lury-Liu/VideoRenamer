using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VideoRenamer
{
    public partial class MaterialRenamerForm
    {
        private Control BuildDirectorySettingsRow(Panel topPanel)
        {
            FlowLayoutPanel directoryPanel = new FlowLayoutPanel();
            directoryPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            directoryPanel.FlowDirection = FlowDirection.LeftToRight;
            directoryPanel.WrapContents = false;
            directoryPanel.AutoScroll = true;
            directoryPanel.BackColor = UiTheme.PanelBack(darkMode);
            directoryPanel.Location = new Point(ZoneBadgeBaselineLeft, 48);
            directoryPanel.Size = new Size(1196, 34);
            directoryPanel.Padding = new Padding(0);
            directoryPanel.Margin = new Padding(0);

            directoryPanel.Controls.Add(NewDirectoryLabel("对比文件夹"));
            txtComparisonDirectory = new TextBox();
            txtComparisonDirectory.Width = 246;
            txtComparisonDirectory.Height = 26;
            txtComparisonDirectory.Margin = new Padding(0, 4, 4, 0);
            txtComparisonDirectory.TextChanged += delegate { ScheduleNamesOnlyRefresh(); };
            directoryPanel.Controls.Add(txtComparisonDirectory);

            btnChooseComparisonDirectory = NewButton("选择", 56);
            btnChooseComparisonDirectory.Margin = new Padding(0, 2, 14, 2);
            btnChooseComparisonDirectory.Click += delegate
            {
                ChooseDirectory(txtComparisonDirectory, "选择已经命名的对比文件夹");
            };
            directoryPanel.Controls.Add(btnChooseComparisonDirectory);

            directoryPanel.Controls.Add(NewDirectoryLabel("输出文件夹"));
            txtOutputDirectory = new TextBox();
            txtOutputDirectory.Width = 246;
            txtOutputDirectory.Height = 26;
            txtOutputDirectory.Margin = new Padding(0, 4, 4, 0);
            txtOutputDirectory.TextChanged += delegate { ScheduleNamesOnlyRefresh(); };
            directoryPanel.Controls.Add(txtOutputDirectory);

            btnChooseOutputDirectory = NewButton("选择", 56);
            btnChooseOutputDirectory.Margin = new Padding(0, 2, 14, 2);
            btnChooseOutputDirectory.Click += delegate
            {
                ChooseDirectory(txtOutputDirectory, "选择重命名或导出的输出文件夹");
            };
            directoryPanel.Controls.Add(btnChooseOutputDirectory);

            chkAutoResolveConflicts = new CheckBox();
            chkAutoResolveConflicts.Text = "冲突自动递增";
            chkAutoResolveConflicts.Checked = true;
            chkAutoResolveConflicts.AutoSize = true;
            chkAutoResolveConflicts.Margin = new Padding(0, 7, 0, 0);
            chkAutoResolveConflicts.CheckedChanged += delegate { ScheduleNamesOnlyRefresh(); };
            directoryPanel.Controls.Add(chkAutoResolveConflicts);

            topPanel.Controls.Add(directoryPanel);
            return directoryPanel;
        }

        private static Label NewDirectoryLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Width = 70;
            label.Height = 30;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            label.Margin = new Padding(0, 2, 4, 0);
            return label;
        }

        private void ChooseDirectory(TextBox target, string description)
        {
            if (target == null)
            {
                return;
            }

            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = description;
                string current = target.Text == null ? "" : target.Text.Trim();
                if (Directory.Exists(current))
                {
                    dialog.SelectedPath = current;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    target.Text = dialog.SelectedPath;
                }
            }
        }

        private HashSet<string> ReadComparisonFileNames()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (txtComparisonDirectory == null)
            {
                return names;
            }

            string directory = txtComparisonDirectory.Text == null ? "" : txtComparisonDirectory.Text.Trim();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return names;
            }

            try
            {
                foreach (string path in Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(path);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
            }
            catch (Exception)
            {
                // A missing or inaccessible comparison folder must not block preview.
            }

            return names;
        }

        private string GetOutputDirectoryForExport()
        {
            return txtOutputDirectory == null || txtOutputDirectory.Text == null
                ? ""
                : txtOutputDirectory.Text.Trim();
        }
    }
}