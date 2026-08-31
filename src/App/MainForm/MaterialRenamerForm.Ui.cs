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
    public partial class MaterialRenamerForm : IStatusSink
    {
        // 所有工作区序号徽章（①②③④）共用同一条左侧基线，避免各工具条
        // 各自的 Padding 让编号在视觉上错位。
        private const int ZoneBadgeBaselineLeft = 14;

        // BuildUi 拆分为具名 Build* 方法（阶段8b，纯代码搬移）：语句顺序与原
        // 单体实现逐条一致——Dock 布局依赖控件添加次序与 BringToFront 调用。
        private void BuildUi()
        {
            ConfigureFormShell();
            BuildHeaderArea();
            BuildFooterBar();
            BuildOperationStrip();
            BuildWorkspaceSplit();
        }

        // 分区标题（阶段10g，与设计稿一致）：序号徽章 + 加粗标题。
        private static Label NewZoneTitle(string text)
        {
            Label title = new Label();
            title.Text = text;
            title.AutoSize = true;
            title.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            title.Margin = new Padding(0, 6, 12, 0);
            return title;
        }

        private void ConfigureFormShell()
        {
            Text = AppInfo.Name;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1240, 820);
            MinimumSize = new Size(820, 680);
            Font = new Font("Microsoft YaHei UI", 9f);
            // 阶段10f：字体基准自动缩放（7x17 = 雅黑 9pt @96dpi 实测值；
            // 96dpi 下缩放系数恰为 1，高 DPI 下按比例放大布局）。
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            AppIcon.Apply(this);
        }

        // 阶段10b：动作按钮迁往各自作用区的工具条后，头部只剩命名设置一行。
        private void BuildHeaderArea()
        {
            Panel headerHost = new Panel();
            headerHost.Dock = DockStyle.Top;
            headerHost.Height = 96;
            Controls.Add(headerHost);

            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Fill;
            topPanel.Padding = new Padding(14, 10, 14, 8);
            topPanel.BackColor = UiTheme.PanelBack(darkMode);
            headerHost.Controls.Add(topPanel);

            BuildSettingsRow(topPanel);
            BuildDirectorySettingsRow(topPanel);
        }

        private void BuildSettingsRow(Panel topPanel)
        {
            FlowLayoutPanel settingsPanel = new FlowLayoutPanel();
            settingsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            settingsPanel.FlowDirection = FlowDirection.LeftToRight;
            settingsPanel.WrapContents = false;
            settingsPanel.BackColor = UiTheme.PanelBack(darkMode);
            settingsPanel.Location = new Point(ZoneBadgeBaselineLeft, 10);
            settingsPanel.Size = new Size(1196, 34);
            topPanel.Controls.Add(settingsPanel);

            settingsPanel.Controls.Add(new ZoneBadge(1));
            settingsPanel.Controls.Add(NewZoneTitle("命名设置"));

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
            numEpisode.Width = 64;
            numEpisode.Margin = new Padding(0, 4, 14, 0);
            // 集数只影响文件名文本，不影响分组标题/项目数——走防抖的增量
            // 刷新（计数不一致时该路径自带整表重建回退）。
            numEpisode.ValueChanged += delegate { ScheduleNamesOnlyRefresh(); };
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
            numScene.Width = 64;
            numScene.Margin = new Padding(0, 4, 14, 0);
            // 场号/各选项只改名字与状态列文本，不改项目数——走防抖的增量
            // 刷新（该路径会同步分组标题，计数不一致时自带整表重建回退）。
            // 场号列常显（阶段10h）：逐行场号关闭时列显示全局值，需就地同步。
            numScene.ValueChanged += delegate { RefreshGridSceneCells(); ScheduleNamesOnlyRefresh(); };
            settingsPanel.Controls.Add(numScene);

            chkRowScene = new CheckBox();
            chkRowScene.Text = "逐行场号";
            chkRowScene.AutoSize = true;
            chkRowScene.Margin = new Padding(0, 7, 14, 0);
            chkRowScene.CheckedChanged += OnRowSceneCheckedChanged;
            settingsPanel.Controls.Add(chkRowScene);

            chkKeepExtension = new CheckBox();
            chkKeepExtension.Text = "保留扩展名大小写";
            chkKeepExtension.Checked = true;
            chkKeepExtension.AutoSize = true;
            chkKeepExtension.Margin = new Padding(0, 7, 14, 0);
            chkKeepExtension.CheckedChanged += delegate { ScheduleNamesOnlyRefresh(); };
            settingsPanel.Controls.Add(chkKeepExtension);

            btnTheme = NewButton("", 112);
            btnTheme.Click += delegate { ToggleTheme(); };
            btnTheme.Margin = new Padding(0, 2, 6, 2);
            settingsPanel.Controls.Add(btnTheme);

            btnAbout = NewButton("关于", 58);
            btnAbout.Click += delegate { ShowAboutInfo(); };
            btnAbout.Margin = new Padding(0, 2, 0, 2);
            settingsPanel.Controls.Add(btnAbout);
        }

        // 镜头表工具条（阶段10b）：行结构操作与素材操作紧挨其作用对象——表格。
        private Control BuildShotTableToolbar()
        {
            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.FlowDirection = FlowDirection.LeftToRight;
            toolbar.WrapContents = false;
            toolbar.Padding = new Padding(ZoneBadgeBaselineLeft, 0, 4, 0);
            toolbar.Margin = new Padding(0);

            toolbar.Controls.Add(new ZoneBadge(2));
            toolbar.Controls.Add(NewZoneTitle("镜头表"));

            Button btnAddRow = NewButton("新增行", 72);
            btnAddRow.Click += delegate { AddEmptyRow(); };
            toolbar.Controls.Add(btnAddRow);

            Button btnMoveRowUp = NewButton("上移", 58);
            btnMoveRowUp.Click += delegate { MoveCurrentRow(-1); };
            toolbar.Controls.Add(btnMoveRowUp);

            Button btnMoveRowDown = NewButton("下移", 58);
            btnMoveRowDown.Click += delegate { MoveCurrentRow(1); };
            toolbar.Controls.Add(btnMoveRowDown);

            Button btnDeleteRow = NewButton("删除行", 72);
            btnDeleteRow.Click += delegate { DeleteCurrentRow(); };
            toolbar.Controls.Add(btnDeleteRow);

            Button btnRemoveTail = NewButton("删除空尾行", 100);
            btnRemoveTail.Click += delegate { RemoveEmptyTailRows(); };
            toolbar.Controls.Add(btnRemoveTail);
            toolbar.Controls.Add(NewActionSeparator());

            Button btnImportCell = NewButton("导入素材", 82);
            btnImportCell.Click += delegate { ImportSelectedCell(); };
            toolbar.Controls.Add(btnImportCell);

            Button btnClearCell = NewButton("清空格", 72);
            btnClearCell.Click += delegate { ClearSelectedCellFiles(); };
            toolbar.Controls.Add(btnClearCell);

            Button btnClearAll = NewButton("全局清空", 88);
            btnClearAll.Click += delegate { ClearAllMaterials(); };
            toolbar.Controls.Add(btnClearAll);

            return toolbar;
        }

        // 底部执行栏（阶段10a，标注：布局变更）：状态文本、导出选项、
        // 取消命名与主按钮同驻一处——改变主按钮含义的开关紧挨按钮本身，
        // 执行反馈出现在用户点击的地方。执行期间由 10d 在此显示进度与取消。
        private void BuildFooterBar()
        {
            footerBar = new Panel();
            footerBar.Dock = DockStyle.Bottom;
            footerBar.Height = 46;
            footerBar.Padding = new Padding(12, 5, 12, 5);
            Controls.Add(footerBar);

            FlowLayoutPanel footerRight = new FlowLayoutPanel();
            footerRight.FlowDirection = FlowDirection.LeftToRight;
            footerRight.WrapContents = false;
            footerRight.AutoSize = true;
            footerRight.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            footerRight.Dock = DockStyle.Right;
            footerRight.Padding = new Padding(0);
            footerRight.Margin = new Padding(0);

            chkExport1080p = new CheckBox();
            chkExport1080p.Text = "导出1080×1920";
            chkExport1080p.AutoSize = true;
            chkExport1080p.Margin = new Padding(0, 8, 12, 0);
            chkExport1080p.CheckedChanged += OnExport1080pCheckedChanged;
            footerRight.Controls.Add(chkExport1080p);

            chkExportWatermark = new CheckBox();
            chkExportWatermark.Text = "文件名水印";
            chkExportWatermark.Checked = false;
            chkExportWatermark.AutoSize = true;
            chkExportWatermark.Margin = new Padding(0, 8, 12, 0);
            chkExportWatermark.CheckedChanged += OnExportWatermarkCheckedChanged;
            footerRight.Controls.Add(chkExportWatermark);
            UpdateWatermarkOptionState();

            btnUndo = NewButton("取消命名", 85);
            btnUndo.Click += delegate { RestoreLastRename(); };
            btnUndo.Margin = new Padding(0, 3, 6, 3);
            footerRight.Controls.Add(btnUndo);

            btnExportOnly = NewButton("仅高清", 100);
            btnExportOnly.Click += delegate { Export1080pOnly(); };
            btnExportOnly.Margin = new Padding(0, 3, 6, 3);
            footerRight.Controls.Add(btnExportOnly);

            btnRename = NewButton("执行重命名", 100);
            btnRename.Tag = "Primary";
            btnRename.Click += delegate { RenameFiles(); };
            btnRename.Margin = new Padding(0, 3, 0, 3);
            footerRight.Controls.Add(btnRename);

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.AutoEllipsis = true;
            statusLabel.Tag = "Muted";
            StatusText = "把视频拖到表格「主要素材」或「备用素材」单元格。";

            // 序号徽章 ④：状态即"执行"分区的反馈（后添加者先停靠 → 徽章在最左）。
            Panel badgeHost = new Panel();
            badgeHost.Dock = DockStyle.Left;
            ZoneBadge footerBadge = new ZoneBadge(4);
            int footerBadgeLeft = Math.Max(0, ZoneBadgeBaselineLeft - footerBar.Padding.Left);
            badgeHost.Width = footerBadgeLeft + footerBadge.Width;
            footerBadge.Location = new Point(footerBadgeLeft, 8);
            badgeHost.Controls.Add(footerBadge);

            footerBar.Controls.Add(statusLabel);
            footerBar.Controls.Add(footerRight);
            footerBar.Controls.Add(badgeHost);
        }

        // 执行中条（阶段10g，与设计稿一致）：操作期间在窗口最底部显示
        // 醒目的"执行中：[进度条] N% [取消]"整条，替代原先挤在执行栏里的
        // 小进度条。空闲时隐藏。
        private void BuildOperationStrip()
        {
            operationStrip = new Panel();
            operationStrip.Dock = DockStyle.Bottom;
            operationStrip.Height = 40;
            operationStrip.Padding = new Padding(12, 7, 12, 7);
            operationStrip.Visible = false;
            Controls.Add(operationStrip);

            operationProgress = new SlimProgressBar();
            operationProgress.Dock = DockStyle.Fill;

            operationPercentLabel = new Label();
            operationPercentLabel.Dock = DockStyle.Right;
            operationPercentLabel.Width = 220;
            operationPercentLabel.TextAlign = ContentAlignment.MiddleRight;
            operationPercentLabel.AutoEllipsis = true;
            operationPercentLabel.Padding = new Padding(8, 0, 0, 0);
            operationPercentLabel.Text = "0%";

            btnCancelOperation = NewButton("取消", 62);
            btnCancelOperation.Dock = DockStyle.Right;
            btnCancelOperation.Click += delegate { CancelRunningOperation(); };

            Label runningLabel = new Label();
            runningLabel.Dock = DockStyle.Left;
            runningLabel.Width = 66;
            runningLabel.TextAlign = ContentAlignment.MiddleLeft;
            runningLabel.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            runningLabel.Text = "执行中：";

            // 停靠按逆序处理：后添加者先停靠（取消=最右外侧，执行中=最左）。
            operationStrip.Controls.Add(operationProgress);
            operationStrip.Controls.Add(operationPercentLabel);
            operationStrip.Controls.Add(btnCancelOperation);
            operationStrip.Controls.Add(runningLabel);
        }

        private void BuildWorkspaceSplit()
        {
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            Controls.Add(split);
            split.BringToFront();

            BuildShotGrid(split.Panel1);
            BuildPreviewArea(split.Panel2);
        }

        private void BuildShotGrid(SplitterPanel host)
        {
            TableLayoutPanel gridShell = new TableLayoutPanel();
            gridShell.Dock = DockStyle.Fill;
            gridShell.ColumnCount = 1;
            gridShell.RowCount = 2;
            gridShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            gridShell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            host.Controls.Add(gridShell);
            gridShell.Controls.Add(BuildShotTableToolbar(), 0, 0);

            grid = new DoubleBufferedGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowDrop = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.BackgroundColor = UiTheme.WindowBack(darkMode);
            grid.BorderStyle = BorderStyle.None;
            grid.ColumnHeadersHeight = 34;
            // 阶段10h（与设计稿一致）：无行头列，行定位由场号/镜号列承担。
            grid.RowHeadersVisible = false;
            grid.RowTemplate.Height = 38;
            grid.RowTemplate.Resizable = DataGridViewTriState.False;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.MultiSelect = false;
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.DefaultCellStyle.Padding = new Padding(4);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            grid.CellEndEdit += OnGridCellEndEdit;
            grid.CellFormatting += OnGridCellFormatting;
            grid.SelectionChanged += delegate { UpdateSelectedCellDetails(); };
            grid.DragEnter += OnGridDragEnterOrOver;
            grid.DragOver += OnGridDragEnterOrOver;
            grid.DragLeave += delegate { ClearDragHighlight(); };
            grid.DragDrop += OnGridDragDrop;

            // 列头无字母前缀；场号列常显（逐任务进度列已移除）。
            DataGridViewTextBoxColumn colScene = new DataGridViewTextBoxColumn();
            colScene.HeaderText = "场号";
            colScene.Width = 72;
            colScene.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn colSeq = new DataGridViewTextBoxColumn();
            colSeq.HeaderText = "镜号";
            colSeq.Width = 82;
            colSeq.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn colMain = new DataGridViewTextBoxColumn();
            colMain.HeaderText = "主要素材";
            colMain.Width = 310;
            colMain.ReadOnly = true;
            colMain.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn colBackup = new DataGridViewTextBoxColumn();
            colBackup.HeaderText = "备用素材";
            // 素材列填充剩余宽度（原「进度」列移除后，备用素材列接管弹性宽度）。
            colBackup.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colBackup.MinimumWidth = 310;
            colBackup.ReadOnly = true;
            colBackup.SortMode = DataGridViewColumnSortMode.NotSortable;

            grid.Columns.Add(colScene);
            grid.Columns.Add(colSeq);
            grid.Columns.Add(colMain);
            grid.Columns.Add(colBackup);
            ApplyGridColumnLayout();
            gridShell.Controls.Add(grid, 0, 1);
        }

        private void BuildPreviewArea(SplitterPanel host)
        {
            // 阶段10g：分区标题并入工具条行（设计稿为单行头），3 行壳 → 2 行。
            TableLayoutPanel previewShell = new TableLayoutPanel();
            previewShell.Dock = DockStyle.Fill;
            previewShell.ColumnCount = 1;
            previewShell.RowCount = 2;
            previewShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            previewShell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            host.Controls.Add(previewShell);

            previewShell.Controls.Add(BuildPreviewToolbar(), 0, 0);

            Panel previewBody = new Panel();
            previewBody.Dock = DockStyle.Fill;
            previewShell.Controls.Add(previewBody, 0, 1);

            detailHost = new Panel();
            detailHost.Dock = DockStyle.Right;
            detailHost.Width = 320;

            detailExpandButton = new Button();
            detailExpandButton.Text = "«";
            detailExpandButton.Dock = DockStyle.Fill;
            detailExpandButton.Visible = false;
            detailExpandButton.Margin = new Padding(0);
            detailExpandButton.Click += delegate { ToggleDetailPanel(); };

            previewList = new DoubleBufferedListView();
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
            previewList.KeyDown += OnPreviewListKeyDown;
            // 阶段10h：Fill 停靠的列表保持最前（最后布局）——右侧信息面板
            // 真正占据自己的 320px，而不是悬浮在列表右缘之上（旧写法在
            // 屏幕上靠 Z 序遮挡"看起来正确"，但列表最右列与滚动条被压在
            // 面板底下，DrawToBitmap 截图也会反序漏画面板）。
            previewBody.Controls.Add(previewList);
            previewBody.Controls.Add(detailHost);
            detailPanelBody = BuildMaterialDetailsPanel();
            detailHost.Controls.Add(detailPanelBody);
            detailHost.Controls.Add(detailExpandButton);
        }

        private void OnRowSceneCheckedChanged(object sender, EventArgs e)
        {
            if (chkRowScene.Checked)
            {
                InitializeRowScenesFromDefaultIfNeeded();
            }

            RenderGrid();
            RefreshPreview();
            UpdateSelectedCellDetails();
        }

        private void OnExport1080pCheckedChanged(object sender, EventArgs e)
        {
            ScheduleNamesOnlyRefresh();
            UpdateRenameButtonText();
            UpdateWatermarkOptionState();
        }

        private void OnExportWatermarkCheckedChanged(object sender, EventArgs e)
        {
            UpdateWatermarkOptionState();
            ScheduleNamesOnlyRefresh();
        }

        // 阶段11a：微调框/选项连打防抖——事件层把 120ms 内的连续变更合并
        // 为一次增量刷新（每次刷新都是全表 BuildPlan + 逐文件 File.Exists）。
        // 只防抖事件接线；直接调用 RefreshPreviewNamesOnly 的路径不变，
        // 测试仍可同步驱动。
        private void ScheduleNamesOnlyRefresh()
        {
            if (namesOnlyRefreshTimer == null)
            {
                namesOnlyRefreshTimer = new System.Windows.Forms.Timer();
                namesOnlyRefreshTimer.Interval = 120;
                namesOnlyRefreshTimer.Tick += delegate
                {
                    namesOnlyRefreshTimer.Stop();
                    RefreshPreviewNamesOnly();
                };
            }

            namesOnlyRefreshTimer.Stop();
            namesOnlyRefreshTimer.Start();
        }

        private void OnPreviewListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelectedPreviewRecord();
                e.Handled = true;
            }
        }

        private Control BuildMaterialDetailsPanel()
        {
            TableLayoutPanel panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 1;
            panel.RowCount = 4;
            panel.Padding = new Padding(10, 8, 10, 10);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Panel titleRow = new Panel();
            titleRow.Dock = DockStyle.Fill;
            titleRow.Margin = new Padding(0);

            Label title = new Label();
            title.Text = "素材信息";
            title.Dock = DockStyle.Fill;
            title.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            title.Padding = new Padding(0, 4, 0, 0);

            Button collapseButton = new Button();
            collapseButton.Text = "»";
            collapseButton.Width = 26;
            collapseButton.Dock = DockStyle.Right;
            collapseButton.Click += delegate { ToggleDetailPanel(); };

            titleRow.Controls.Add(title);
            titleRow.Controls.Add(collapseButton);
            panel.Controls.Add(titleRow, 0, 0);

            detailTitleLabel = new Label();
            detailTitleLabel.Dock = DockStyle.Fill;
            detailTitleLabel.AutoEllipsis = true;
            detailTitleLabel.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            detailTitleLabel.Padding = new Padding(0, 8, 0, 0);
            panel.Controls.Add(detailTitleLabel, 0, 1);

            detailInfoLabel = new Label();
            detailInfoLabel.Dock = DockStyle.Fill;
            detailInfoLabel.AutoEllipsis = false;
            detailInfoLabel.Padding = new Padding(0, 4, 0, 0);
            panel.Controls.Add(detailInfoLabel, 0, 2);

            detailPathLabel = new Label();
            detailPathLabel.Dock = DockStyle.Fill;
            detailPathLabel.Tag = "Muted";
            detailPathLabel.AutoEllipsis = true;
            detailPathLabel.Padding = new Padding(0, 4, 0, 0);
            panel.Controls.Add(detailPathLabel, 0, 3);

            ShowNoMaterialDetails();
            return panel;
        }

        // 预览工具条（阶段10b/10g）：分区徽章+标题、删除记录（作用于预览
        // 选中项）、自定义末尾工具同驻一行，紧挨预览列表。
        private Control BuildPreviewToolbar()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.FlowDirection = FlowDirection.LeftToRight;
            panel.WrapContents = false;
            panel.Padding = new Padding(ZoneBadgeBaselineLeft, 6, 6, 4);
            panel.Margin = new Padding(0);

            panel.Controls.Add(new ZoneBadge(3));
            panel.Controls.Add(NewZoneTitle("重命名预览"));

            Button btnDeleteRecord = NewButton("删除记录", 82);
            btnDeleteRecord.Click += delegate { DeleteSelectedPreviewRecord(); };
            btnDeleteRecord.Margin = new Padding(0, 0, 4, 0);
            panel.Controls.Add(btnDeleteRecord);
            panel.Controls.Add(NewActionSeparator());

            // 阶段10g（标注：文案合并）：原"新文件名末尾"说明标签 + "自定义"
            // 勾选框合并为单个"自定义末尾"勾选框（与设计稿一致，悬浮说明保留）。
            chkCustomTail = new CheckBox();
            chkCustomTail.Text = "自定义末尾";
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
            txtCustomTail.Width = 120;
            txtCustomTail.Margin = new Padding(0, 2, 8, 0);
            txtCustomTail.KeyDown += OnCustomTailKeyDown;
            panel.Controls.Add(txtCustomTail);

            Button btnBatchTail = NewButton("批量应用", 82);
            btnBatchTail.Margin = new Padding(0, 2, 0, 0);
            btnBatchTail.Click += delegate { ApplyBatchCustomTail(); };
            panel.Controls.Add(btnBatchTail);

            return panel;
        }

        private void OnCustomTailKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplySelectedCustomTail(true);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
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

        // 1px 发丝分隔线（阶段9b：替换原"|"文字标签；颜色由主题 Hairline 角色供给）。
        private static Control NewActionSeparator()
        {
            Panel separator = new Panel();
            separator.Tag = "Hairline";
            separator.Width = 1;
            separator.Height = 22;
            separator.Margin = new Padding(7, 6, 7, 2);
            return separator;
        }

        private void UpdateRenameButtonText()
        {
            if (btnRename != null)
            {
                btnRename.Text = IsExport1080pEnabled() ? "高清命名" : "执行重命名";
            }
        }

        private void ShowAboutInfo()
        {
            using (AboutForm dialog = new AboutForm(activeLicenseInfo, darkMode))
            {
                dialog.ShowDialog(this);
            }
        }

        // IStatusSink 实现：状态栏文本唯一写入点（原先散布在 8 个分部文件里的
        // statusLabel.Text 直写全部经此收拢）。
        public void SetStatus(string text)
        {
            if (statusLabel != null)
            {
                statusLabel.Text = text;
            }
        }

        // 写入专用属性：让原有赋值语句形态（含多行三元表达式）保持不变。
        private string StatusText
        {
            set { SetStatus(value); }
        }

        // 操作期间整窗锁定（重命名/导出共用）。执行栏整体保持可用——状态
        // 文本持续更新，取消按钮（10d 接线）必须在锁定期间可点；栏内的
        // 选项与主按钮逐项禁用。
        private void SetOperationUiEnabled(bool enabled)
        {
            operationRunning = !enabled;
            UseWaitCursor = !enabled;
            foreach (Control control in Controls)
            {
                if (object.ReferenceEquals(control, footerBar) || object.ReferenceEquals(control, operationStrip))
                {
                    continue;
                }
                control.Enabled = enabled;
            }

            if (chkExport1080p != null)
            {
                chkExport1080p.Enabled = enabled;
            }
            if (chkExportWatermark != null)
            {
                chkExportWatermark.Enabled = enabled;
            }
            if (btnUndo != null)
            {
                btnUndo.Enabled = enabled;
            }
            if (btnExportOnly != null)
            {
                btnExportOnly.Enabled = enabled;
            }
            if (btnRename != null)
            {
                btnRename.Enabled = enabled;
            }
            if (statusLabel != null)
            {
                statusLabel.Enabled = true;
            }
        }

        // 执行中的取消（阶段10d）：导出批次经 FfmpegCancellation 立即杀掉
        // 活动 ffmpeg 进程；重命名批次在文件间停下。两条路径都保留已完成
        // 的文件（重命名的已完成部分照常写入撤销历史）。
        private void CancelRunningOperation()
        {
            renameCancelRequested = true;
            exportController.CancelActive();
            StatusText = "正在取消当前任务...";
            if (btnCancelOperation != null)
            {
                btnCancelOperation.Enabled = false;
            }
        }

        // 执行中条显隐与数值（导出/重命名共用）。文案与设计稿一致：
        // "N% · 当前文件名"（导出=目标名，重命名=原名）。
        private void SetOperationProgressVisible(bool visible)
        {
            operationPercentValue = 0;
            operationCurrentFile = "";
            if (operationProgress != null)
            {
                operationProgress.Value = 0;
            }
            UpdateOperationPercentText();
            if (btnCancelOperation != null)
            {
                btnCancelOperation.Enabled = visible;
            }
            if (operationStrip != null)
            {
                operationStrip.Visible = visible;
            }
        }

        private void SetOperationProgressValue(int percent)
        {
            operationPercentValue = Math.Max(0, Math.Min(100, percent));
            if (operationProgress != null)
            {
                operationProgress.Value = operationPercentValue;
            }
            UpdateOperationPercentText();
        }

        private void SetOperationProgressFile(string fileName)
        {
            string name = fileName == null ? "" : fileName;
            if (name != operationCurrentFile)
            {
                operationCurrentFile = name;
                UpdateOperationPercentText();
            }
        }

        private void UpdateOperationPercentText()
        {
            if (operationPercentLabel != null)
            {
                operationPercentLabel.Text = operationCurrentFile.Length == 0
                    ? operationPercentValue + "%"
                    : operationPercentValue + "% · " + operationCurrentFile;
            }
        }

        // 详情面板折叠/展开（阶段10c）：折叠成 28px 细条，仅留展开按钮。
        // 状态用显式字段跟踪——Control.Visible 在窗体未显示时恒为 false，
        // 不能当状态用（冒烟门禁抓出的坑）。
        private void ToggleDetailPanel()
        {
            if (detailHost == null || detailPanelBody == null || detailExpandButton == null)
            {
                return;
            }

            detailPanelCollapsed = !detailPanelCollapsed;
            if (detailPanelCollapsed)
            {
                detailPanelBody.Visible = false;
                detailExpandButton.Visible = true;
                detailHost.Width = 28;
            }
            else
            {
                detailHost.Width = 320;
                detailPanelBody.Visible = true;
                detailExpandButton.Visible = false;
            }
        }
    }
}
