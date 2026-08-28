using System;
using System.Drawing;
using System.Windows.Forms;

namespace VideoRenamer
{
    // 内嵌 libvlc 的播放器控件：视频画面 + 播放/暂停 + 可拖拽进度条 + 时间。
    // 视频通过 hwnd 模式直接渲染到 Panel 窗口句柄（替代不稳定的 vmem 回调）。
    // 进度用 200ms 轮询刷新；拖动进度条期间暂停轮询写回，避免打架。
    public sealed class VideoPlayerControl : UserControl
    {
        private readonly VlcMediaPlayer player = new VlcMediaPlayer();
        private readonly Panel videoSurface;
        private readonly Panel controlBar;
        private readonly Button btnPlayPause;
        private readonly TrackBar progressBar;
        private readonly Label timeLabel;
        private readonly System.Windows.Forms.Timer progressTimer;
        private bool seeking;
        private bool darkMode;

        public VideoPlayerControl()
        {
            videoSurface = new Panel();
            videoSurface.Dock = DockStyle.Fill;
            videoSurface.BackColor = Color.Black;
            videoSurface.Margin = new Padding(0);

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
            if (string.IsNullOrWhiteSpace(path))
            {
                StopPlayback();
                return;
            }

            player.LoadMedia(path);
            // hwnd 模式：把 VLC 输出窗口绑定到 videoSurface 的句柄
            if (videoSurface != null && videoSurface.IsHandleCreated)
            {
                player.SetHwnd(videoSurface.Handle);
            }
            player.SetVolume(70);
            ResetUiToStart();
            progressTimer.Start();
        }

        public void StopPlayback()
        {
            progressTimer.Stop();
            player.Stop();
            ResetUiToStart();
        }

        public bool IsAvailable
        {
            get { return player.IsAvailable; }
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
            if (player.IsPlaying)
            {
                player.Pause();
                btnPlayPause.Text = "播放";
            }
            else
            {
                player.Play();
                btnPlayPause.Text = "暂停";
            }
        }

        private void OnProgressScroll(object sender, EventArgs e)
        {
            float position = progressBar.Value / 1000f;
            player.SeekPosition(position);
            UpdateTimeText();
        }

        private void OnProgressTimerTick(object sender, EventArgs e)
        {
            if (seeking)
            {
                return;
            }

            long length = player.LengthMilliseconds;
            long time = player.TimeMilliseconds;

            if (length > 0)
            {
                int value = (int)Math.Round(time * 1000.0 / length);
                progressBar.Value = Math.Max(0, Math.Min(1000, value));
            }

            UpdateTimeText();

            if (btnPlayPause.Text == "暂停" && !player.IsPlaying && length > 0 && time >= length - 400)
            {
                btnPlayPause.Text = "播放";
                player.Stop();
            }
        }

        private void UpdateTimeText()
        {
            long length = player.LengthMilliseconds;
            long time = player.TimeMilliseconds;
            timeLabel.Text = FormatTime(time) + " / " + FormatTime(length);
        }

        private static string FormatTime(long milliseconds)
        {
            if (milliseconds < 0)
            {
                milliseconds = 0;
            }
            long totalSeconds = milliseconds / 1000;
            long minutes = totalSeconds / 60;
            long seconds = totalSeconds % 60;
            return string.Format("{0:00}:{1:00}", minutes, seconds);
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
                player.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
