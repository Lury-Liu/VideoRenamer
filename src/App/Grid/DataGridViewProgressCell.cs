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
        // 两种进度填充色是固定值：静态复用，避免导出期间每次重绘每个
        // 可见进度格都分配再释放 SolidBrush（track/border 颜色依赖单元格
        // 样式，保持按需创建）。
        private static readonly Brush CompletedFillBrush = new SolidBrush(Color.FromArgb(43, 150, 92));
        private static readonly Brush ActiveFillBrush = new SolidBrush(Color.FromArgb(35, 120, 210));

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
