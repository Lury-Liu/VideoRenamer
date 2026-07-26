using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VideoRenamer
{
    // 工作区序号徽章（阶段10g）：①②③④ 的圆形小标，把"设置→填表→预览→执行"
    // 的工作流顺序画进界面。颜色经 UiTheme 角色取得（主题切换时由
    // ApplyControl 调 ApplyBadgeTheme）。
    public sealed class ZoneBadge : Control
    {
        private readonly int number;
        private Color badgeBack;
        private Color badgeFore;

        public ZoneBadge(int badgeNumber)
        {
            number = badgeNumber;
            Size = new Size(20, 20);
            Margin = new Padding(0, 7, 6, 0);
            badgeBack = UiTheme.ZoneBadgeBack(false);
            badgeFore = UiTheme.ZoneBadgeFore(false);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void ApplyBadgeTheme(bool dark)
        {
            badgeBack = UiTheme.ZoneBadgeBack(dark);
            badgeFore = UiTheme.ZoneBadgeFore(dark);
            BackColor = Parent == null ? UiTheme.PanelBack(dark) : Parent.BackColor;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush back = new SolidBrush(badgeBack))
            {
                e.Graphics.FillEllipse(back, 1, 1, Width - 3, Height - 3);
            }
            TextRenderer.DrawText(
                e.Graphics,
                number.ToString(),
                Font,
                ClientRectangle,
                badgeFore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
