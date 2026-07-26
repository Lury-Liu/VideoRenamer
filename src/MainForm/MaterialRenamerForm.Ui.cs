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
    public partial class MaterialRenamerForm
    {

        private void BuildUi()
        {
            Text = "视频素材镜头表命名工具";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1240, 820);
            MinimumSize = new Size(820, 680);
            Font = new Font("Microsoft YaHei UI", 9f);
            AppIcon.Apply(this);

            Panel headerHost = new Panel();
            headerHost.Dock = DockStyle.Top;
            headerHost.Height = 100;
            Controls.Add(headerHost);

            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Fill;
            topPanel.Padding = new Padding(14, 10, 14, 8);
            topPanel.BackColor = Color.FromArgb(246, 247, 249);
            headerHost.Controls.Add(topPanel);

            FlowLayoutPanel settingsPanel = new FlowLayoutPanel();
            settingsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            settingsPanel.FlowDirection = FlowDirection.LeftToRight;
            settingsPanel.WrapContents = false;
            settingsPanel.BackColor = Color.FromArgb(246, 247, 249);
            settingsPanel.Location = new Point(14, 10);
            settingsPanel.Size = new Size(1196, 34);
            topPanel.Controls.Add(settingsPanel);

            Label labelEpisode = new Label();
            labelEpisode.Text = "集数 E";
            labelEpisode.AutoSize = true;
            labelEpisode.Font = new Font(Font, FontStyle.Bold);
            labelEpisode.Margin = new Padding(4, 8, 4, 0);
            settingsPanel.Controls.Add(labelEpisode);

            numEpisode = new NumericUpDown();
            numEpisode.Minimum = 1;
            numEpisode.Maximum = 9999;
            numEpisode.Value = 5;
            numEpisode.Width = 78;
            numEpisode.Margin = new Padding(0, 4, 14, 0);
            numEpisode.ValueChanged += delegate { RefreshPreview(); };
            settingsPanel.Controls.Add(numEpisode);

            Label labelScene = new Label();
            labelScene.Text = "场号 S";
            labelScene.AutoSize = true;
            labelScene.Font = new Font(Font, FontStyle.Bold);
            labelScene.Margin = new Padding(4, 8, 4, 0);
            settingsPanel.Controls.Add(labelScene);

            numScene = new NumericUpDown();
            numScene.Minimum = 1;
            numScene.Maximum = 9999;
            numScene.Value = 1;
            numScene.Width = 78;
            numScene.Margin = new Padding(0, 4, 14, 0);
            numScene.ValueChanged += delegate { RefreshPreview(); };
            settingsPanel.Controls.Add(numScene);

            chkRowScene = new CheckBox();
            chkRowScene.Text = "逐行场号";
            chkRowScene.AutoSize = true;
            chkRowScene.Margin = new Padding(0, 7, 14, 0);
            chkRowScene.CheckedChanged += delegate
            {
                if (chkRowScene.Checked)
                {
                    InitializeRowScenesFromDefaultIfNeeded();
                }

                RenderGrid();
                RefreshPreview();
                UpdateSelectedCellDetails();
            };
            settingsPanel.Controls.Add(chkRowScene);

            chkKeepExtension = new CheckBox();
            chkKeepExtension.Text = "保留扩展名大小写";
            chkKeepExtension.Checked = true;
            chkKeepExtension.AutoSize = true;
            chkKeepExtension.Margin = new Padding(0, 7, 14, 0);
            chkKeepExtension.CheckedChanged += delegate { RefreshPreview(); };
            settingsPanel.Controls.Add(chkKeepExtension);

            chkExport1080p = new CheckBox();
            chkExport1080p.Text = "导出1080x1920";
            chkExport1080p.AutoSize = true;
            chkExport1080p.Margin = new Padding(0, 7, 14, 0);
            chkExport1080p.CheckedChanged += delegate
            {
                RefreshPreview();
                UpdateRenameButtonText();
                UpdateWatermarkOptionState();
            };
            settingsPanel.Controls.Add(chkExport1080p);

            chkExportWatermark = new CheckBox();
            chkExportWatermark.Text = "文件名水印";
            chkExportWatermark.Checked = false;
            chkExportWatermark.AutoSize = true;
            chkExportWatermark.Margin = new Padding(0, 7, 14, 0);
            chkExportWatermark.CheckedChanged += delegate
            {
                UpdateWatermarkOptionState();
                RefreshPreview();
            };
            settingsPanel.Controls.Add(chkExportWatermark);
            UpdateWatermarkOptionState();

            btnTheme = NewButton("", 112);
            btnTheme.Click += delegate { ToggleTheme(); };
            btnTheme.Margin = new Padding(0, 2, 6, 2);
            settingsPanel.Controls.Add(btnTheme);

            btnAbout = NewButton("关于", 58);
            btnAbout.Click += delegate { ShowAboutInfo(); };
            btnAbout.Margin = new Padding(0, 2, 0, 2);
            settingsPanel.Controls.Add(btnAbout);

            FlowLayoutPanel actionBar = new FlowLayoutPanel();
            actionBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            actionBar.BackColor = Color.FromArgb(246, 247, 249);
            actionBar.FlowDirection = FlowDirection.LeftToRight;
            actionBar.WrapContents = false;
            actionBar.Location = new Point(14, 48);
            actionBar.Size = new Size(1196, 36);
            actionBar.Padding = new Padding(0);
            actionBar.Margin = new Padding(0);
            topPanel.Controls.Add(actionBar);

            Button btnAddRow = NewButton("新增行", 72);
            btnAddRow.Click += delegate { AddEmptyRow(); };
            actionBar.Controls.Add(btnAddRow);

            Button btnMoveRowUp = NewButton("上移", 58);
            btnMoveRowUp.Click += delegate { MoveCurrentRow(-1); };
            actionBar.Controls.Add(btnMoveRowUp);

            Button btnMoveRowDown = NewButton("下移", 58);
            btnMoveRowDown.Click += delegate { MoveCurrentRow(1); };
            actionBar.Controls.Add(btnMoveRowDown);

            Button btnDeleteRow = NewButton("删除行", 72);
            btnDeleteRow.Click += delegate { DeleteCurrentRow(); };
            actionBar.Controls.Add(btnDeleteRow);
            actionBar.Controls.Add(NewActionSeparator());

            Button btnImportCell = NewButton("导入素材", 82);
            btnImportCell.Click += delegate { ImportSelectedCell(); };
            actionBar.Controls.Add(btnImportCell);

            Button btnDeleteRecord = NewButton("删除记录", 82);
            btnDeleteRecord.Click += delegate { DeleteSelectedPreviewRecord(); };
            actionBar.Controls.Add(btnDeleteRecord);

            Button btnClearCell = NewButton("清空格", 72);
            btnClearCell.Click += delegate { ClearSelectedCellFiles(); };
            actionBar.Controls.Add(btnClearCell);

            Button btnRemoveTail = NewButton("删除空尾行", 100);
            btnRemoveTail.Click += delegate { RemoveEmptyTailRows(); };
            actionBar.Controls.Add(btnRemoveTail);

            Button btnClearAll = NewButton("全局清空", 88);
            btnClearAll.Click += delegate { ClearAllMaterials(); };
            actionBar.Controls.Add(btnClearAll);
            actionBar.Controls.Add(NewActionSeparator());

            Button btnUndo = NewButton("取消命名", 85);
            btnUndo.Click += delegate { RestoreLastRename(); };
            actionBar.Controls.Add(btnUndo);

            btnRename = NewButton("执行重命名",100);
            btnRename.Tag = "Primary";
            btnRename.Click += delegate { RenameFiles(); };
            actionBar.Controls.Add(btnRename);

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.Height = 28;
            statusLabel.Padding = new Padding(12, 6, 12, 0);
            statusLabel.Tag = "Muted";
            statusLabel.Text = "把视频拖到表格 B「主要素材」或 C「备用素材」单元格。";
            Controls.Add(statusLabel);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            Controls.Add(split);
            split.BringToFront();

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowDrop = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.ColumnHeadersHeight = 34;
            grid.RowHeadersVisible = true;
            grid.RowTemplate.Height = 38;
            grid.RowTemplate.Resizable = DataGridViewTriState.False;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.MultiSelect = false;
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.DefaultCellStyle.Padding = new Padding(4);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            grid.CellEndEdit += OnGridCellEndEdit;
            grid.SelectionChanged += delegate { UpdateSelectedCellDetails(); };
            grid.DragEnter += OnGridDragEnterOrOver;
            grid.DragOver += OnGridDragEnterOrOver;
            grid.DragLeave += delegate { ClearDragHighlight(); };
            grid.DragDrop += OnGridDragDrop;

            DataGridViewTextBoxColumn colScene = new DataGridViewTextBoxColumn();
            colScene.HeaderText = "A 场号";
            colScene.Width = 72;
            colScene.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn colSeq = new DataGridViewTextBoxColumn();
            colSeq.HeaderText = "B 镜号";
            colSeq.Width = 82;
            colSeq.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn colMain = new DataGridViewTextBoxColumn();
            colMain.HeaderText = "C 主要素材";
            colMain.Width = 310;
            colMain.ReadOnly = true;
            colMain.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn colBackup = new DataGridViewTextBoxColumn();
            colBackup.HeaderText = "D 备用素材";
            colBackup.Width = 310;
            colBackup.ReadOnly = true;
            colBackup.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewProgressColumn colProgress = new DataGridViewProgressColumn();
            colProgress.HeaderText = "E 进度";
            colProgress.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colProgress.MinimumWidth = 130;
            colProgress.SortMode = DataGridViewColumnSortMode.NotSortable;
            colProgress.Visible = false;

            grid.Columns.Add(colScene);
            grid.Columns.Add(colSeq);
            grid.Columns.Add(colMain);
            grid.Columns.Add(colBackup);
            grid.Columns.Add(colProgress);
            ApplyGridColumnLayout();
            split.Panel1.Controls.Add(grid);

            TableLayoutPanel previewShell = new TableLayoutPanel();
            previewShell.Dock = DockStyle.Fill;
            previewShell.ColumnCount = 1;
            previewShell.RowCount = 3;
            previewShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            previewShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            previewShell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            split.Panel2.Controls.Add(previewShell);

            Label previewTitle = new Label();
            previewTitle.Text = "重命名预览";
            previewTitle.Dock = DockStyle.Fill;
            previewTitle.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            previewTitle.Padding = new Padding(4, 6, 0, 0);
            previewShell.Controls.Add(previewTitle, 0, 0);

            previewShell.Controls.Add(BuildCustomTailPanel(), 0, 1);

            Panel previewBody = new Panel();
            previewBody.Dock = DockStyle.Fill;
            previewShell.Controls.Add(previewBody, 0, 2);

            Panel detailHost = new Panel();
            detailHost.Dock = DockStyle.Right;
            detailHost.Width = 320;
            detailHost.MinimumSize = new Size(280, 0);

            previewList = new ListView();
            previewList.Dock = DockStyle.Fill;
            previewList.View = View.Details;
            previewList.FullRowSelect = true;
            previewList.GridLines = true;
            previewList.HideSelection = false;
            previewList.MultiSelect = true;
            previewList.ShowGroups = true;
            previewList.Columns.Add("行", 56);
            previewList.Columns.Add("镜号", 58);
            previewList.Columns.Add("末尾", 68);
            previewList.Columns.Add("列", 90);
            previewList.Columns.Add("原文件名", 144);
            previewList.Columns.Add("新文件名", 260);
            previewList.Columns.Add("状态", 110);
            previewList.Columns.Add("信息", 160);
            previewList.SelectedIndexChanged += delegate { UpdateSelectedPreviewDetails(); };
            previewList.SizeChanged += delegate { SchedulePreviewColumnResize(); };
            previewList.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete)
                {
                    DeleteSelectedPreviewRecord();
                    e.Handled = true;
                }
            };
            previewBody.Controls.Add(previewList);
            previewBody.Controls.Add(detailHost);
            detailHost.BringToFront();
            detailHost.Controls.Add(BuildVideoDetailsPanel());
        }

        private Control BuildVideoDetailsPanel()
        {
            TableLayoutPanel panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 1;
            panel.RowCount = 5;
            panel.Padding = new Padding(10, 8, 10, 10);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Label title = new Label();
            title.Text = "素材信息预览";
            title.Dock = DockStyle.Fill;
            title.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            title.Padding = new Padding(0, 4, 0, 0);
            panel.Controls.Add(title, 0, 0);

            thumbnailBox = new PictureBox();
            thumbnailBox.Dock = DockStyle.Fill;
            thumbnailBox.SizeMode = PictureBoxSizeMode.Zoom;
            thumbnailBox.BorderStyle = BorderStyle.FixedSingle;
            thumbnailBox.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (thumbnailBox.Width > 1)
                {
                    double ratio = Math.Max(0.0, Math.Min(1.0, (double)e.X / thumbnailBox.Width));
                    ShowFrameAtRatio(ratio);
                }
            };
            thumbnailBox.MouseLeave += delegate { RestoreStaticThumbnail(); };
            panel.Controls.Add(thumbnailBox, 0, 1);

            detailTitleLabel = new Label();
            detailTitleLabel.Dock = DockStyle.Fill;
            detailTitleLabel.AutoEllipsis = true;
            detailTitleLabel.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            detailTitleLabel.Padding = new Padding(0, 8, 0, 0);
            panel.Controls.Add(detailTitleLabel, 0, 2);

            detailInfoLabel = new Label();
            detailInfoLabel.Dock = DockStyle.Fill;
            detailInfoLabel.AutoEllipsis = false;
            detailInfoLabel.Padding = new Padding(0, 4, 0, 0);
            panel.Controls.Add(detailInfoLabel, 0, 3);

            detailPathLabel = new Label();
            detailPathLabel.Dock = DockStyle.Fill;
            detailPathLabel.Tag = "Muted";
            detailPathLabel.AutoEllipsis = true;
            detailPathLabel.Padding = new Padding(0, 4, 0, 0);
            panel.Controls.Add(detailPathLabel, 0, 4);

            ShowNoVideoDetails();
            return panel;
        }

        private Control BuildCustomTailPanel()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.FlowDirection = FlowDirection.LeftToRight;
            panel.WrapContents = false;
            panel.Padding = new Padding(6, 6, 6, 4);
            panel.Margin = new Padding(0);

            Label label = new Label();
            label.Text = "新文件名末尾";
            label.Tag = "Muted";
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Size = new Size(92, 26);
            label.Margin = new Padding(0, 0, 8, 0);
            panel.Controls.Add(label);

            chkCustomTail = new CheckBox();
            chkCustomTail.Text = "自定义";
            chkCustomTail.AutoSize = true;
            chkCustomTail.Margin = new Padding(0, 5, 8, 0);
            chkCustomTail.CheckedChanged += delegate { UpdateCustomTailInputState(); };
            ToolTip customTailTip = new ToolTip();
            customTailTip.AutoPopDelay = 20000;
            customTailTip.InitialDelay = 400;
            customTailTip.ReshowDelay = 100;
            customTailTip.ShowAlways = true;
            customTailTip.SetToolTip(chkCustomTail,
                "自定义新文件名末尾（替换默认的 T 编号）：\r\n" +
                "· 在下方输入框填写文字或编号，回车应用到当前选中的一条素材。\r\n" +
                "· 用于补拍/替换镜头命名，如 补1、补手机特写、替换 等。\r\n" +
                "· 多选多条素材后点“批量应用”，会按基名自动递增：\r\n" +
                "  例如输入“补” → 补_1、补_2、补_3；输入“替换1” → 替换_1、替换_2……\r\n" +
                "· 文字与自动序号之间会自动加下划线，避免数字粘连（TT1 → TT1_2）。\r\n" +
                "· 留空并应用可恢复为默认 T 编号。");
            panel.Controls.Add(chkCustomTail);

            txtCustomTail = new TextBox();
            txtCustomTail.Width = 230;
            txtCustomTail.Margin = new Padding(0, 2, 8, 0);
            txtCustomTail.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    ApplySelectedCustomTail(true);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            panel.Controls.Add(txtCustomTail);

            Button btnBatchTail = NewButton("批量应用", 82);
            btnBatchTail.Margin = new Padding(0, 2, 0, 0);
            btnBatchTail.Click += delegate { ApplyBatchCustomTail(); };
            panel.Controls.Add(btnBatchTail);

            return panel;
        }

        private static Button NewButton(string text, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = Math.Max(44, (int)Math.Round(width * 0.8));
            button.Height = 30;
            button.Margin = new Padding(1, 2, 2, 2);
            button.AutoEllipsis = true;
            return button;
        }

        private static Label NewActionSeparator()
        {
            Label separator = new Label();
            separator.Text = "|";
            separator.Tag = "Muted";
            separator.AutoSize = false;
            separator.Width = 10;
            // 分割|的宽度设置
            separator.Height = 30;
            separator.TextAlign = ContentAlignment.MiddleCenter;
            separator.Margin = new Padding(6, 2, 6, 2);
            return separator;
        }

        private void UpdateRenameButtonText()
        {
            if (btnRename != null)
            {
                btnRename.Text = IsExport1080pEnabled() ? "导出1080p" : "执行重命名";
            }
        }

        private void ShowAboutInfo()
        {
            using (AboutForm dialog = new AboutForm(activeLicenseInfo, darkMode))
            {
                dialog.ShowDialog(this);
            }
        }

        // 操作期间整窗锁定（重命名/导出共用）。原先误放在 History 分部文件里。
        private void SetOperationUiEnabled(bool enabled)
        {
            operationRunning = !enabled;
            UseWaitCursor = !enabled;
            foreach (Control control in Controls)
            {
                if (object.ReferenceEquals(control, statusLabel))
                {
                    continue;
                }
                control.Enabled = enabled;
            }

            if (statusLabel != null)
            {
                statusLabel.Enabled = true;
            }
        }
    }
}
