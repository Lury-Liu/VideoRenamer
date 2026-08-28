using System;
using System.Drawing;
using System.Windows.Forms;

namespace VideoRenamer
{
    // 内嵌 Windows Media Player 的播放器控件：视频画面 + 播放/暂停 + 可拖拽进度条 + 时间。
    // 使用 AxWindowsMediaPlayer ActiveX 控件（Windows 内置，无外部依赖）。
    // 进度用 200ms 轮询刷新；拖动进度条期间暂停轮询写回，避免打架。
    public sealed class VideoPlayerControl : UserControl
    {
        private readonly dynamic wmpPlayer;
        private readonly Panel videoSurface;
        private readonly Panel controlBar;
        private readonly Button btnPlayPause;
        private readonly TrackBar progressBar;
        private readonly Label timeLabel;
        private readonly System.Windows.Forms.Timer progressTimer;
        private bool seeking;
        private bool darkMode;
        private string currentPath = "";

        public VideoPlayerControl()
        {
            videoSurface = new Panel();
            videoSurface.Dock = DockStyle.Fill;
            videoSurface.BackColor = Color.Black;
            videoSurface.Margin = new Padding(0);

            // 创建 Windows Media Player ActiveX 控件
            try
            {
                Type wmpType = Type.GetTypeFromCLSID(new Guid("6BF52A52-394A-11d3-B153-00C04F79FAA6"));
                wmpPlayer = Activator.CreateInstance(wmpType);

                // 包装为 Control 并嵌入
                Control wmpControl = (Control)wmpPlayer;
                wmpControl.Dock = DockStyle.Fill;
                wmpControl.BackColor = Color.Black;
                videoSurface.Controls.Add(wmpControl);

                // 隐藏 WMP 自带的控制条（我们自己做）
                wmpPlayer.uiMode = "none";
                wmpPlayer.settings.autoStart = false;
                wmpPlayer.settings.volume = 70;
            }
            catch
            {
                // WMP 不可用时静默降级（显示黑屏，功能禁用）
                wmpPlayer = null;
            }

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

            Controls.Add(videoSurface);
            Controls.Add(controlBar);

            progressTimer = new System.Windows.Forms.Timer();
            progressTimer.Interval = 200;
            progressTimer.Tick += OnProgressTimerTick;
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
            if (wmpPlayer == null || string.IsNullOrWhiteSpace(path))
            {
                StopPlayback();
                return;
            }

            try
            {
                currentPath = path;
                wmpPlayer.URL = path;
                ResetUiToStart();
                progressTimer.Start();
            }
            catch
            {
                StopPlayback();
            }
        }

        public void StopPlayback()
        {
            progressTimer.Stop();
            currentPath = "";
            if (wmpPlayer != null)
            {
                try
                {
                    wmpPlayer.controls.stop();
                }
                catch
                {
                }
            }
            ResetUiToStart();
        }

        public bool IsAvailable
        {
            get { return wmpPlayer != null; }
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
            if (wmpPlayer == null)
            {
                return;
            }

            try
            {
                string state = wmpPlayer.playState.ToString();
                // WMPPlayState: 1=Stopped, 2=Paused, 3=Playing
                if (state == "3")
                {
                    wmpPlayer.controls.pause();
                    btnPlayPause.Text = "播放";
                }
                else
                {
                    wmpPlayer.controls.play();
                    btnPlayPause.Text = "暂停";
                }
            }
            catch
            {
            }
        }

        private void OnProgressScroll(object sender, EventArgs e)
        {
            if (wmpPlayer == null)
            {
                return;
            }

            try
            {
                double duration = wmpPlayer.currentMedia.duration;
                double position = (progressBar.Value / 1000.0) * duration;
                wmpPlayer.controls.currentPosition = position;
                UpdateTimeText();
            }
            catch
            {
            }
        }

        private void OnProgressTimerTick(object sender, EventArgs e)
        {
            if (wmpPlayer == null || seeking)
            {
                return;
            }

            try
            {
                double duration = wmpPlayer.currentMedia.duration;
                double position = wmpPlayer.controls.currentPosition;

                if (duration > 0)
                {
                    int value = (int)Math.Round(position * 1000.0 / duration);
                    progressBar.Value = Math.Max(0, Math.Min(1000, value));
                }

                UpdateTimeText();

                string state = wmpPlayer.playState.ToString();
                if (btnPlayPause.Text == "暂停" && state != "3" && duration > 0 && position >= duration - 0.4)
                {
                    btnPlayPause.Text = "播放";
                    wmpPlayer.controls.stop();
                }
            }
            catch
            {
            }
        }

        private void UpdateTimeText()
        {
            if (wmpPlayer == null)
            {
                return;
            }

            try
            {
                double duration = wmpPlayer.currentMedia.duration;
                double position = wmpPlayer.controls.currentPosition;
                timeLabel.Text = FormatTime(position) + " / " + FormatTime(duration);
            }
            catch
            {
                timeLabel.Text = "00:00 / 00:00";
            }
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (progressTimer != null)
                {
                    progressTimer.Stop();
                    progressTimer.Dispose();
                }
                if (wmpPlayer != null)
                {
                    try
                    {
                        wmpPlayer.close();
                    }
                    catch
                    {
                    }
                }
            }
            base.Dispose(disposing);
        }
    }
}
