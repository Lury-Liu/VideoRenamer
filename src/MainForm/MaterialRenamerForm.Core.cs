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
        private List<Image> frameStrip = new List<Image>();
        private string frameStripPath = "";
        private int frameStripVersion;
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

            List<RenamePlan> plan = RenamePlanBuilder.BuildPlan(new List<ShotRow> { row }, 5, 1, true, false);
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
            List<RenamePlan> customPlan = RenamePlanBuilder.BuildPlan(new List<ShotRow> { customRow }, 5, 1, true, false);
            if (customPlan.Count != 1 || customPlan[0].NewName != "E5-S1-17-T1.mp4")
            {
                throw new Exception("自定义镜号测试失败：" + (customPlan.Count == 0 ? "无预览" : customPlan[0].NewName));
            }

            ShotRow customSceneRow = new ShotRow { Scene = 3, Sequence = 7 };
            customSceneRow.MainFiles.Add(@"C:\Temp\custom_scene.mp4");
            List<RenamePlan> customScenePlan = RenamePlanBuilder.BuildPlan(new List<ShotRow> { customSceneRow }, 5, 1, true, false, true);
            if (customScenePlan.Count != 1 || customScenePlan[0].NewName != "E5-S3-7-T1.mp4" || customScenePlan[0].Scene != 3)
            {
                throw new Exception("自定义场号测试失败：" + (customScenePlan.Count == 0 ? "无预览" : customScenePlan[0].NewName));
            }

            List<RenamePlan> defaultScenePlan = RenamePlanBuilder.BuildPlan(new List<ShotRow> { customSceneRow }, 5, 1, true, false, false);
            if (defaultScenePlan.Count != 1 || defaultScenePlan[0].NewName != "E5-S1-7-T1.mp4")
            {
                throw new Exception("默认场号测试失败：" + (defaultScenePlan.Count == 0 ? "无预览" : defaultScenePlan[0].NewName));
            }

            ShotRow customTailRow = new ShotRow { Sequence = 1 };
            customTailRow.MainFiles.Add(@"C:\Temp\custom_tail.mp4");
            customTailRow.MainTailOverrides.Add("补+文字");
            List<RenamePlan> customTailPlan = RenamePlanBuilder.BuildPlan(new List<ShotRow> { customTailRow }, 5, 1, true, false);
            if (customTailPlan.Count != 1 || customTailPlan[0].NewName != "E5-S1-1-补+文字.mp4" || customTailPlan[0].TailSegment != "补+文字")
            {
                throw new Exception("自定义末尾编号测试失败：" + (customTailPlan.Count == 0 ? "无预览" : customTailPlan[0].NewName));
            }

            ShotRow duplicateTailRow = new ShotRow { Sequence = 5 };
            duplicateTailRow.MainFiles.Add(@"C:\Temp\dup1.mp4");
            duplicateTailRow.MainFiles.Add(@"C:\Temp\dup2.mp4");
            duplicateTailRow.MainTailOverrides.Add("补手机");
            duplicateTailRow.MainTailOverrides.Add("");
            List<RenamePlan> duplicateTailPlan = RenamePlanBuilder.BuildPlan(new List<ShotRow> { duplicateTailRow }, 5, 6, true, false);
            string uniqueTail = RenamePlanBuilder.GetUniqueCustomTail(duplicateTailPlan[1], "补手机", duplicateTailPlan, 5, 6, true);
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
                List<RenamePlan> exportPlan = RenamePlanBuilder.BuildPlan(new List<ShotRow> { exportRow }, 5, 1, true, true);
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

            string stripArgs = VideoFrameStripProvider.BuildStripArguments(@"C:\Temp\in.mp4", @"C:\Temp\out\f_%03d.png", 12);
            if (!stripArgs.Contains("-i") || !stripArgs.Contains("-frames:v") || !stripArgs.Contains("12") || !stripArgs.Contains("thumbnail"))
            {
                throw new Exception("抽帧参数测试失败：" + stripArgs);
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
            ClearFrameStrip();

            thumbnailCache.Clear();
            thumbnailCacheOrder.Clear();
            thumbnailCacheNodes.Clear();

            base.OnFormClosed(e);
        }
    }
}
