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
    public partial class MaterialRenamerForm : Form
    {
        private const int ThumbnailCacheLimit = 200;
        private const int PlanStatusCheckBatchSize = 50;
        private const int GridSceneColumn = 0;
        private const int GridShotColumn = 1;
        private const int GridMainColumn = 2;
        private const int GridBackupColumn = 3;
        private const int GridProgressColumn = 4;

        private static readonly HashSet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".wmv", ".flv",
            ".webm", ".mts", ".m2ts", ".3gp", ".mpeg", ".mpg"
        };

        private readonly List<ShotRow> rows = new List<ShotRow>();
        private readonly List<RenamePlan> currentPlan = new List<RenamePlan>();
        private readonly Stack<List<RenameOperation>> undoStack = new Stack<List<RenameOperation>>();
        private readonly Dictionary<string, VideoFileInfo> videoInfoCache = new Dictionary<string, VideoFileInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Image> thumbnailCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> thumbnailCacheOrder = new LinkedList<string>();
        private readonly Dictionary<string, LinkedListNode<string>> thumbnailCacheNodes = new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pendingVideoInfoLoads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pendingThumbnailLoads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly LicenseInfo activeLicenseInfo;
        private readonly string historyPath = Path.Combine(Environment.CurrentDirectory, "rename_history.tsv");

        private DataGridView grid;
        private ListView previewList;
        private PictureBox thumbnailBox;
        private Label detailTitleLabel;
        private Label detailInfoLabel;
        private Label detailPathLabel;
        private NumericUpDown numEpisode;
        private NumericUpDown numScene;
        private CheckBox chkKeepExtension;
        private CheckBox chkRowScene;
        private CheckBox chkExport1080p;
        private CheckBox chkExportWatermark;
        private CheckBox chkCustomTail;
        private TextBox txtCustomTail;
        private Label statusLabel;
        private Button btnRename;
        private Button btnTheme;
        private Button btnAbout;
        private System.Windows.Forms.Timer previewColumnResizeTimer;
        private Image ownedDetailImage;
        private int dragHighlightRow = -1;
        private int dragHighlightColumn = -1;
        private int detailLoadVersion;
        private int planCheckVersion;
        private string currentDetailPath = "";
        private string currentDetailNewName = "";
        private string currentDetailContext = "";
        private bool darkMode;
        private bool operationRunning;
        private bool rendering;
        private bool progressColumnVisible;
        private bool rowSceneModeInitialized;

        public MaterialRenamerForm()
            : this(null)
        {
        }

        public MaterialRenamerForm(LicenseInfo licenseInfo)
        {
            activeLicenseInfo = licenseInfo;
            darkMode = UiTheme.DetectWindowsDarkMode();
            for (int i = 1; i <= AppInfo.DefaultRowCount; i++)
            {
                rows.Add(new ShotRow { Scene = 1, Sequence = i });
            }

            BuildUi();
            ApplyTheme();
            RenderAll();
        }

        public static string RunSelfTest()
        {
            ShotRow row = new ShotRow { Sequence = 5 };
            row.MainFiles.Add(@"C:\Temp\main1.mp4");
            row.MainFiles.Add(@"C:\Temp\main2.mp4");
            row.MainFiles.Add(@"C:\Temp\main3.mp4");
            row.BackupFiles.Add(@"C:\Temp\backup1.mp4");
            row.BackupFiles.Add(@"C:\Temp\backup2.mp4");
            row.BackupFiles.Add(@"C:\Temp\backup3.mp4");

            List<RenamePlan> plan = BuildPlan(new List<ShotRow> { row }, 5, 1, true, false);
            string[] expected = new string[]
            {
                "E5-S1-5-T1.mp4",
                "E5-S1-5-T2.mp4",
                "E5-S1-5-T3.mp4",
                "E5-S1-5-T4.mp4",
                "E5-S1-5-T5.mp4",
                "E5-S1-5-T6.mp4"
            };

            string actual = string.Join("|", plan.Select(p => p.NewName).ToArray());
            string want = string.Join("|", expected);
            if (actual != want)
            {
                throw new Exception("同一行 B/C 连续编号测试失败：" + actual);
            }

            string watermarkedArgs = BuildFfmpegArguments(@"C:\Temp\input.mp4", @"C:\Temp\output.mp4", true, "E5-S1-1-T1.mp4");
            if (!watermarkedArgs.Contains("-vf") || !watermarkedArgs.Contains("drawtext=") || watermarkedArgs.Contains("-filter_complex") || watermarkedArgs.Contains("-loop"))
            {
                throw new Exception("文件名水印导出参数测试失败：" + watermarkedArgs);
            }

            string noWatermarkArgs = BuildFfmpegArguments(@"C:\Temp\input.mp4", @"C:\Temp\output.mp4", true, "");
            if (noWatermarkArgs.Contains("drawtext=") || noWatermarkArgs.Contains("未命名视频"))
            {
                throw new Exception("关闭文件名水印测试失败：" + noWatermarkArgs);
            }

            UpdateManager.RunSelfTest();

            ShotRow customRow = new ShotRow { Sequence = 17 };
            customRow.MainFiles.Add(@"C:\Temp\custom.mp4");
            List<RenamePlan> customPlan = BuildPlan(new List<ShotRow> { customRow }, 5, 1, true, false);
            if (customPlan.Count != 1 || customPlan[0].NewName != "E5-S1-17-T1.mp4")
            {
                throw new Exception("自定义镜号测试失败：" + (customPlan.Count == 0 ? "无预览" : customPlan[0].NewName));
            }

            ShotRow customSceneRow = new ShotRow { Scene = 3, Sequence = 7 };
            customSceneRow.MainFiles.Add(@"C:\Temp\custom_scene.mp4");
            List<RenamePlan> customScenePlan = BuildPlan(new List<ShotRow> { customSceneRow }, 5, 1, true, false, true);
            if (customScenePlan.Count != 1 || customScenePlan[0].NewName != "E5-S3-7-T1.mp4" || customScenePlan[0].Scene != 3)
            {
                throw new Exception("自定义场号测试失败：" + (customScenePlan.Count == 0 ? "无预览" : customScenePlan[0].NewName));
            }

            List<RenamePlan> defaultScenePlan = BuildPlan(new List<ShotRow> { customSceneRow }, 5, 1, true, false, false);
            if (defaultScenePlan.Count != 1 || defaultScenePlan[0].NewName != "E5-S1-7-T1.mp4")
            {
                throw new Exception("默认场号测试失败：" + (defaultScenePlan.Count == 0 ? "无预览" : defaultScenePlan[0].NewName));
            }

            ShotRow customTailRow = new ShotRow { Sequence = 1 };
            customTailRow.MainFiles.Add(@"C:\Temp\custom_tail.mp4");
            customTailRow.MainTailOverrides.Add("补+文字");
            List<RenamePlan> customTailPlan = BuildPlan(new List<ShotRow> { customTailRow }, 5, 1, true, false);
            if (customTailPlan.Count != 1 || customTailPlan[0].NewName != "E5-S1-1-补+文字.mp4" || customTailPlan[0].TailSegment != "补+文字")
            {
                throw new Exception("自定义末尾编号测试失败：" + (customTailPlan.Count == 0 ? "无预览" : customTailPlan[0].NewName));
            }

            ShotRow duplicateTailRow = new ShotRow { Sequence = 5 };
            duplicateTailRow.MainFiles.Add(@"C:\Temp\dup1.mp4");
            duplicateTailRow.MainFiles.Add(@"C:\Temp\dup2.mp4");
            duplicateTailRow.MainTailOverrides.Add("补手机");
            duplicateTailRow.MainTailOverrides.Add("");
            List<RenamePlan> duplicateTailPlan = BuildPlan(new List<ShotRow> { duplicateTailRow }, 5, 6, true, false);
            string uniqueTail = GetUniqueCustomTail(duplicateTailPlan[1], "补手机", duplicateTailPlan, 5, 6, true);
            if (uniqueTail != "补手机2")
            {
                throw new Exception("自定义末尾自动补号测试失败：" + uniqueTail);
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "VideoMaterialRenamerSelfTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string alreadyNamed = Path.Combine(tempDir, "E5-S1-17-T1.mp4");
                File.WriteAllText(alreadyNamed, "test");
                ShotRow exportRow = new ShotRow { Sequence = 17 };
                exportRow.MainFiles.Add(alreadyNamed);
                List<RenamePlan> exportPlan = BuildPlan(new List<ShotRow> { exportRow }, 5, 1, true, true);
                if (exportPlan.Count != 1 || exportPlan[0].Status != "待覆盖导出1080p")
                {
                    throw new Exception("覆盖导出预览测试失败：" + (exportPlan.Count == 0 ? "无预览" : exportPlan[0].Status));
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                }
            }

            return "SelfTest OK";
        }

        public static string RunSmokeTest()
        {
            using (MaterialRenamerForm form = new MaterialRenamerForm())
            {
                form.RenderAll();
                if (form.btnAbout == null || form.btnAbout.Text != "关于")
                {
                    throw new Exception("关于按钮初始化失败。");
                }
                if (form.chkExportWatermark == null || form.chkExportWatermark.Checked)
                {
                    throw new Exception("文件名水印默认状态测试失败。");
                }
                if (form.grid == null ||
                    form.grid.Columns[GridSceneColumn].DefaultCellStyle.ForeColor != form.GetSceneColumnTextColor() ||
                    form.grid.Columns[GridShotColumn].DefaultCellStyle.ForeColor != form.GetShotColumnTextColor())
                {
                    throw new Exception("场号/镜号列颜色测试失败。");
                }
                form.darkMode = true;
                form.ApplyTheme();
                if (form.chkExportWatermark.ForeColor != UiTheme.TextColor(true) ||
                    form.chkCustomTail.ForeColor != UiTheme.TextColor(true))
                {
                    throw new Exception("护眼模式默认复选框颜色测试失败。");
                }
                form.chkExport1080p.Checked = true;
                form.UpdateWatermarkOptionState();
                if (form.chkExportWatermark.ForeColor != UiTheme.TextColor(true))
                {
                    throw new Exception("护眼模式水印复选框颜色测试失败。");
                }
            }

            return "SmokeTest OK";
        }

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
            previewList.MultiSelect = false;
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

        private void ToggleTheme()
        {
            darkMode = !darkMode;
            ApplyTheme();
            RefreshPreview();
        }

        private bool IsExport1080pEnabled()
        {
            return chkExport1080p != null && chkExport1080p.Checked;
        }

        private bool IsExportWatermarkEnabled()
        {
            return IsExport1080pEnabled() && chkExportWatermark != null && chkExportWatermark.Checked;
        }

        private void UpdateWatermarkOptionState()
        {
            if (chkExportWatermark != null)
            {
                if (!IsExport1080pEnabled() && chkExportWatermark.Checked)
                {
                    chkExportWatermark.Checked = false;
                }
                chkExportWatermark.Enabled = true;
                UiTheme.ApplyControl(chkExportWatermark, darkMode);
            }
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

        private void ApplyTheme()
        {
            UpdateRenameButtonText();
            UpdateWatermarkOptionState();
            if (btnTheme != null)
            {
                btnTheme.Text = "护眼模式";
            }

            UiTheme.ApplyForm(this, darkMode);
            ApplyGridNumberColumnStyles();
            if (thumbnailBox != null)
            {
                thumbnailBox.BackColor = UiTheme.ControlBack(darkMode);
            }
            if (detailTitleLabel != null && (string.IsNullOrWhiteSpace(detailTitleLabel.Text) || detailTitleLabel.Text == "未选择素材"))
            {
                ShowNoVideoDetails();
            }
            ReapplyDragHighlight();
        }

        private Color GetSceneColumnTextColor()
        {
            return darkMode ? Color.FromArgb(255, 128, 128) : Color.FromArgb(190, 35, 35);
        }

        private Color GetShotColumnTextColor()
        {
            return darkMode ? UiTheme.TextColor(darkMode) : Color.Black;
        }

        private void ApplyGridNumberColumnStyles()
        {
            if (grid == null || grid.Columns.Count <= GridShotColumn)
            {
                return;
            }

            Color sceneColor = GetSceneColumnTextColor();
            Color shotColor = GetShotColumnTextColor();
            grid.Columns[GridSceneColumn].DefaultCellStyle.ForeColor = sceneColor;
            grid.Columns[GridSceneColumn].DefaultCellStyle.SelectionForeColor = Color.White;
            grid.Columns[GridSceneColumn].HeaderCell.Style.ForeColor = sceneColor;
            grid.Columns[GridShotColumn].DefaultCellStyle.ForeColor = shotColor;
            grid.Columns[GridShotColumn].DefaultCellStyle.SelectionForeColor = Color.White;
            grid.Columns[GridShotColumn].HeaderCell.Style.ForeColor = shotColor;

            foreach (DataGridViewRow row in grid.Rows)
            {
                ApplyGridNumberCellStyles(row);
            }
        }

        private void ApplyGridNumberCellStyles(DataGridViewRow row)
        {
            if (row == null || row.Cells.Count <= GridShotColumn)
            {
                return;
            }

            row.Cells[GridSceneColumn].Style.ForeColor = GetSceneColumnTextColor();
            row.Cells[GridSceneColumn].Style.SelectionForeColor = Color.White;
            row.Cells[GridShotColumn].Style.ForeColor = GetShotColumnTextColor();
            row.Cells[GridShotColumn].Style.SelectionForeColor = Color.White;
        }

        private void ClearDragHighlight()
        {
            if (grid == null || dragHighlightRow < 0 || dragHighlightColumn < 0)
            {
                dragHighlightRow = -1;
                dragHighlightColumn = -1;
                return;
            }

            int row = dragHighlightRow;
            int column = dragHighlightColumn;
            dragHighlightRow = -1;
            dragHighlightColumn = -1;
            ResetGridCellStyle(row, column);
        }

        private void SetDragHighlight(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= rows.Count || (columnIndex != GridMainColumn && columnIndex != GridBackupColumn))
            {
                ClearDragHighlight();
                return;
            }

            if (dragHighlightRow == rowIndex && dragHighlightColumn == columnIndex)
            {
                return;
            }

            ClearDragHighlight();
            dragHighlightRow = rowIndex;
            dragHighlightColumn = columnIndex;
            ReapplyDragHighlight();
        }

        private void ReapplyDragHighlight()
        {
            if (grid == null || dragHighlightRow < 0 || dragHighlightColumn < 0 ||
                dragHighlightRow >= grid.Rows.Count || dragHighlightColumn >= grid.Columns.Count)
            {
                return;
            }

            DataGridViewCell cell = grid.Rows[dragHighlightRow].Cells[dragHighlightColumn];
            cell.Style.BackColor = UiTheme.DropTargetBack(darkMode);
            cell.Style.ForeColor = UiTheme.DropTargetFore(darkMode);
            cell.Style.SelectionBackColor = UiTheme.DropTargetBack(darkMode);
            cell.Style.SelectionForeColor = UiTheme.DropTargetFore(darkMode);
        }

        private void ResetGridCellStyle(int rowIndex, int columnIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.Rows.Count || columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                return;
            }

            DataGridViewCell cell = grid.Rows[rowIndex].Cells[columnIndex];
            cell.Style.BackColor = Color.Empty;
            cell.Style.ForeColor = Color.Empty;
            cell.Style.SelectionBackColor = Color.Empty;
            cell.Style.SelectionForeColor = Color.Empty;
        }

        private bool QueueOnUi(MethodInvoker action)
        {
            if (action == null || IsDisposed || !IsHandleCreated)
            {
                return false;
            }

            try
            {
                BeginInvoke(action);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void StartStaBackground(ThreadStart action)
        {
            if (action == null)
            {
                return;
            }

            Thread thread = new Thread(action);
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private VideoFileInfo GetFastVideoInfo(string path)
        {
            VideoFileInfo info = new VideoFileInfo();
            info.Path = path ?? "";
            info.FileName = string.IsNullOrWhiteSpace(path) ? "" : Path.GetFileName(path);
            info.SizeText = "";
            info.ResolutionText = "读取中";
            info.ModifiedText = "";
            info.Exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);

            if (!info.Exists)
            {
                info.ResolutionText = "未知";
                return info;
            }

            try
            {
                FileInfo file = new FileInfo(path);
                info.SizeText = VideoMetadataReader.FormatBytes(file.Length);
                info.ModifiedText = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
            }

            return info;
        }

        private void QueueVideoInfoLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || videoInfoCache.ContainsKey(path) || pendingVideoInfoLoads.Contains(path))
            {
                return;
            }

            pendingVideoInfoLoads.Add(path);
            StartStaBackground(delegate
            {
                VideoFileInfo info = VideoMetadataReader.Read(path);
                QueueOnUi(delegate
                {
                    pendingVideoInfoLoads.Remove(path);
                    videoInfoCache[path] = info;
                    UpdatePreviewListInfoSummary(path, info);
                    if (IsCurrentDetailPath(path))
                    {
                        RenderVideoInfo(info, currentDetailNewName, currentDetailContext);
                    }
                });
            });
        }

        private bool TryGetThumbnailFromCache(string path, out Image image)
        {
            image = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (!thumbnailCache.TryGetValue(path, out image))
            {
                return false;
            }

            TouchThumbnailCacheNode(path);
            return image != null;
        }

        private void TouchThumbnailCacheNode(string path)
        {
            LinkedListNode<string> node;
            if (!thumbnailCacheNodes.TryGetValue(path, out node))
            {
                node = thumbnailCacheOrder.AddLast(path);
                thumbnailCacheNodes[path] = node;
                return;
            }

            thumbnailCacheOrder.Remove(node);
            thumbnailCacheOrder.AddLast(node);
        }

        private void AddThumbnailToCache(string path, Image image)
        {
            if (string.IsNullOrWhiteSpace(path) || image == null)
            {
                if (image != null)
                {
                    image.Dispose();
                }
                return;
            }

            Image existing;
            if (thumbnailCache.TryGetValue(path, out existing) && !object.ReferenceEquals(existing, image))
            {
                existing.Dispose();
            }

            thumbnailCache[path] = image;
            TouchThumbnailCacheNode(path);
            TrimThumbnailCache();
        }

        private void TrimThumbnailCache()
        {
            while (thumbnailCache.Count > ThumbnailCacheLimit && thumbnailCacheOrder.First != null)
            {
                string path = thumbnailCacheOrder.First.Value;
                thumbnailCacheOrder.RemoveFirst();
                thumbnailCacheNodes.Remove(path);

                Image image;
                if (thumbnailCache.TryGetValue(path, out image))
                {
                    thumbnailCache.Remove(path);
                    if (image != null)
                    {
                        image.Dispose();
                    }
                }
            }
        }

        private void QueueThumbnailLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || pendingThumbnailLoads.Contains(path))
            {
                return;
            }

            Image cached;
            if (TryGetThumbnailFromCache(path, out cached))
            {
                return;
            }

            pendingThumbnailLoads.Add(path);
            StartStaBackground(delegate
            {
                Image image = VideoThumbnailProvider.GetThumbnail(path, new Size(300, 166));
                bool queued = QueueOnUi(delegate
                {
                    pendingThumbnailLoads.Remove(path);
                    if (image != null)
                    {
                        AddThumbnailToCache(path, image);
                    }

                    if (IsCurrentDetailPath(path))
                    {
                        if (image != null)
                        {
                            SetDetailImage(image, false);
                        }
                        else
                        {
                            SetDetailImage(CreatePlaceholderImage("无缩略图"), true);
                        }
                    }
                });

                if (!queued && image != null)
                {
                    image.Dispose();
                }
            });
        }

        private string GetListVideoSummary(string path)
        {
            VideoFileInfo cached;
            if (!string.IsNullOrWhiteSpace(path) && videoInfoCache.TryGetValue(path, out cached))
            {
                return cached.ListSummary;
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return "文件不存在";
            }

            try
            {
                FileInfo file = new FileInfo(path);
                return VideoMetadataReader.FormatBytes(file.Length) + " | " + (Path.GetExtension(path) ?? "").TrimStart('.').ToUpperInvariant();
            }
            catch
            {
                return "";
            }
        }

        private void UpdateSelectedPreviewDetails()
        {
            if (previewList == null || previewList.SelectedItems.Count == 0)
            {
                return;
            }

            RenamePlan entry = previewList.SelectedItems[0].Tag as RenamePlan;
            if (entry == null)
            {
                ShowNoVideoDetails();
                return;
            }

            string context = string.Format("第 {0} 行 / 场号 {1} / 镜号 {2} / {3} / {4}", entry.RowIndex, entry.Scene, entry.Shot, entry.ColumnName, entry.TailSegment);
            ShowVideoDetails(entry.OldPath, entry.NewName, context);
            UpdateCustomTailControls(entry);
        }

        private void UpdateSelectedCellDetails()
        {
            if (rendering || grid == null || grid.CurrentCell == null || thumbnailBox == null)
            {
                return;
            }

            int rowIndex = grid.CurrentCell.RowIndex;
            int columnIndex = grid.CurrentCell.ColumnIndex;
            if (rowIndex < 0 || rowIndex >= rows.Count || (columnIndex != GridMainColumn && columnIndex != GridBackupColumn))
            {
                ShowNoVideoDetails();
                return;
            }

            ShotRow row = rows[rowIndex];
            List<string> files = columnIndex == GridMainColumn ? row.MainFiles : row.BackupFiles;
            if (files.Count == 0)
            {
                ShowNoVideoDetails();
                return;
            }

            bool isMain = columnIndex == GridMainColumn;
            RenamePlan firstPlan = currentPlan.FirstOrDefault(p => p.Row == row && p.IsMain == isMain && p.FileIndex == 0);
            string newName = firstPlan == null ? "" : firstPlan.NewName;
            string context = string.Format("第 {0} 行 / 场号 {1} / 镜号 {2} / {3}，单元格共 {4} 个视频，当前显示第 1 个", rowIndex + 1, GetEffectiveScene(row, GetDefaultScene(), IsRowSceneEnabled()), row.Sequence, isMain ? "主要素材" : "备用素材", files.Count);
            ShowVideoDetails(files[0], newName, context);
            UpdateCustomTailControls(firstPlan);
        }

        private void ShowVideoDetails(string path, string newName, string context)
        {
            if (thumbnailBox == null || detailTitleLabel == null || detailInfoLabel == null || detailPathLabel == null)
            {
                return;
            }

            detailLoadVersion++;
            currentDetailPath = path ?? "";
            currentDetailNewName = newName ?? "";
            currentDetailContext = context ?? "";

            VideoFileInfo info;
            if (string.IsNullOrWhiteSpace(path) || !videoInfoCache.TryGetValue(path, out info))
            {
                info = GetFastVideoInfo(path);
                QueueVideoInfoLoad(path);
            }

            RenderVideoInfo(info, currentDetailNewName, currentDetailContext);
            Image thumbnail;
            if (TryGetThumbnailFromCache(path, out thumbnail))
            {
                SetDetailImage(thumbnail, false);
            }
            else
            {
                SetDetailImage(CreatePlaceholderImage(info.Exists ? "缩略图读取中" : "无缩略图"), true);
                QueueThumbnailLoad(path);
            }
        }

        private bool IsCurrentDetailPath(string path)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(currentDetailPath ?? "", path ?? "");
        }

        private void RenderVideoInfo(VideoFileInfo info, string newName, string context)
        {
            if (info == null)
            {
                info = GetFastVideoInfo("");
            }

            detailTitleLabel.Text = string.IsNullOrWhiteSpace(info.FileName) ? "未选择素材" : info.FileName;
            UpdatePreviewListInfoSummary(info.Path, info);

            List<string> lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(context))
            {
                lines.Add(context);
            }
            if (!string.IsNullOrWhiteSpace(newName))
            {
                lines.Add("目标文件名：" + newName);
            }
            lines.Add("大小：" + (string.IsNullOrWhiteSpace(info.SizeText) ? "未知" : info.SizeText));
            lines.Add("分辨率：" + info.ResolutionText);
            lines.Add("修改时间：" + (string.IsNullOrWhiteSpace(info.ModifiedText) ? "未知" : info.ModifiedText));
            detailInfoLabel.Text = string.Join("\r\n", lines.ToArray());
            detailPathLabel.Text = info.Path ?? "";
        }

        private void UpdateCustomTailControls(RenamePlan entry)
        {
            if (chkCustomTail == null || txtCustomTail == null)
            {
                return;
            }

            bool enabled = entry != null && entry.Row != null;
            chkCustomTail.Enabled = enabled;
            txtCustomTail.Text = enabled && entry.HasCustomTail ? entry.CustomTailText : "";

            UpdateCustomTailInputState();
        }

        private void UpdateCustomTailInputState()
        {
            if (chkCustomTail == null || txtCustomTail == null)
            {
                return;
            }

            bool hasSelection = GetSelectedPreviewEntry() != null;
            chkCustomTail.Enabled = true;
            txtCustomTail.Enabled = hasSelection && chkCustomTail.Checked;
            UiTheme.ApplyControl(chkCustomTail, darkMode);
            UiTheme.ApplyControl(txtCustomTail, darkMode);
        }

        private RenamePlan GetSelectedPreviewEntry()
        {
            if (previewList == null || previewList.SelectedItems.Count == 0)
            {
                return null;
            }

            return previewList.SelectedItems[0].Tag as RenamePlan;
        }

        private void ApplySelectedCustomTail(bool showMessage)
        {
            RenamePlan entry = GetSelectedPreviewEntry();
            if (entry == null || entry.Row == null)
            {
                if (showMessage)
                {
                    MessageBox.Show(this, "请先在底部预览中选中一条素材记录。", "未选择素材", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            if (chkCustomTail != null && !chkCustomTail.Checked)
            {
                if (showMessage)
                {
                    MessageBox.Show(this, "请先勾选自定义，再输入末尾编号或文字。", "未启用自定义", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            string normalized = "";
            string requestedNormalized = "";
            normalized = NormalizeCustomTailText(txtCustomTail == null ? "" : txtCustomTail.Text);
            requestedNormalized = normalized;
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                normalized = GetUniqueCustomTail(entry, normalized, currentPlan, (int)numEpisode.Value, entry.Scene, chkKeepExtension.Checked);
            }

            ShotRow row = entry.Row;
            bool isMain = entry.IsMain;
            int fileIndex = entry.FileIndex;

            SetTailOverride(entry, normalized);

            RefreshPreview();
            SelectPreviewEntry(row, isMain, fileIndex);
            if (statusLabel != null)
            {
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    statusLabel.Text = "已恢复默认 T 编号。";
                }
                else if (!StringComparer.Ordinal.Equals(normalized, requestedNormalized))
                {
                    statusLabel.Text = "已应用自定义末尾：" + normalized + "（自动补号）";
                }
                else
                {
                    statusLabel.Text = "已应用自定义末尾：" + normalized;
                }
            }
        }

        private void SelectPreviewEntry(ShotRow row, bool isMain, int fileIndex)
        {
            if (previewList == null)
            {
                return;
            }

            foreach (ListViewItem item in previewList.Items)
            {
                RenamePlan entry = item.Tag as RenamePlan;
                if (entry != null && entry.Row == row && entry.IsMain == isMain && entry.FileIndex == fileIndex)
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    UpdateSelectedPreviewDetails();
                    return;
                }
            }
        }

        private void UpdatePreviewListInfoSummary(string path, VideoFileInfo info)
        {
            if (previewList == null || string.IsNullOrWhiteSpace(path) || info == null)
            {
                return;
            }

            foreach (ListViewItem item in previewList.Items)
            {
                RenamePlan entry = item.Tag as RenamePlan;
                if (entry != null &&
                    StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, path) &&
                    item.SubItems.Count > 7)
                {
                    item.SubItems[7].Text = info.ListSummary;
                }
            }
        }

        private void ShowNoVideoDetails()
        {
            if (thumbnailBox == null || detailTitleLabel == null || detailInfoLabel == null || detailPathLabel == null)
            {
                return;
            }

            detailLoadVersion++;
            currentDetailPath = "";
            currentDetailNewName = "";
            currentDetailContext = "";
            detailTitleLabel.Text = "未选择素材";
            detailInfoLabel.Text = "选中底部预览记录，或选中 B/C 中已有素材的单元格，可查看缩略图、分辨率和文件大小。";
            detailPathLabel.Text = "";
            UpdateCustomTailControls(null);
            SetDetailImage(CreatePlaceholderImage("视频预览"), true);
        }

        private Image CreatePlaceholderImage(string text)
        {
            Bitmap bitmap = new Bitmap(300, 166);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(UiTheme.ControlBack(darkMode));
                using (Pen border = new Pen(UiTheme.BorderColor(darkMode)))
                {
                    graphics.DrawRectangle(border, 0, 0, bitmap.Width - 1, bitmap.Height - 1);
                }
                using (Brush brush = new SolidBrush(UiTheme.MutedText(darkMode)))
                using (Font font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold))
                {
                    SizeF size = graphics.MeasureString(text, font);
                    graphics.DrawString(text, font, brush, (bitmap.Width - size.Width) / 2, (bitmap.Height - size.Height) / 2);
                }
            }
            return bitmap;
        }

        private void SetDetailImage(Image image, bool ownsImage)
        {
            if (thumbnailBox == null)
            {
                if (ownsImage && image != null)
                {
                    image.Dispose();
                }
                return;
            }

            if (ownedDetailImage != null && !object.ReferenceEquals(ownedDetailImage, image))
            {
                ownedDetailImage.Dispose();
            }

            thumbnailBox.Image = image;
            ownedDetailImage = ownsImage ? image : null;
        }

        private void RenderAll()
        {
            RenderGrid();
            RefreshPreview();
        }

        private void RenderGrid()
        {
            rendering = true;
            try
            {
                ApplyGridColumnLayout();
                dragHighlightRow = -1;
                dragHighlightColumn = -1;
                grid.Rows.Clear();
                foreach (ShotRow row in rows)
                {
                    int index = grid.Rows.Add();
                    DataGridViewRow gridRow = grid.Rows[index];
                    gridRow.Tag = row;
                    gridRow.Height = grid.RowTemplate.Height;
                    gridRow.Resizable = DataGridViewTriState.False;
                    gridRow.Cells[GridSceneColumn].Value = GetEffectiveScene(row, GetDefaultScene(), true);
                    gridRow.Cells[GridShotColumn].Value = row.Sequence;
                    gridRow.Cells[GridMainColumn].Value = GetCellSummary(row.MainFiles);
                    gridRow.Cells[GridBackupColumn].Value = GetCellSummary(row.BackupFiles);
                    gridRow.Cells[GridProgressColumn].Value = row.ProgressPercent;
                    gridRow.Cells[GridMainColumn].ToolTipText = GetCellSummary(row.MainFiles);
                    gridRow.Cells[GridBackupColumn].ToolTipText = GetCellSummary(row.BackupFiles);
                    ApplyGridNumberCellStyles(gridRow);
                }
                ApplyGridNumberColumnStyles();
            }
            finally
            {
                rendering = false;
            }
        }

        private void RenderGridRow(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= rows.Count || rowIndex >= grid.Rows.Count)
            {
                RenderGrid();
                return;
            }

            rendering = true;
            try
            {
                ShotRow row = rows[rowIndex];
                DataGridViewRow gridRow = grid.Rows[rowIndex];
                gridRow.Tag = row;
                gridRow.Height = grid.RowTemplate.Height;
                gridRow.Resizable = DataGridViewTriState.False;
                gridRow.Cells[GridSceneColumn].Value = GetEffectiveScene(row, GetDefaultScene(), true);
                gridRow.Cells[GridShotColumn].Value = row.Sequence;
                gridRow.Cells[GridMainColumn].Value = GetCellSummary(row.MainFiles);
                gridRow.Cells[GridBackupColumn].Value = GetCellSummary(row.BackupFiles);
                gridRow.Cells[GridProgressColumn].Value = row.ProgressPercent;
                gridRow.Cells[GridMainColumn].ToolTipText = GetCellSummary(row.MainFiles);
                gridRow.Cells[GridBackupColumn].ToolTipText = GetCellSummary(row.BackupFiles);
                ApplyGridNumberCellStyles(gridRow);
            }
            finally
            {
                rendering = false;
            }
        }

        private void RenderGridProgress(int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= rows.Count || rowIndex >= grid.Rows.Count || grid.Columns.Count <= GridProgressColumn)
            {
                return;
            }

            grid.Rows[rowIndex].Cells[GridProgressColumn].Value = rows[rowIndex].ProgressPercent;
            grid.InvalidateCell(GridProgressColumn, rowIndex);
        }

        private int GetDefaultScene()
        {
            return numScene == null ? 1 : Math.Max(1, (int)numScene.Value);
        }

        private bool IsRowSceneEnabled()
        {
            return chkRowScene != null && chkRowScene.Checked;
        }

        private void InitializeRowScenesFromDefaultIfNeeded()
        {
            if (rowSceneModeInitialized)
            {
                return;
            }

            int defaultScene = GetDefaultScene();
            foreach (ShotRow row in rows)
            {
                row.Scene = defaultScene;
            }

            rowSceneModeInitialized = true;
        }

        private static int GetEffectiveScene(ShotRow row, int defaultScene, bool useRowScene)
        {
            return useRowScene && row != null && row.Scene > 0 ? row.Scene : Math.Max(1, defaultScene);
        }

        private void SetProgressColumnVisible(bool visible)
        {
            progressColumnVisible = visible;
            ApplyGridColumnLayout();
        }

        private int GetDefaultGridFocusColumn()
        {
            return IsRowSceneEnabled() ? GridSceneColumn : GridShotColumn;
        }

        private void ApplyGridColumnLayout()
        {
            if (grid == null || grid.Columns.Count <= GridProgressColumn)
            {
                return;
            }

            bool rowScene = IsRowSceneEnabled();
            if (grid.CurrentCell != null)
            {
                int currentColumn = grid.CurrentCell.ColumnIndex;
                bool currentColumnWillHide =
                    (!rowScene && currentColumn == GridSceneColumn) ||
                    (!progressColumnVisible && currentColumn == GridProgressColumn);
                if (currentColumnWillHide)
                {
                    SelectGridCell(grid.CurrentCell.RowIndex, GetDefaultGridFocusColumn());
                }
            }

            grid.Columns[GridSceneColumn].Visible = rowScene;
            grid.Columns[GridShotColumn].HeaderText = rowScene ? "B 镜号" : "A 镜号";
            grid.Columns[GridMainColumn].HeaderText = rowScene ? "C 主要素材" : "B 主要素材";
            grid.Columns[GridBackupColumn].HeaderText = rowScene ? "D 备用素材" : "C 备用素材";
            grid.Columns[GridProgressColumn].HeaderText = rowScene ? "E 进度" : "D 进度";
            grid.Columns[GridProgressColumn].Visible = progressColumnVisible;
        }

        private string GetMainColumnDisplayName()
        {
            return IsRowSceneEnabled() ? "C「主要素材」" : "B「主要素材」";
        }

        private string GetBackupColumnDisplayName()
        {
            return IsRowSceneEnabled() ? "D「备用素材」" : "C「备用素材」";
        }

        private string GetMainColumnLetter()
        {
            return IsRowSceneEnabled() ? "C" : "B";
        }

        private string GetBackupColumnLetter()
        {
            return IsRowSceneEnabled() ? "D" : "C";
        }

        private void ResetProgressBars()
        {
            foreach (ShotRow row in rows)
            {
                row.ProgressPercent = 0;
            }

            RenderGrid();
        }

        private void RefreshPreview()
        {
            if (previewList == null)
            {
                return;
            }

            currentPlan.Clear();
            currentPlan.AddRange(BuildPlan(rows, (int)numEpisode.Value, GetDefaultScene(), chkKeepExtension.Checked, IsExport1080pEnabled(), IsRowSceneEnabled()));

            previewList.BeginUpdate();
            try
            {
                previewList.Items.Clear();
                previewList.Groups.Clear();
                Dictionary<int, ListViewGroup> previewGroups = new Dictionary<int, ListViewGroup>();
                foreach (RenamePlan entry in currentPlan)
                {
                    bool firstInGroup = !previewGroups.ContainsKey(entry.RowIndex);
                    if (firstInGroup)
                    {
                        ListViewGroup group = new ListViewGroup(
                            string.Format("第 {0} 行 / 场号 {1} / 镜号 {2}", entry.RowIndex, entry.Scene, entry.Shot),
                            HorizontalAlignment.Left);
                        previewGroups[entry.RowIndex] = group;
                        previewList.Groups.Add(group);
                    }

                    ListViewItem item = new ListViewItem(firstInGroup ? "▶ " + entry.RowIndex : "  " + entry.RowIndex);
                    item.Tag = entry;
                    item.Group = previewGroups[entry.RowIndex];
                    item.SubItems.Add(entry.Shot.ToString());
                    item.SubItems.Add(entry.TailSegment);
                    item.SubItems.Add(entry.ColumnName);
                    item.SubItems.Add(entry.OldName);
                    item.SubItems.Add(entry.NewName);
                    item.SubItems.Add(entry.Status);
                    item.SubItems.Add(GetListVideoSummary(entry.OldPath));
                    if (firstInGroup)
                    {
                        item.Font = new Font(previewList.Font, FontStyle.Bold);
                    }
                    ApplyPreviewItemStatusStyle(item, entry);

                    previewList.Items.Add(item);
                }

                UpdatePreviewStatusSummary();
            }
            finally
            {
                previewList.EndUpdate();
            }

            SchedulePreviewColumnResize();
            SchedulePlanStatusChecks();
            if (previewList.SelectedItems.Count == 0)
            {
                UpdateSelectedCellDetails();
            }
        }

        private void SchedulePreviewColumnResize()
        {
            if (previewList == null)
            {
                return;
            }

            if (previewColumnResizeTimer == null)
            {
                previewColumnResizeTimer = new System.Windows.Forms.Timer();
                previewColumnResizeTimer.Interval = 120;
                previewColumnResizeTimer.Tick += delegate
                {
                    previewColumnResizeTimer.Stop();
                    AutoResizePreviewColumns();
                };
            }

            previewColumnResizeTimer.Stop();
            previewColumnResizeTimer.Start();
        }

        private void AutoResizePreviewColumns()
        {
            if (previewList == null || previewList.Columns.Count < 8)
            {
                return;
            }

            const int originalNameColumn = 4;
            previewList.Columns[originalNameColumn].Width = 144;
            for (int index = 0; index < previewList.Columns.Count; index++)
            {
                if (index == originalNameColumn)
                {
                    continue;
                }

                previewList.Columns[index].Width = GetPreviewColumnWidth(index);
            }
        }

        private int GetPreviewColumnWidth(int columnIndex)
        {
            int width = MeasurePreviewText(previewList.Columns[columnIndex].Text, previewList.Font) + 20;
            int measuredRows = 0;
            foreach (ListViewItem item in previewList.Items)
            {
                if (columnIndex < item.SubItems.Count)
                {
                    width = Math.Max(width, MeasurePreviewText(item.SubItems[columnIndex].Text, item.Font) + 20);
                }

                measuredRows++;
                if (measuredRows >= 120)
                {
                    break;
                }
            }

            return Math.Min(GetPreviewColumnMaximumWidth(columnIndex), Math.Max(GetPreviewColumnMinimumWidth(columnIndex), width));
        }

        private static int MeasurePreviewText(string text, Font font)
        {
            return TextRenderer.MeasureText(string.IsNullOrEmpty(text) ? " " : text, font).Width;
        }

        private static int GetPreviewColumnMinimumWidth(int columnIndex)
        {
            switch (columnIndex)
            {
                case 0:
                    return 48;
                case 1:
                    return 54;
                case 2:
                    return 60;
                case 3:
                    return 76;
                case 5:
                    return 160;
                case 6:
                    return 86;
                case 7:
                    return 120;
                default:
                    return 72;
            }
        }

        private static int GetPreviewColumnMaximumWidth(int columnIndex)
        {
            switch (columnIndex)
            {
                case 0:
                    return 84;
                case 1:
                    return 92;
                case 2:
                    return 180;
                case 3:
                    return 120;
                case 5:
                    return 420;
                case 6:
                    return 160;
                case 7:
                    return 220;
                default:
                    return 240;
            }
        }

        private void ApplyPreviewItemStatusStyle(ListViewItem item, RenamePlan entry)
        {
            if (item == null || entry == null)
            {
                return;
            }

            item.BackColor = entry.RowIndex % 2 == 0 ? UiTheme.PreviewAltBack(darkMode) : UiTheme.ControlBack(darkMode);
            item.ForeColor = UiTheme.TextColor(darkMode);

            if (IsBlockingIssue(entry))
            {
                item.BackColor = UiTheme.PreviewErrorBack(darkMode);
            }
            else if (entry.Status == "未变化")
            {
                item.BackColor = UiTheme.PreviewNeutralBack(darkMode);
            }
        }

        private void UpdatePreviewStatusSummary()
        {
            if (statusLabel == null)
            {
                return;
            }

            int errors = currentPlan.Count(IsBlockingIssue);
            if (currentPlan.Count == 0)
            {
                statusLabel.Text = "把视频拖到表格 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 单元格。";
            }
            else if (errors > 0)
            {
                statusLabel.Text = string.Format("共 {0} 个视频，发现 {1} 个命名问题；请处理后再执行。", currentPlan.Count, errors);
            }
            else if (IsExport1080pEnabled())
            {
                statusLabel.Text = IsExportWatermarkEnabled()
                    ? string.Format("共 {0} 个视频，预览无冲突；导出时会在左上角加入新文件名水印。", currentPlan.Count)
                    : string.Format("共 {0} 个视频，预览无冲突；执行时可选择覆盖原文件或另存为新文件。", currentPlan.Count);
            }
            else
            {
                statusLabel.Text = string.Format("共 {0} 个视频，预览无冲突。{1} 列先编号，{2} 列接着编号。", currentPlan.Count, GetMainColumnLetter(), GetBackupColumnLetter());
            }
        }

        private void RefreshPreviewStatusOnly()
        {
            if (previewList == null)
            {
                return;
            }

            previewList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in previewList.Items)
                {
                    RenamePlan entry = item.Tag as RenamePlan;
                    if (entry == null)
                    {
                        continue;
                    }

                    if (item.SubItems.Count > 6)
                    {
                        item.SubItems[6].Text = entry.Status;
                    }
                    ApplyPreviewItemStatusStyle(item, entry);
                }
            }
            finally
            {
                previewList.EndUpdate();
            }

            UpdatePreviewStatusSummary();
        }

        private void SchedulePlanStatusChecks()
        {
            int version = ++planCheckVersion;
            List<RenamePlan> targets = currentPlan
                .Where(p => p.Status == "目标已存在" && !string.IsNullOrWhiteSpace(p.TargetPath))
                .ToList();

            if (targets.Count == 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int offset = 0; offset < targets.Count; offset += PlanStatusCheckBatchSize)
                {
                    List<RenamePlan> batch = targets.Skip(offset).Take(PlanStatusCheckBatchSize).ToList();
                    HashSet<string> lockedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (RenamePlan entry in batch)
                    {
                        if (IsFileLocked(entry.TargetPath))
                        {
                            lockedPaths.Add(entry.TargetPath);
                        }
                    }

                    if (lockedPaths.Count > 0)
                    {
                        QueueOnUi(delegate
                        {
                            if (version != planCheckVersion)
                            {
                                return;
                            }

                            foreach (RenamePlan entry in currentPlan)
                            {
                                if (entry.Status == "目标已存在" && lockedPaths.Contains(entry.TargetPath))
                                {
                                    entry.Status = "目标文件被占用";
                                }
                            }

                            RefreshPreviewStatusOnly();
                        });
                    }

                    Thread.Sleep(20);
                }
            });
        }

        private static List<RenamePlan> BuildPlan(List<ShotRow> sourceRows, int episode, int scene, bool keepExtensionCase, bool export1080p, bool useRowScene = false)
        {
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            List<RenamePlan> plan = new List<RenamePlan>();
            int rowIndex = 1;

            foreach (ShotRow row in sourceRows)
            {
                int rowScene = GetEffectiveScene(row, scene, useRowScene);
                int shot = Math.Max(1, row.Sequence);
                int take = 1;

                EnsureTailOverrideSize(row, true);
                EnsureTailOverrideSize(row, false);
                AddFilesToPlan(plan, seen, row, rowIndex, "主要素材", true, row.MainFiles, row.MainTailOverrides, episode, rowScene, shot, ref take, keepExtensionCase, export1080p);
                AddFilesToPlan(plan, seen, row, rowIndex, "备用素材", false, row.BackupFiles, row.BackupTailOverrides, episode, rowScene, shot, ref take, keepExtensionCase, export1080p);
                rowIndex++;
            }

            return plan;
        }

        private static void AddFilesToPlan(
            List<RenamePlan> plan,
            Dictionary<string, bool> seen,
            ShotRow row,
            int rowIndex,
            string columnName,
            bool isMain,
            List<string> files,
            List<string> tailOverrides,
            int episode,
            int scene,
            int shot,
            ref int take,
            bool keepExtensionCase,
            bool export1080p)
        {
            for (int fileIndex = 0; fileIndex < files.Count; fileIndex++)
            {
                string oldPath = Path.GetFullPath(files[fileIndex]);
                string customTail = tailOverrides != null && fileIndex < tailOverrides.Count ? NormalizeCustomTailText(tailOverrides[fileIndex]) : "";
                string tailSegment = GetTailSegment(take, customTail);
                string newName = GetMaterialFileName(episode, scene, shot, tailSegment, oldPath, keepExtensionCase);
                string directory = Path.GetDirectoryName(oldPath);
                string targetPath = Path.GetFullPath(Path.Combine(directory, newName));
                string status = "就绪";

                if (!File.Exists(oldPath))
                {
                    status = "源文件丢失";
                }
                else if (StringComparer.OrdinalIgnoreCase.Equals(targetPath, oldPath))
                {
                    status = export1080p ? "待覆盖导出1080p" : "未变化";
                }
                else if (File.Exists(targetPath))
                {
                    status = "目标已存在";
                }

                if (export1080p && status == "就绪")
                {
                    status = "待覆盖导出1080p";
                }

                if (seen.ContainsKey(targetPath))
                {
                    status = "新文件名重复";
                }

                seen[targetPath] = true;
                plan.Add(new RenamePlan
                {
                    Row = row,
                    RowIndex = rowIndex,
                    ColumnName = columnName,
                    IsMain = isMain,
                    FileIndex = fileIndex,
                    Scene = scene,
                    Shot = shot,
                    Take = take,
                    TailSegment = tailSegment,
                    CustomTailText = customTail,
                    HasCustomTail = !string.IsNullOrWhiteSpace(customTail),
                    OldPath = oldPath,
                    TargetPath = targetPath,
                    OldName = Path.GetFileName(oldPath),
                    NewName = Path.GetFileName(targetPath),
                    Status = status
                });

                take++;
            }
        }

        private static bool IsBlockingIssue(RenamePlan entry)
        {
            return entry != null &&
                (entry.Status == "目标已存在" ||
                 entry.Status == "目标文件被占用" ||
                 entry.Status == "新文件名重复" ||
                 entry.Status == "源文件丢失");
        }

        private static bool IsFileLocked(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        private static string GetMaterialFileName(int episode, int scene, int shot, string tailSegment, string sourcePath, bool keepExtensionCase)
        {
            string extension = Path.GetExtension(sourcePath) ?? "";
            if (!keepExtensionCase)
            {
                extension = extension.ToLowerInvariant();
            }

            string safeTail = string.IsNullOrWhiteSpace(tailSegment) ? "T1" : tailSegment;
            return string.Format("E{0}-S{1}-{2}-{3}{4}", Math.Max(1, episode), Math.Max(1, scene), Math.Max(1, shot), safeTail, extension);
        }

        private static string GetTailSegment(int take, string customTail)
        {
            string normalized = NormalizeCustomTailText(customTail);
            return string.IsNullOrWhiteSpace(normalized) ? "T" + Math.Max(1, take) : normalized;
        }

        private static string NormalizeCustomTailText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string text = value.Trim();
            HashSet<char> invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            StringBuilder builder = new StringBuilder();
            foreach (char ch in text)
            {
                if (invalid.Contains(ch) || char.IsControl(ch))
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(ch);
                }
            }

            string normalized = builder.ToString().Trim().Trim('.');
            if (normalized.Length > 80)
            {
                normalized = normalized.Substring(0, 80).Trim();
            }

            return normalized;
        }

        private static List<string> GetTailOverrideList(ShotRow row, bool isMain)
        {
            return isMain ? row.MainTailOverrides : row.BackupTailOverrides;
        }

        private static List<string> GetFileList(ShotRow row, bool isMain)
        {
            return isMain ? row.MainFiles : row.BackupFiles;
        }

        private static void EnsureTailOverrideSize(ShotRow row, bool isMain)
        {
            if (row == null)
            {
                return;
            }

            List<string> files = GetFileList(row, isMain);
            List<string> tails = GetTailOverrideList(row, isMain);
            while (tails.Count < files.Count)
            {
                tails.Add("");
            }
            while (tails.Count > files.Count)
            {
                tails.RemoveAt(tails.Count - 1);
            }
        }

        private static string SetTailOverride(RenamePlan entry, string value)
        {
            if (entry == null || entry.Row == null)
            {
                return "";
            }

            EnsureTailOverrideSize(entry.Row, entry.IsMain);
            List<string> tails = GetTailOverrideList(entry.Row, entry.IsMain);
            if (entry.FileIndex < 0 || entry.FileIndex >= tails.Count)
            {
                return "";
            }

            string normalized = NormalizeCustomTailText(value);
            tails[entry.FileIndex] = normalized;
            return normalized;
        }

        private static string GetUniqueCustomTail(RenamePlan selectedEntry, string requestedTail, IEnumerable<RenamePlan> existingPlan, int episode, int scene, bool keepExtensionCase)
        {
            string baseTail = NormalizeCustomTailText(requestedTail);
            if (selectedEntry == null || string.IsNullOrWhiteSpace(baseTail))
            {
                return baseTail;
            }

            for (int counter = 1; counter < 10000; counter++)
            {
                string candidateTail = counter == 1 ? baseTail : AppendCustomTailCounter(baseTail, counter);
                string candidatePath = BuildTargetPathForTail(selectedEntry, candidateTail, episode, scene, keepExtensionCase);
                bool duplicate = existingPlan != null && existingPlan.Any(delegate(RenamePlan entry)
                {
                    return entry != null &&
                        !IsSamePlanEntry(entry, selectedEntry) &&
                        StringComparer.OrdinalIgnoreCase.Equals(entry.TargetPath, candidatePath);
                });

                if (!duplicate)
                {
                    return candidateTail;
                }
            }

            return AppendCustomTailCounter(baseTail, Environment.TickCount & 0x7fffffff);
        }

        private static string AppendCustomTailCounter(string baseTail, int counter)
        {
            string suffix = Math.Max(2, counter).ToString();
            int maxBaseLength = Math.Max(1, 80 - suffix.Length);
            string trimmedBase = baseTail.Length > maxBaseLength ? baseTail.Substring(0, maxBaseLength).Trim() : baseTail;
            return NormalizeCustomTailText(trimmedBase + suffix);
        }

        private static string BuildTargetPathForTail(RenamePlan entry, string tailSegment, int episode, int scene, bool keepExtensionCase)
        {
            string directory = Path.GetDirectoryName(entry.OldPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Environment.CurrentDirectory;
            }

            string newName = GetMaterialFileName(episode, scene, entry.Shot, tailSegment, entry.OldPath, keepExtensionCase);
            return Path.GetFullPath(Path.Combine(directory, newName));
        }

        private static bool IsSamePlanEntry(RenamePlan left, RenamePlan right)
        {
            return left != null &&
                right != null &&
                left.Row == right.Row &&
                left.IsMain == right.IsMain &&
                left.FileIndex == right.FileIndex;
        }

        private static string GetUniquePathWithSuffix(string path, string suffix)
        {
            string directory = Path.GetDirectoryName(path);
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            string safeSuffix = string.IsNullOrWhiteSpace(suffix) ? "_副本" : suffix;
            string first = Path.Combine(directory, stem + safeSuffix + extension);
            if (!File.Exists(first))
            {
                return first;
            }

            int counter = 2;
            while (true)
            {
                string candidate = Path.Combine(directory, string.Format("{0}{1}{2}{3}", stem, safeSuffix, counter, extension));
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
                counter++;
            }
        }

        private static string GetCellSummary(List<string> files)
        {
            if (files == null || files.Count == 0)
            {
                return "";
            }

            string[] names = files.Take(2).Select(Path.GetFileName).ToArray();
            if (files.Count > 2)
            {
                return string.Format("{0}条：{1} ...", files.Count, string.Join("；", names));
            }

            return string.Format("{0}条：{1}", files.Count, string.Join("；", names));
        }

        private void OnGridDragEnterOrOver(object sender, DragEventArgs e)
        {
            try
            {
                if (e != null && e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    int rowIndex;
                    int columnIndex;
                    if (TryGetDropTargetCell(e, out rowIndex, out columnIndex))
                    {
                        e.Effect = DragDropEffects.Copy;
                        SetDragHighlight(rowIndex, columnIndex);
                        statusLabel.Text = string.Format(
                            "松开鼠标后会导入到第 {0} 行 {1}。",
                            rowIndex + 1,
                            columnIndex == GridMainColumn ? GetMainColumnDisplayName() : GetBackupColumnDisplayName());
                    }
                    else
                    {
                        e.Effect = DragDropEffects.None;
                        ClearDragHighlight();
                    }
                }
                else if (e != null)
                {
                    e.Effect = DragDropEffects.None;
                    ClearDragHighlight();
                }
            }
            catch
            {
                if (e != null)
                {
                    e.Effect = DragDropEffects.None;
                }
                ClearDragHighlight();
            }
        }

        private void OnGridDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (e == null || e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    statusLabel.Text = "没有检测到可拖入的文件。";
                    return;
                }

                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null || paths.Length == 0)
                {
                    statusLabel.Text = "没有检测到可拖入的文件。";
                    return;
                }

                int rowIndex = -1;
                int columnIndex = -1;

                if (!TryGetDropTargetCell(e, out rowIndex, out columnIndex))
                {
                    statusLabel.Text = "请拖到 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 单元格。";
                    return;
                }

                AddFilesToShotCell(rowIndex, columnIndex, paths);
            }
            catch (Exception ex)
            {
                statusLabel.Text = "拖入失败：" + ex.Message;
            }
            finally
            {
                ClearDragHighlight();
            }
        }

        private bool TryGetDropTargetCell(DragEventArgs e, out int rowIndex, out int columnIndex)
        {
            rowIndex = -1;
            columnIndex = -1;

            if (grid == null || e == null)
            {
                return false;
            }

            Point point = grid.PointToClient(new Point(e.X, e.Y));
            DataGridView.HitTestInfo hit = grid.HitTest(point.X, point.Y);
            if (hit.RowIndex >= 0 && hit.RowIndex < rows.Count && (hit.ColumnIndex == GridMainColumn || hit.ColumnIndex == GridBackupColumn))
            {
                rowIndex = hit.RowIndex;
                columnIndex = hit.ColumnIndex;
                return true;
            }

            if (grid.CurrentCell != null &&
                grid.CurrentCell.RowIndex >= 0 &&
                grid.CurrentCell.RowIndex < rows.Count &&
                (grid.CurrentCell.ColumnIndex == GridMainColumn || grid.CurrentCell.ColumnIndex == GridBackupColumn))
            {
                rowIndex = grid.CurrentCell.RowIndex;
                columnIndex = grid.CurrentCell.ColumnIndex;
                return true;
            }

            return false;
        }

        private void AddFilesToShotCell(int rowIndex, int columnIndex, string[] paths)
        {
            if (rowIndex < 0 || rowIndex >= rows.Count)
            {
                statusLabel.Text = "目标行无效。";
                return;
            }

            if (columnIndex != GridMainColumn && columnIndex != GridBackupColumn)
            {
                statusLabel.Text = "请把视频拖到 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 列。";
                return;
            }

            List<string> videoPaths = GetVideoFilePaths(paths);
            if (videoPaths.Count == 0)
            {
                statusLabel.Text = "没有发现支持的视频文件。";
                return;
            }

            ShotRow targetRow = rows[rowIndex];
            List<string> targetFiles = columnIndex == GridMainColumn ? targetRow.MainFiles : targetRow.BackupFiles;
            List<string> targetTails = columnIndex == GridMainColumn ? targetRow.MainTailOverrides : targetRow.BackupTailOverrides;
            EnsureTailOverrideSize(targetRow, columnIndex == GridMainColumn);
            HashSet<string> existing = GetAllFileKeys();
            List<string> skippedNames = new List<string>();
            int added = 0;
            int skipped = 0;

            foreach (string path in videoPaths)
            {
                if (existing.Contains(path))
                {
                    skipped++;
                    skippedNames.Add(Path.GetFileName(path));
                    continue;
                }

                targetFiles.Add(path);
                targetTails.Add("");
                existing.Add(path);
                added++;
            }

            RenderGridRow(rowIndex);
            if (rowIndex >= 0 && rowIndex < grid.Rows.Count && columnIndex >= 0 && columnIndex < grid.Columns.Count)
            {
                grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
            }
            RefreshPreview();

            string columnName = columnIndex == GridMainColumn ? "主要素材" : "备用素材";
            statusLabel.Text = string.Format("第 {0} 行 {1} 已加入 {2} 个视频；跳过重复 {3} 个。", rowIndex + 1, columnName, added, skipped);
            if (skippedNames.Count > 0)
            {
                ShowDuplicateFileWarning(skippedNames);
            }
            ShowCurrentPlanIssueWarning();
        }

        private void ShowDuplicateFileWarning(List<string> duplicateNames)
        {
            if (duplicateNames == null || duplicateNames.Count == 0)
            {
                return;
            }

            List<string> lines = duplicateNames.Take(10).Select(name => " - " + name).ToList();
            if (duplicateNames.Count > 10)
            {
                lines.Add("... 另有 " + (duplicateNames.Count - 10) + " 个重复文件");
            }

            MessageBox.Show(
                this,
                "检测到重复文件，已自动跳过，不会重复加入表格。\r\n\r\n" + string.Join("\r\n", lines.ToArray()),
                "重复文件已跳过",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private void ShowCurrentPlanIssueWarning()
        {
            if (currentPlan == null || currentPlan.Count == 0)
            {
                return;
            }

            List<RenamePlan> issues = currentPlan.Where(IsBlockingIssue).ToList();
            if (issues.Count == 0)
            {
                return;
            }

            MessageBox.Show(
                this,
                BuildIssueMessage(issues),
                "检测到文件问题",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private static string BuildIssueMessage(List<RenamePlan> issues)
        {
            List<string> lines = new List<string>();
            lines.Add("检测到以下问题，请处理后再执行重命名：");
            lines.Add("");

            foreach (RenamePlan issue in issues.Take(10))
            {
                lines.Add(string.Format(
                    "第 {0} 行 {1} {2}：{3}",
                    issue.RowIndex,
                    issue.ColumnName,
                    issue.TailSegment,
                    issue.Status));
                lines.Add("  原文件：" + issue.OldName);
                lines.Add("  目标名：" + issue.NewName);
            }

            if (issues.Count > 10)
            {
                lines.Add("");
                lines.Add("... 另有 " + (issues.Count - 10) + " 个问题");
            }

            return string.Join("\r\n", lines.ToArray());
        }

        private static List<string> GetVideoFilePaths(string[] paths)
        {
            List<string> files = new List<string>();
            if (paths == null)
            {
                return files;
            }

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (File.Exists(path))
                {
                    string extension = Path.GetExtension(path);
                    if (extension != null && VideoExtensions.Contains(extension))
                    {
                        files.Add(Path.GetFullPath(path));
                    }
                    continue;
                }

                if (Directory.Exists(path))
                {
                    foreach (string file in Directory.GetFiles(path))
                    {
                        string extension = Path.GetExtension(file);
                        if (extension != null && VideoExtensions.Contains(extension))
                        {
                            files.Add(Path.GetFullPath(file));
                        }
                    }
                }
            }

            files.Sort(new NaturalPathComparer());
            return files;
        }

        private HashSet<string> GetAllFileKeys()
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ShotRow row in rows)
            {
                foreach (string file in row.MainFiles)
                {
                    keys.Add(file);
                }
                foreach (string file in row.BackupFiles)
                {
                    keys.Add(file);
                }
            }
            return keys;
        }

        private void OnGridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (rendering || e == null || e.RowIndex < 0 || e.RowIndex >= rows.Count)
            {
                return;
            }

            ShotRow row = rows[e.RowIndex];
            if (e.ColumnIndex == GridSceneColumn)
            {
                object value = grid.Rows[e.RowIndex].Cells[GridSceneColumn].Value;
                int parsed;
                if (value != null && int.TryParse(value.ToString(), out parsed) && parsed > 0)
                {
                    row.Scene = parsed;
                }
                else
                {
                    row.Scene = GetEffectiveScene(row, GetDefaultScene(), true);
                    grid.Rows[e.RowIndex].Cells[GridSceneColumn].Value = row.Scene;
                    statusLabel.Text = "A 列场号必须是大于 0 的整数，已保留原场号。";
                }
            }
            else if (e.ColumnIndex == GridShotColumn)
            {
                object value = grid.Rows[e.RowIndex].Cells[GridShotColumn].Value;
                int parsed;
                if (value != null && int.TryParse(value.ToString(), out parsed) && parsed > 0)
                {
                    row.Sequence = parsed;
                }
                else
                {
                    row.Sequence = Math.Max(1, row.Sequence);
                    grid.Rows[e.RowIndex].Cells[GridShotColumn].Value = row.Sequence;
                    statusLabel.Text = (IsRowSceneEnabled() ? "B" : "A") + " 列镜号必须是大于 0 的整数，已保留原镜号。";
                }
            }
            else if (e.ColumnIndex == GridProgressColumn)
            {
                grid.Rows[e.RowIndex].Cells[GridProgressColumn].Value = row.ProgressPercent;
            }

            RenderAll();
        }

        private void ImportSelectedCell()
        {
            if (grid.CurrentCell == null || (grid.CurrentCell.ColumnIndex != GridMainColumn && grid.CurrentCell.ColumnIndex != GridBackupColumn))
            {
                MessageBox.Show("请先选中一个 " + GetMainColumnDisplayName() + " 或 " + GetBackupColumnDisplayName() + " 单元格。", "未选择素材格", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择视频文件";
                dialog.Multiselect = true;
                dialog.Filter = "视频文件|*.mp4;*.mov;*.m4v;*.avi;*.mkv;*.wmv;*.flv;*.webm;*.mts;*.m2ts;*.3gp;*.mpeg;*.mpg|所有文件|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddFilesToShotCell(grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex, dialog.FileNames);
                }
            }
        }

        private void DeleteSelectedPreviewRecord()
        {
            if (previewList.SelectedItems.Count == 0)
            {
                statusLabel.Text = "请先在底部预览中选中一条素材记录。";
                return;
            }

            RenamePlan entry = previewList.SelectedItems[0].Tag as RenamePlan;
            if (entry == null || entry.Row == null)
            {
                statusLabel.Text = "选中的预览记录无效。";
                return;
            }

            List<string> files = entry.IsMain ? entry.Row.MainFiles : entry.Row.BackupFiles;
            List<string> tails = entry.IsMain ? entry.Row.MainTailOverrides : entry.Row.BackupTailOverrides;
            EnsureTailOverrideSize(entry.Row, entry.IsMain);
            if (entry.FileIndex < 0 || entry.FileIndex >= files.Count)
            {
                statusLabel.Text = "选中的素材记录已经变化，请重新选择。";
                RefreshPreview();
                return;
            }

            string removed = Path.GetFileName(files[entry.FileIndex]);
            files.RemoveAt(entry.FileIndex);
            if (entry.FileIndex >= 0 && entry.FileIndex < tails.Count)
            {
                tails.RemoveAt(entry.FileIndex);
            }
            RenderGridRow(entry.RowIndex - 1);
            RefreshPreview();
            statusLabel.Text = "已删除单条记录：" + removed;
        }

        private int GetCurrentGridRowIndex()
        {
            if (grid.CurrentCell != null && grid.CurrentCell.RowIndex >= 0 && grid.CurrentCell.RowIndex < rows.Count)
            {
                return grid.CurrentCell.RowIndex;
            }

            if (grid.SelectedCells.Count > 0)
            {
                int rowIndex = grid.SelectedCells[0].RowIndex;
                if (rowIndex >= 0 && rowIndex < rows.Count)
                {
                    return rowIndex;
                }
            }

            return -1;
        }

        private int GetNextShotSequence()
        {
            int max = 0;
            foreach (ShotRow row in rows)
            {
                if (row.Sequence > max)
                {
                    max = row.Sequence;
                }
            }

            return Math.Max(1, max + 1);
        }

        private void SelectGridCell(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            {
                return;
            }

            if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
            {
                columnIndex = GetDefaultGridFocusColumn();
            }

            if (columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
            {
                columnIndex = GetDefaultGridFocusColumn();
            }

            if (columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
            {
                for (int index = 0; index < grid.Columns.Count; index++)
                {
                    if (grid.Columns[index].Visible)
                    {
                        columnIndex = index;
                        break;
                    }
                }
            }

            if (columnIndex < 0 || columnIndex >= grid.Columns.Count || !grid.Columns[columnIndex].Visible)
            {
                return;
            }

            grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
        }

        private void AddEmptyRow()
        {
            int currentRowIndex = GetCurrentGridRowIndex();
            int insertIndex = currentRowIndex >= 0 ? currentRowIndex + 1 : rows.Count;
            rows.Insert(insertIndex, new ShotRow { Scene = GetDefaultScene(), Sequence = GetNextShotSequence() });
            RenderAll();
            SelectGridCell(insertIndex, GetDefaultGridFocusColumn());
            statusLabel.Text = IsRowSceneEnabled()
                ? "已新增一条空记录；A 列场号、B 列镜号可直接改成任意正整数。"
                : "已新增一条空记录；A 列镜号可直接改成任意正整数。";
        }

        private void MoveCurrentRow(int direction)
        {
            int currentRowIndex = GetCurrentGridRowIndex();
            if (currentRowIndex < 0)
            {
                statusLabel.Text = "请先选中要移动的行。";
                return;
            }

            int targetIndex = currentRowIndex + direction;
            if (targetIndex < 0 || targetIndex >= rows.Count)
            {
                statusLabel.Text = "当前行已经在边界位置。";
                return;
            }

            int columnIndex = grid.CurrentCell != null ? grid.CurrentCell.ColumnIndex : 0;
            ShotRow moving = rows[currentRowIndex];
            rows.RemoveAt(currentRowIndex);
            rows.Insert(targetIndex, moving);
            RenderAll();
            SelectGridCell(targetIndex, columnIndex);
            statusLabel.Text = IsRowSceneEnabled()
                ? "已移动当前行；A 列场号、B 列镜号保持不变。"
                : "已移动当前行；A 列镜号保持不变。";
        }

        private void DeleteCurrentRow()
        {
            int currentRowIndex = GetCurrentGridRowIndex();
            if (currentRowIndex < 0)
            {
                statusLabel.Text = "请先选中要删除的行。";
                return;
            }

            ShotRow row = rows[currentRowIndex];
            bool hasContent = row.MainFiles.Count > 0 || row.BackupFiles.Count > 0;
            if (hasContent)
            {
                DialogResult confirm = MessageBox.Show(
                    this,
                    "是否删除第 " + (currentRowIndex + 1) + " 行及其中所有素材记录？\r\n\r\n该操作只会从表格中移除记录，不会删除磁盘上的视频文件。",
                    "确认删除当前行",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    return;
                }
            }

            int columnIndex = grid.CurrentCell != null ? grid.CurrentCell.ColumnIndex : 0;
            rows.RemoveAt(currentRowIndex);
            if (rows.Count == 0)
            {
                rows.Add(new ShotRow { Scene = GetDefaultScene(), Sequence = 1 });
            }

            RenderAll();
            int nextRowIndex = Math.Min(currentRowIndex, rows.Count - 1);
            SelectGridCell(nextRowIndex, columnIndex);
            statusLabel.Text = IsRowSceneEnabled()
                ? "已删除当前行，下方行已上移；A 列场号、B 列镜号保持不变。"
                : "已删除当前行，下方行已上移；A 列镜号保持不变。";
        }

        private void ClearSelectedCellFiles()
        {
            if (grid.CurrentCell == null)
            {
                return;
            }

            int rowIndex = grid.CurrentCell.RowIndex;
            int columnIndex = grid.CurrentCell.ColumnIndex;
            if (rowIndex < 0 || rowIndex >= rows.Count)
            {
                return;
            }

            if (columnIndex == GridMainColumn)
            {
                rows[rowIndex].MainFiles.Clear();
                rows[rowIndex].MainTailOverrides.Clear();
            }
            else if (columnIndex == GridBackupColumn)
            {
                rows[rowIndex].BackupFiles.Clear();
                rows[rowIndex].BackupTailOverrides.Clear();
            }
            else
            {
                statusLabel.Text = "当前单元格不是素材列。";
                return;
            }

            RenderGridRow(rowIndex);
            RefreshPreview();
            grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
        }

        private void RemoveEmptyTailRows()
        {
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                ShotRow row = rows[i];
                if (row.MainFiles.Count == 0 && row.BackupFiles.Count == 0)
                {
                    rows.RemoveAt(i);
                }
                else
                {
                    break;
                }
            }

            if (rows.Count == 0)
            {
                for (int i = 1; i <= AppInfo.DefaultRowCount; i++)
                {
                    rows.Add(new ShotRow { Scene = GetDefaultScene(), Sequence = i });
                }
            }

            RenderAll();
            statusLabel.Text = IsRowSceneEnabled()
                ? "已删除尾部空白行；A 列场号、B 列镜号保持不变。"
                : "已删除尾部空白行；A 列镜号保持不变。";
        }

        private void ClearAllMaterials()
        {
            DialogResult confirm = MessageBox.Show(this, "是否清空表格中所有素材？场号和镜号会保留，进度会清零。", "确认清空", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            foreach (ShotRow row in rows)
            {
                row.MainFiles.Clear();
                row.BackupFiles.Clear();
                row.MainTailOverrides.Clear();
                row.BackupTailOverrides.Clear();
                row.ProgressPercent = 0;
            }
            RenderAll();
        }

        private static string EncodeHistoryValue(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string DecodeHistoryValue(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
        }

        private void SaveRenameHistory(List<RenameOperation> operations)
        {
            if (operations == null || operations.Count == 0)
            {
                return;
            }

            List<string> lines = new List<string>();
            lines.Add("VideoMaterialRenamerHistoryV1");
            foreach (RenameOperation op in operations)
            {
                lines.Add(string.Join("\t", new string[]
                {
                    op.RowIndex.ToString(),
                    op.IsMain ? "1" : "0",
                    op.FileIndex.ToString(),
                    EncodeHistoryValue(op.OriginalPath),
                    EncodeHistoryValue(op.RenamedPath)
                }));
            }

            File.WriteAllLines(historyPath, lines.ToArray(), Encoding.UTF8);
        }

        private List<RenameOperation> LoadRenameHistory()
        {
            List<RenameOperation> operations = new List<RenameOperation>();
            if (!File.Exists(historyPath))
            {
                return operations;
            }

            string[] lines = File.ReadAllLines(historyPath, Encoding.UTF8);
            if (lines.Length == 0 || lines[0] != "VideoMaterialRenamerHistoryV1")
            {
                return operations;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('\t');
                if (parts.Length != 5)
                {
                    continue;
                }

                int rowIndex;
                int fileIndex;
                if (!int.TryParse(parts[0], out rowIndex) || !int.TryParse(parts[2], out fileIndex))
                {
                    continue;
                }

                operations.Add(new RenameOperation
                {
                    Row = rowIndex >= 1 && rowIndex <= rows.Count ? rows[rowIndex - 1] : null,
                    RowIndex = rowIndex,
                    IsMain = parts[1] == "1",
                    FileIndex = fileIndex,
                    OriginalPath = DecodeHistoryValue(parts[3]),
                    RenamedPath = DecodeHistoryValue(parts[4])
                });
            }

            return operations;
        }

        private void RestoreLastRename()
        {
            bool fromMemory = undoStack.Count > 0;
            List<RenameOperation> operations = fromMemory ? undoStack.Peek() : LoadRenameHistory();
            if (operations.Count == 0)
            {
                MessageBox.Show(this, "没有可还原的重命名记录。", "无法还原", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                this,
                "即将把上次成功重命名的 " + operations.Count + " 个文件还原为原文件名，是否继续？",
                "确认还原",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            List<string> failures = new List<string>();
            for (int i = operations.Count - 1; i >= 0; i--)
            {
                RenameOperation op = operations[i];
                try
                {
                    if (!File.Exists(op.RenamedPath))
                    {
                        failures.Add(Path.GetFileName(op.RenamedPath) + ": 当前文件不存在");
                        continue;
                    }

                    if (File.Exists(op.OriginalPath) && !StringComparer.OrdinalIgnoreCase.Equals(op.OriginalPath, op.RenamedPath))
                    {
                        failures.Add(Path.GetFileName(op.RenamedPath) + ": 原文件名已被占用");
                        continue;
                    }

                    if (!StringComparer.OrdinalIgnoreCase.Equals(op.RenamedPath, op.OriginalPath))
                    {
                        File.Move(op.RenamedPath, op.OriginalPath);
                    }

                    if (op.Row != null)
                    {
                        List<string> files = op.IsMain ? op.Row.MainFiles : op.Row.BackupFiles;
                        if (op.FileIndex >= 0 && op.FileIndex < files.Count && StringComparer.OrdinalIgnoreCase.Equals(files[op.FileIndex], op.RenamedPath))
                        {
                            files[op.FileIndex] = op.OriginalPath;
                        }
                        else
                        {
                            int currentIndex = files.FindIndex(p => StringComparer.OrdinalIgnoreCase.Equals(p, op.RenamedPath));
                            if (currentIndex >= 0)
                            {
                                files[currentIndex] = op.OriginalPath;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(Path.GetFileName(op.RenamedPath) + ": " + ex.Message);
                }
            }

            if (failures.Count == 0)
            {
                if (fromMemory)
                {
                    undoStack.Pop();
                }
                if (File.Exists(historyPath))
                {
                    File.Delete(historyPath);
                }
                RenderAll();
                MessageBox.Show(this, "已取消上次命名。", "取消命名完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            RenderAll();
            MessageBox.Show(this, string.Join("\r\n", failures.Take(8).ToArray()), "部分文件还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

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

        private static string FindFfmpegPath()
        {
            List<string> candidates = new List<string>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentDir = Environment.CurrentDirectory;
            candidates.Add(Path.Combine(baseDir, "ffmpeg.exe"));
            candidates.Add(Path.Combine(baseDir, "tools", "ffmpeg.exe"));
            candidates.Add(Path.Combine(currentDir, "ffmpeg.exe"));
            candidates.Add(Path.Combine(currentDir, "tools", "ffmpeg.exe"));

            string pathText = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string directory in pathText.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    candidates.Add(Path.Combine(directory.Trim(), "ffmpeg.exe"));
                }
                catch
                {
                }
            }

            string embeddedFfmpeg = ExtractEmbeddedFfmpeg();
            if (!string.IsNullOrWhiteSpace(embeddedFfmpeg))
            {
                candidates.Add(embeddedFfmpeg);
            }

            foreach (string candidate in candidates)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private static string ExtractEmbeddedFfmpeg()
        {
            const string resourceName = "VideoMaterialRenamer.ffmpeg.exe";
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream resource = assembly.GetManifestResourceStream(resourceName))
                {
                    if (resource == null)
                    {
                        return "";
                    }

                    string toolsDir = Path.Combine(AppInfo.AppDataDirectory, "tools");
                    Directory.CreateDirectory(toolsDir);
                    string ffmpegPath = Path.Combine(toolsDir, "ffmpeg.exe");
                    long resourceLength = resource.CanSeek ? resource.Length : -1;
                    if (File.Exists(ffmpegPath) && resourceLength > 0)
                    {
                        FileInfo existing = new FileInfo(ffmpegPath);
                        if (existing.Length == resourceLength)
                        {
                            return ffmpegPath;
                        }
                    }

                    string tempPath = ffmpegPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (FileStream output = File.Create(tempPath))
                    {
                        resource.CopyTo(output);
                    }

                    if (File.Exists(ffmpegPath))
                    {
                        File.Delete(ffmpegPath);
                    }
                    File.Move(tempPath, ffmpegPath);
                    return ffmpegPath;
                }
            }
            catch
            {
                return "";
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static string TrimProcessLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "未知错误";
            }

            text = text.Trim();
            if (text.Length <= 500)
            {
                return text;
            }

            return text.Substring(text.Length - 500);
        }

        private static double ParseClockSeconds(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return -1;
            }

            Match match = Regex.Match(value.Trim(), @"(?<h>\d+):(?<m>\d+):(?<s>\d+(?:\.\d+)?)");
            if (!match.Success)
            {
                return -1;
            }

            double seconds;
            if (!double.TryParse(match.Groups["s"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds))
            {
                return -1;
            }

            return int.Parse(match.Groups["h"].Value) * 3600 + int.Parse(match.Groups["m"].Value) * 60 + seconds;
        }

        private static double ParseProgressSeconds(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return -1;
            }

            string[] parts = line.Split(new char[] { '=' }, 2);
            if (parts.Length != 2)
            {
                return -1;
            }

            string key = parts[0].Trim();
            string value = parts[1].Trim();
            if (key == "out_time" || key == "out_time_str")
            {
                return ParseClockSeconds(value);
            }

            if (key == "out_time_ms" || key == "out_time_us")
            {
                double raw;
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out raw))
                {
                    return raw / 1000000.0;
                }
            }

            return -1;
        }

        private static double ParseDurationSeconds(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return -1;
            }

            Match match = Regex.Match(line, @"Duration:\s*(?<time>\d+:\d+:\d+(?:\.\d+)?)");
            if (!match.Success)
            {
                return -1;
            }

            return ParseClockSeconds(match.Groups["time"].Value);
        }

        private static RenamePlan CloneRenamePlan(RenamePlan entry)
        {
            if (entry == null)
            {
                return null;
            }

            return new RenamePlan
            {
                Row = entry.Row,
                RowIndex = entry.RowIndex,
                ColumnName = entry.ColumnName,
                IsMain = entry.IsMain,
                FileIndex = entry.FileIndex,
                Scene = entry.Scene,
                Shot = entry.Shot,
                Take = entry.Take,
                TailSegment = entry.TailSegment,
                CustomTailText = entry.CustomTailText,
                HasCustomTail = entry.HasCustomTail,
                OldPath = entry.OldPath,
                TargetPath = entry.TargetPath,
                OldName = entry.OldName,
                NewName = entry.NewName,
                Status = entry.Status
            };
        }

        private static List<RenamePlan> PrepareExportPlan(List<RenamePlan> sourcePlan, ExportOutputMode outputMode)
        {
            List<RenamePlan> prepared = new List<RenamePlan>();
            Dictionary<string, bool> targets = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (RenamePlan source in sourcePlan)
            {
                RenamePlan entry = CloneRenamePlan(source);
                if (entry == null)
                {
                    continue;
                }

                if (outputMode == ExportOutputMode.SaveAsNewFile && StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
                {
                    entry.TargetPath = GetUniquePathWithSuffix(entry.TargetPath, "_1080p");
                    entry.NewName = Path.GetFileName(entry.TargetPath);
                    entry.Status = "另存为新文件";
                }

                if (outputMode == ExportOutputMode.SaveAsNewFile &&
                    File.Exists(entry.TargetPath) &&
                    !StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
                {
                    throw new IOException("目标文件已存在：" + entry.NewName);
                }

                if (targets.ContainsKey(entry.TargetPath))
                {
                    throw new IOException("新文件名重复：" + entry.NewName);
                }

                targets[entry.TargetPath] = true;
                prepared.Add(entry);
            }

            return prepared;
        }

        private static string NormalizeWatermarkText(string text)
        {
            string value = Path.GetFileName(text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsControl(chars[i]))
                {
                    chars[i] = ' ';
                }
            }

            return new string(chars).Trim();
        }

        private static string EscapeFfmpegFilterValue(string value)
        {
            if (value == null)
            {
                return "";
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace(":", "\\:")
                .Replace(",", "\\,")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Replace(";", "\\;");
        }

        private static string GetWatermarkFontFile()
        {
            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] candidates = new string[]
            {
                Path.Combine(windowsDir, "Fonts", "msyh.ttc"),
                Path.Combine(windowsDir, "Fonts", "msyhbd.ttc"),
                Path.Combine(windowsDir, "Fonts", "simhei.ttf"),
                Path.Combine(windowsDir, "Fonts", "arial.ttf")
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        return candidate.Replace('\\', '/');
                    }
                }
                catch
                {
                }
            }

            return "";
        }

        private static string BuildVideoFilter(string watermarkText)
        {
            string baseFilter = "scale=1080:1920:flags=bicubic,setsar=1";
            string normalized = NormalizeWatermarkText(watermarkText);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return baseFilter;
            }

            string fontFile = GetWatermarkFontFile();
            string fontPart = string.IsNullOrWhiteSpace(fontFile) ? "" : "fontfile='" + EscapeFfmpegFilterValue(fontFile) + "':";
            string text = EscapeFfmpegFilterValue(normalized);
            return baseFilter +
                ",drawtext=" + fontPart +
                "text='" + text + "':" +
                "x=24:y=24:fontsize=24:" +
                "fontcolor=white@0.92:" +
                "box=1:boxcolor=black@0.55:boxborderw=10:" +
                "expansion=none";
        }

        private static string BuildFfmpegArguments(string inputPath, string outputPath, bool copyAudio, string watermarkText)
        {
            List<string> args = new List<string>();
            args.Add("-hide_banner");
            args.Add("-nostdin");
            args.Add("-nostats");
            args.Add("-y");
            args.Add("-i");
            args.Add(QuoteArgument(inputPath));
            args.Add("-vf");
            args.Add(QuoteArgument(BuildVideoFilter(watermarkText)));
            args.Add("-c:v");
            args.Add("libx264");
            args.Add("-preset");
            args.Add("veryfast");
            args.Add("-crf");
            args.Add("20");
            args.Add("-pix_fmt");
            args.Add("yuv420p");
            args.Add("-threads");
            args.Add("0");
            args.Add("-progress");
            args.Add("pipe:1");

            if (copyAudio)
            {
                args.Add("-c:a");
                args.Add("copy");
            }
            else
            {
                args.Add("-c:a");
                args.Add("aac");
                args.Add("-b:a");
                args.Add("160k");
            }

            args.Add(QuoteArgument(outputPath));
            return string.Join(" ", args.ToArray());
        }

        private static void RunFfmpegExport(string ffmpegPath, string inputPath, string outputPath, bool copyAudio, string watermarkText, Action<int> progressCallback)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = ffmpegPath;
            startInfo.Arguments = BuildFfmpegArguments(inputPath, outputPath, copyAudio, watermarkText);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;

            using (Process process = Process.Start(startInfo))
            {
                if (progressCallback != null)
                {
                    progressCallback(5);
                }

                StringBuilder error = new StringBuilder();
                double durationSeconds = -1;
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        error.AppendLine(e.Data);
                        double parsedDuration = ParseDurationSeconds(e.Data);
                        if (parsedDuration > 0)
                        {
                            durationSeconds = parsedDuration;
                        }
                    }
                };
                process.BeginErrorReadLine();

                string line;
                bool sawProgress = false;
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    if (progressCallback != null)
                    {
                        if (!sawProgress && (line.StartsWith("out_time=", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("out_time_us=", StringComparison.OrdinalIgnoreCase)))
                        {
                            sawProgress = true;
                        }

                        double outputSeconds = ParseProgressSeconds(line);
                        if (outputSeconds >= 0)
                        {
                            if (durationSeconds > 0)
                            {
                                int percent = 5 + (int)Math.Round(Math.Min(1.0, outputSeconds / durationSeconds) * 90.0);
                                progressCallback(Math.Max(5, Math.Min(95, percent)));
                            }
                            else
                            {
                                progressCallback(sawProgress ? 50 : 15);
                            }
                        }
                        else if (line.Trim() == "progress=end")
                        {
                            progressCallback(95);
                        }
                    }
                }

                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new Exception(TrimProcessLog(error.ToString()));
                }
            }
        }

        private static void ReplaceOriginalWithExport(string tempPath, RenamePlan entry)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
            {
                File.Replace(tempPath, entry.OldPath, null);
                return;
            }

            if (File.Exists(entry.TargetPath))
            {
                throw new IOException("目标文件已存在。");
            }

            File.Move(tempPath, entry.TargetPath);
            try
            {
                File.Delete(entry.OldPath);
            }
            catch (Exception ex)
            {
                throw new IOException("已生成新文件，但原文件删除失败：" + ex.Message);
            }
        }

        private static void ExportOneVideoTo1080p(string ffmpegPath, RenamePlan entry, ExportOutputMode outputMode, bool watermarkEnabled, Action<int> progressCallback)
        {
            if (entry == null)
            {
                throw new InvalidOperationException("导出记录无效。");
            }

            if (!File.Exists(entry.OldPath))
            {
                throw new FileNotFoundException("源文件不存在。", entry.OldPath);
            }

            if (outputMode == ExportOutputMode.SaveAsNewFile &&
                File.Exists(entry.TargetPath) &&
                !StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
            {
                throw new IOException("目标文件已存在。");
            }

            string directory = outputMode == ExportOutputMode.OverwriteOriginal ? Path.GetDirectoryName(entry.OldPath) : Path.GetDirectoryName(entry.TargetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string outputPath = entry.TargetPath;
            string tempPath = "";
            string watermarkText = watermarkEnabled ? entry.NewName : "";
            if (outputMode == ExportOutputMode.OverwriteOriginal)
            {
                tempPath = Path.Combine(directory, ".vmr_" + Guid.NewGuid().ToString("N") + Path.GetExtension(entry.TargetPath));
                outputPath = tempPath;
            }

            try
            {
                try
                {
                    RunFfmpegExport(ffmpegPath, entry.OldPath, outputPath, true, watermarkText, progressCallback);
                }
                catch
                {
                    if (File.Exists(outputPath))
                    {
                        File.Delete(outputPath);
                    }
                    if (progressCallback != null)
                    {
                        progressCallback(0);
                    }
                    RunFfmpegExport(ffmpegPath, entry.OldPath, outputPath, false, watermarkText, progressCallback);
                }

                if (outputMode == ExportOutputMode.OverwriteOriginal)
                {
                    ReplaceOriginalWithExport(tempPath, entry);
                    tempPath = "";
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void StartExport1080p(List<RenamePlan> plan, string ffmpegPath, ExportOutputMode outputMode, bool watermarkEnabled)
        {
            SetProgressColumnVisible(true);
            ResetProgressBars();
            SetOperationUiEnabled(false);
            statusLabel.Text = outputMode == ExportOutputMode.OverwriteOriginal
                ? (watermarkEnabled ? "正在导出并添加文件名水印，请等待..." : "正在导出并覆盖原文件，请等待...")
                : (watermarkEnabled ? "正在导出 1080x1920 新文件并添加文件名水印，请等待..." : "正在导出 1080x1920 新文件，请等待...");

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> failures = new List<string>();
                List<RenameOperation> successfulOperations = new List<RenameOperation>();
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
                    index++;
                    int currentIndex = index;
                    QueueOnUi(delegate
                    {
                        statusLabel.Text = string.Format("正在导出 {0}/{1}：{2}", currentIndex, total, entry.NewName);
                    });

                    try
                    {
                        Action<int> progressCallback = delegate(int percent)
                        {
                            if (entry.Row == null)
                            {
                                return;
                            }

                            int completed = rowCompleted.ContainsKey(entry.Row) ? rowCompleted[entry.Row] : 0;
                            int rowTotal = rowTotals.ContainsKey(entry.Row) ? Math.Max(1, rowTotals[entry.Row]) : 1;
                            int safePercent = Math.Max(0, Math.Min(100, percent));
                            int rowPercent = (int)Math.Max(0, Math.Min(100, Math.Round((completed + safePercent / 100.0) * 100.0 / rowTotal)));
                            QueueOnUi(delegate
                            {
                                entry.Row.ProgressPercent = rowPercent;
                                RenderGridProgress(entry.RowIndex - 1);
                                statusLabel.Text = string.Format("正在导出 {0}/{1}：{2}（{3}%）", currentIndex, total, entry.NewName, safePercent);
                            });
                        };

                        ExportOneVideoTo1080p(ffmpegPath, entry, outputMode, watermarkEnabled, progressCallback);
                        if (entry.Row != null && rowCompleted.ContainsKey(entry.Row))
                        {
                            rowCompleted[entry.Row]++;
                            progressCallback(100);
                        }
                        successfulOperations.Add(new RenameOperation
                        {
                            Row = entry.Row,
                            RowIndex = entry.RowIndex,
                            IsMain = entry.IsMain,
                            FileIndex = entry.FileIndex,
                            OriginalPath = entry.OldPath,
                            RenamedPath = entry.TargetPath
                        });
                    }
                    catch (Exception ex)
                    {
                        failures.Add(entry.OldName + ": " + ex.Message);
                        if (entry.Row != null && rowCompleted.ContainsKey(entry.Row))
                        {
                            rowCompleted[entry.Row]++;
                        }
                    }
                }

                QueueOnUi(delegate
                {
                    foreach (RenameOperation op in successfulOperations)
                    {
                        if (op.Row == null)
                        {
                            continue;
                        }

                        List<string> files = op.IsMain ? op.Row.MainFiles : op.Row.BackupFiles;
                        if (op.FileIndex >= 0 && op.FileIndex < files.Count && StringComparer.OrdinalIgnoreCase.Equals(files[op.FileIndex], op.OriginalPath))
                        {
                            files[op.FileIndex] = op.RenamedPath;
                        }
                        else
                        {
                            int currentIndex = files.FindIndex(p => StringComparer.OrdinalIgnoreCase.Equals(p, op.OriginalPath));
                            if (currentIndex >= 0)
                            {
                                files[currentIndex] = op.RenamedPath;
                            }
                        }
                    }

                    RenderAll();
                    SetProgressColumnVisible(false);
                    SetOperationUiEnabled(true);

                    if (failures.Count > 0)
                    {
                        MessageBox.Show(this, string.Join("\r\n", failures.Take(8).ToArray()), "部分视频导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        string finishMessage = outputMode == ExportOutputMode.OverwriteOriginal
                            ? "已处理 " + successfulOperations.Count + " 个视频，并覆盖原文件。"
                            : "已导出 " + successfulOperations.Count + " 个 1080x1920 新文件，原始素材已保留。";
                        MessageBox.Show(this, finishMessage, "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                });
            });
        }

        private bool TryChooseExportOutputMode(out ExportOutputMode outputMode)
        {
            outputMode = ExportOutputMode.OverwriteOriginal;
            DialogResult result = MessageBox.Show(
                this,
                "请选择 1080x1920 导出后的保存方式：\r\n\r\n是：覆盖原文件（默认，原文件会被替换）\r\n否：另存为新文件（保留原文件）\r\n取消：不执行",
                "选择导出保存方式",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (result == DialogResult.Cancel)
            {
                return false;
            }

            outputMode = result == DialogResult.Yes ? ExportOutputMode.OverwriteOriginal : ExportOutputMode.SaveAsNewFile;
            return true;
        }

        private void RenameFiles()
        {
            if (operationRunning)
            {
                statusLabel.Text = "当前正在处理视频，请等待完成。";
                return;
            }

            RefreshPreview();
            if (currentPlan.Count == 0)
            {
                MessageBox.Show(this, "请先把视频拖到 " + GetMainColumnLetter() + " 或 " + GetBackupColumnLetter() + " 列。", "没有素材", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<RenamePlan> badRows = currentPlan.Where(IsBlockingIssue).ToList();
            if (badRows.Count > 0)
            {
                MessageBox.Show(this, BuildIssueMessage(badRows), "存在文件问题", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<string> preview = currentPlan.Take(8).Select(p => p.OldName + "  ->  " + p.NewName).ToList();
            if (currentPlan.Count > 8)
            {
                preview.Add("... 另有 " + (currentPlan.Count - 8) + " 个文件");
            }

            bool export1080p = IsExport1080pEnabled();
            if (export1080p)
            {
                ExportOutputMode outputMode;
                if (!TryChooseExportOutputMode(out outputMode))
                {
                    return;
                }

                string ffmpegPath = FindFfmpegPath();
                if (string.IsNullOrWhiteSpace(ffmpegPath))
                {
                    MessageBox.Show(
                        this,
                        "没有找到 ffmpeg.exe，无法导出 1080x1920 视频。\r\n\r\n请把 ffmpeg.exe 放到软件 EXE 同目录，或放到软件目录下的 tools 文件夹中，然后重新运行。",
                        "缺少 FFmpeg",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                List<RenamePlan> exportPlan;
                try
                {
                    exportPlan = PrepareExportPlan(currentPlan.ToList(), outputMode);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "导出目标异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                List<string> exportPreview = exportPlan.Take(8).Select(p => p.OldName + "  ->  " + p.NewName).ToList();
                if (exportPlan.Count > 8)
                {
                    exportPreview.Add("... 另有 " + (exportPlan.Count - 8) + " 个文件");
                }

                string modeText = outputMode == ExportOutputMode.OverwriteOriginal
                    ? "即将导出 1080x1920 并覆盖原文件。该操作不会保留原始 720p 文件，是否继续？"
                    : "即将导出 1080x1920 新视频文件，原始素材会保留。是否继续？";
                bool watermarkEnabled = IsExportWatermarkEnabled();
                if (watermarkEnabled)
                {
                    modeText += "\r\n\r\n导出画面左上角会加入新文件名水印。";
                }
                string exportMessage = modeText + "\r\n\r\n" + string.Join("\r\n", exportPreview.ToArray());
                DialogResult exportConfirm = MessageBox.Show(this, exportMessage, "确认导出1080p", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (exportConfirm != DialogResult.Yes)
                {
                    return;
                }

                StartExport1080p(exportPlan, ffmpegPath, outputMode, watermarkEnabled);
                return;
            }

            string message = "即将直接修改原视频文件名，是否继续？\r\n\r\n" + string.Join("\r\n", preview.ToArray());
            DialogResult confirm = MessageBox.Show(this, message, "确认重命名", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            List<string> failures = new List<string>();
            List<RenameOperation> successfulOperations = new List<RenameOperation>();
            foreach (RenamePlan entry in currentPlan)
            {
                try
                {
                    string originalPath = entry.OldPath;
                    string renamedPath = entry.TargetPath;
                    if (!StringComparer.OrdinalIgnoreCase.Equals(entry.OldPath, entry.TargetPath))
                    {
                        File.Move(entry.OldPath, entry.TargetPath);
                    }

                    if (entry.IsMain)
                    {
                        entry.Row.MainFiles[entry.FileIndex] = entry.TargetPath;
                    }
                    else
                    {
                        entry.Row.BackupFiles[entry.FileIndex] = entry.TargetPath;
                    }

                    if (!StringComparer.OrdinalIgnoreCase.Equals(originalPath, renamedPath))
                    {
                        successfulOperations.Add(new RenameOperation
                        {
                            Row = entry.Row,
                            RowIndex = entry.RowIndex,
                            IsMain = entry.IsMain,
                            FileIndex = entry.FileIndex,
                            OriginalPath = originalPath,
                            RenamedPath = renamedPath
                        });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(entry.OldName + ": " + ex.Message);
                }
            }

            if (successfulOperations.Count > 0)
            {
                undoStack.Push(successfulOperations);
                SaveRenameHistory(successfulOperations);
            }

            RenderAll();

            if (failures.Count > 0)
            {
                MessageBox.Show(this, string.Join("\r\n", failures.Take(8).ToArray()), "部分文件重命名失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                MessageBox.Show(this, "已处理 " + currentPlan.Count + " 个视频文件。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (previewColumnResizeTimer != null)
            {
                previewColumnResizeTimer.Stop();
                previewColumnResizeTimer.Dispose();
                previewColumnResizeTimer = null;
            }

            if (ownedDetailImage != null)
            {
                ownedDetailImage.Dispose();
                ownedDetailImage = null;
            }

            foreach (Image image in thumbnailCache.Values)
            {
                if (image != null)
                {
                    image.Dispose();
                }
            }
            thumbnailCache.Clear();
            thumbnailCacheOrder.Clear();
            thumbnailCacheNodes.Clear();

            base.OnFormClosed(e);
        }
    }
}
