using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VideoRenamer
{
    // 主题化细进度条（阶段10h）：执行中条专用，替代系统样式 ProgressBar
    //（绿色光泽与暖纸色系冲突）。圆角胶囊造型与设计稿一致；颜色经
    // UiTheme 角色取得（主题切换时由 ApplyControl 调 ApplyBarTheme）。
    public sealed class SlimProgressBar : Control
    {
        private int progressValue;
        private Color trackColor;
        private Color fillColor;

        public SlimProgressBar()
        {
            trackColor = UiTheme.BorderColor(false);
            fillColor = UiTheme.AccentBack(false);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public int Value
        {
            get { return progressValue; }
            set
            {
                int clamped = Math.Max(0, Math.Min(100, value));
                if (clamped != progressValue)
                {
                    progressValue = clamped;
                    Invalidate();
                }
            }
        }

        public void ApplyBarTheme(bool dark)
        {
            trackColor = UiTheme.BorderColor(dark);
            fillColor = UiTheme.AccentBack(dark);
            BackColor = Parent == null ? UiTheme.PanelBack(dark) : Parent.BackColor;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int barHeight = Math.Min(10, Math.Max(6, Height - 4));
            int y = (Height - barHeight) / 2;
            Rectangle track = new Rectangle(0, y, Math.Max(barHeight + 1, Width - 1), barHeight);
            using (GraphicsPath path = RoundedCapsule(track))
            using (SolidBrush brush = new SolidBrush(trackColor))
            {
                e.Graphics.FillPath(brush, path);
            }

            int fillWidth = (int)Math.Round(track.Width * (progressValue / 100.0));
            if (fillWidth > 0)
            {
                // 极小百分比也画出一枚圆点，避免"已开始却看不到进度"。
                fillWidth = Math.Max(fillWidth, barHeight + 1);
                Rectangle fill = new Rectangle(track.X, track.Y, fillWidth, barHeight);
                using (GraphicsPath path = RoundedCapsule(fill))
                using (SolidBrush brush = new SolidBrush(fillColor))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }
        }

        private static GraphicsPath RoundedCapsule(Rectangle bounds)
        {
            int diameter = bounds.Height;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 90, 180);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 180);
            path.CloseFigure();
            return path;
        }
    }
}
