using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VideoRenamer
{
    // 内嵌 HTML5 video 的播放器控件：通过 WebBrowser 渲染 video 标签。
    // 使用浏览器内置的视频播放能力，零外部依赖，格式支持取决于 IE11/Edge。
    // 进度用 200ms 轮询刷新；拖动进度条期间暂停轮询写回，避免打架。
    public sealed class VideoPlayerControl : UserControl
    {
        private readonly WebBrowser browser;
        private readonly Panel controlBar;
        private readonly Button btnPlayPause;
        private readonly TrackBar progressBar;
        private readonly Label timeLabel;
        private readonly System.Windows.Forms.Timer progressTimer;
        private bool seeking;
        private bool darkMode;
        private bool browserReady;

        public VideoPlayerControl()
        {
            browser = new WebBrowser();
            browser.Dock = DockStyle.Fill;
            browser.ScrollBarsEnabled = false;
            browser.IsWebBrowserContextMenuEnabled = false;
            browser.WebBrowserShortcutsEnabled = false;
            browser.AllowWebBrowserDrop = false;
            browser.ScriptErrorsSuppressed = true;
            browser.DocumentCompleted += OnBrowserReady;

            controlBar = new Panel();
            controlBar.Dock = DockStyle.Bottom;
            controlBar.Height = 42;
            controlBar.Padding = new Padding(4, 4, 6, 4);

            btnPlayPause = new Button();
            btnPlayPause.Text = "播放";
            btnPlayPause.Width = 52;
            btnPlayPause.Height = 30;
            btnPlayPause.Dock = DockStyle.Left;
            btnPlayPause.Click += OnPlayPauseClick;

            timeLabel = new Label();
            timeLabel.Dock = DockStyle.Right;
            timeLabel.Width = 116;
            timeLabel.TextAlign = ContentAlignment.MiddleRight;
            timeLabel.Text = "00:00 / 00:00";

            progressBar = new TrackBar();
            progressBar.Dock = DockStyle.Fill;
            progressBar.Minimum = 0;
            progressBar.Maximum = 1000;
            progressBar.Value = 0;
            progressBar.TickStyle = TickStyle.None;
            progressBar.AutoSize = false;
            progressBar.Height = 34;
            progressBar.Margin = new Padding(6, 0, 6, 0);
            progressBar.Scroll += OnProgressScroll;
            progressBar.MouseDown += delegate { seeking = true; };
            progressBar.MouseUp += delegate { seeking = false; };

            controlBar.Controls.Add(progressBar);
            controlBar.Controls.Add(timeLabel);
            controlBar.Controls.Add(btnPlayPause);

            Controls.Add(browser);
            Controls.Add(controlBar);

            progressTimer = new System.Windows.Forms.Timer();
            progressTimer.Interval = 200;
            progressTimer.Tick += OnProgressTimerTick;

            // 初始化空白页面
            browser.DocumentText = GetEmptyPageHtml();
        }

        public void ApplyTheme(bool isDarkMode)
        {
            darkMode = isDarkMode;
            UiTheme.ApplyControl(btnPlayPause, darkMode);
            UiTheme.ApplyControl(timeLabel, darkMode);
            UiTheme.ApplyControl(controlBar, darkMode);
        }

        public void LoadVideo(string path)
        {
            AppLog.Write("player", "LoadVideo: " + (path ?? "(null)"));

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AppLog.Write("player", "LoadVideo 跳过: 文件不存在");
                StopPlayback();
                return;
            }

            try
            {
                string html = GetVideoPageHtml(path);
                browser.DocumentText = html;
                browserReady = false;
                ResetUiToStart();
                AppLog.Write("player", "HTML5 video 页面已加载");
            }
            catch (Exception ex)
            {
                AppLog.Write("player", "LoadVideo 失败", ex);
                StopPlayback();
            }
        }

        public void StopPlayback()
        {
            progressTimer.Stop();
            browserReady = false;
            try
            {
                RunScript("if(window.videoEl) window.videoEl.pause();");
            }
            catch
            {
            }
            ResetUiToStart();
        }

        public bool IsAvailable
        {
            get { return true; }
        }

        private void OnBrowserReady(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            browserReady = true;
            progressTimer.Start();
            AppLog.Write("player", "浏览器文档加载完成");
        }

        private void ResetUiToStart()
        {
            seeking = false;
            progressBar.Value = 0;
            btnPlayPause.Text = "播放";
            timeLabel.Text = "00:00 / 00:00";
        }

        private void OnPlayPauseClick(object sender, EventArgs e)
        {
            if (!browserReady)
            {
                AppLog.Write("player", "OnPlayPauseClick: 浏览器未就绪");
                return;
            }

            try
            {
                bool isPaused = GetScriptResult("window.videoEl ? window.videoEl.paused : true") == "true";
                AppLog.Write("player", "OnPlayPauseClick: 当前状态 " + (isPaused ? "暂停" : "播放"));

                if (isPaused)
                {
                    RunScript("if(window.videoEl) window.videoEl.play();");
                    btnPlayPause.Text = "暂停";
                    AppLog.Write("player", "已开始播放");
                }
                else
                {
                    RunScript("if(window.videoEl) window.videoEl.pause();");
                    btnPlayPause.Text = "播放";
                    AppLog.Write("player", "已暂停");
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("player", "OnPlayPauseClick 失败", ex);
            }
        }

        private void OnProgressScroll(object sender, EventArgs e)
        {
            if (!browserReady)
            {
                return;
            }

            try
            {
                double position = progressBar.Value / 1000.0;
                RunScript(string.Format("if(window.videoEl) window.videoEl.currentTime = window.videoEl.duration * {0};", position.ToString("0.000")));
            }
            catch
            {
            }
        }

        private void OnProgressTimerTick(object sender, EventArgs e)
        {
            if (!browserReady || seeking)
            {
                return;
            }

            try
            {
                string durationStr = GetScriptResult("window.videoEl ? window.videoEl.duration : 0");
                string currentStr = GetScriptResult("window.videoEl ? window.videoEl.currentTime : 0");
                string pausedStr = GetScriptResult("window.videoEl ? window.videoEl.paused : true");

                double duration = ParseDouble(durationStr);
                double current = ParseDouble(currentStr);
                bool paused = pausedStr == "true";

                if (duration > 0)
                {
                    int value = (int)Math.Round(current * 1000.0 / duration);
                    progressBar.Value = Math.Max(0, Math.Min(1000, value));
                }

                timeLabel.Text = FormatTime(current) + " / " + FormatTime(duration);

                if (btnPlayPause.Text == "暂停" && paused && duration > 0 && current >= duration - 0.4)
                {
                    btnPlayPause.Text = "播放";
                }
            }
            catch
            {
            }
        }

        private string GetScriptResult(string script)
        {
            if (browser.Document == null)
            {
                return "";
            }

            object result = browser.Document.InvokeScript("eval", new object[] { script });
            return result == null ? "" : result.ToString();
        }

        private void RunScript(string script)
        {
            if (browser.Document != null)
            {
                browser.Document.InvokeScript("eval", new object[] { script });
            }
        }

        private double ParseDouble(string s)
        {
            double result;
            if (double.TryParse(s, out result))
            {
                return result;
            }
            return 0;
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                seconds = 0;
            }
            int totalSeconds = (int)Math.Round(seconds);
            int minutes = totalSeconds / 60;
            int secs = totalSeconds % 60;
            return string.Format("{0:00}:{1:00}", minutes, secs);
        }

        private string GetEmptyPageHtml()
        {
            return @"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><style>body{margin:0;background:#000;}</style></head>
<body></body>
</html>";
        }

        private string GetVideoPageHtml(string videoPath)
        {
            string fileUrl = "file:///" + videoPath.Replace("\\", "/");
            return string.Format(@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<meta http-equiv='X-UA-Compatible' content='IE=edge'>
<style>
body {{
    margin: 0;
    padding: 0;
    background: #000;
    overflow: hidden;
}}
video {{
    width: 100%;
    height: 100%;
    object-fit: contain;
}}
</style>
</head>
<body>
<video id='videoEl' preload='auto'>
<source src='{0}'>
</video>
<script>
window.videoEl = document.getElementById('videoEl');
</script>
</body>
</html>", fileUrl);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (progressTimer != null)
                {
                    progressTimer.Stop();
                    progressTimer.Dispose();
                }
                if (browser != null)
                {
                    browser.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
