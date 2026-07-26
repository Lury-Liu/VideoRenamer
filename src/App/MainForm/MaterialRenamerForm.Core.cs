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
        private readonly Dictionary<string, List<ListViewItem>> previewItemsByPath = new Dictionary<string, List<ListViewItem>>(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<List<RenameOperation>> undoStack = new Stack<List<RenameOperation>>();
        private readonly Dictionary<string, VideoFileInfo> videoInfoCache = new Dictionary<string, VideoFileInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly ThumbnailCache thumbnailCache;
        private readonly MediaLoadScheduler mediaScheduler = new MediaLoadScheduler();
        private readonly ExportController exportController;
        private readonly RenameController renameController;
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
        private Font previewGroupFont;
        private List<Image> frameStrip = new List<Image>();
        private string frameStripPath = "";
        private int frameStripVersion;
        private int dragHighlightRow = -1;
        private int dragHighlightColumn = -1;
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
            // 保留守卫：正在详情面板展示的缓存图像被 LRU 淘汰时不 Dispose
            //（修复淘汰后重绘触发 GDI+ 异常的既有窗口）。
            thumbnailCache = new ThumbnailCache(ThumbnailCacheLimit, delegate
            {
                return thumbnailBox == null ? null : thumbnailBox.Image;
            });
            exportController = new ExportController(this, this);
            renameController = new RenameController(this, this);
            darkMode = UiTheme.DetectWindowsDarkMode();
            for (int i = 1; i <= AppInfo.DefaultRowCount; i++)
            {
                rows.Add(new ShotRow { Scene = 1, Sequence = i });
            }

            BuildUi();
            ApplyTheme();
            RenderAll();
        }

        // 冻结契约：返回 "SelfTest OK"（加载器回显、打包/发布脚本据此放行），失败抛异常。
        // 原 155 行单体自检已拆分为 src/Tests/ 下的具名用例（全部执行，不再首错即停）。
        public static string RunSelfTest()
        {
            return Tests.TestRunner.RunAll();
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

        // 新行为（阶段5b）：任务进行中关窗需确认；确认后立即杀掉活动
        // ffmpeg 进程（当前 .vmr_ 临时文件由导出服务的 finally 清理），
        // 不再把 100% CPU 的 ffmpeg 子进程孤儿化。
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (operationRunning && e.CloseReason == CloseReason.UserClosing)
            {
                DialogResult confirm = MessageBox.Show(
                    this,
                    "任务仍在进行中，关闭窗口将中止导出并清理临时文件。确定要退出吗？",
                    "确认退出",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes)
                {
                    e.Cancel = true;
                }
                else
                {
                    exportController.CancelActive();
                }
            }

            base.OnFormClosing(e);
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

            ClearFrameStrip();
            if (previewGroupFont != null)
            {
                previewGroupFont.Dispose();
                previewGroupFont = null;
            }

            mediaScheduler.Dispose();
            thumbnailCache.Dispose();

            base.OnFormClosed(e);
        }
    }
}
