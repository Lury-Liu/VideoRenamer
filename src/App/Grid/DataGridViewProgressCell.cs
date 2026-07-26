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
    public class DataGridViewProgressCell : DataGridViewTextBoxCell
    {
        // 进度填充刷静态复用（避免导出期间每次重绘每个可见进度格都分配再
        // 释放 SolidBrush）；颜色随主题经 ApplyPalette 换刷（阶段9a——
        // 换刷与绘制都在 UI 线程，无并发使用窗口）。
        private static Brush CompletedFillBrush = new SolidBrush(UiTheme.ProgressCompletedFill(false));
        private static Brush ActiveFillBrush = new SolidBrush(UiTheme.ProgressActiveFill(false));

        public static void ApplyPalette(Color activeFill, Color completedFill)
        {
            Brush oldActive = ActiveFillBrush;
            Brush oldCompleted = CompletedFillBrush;
            ActiveFillBrush = new SolidBrush(activeFill);
            CompletedFillBrush = new SolidBrush(completedFill);
            if (oldActive != null)
            {
                oldActive.Dispose();
            }
            if (oldCompleted != null)
            {
                oldCompleted.Dispose();
            }
        }

        protected override void Paint(
            Graphics graphics,
            Rectangle clipBounds,
            Rectangle cellBounds,
            int rowIndex,
            DataGridViewElementStates cellState,
            object value,
            object formattedValue,
            string errorText,
            DataGridViewCellStyle cellStyle,
            DataGridViewAdvancedBorderStyle advancedBorderStyle,
            DataGridViewPaintParts paintParts)
        {
            int progress = 0;
            if (value != null)
            {
                int.TryParse(value.ToString(), out progress);
            }
            progress = Math.Max(0, Math.Min(100, progress));

            base.Paint(
                graphics,
                clipBounds,
                cellBounds,
                rowIndex,
                cellState,
                value,
                formattedValue,
                errorText,
                cellStyle,
                advancedBorderStyle,
                paintParts & ~DataGridViewPaintParts.ContentForeground);

            bool selected = (cellState & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected;
            Color textColor = selected ? cellStyle.SelectionForeColor : cellStyle.ForeColor;
            Color trackColor = ControlPaint.Light(selected ? cellStyle.SelectionBackColor : cellStyle.BackColor);
            Brush fillBrush = progress >= 100 ? CompletedFillBrush : ActiveFillBrush;

            Rectangle bar = new Rectangle(cellBounds.X + 8, cellBounds.Y + 12, Math.Max(4, cellBounds.Width - 16), Math.Max(8, cellBounds.Height - 24));
            using (Brush track = new SolidBrush(trackColor))
            {
                graphics.FillRectangle(track, bar);
            }

            int fillWidth = (int)Math.Round(bar.Width * (progress / 100.0));
            if (fillWidth > 0)
            {
                graphics.FillRectangle(fillBrush, new Rectangle(bar.X, bar.Y, fillWidth, bar.Height));
            }

            using (Pen border = new Pen(ControlPaint.Dark(trackColor)))
            {
                graphics.DrawRectangle(border, bar);
            }

            string text = progress + "%";
            TextRenderer.DrawText(
                graphics,
                text,
                cellStyle.Font,
                cellBounds,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
