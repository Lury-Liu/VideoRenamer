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
    // 调色板唯一所有者（阶段9a 起为暖纸色系）：应用内所有颜色都必须经
    // 本类的具名角色函数取得——不允许在其他文件散落 Color.FromArgb
    //（阶段13 起由 Assert-PaletteOwnership 构建门禁强制）。
    //
    // 色系：暖白纸面 + 陶土主色（浅色 #BA5B34 保证白字对比度；深色 #D97757
    // 配深字），语义底色一律"同色系浅底深字"。数值被 palette_pins 用例锁定。
    public static class UiTheme
    {
        public static bool DetectWindowsDarkMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value != null)
                    {
                        return Convert.ToInt32(value) == 0;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        // --- 表面 ---

        public static Color WindowBack(bool dark)
        {
            return dark ? Color.FromArgb(38, 37, 33) : Color.FromArgb(250, 249, 245);
        }

        public static Color PanelBack(bool dark)
        {
            return dark ? Color.FromArgb(45, 43, 39) : Color.FromArgb(244, 242, 236);
        }

        public static Color ControlBack(bool dark)
        {
            return dark ? Color.FromArgb(52, 50, 45) : Color.White;
        }

        public static Color HeaderBack(bool dark)
        {
            return dark ? Color.FromArgb(59, 56, 51) : Color.FromArgb(239, 237, 229);
        }

        // --- 文本 ---

        public static Color TextColor(bool dark)
        {
            return dark ? Color.FromArgb(237, 234, 227) : Color.FromArgb(61, 58, 52);
        }

        public static Color MutedText(bool dark)
        {
            return dark ? Color.FromArgb(168, 163, 153) : Color.FromArgb(120, 116, 106);
        }

        public static Color ErrorText(bool dark)
        {
            return dark ? Color.FromArgb(232, 160, 143) : Color.FromArgb(140, 58, 40);
        }

        // 场号列数字色（自 Theme 分部收拢，阶段9a）。
        public static Color SceneNumberText(bool dark)
        {
            return dark ? Color.FromArgb(232, 144, 124) : Color.FromArgb(168, 64, 44);
        }

        // --- 边框 ---

        public static Color BorderColor(bool dark)
        {
            return dark ? Color.FromArgb(74, 70, 63) : Color.FromArgb(226, 223, 213);
        }

        // --- 主色（陶土）：主按钮/进度 ---

        public static Color AccentBack(bool dark)
        {
            return dark ? Color.FromArgb(217, 119, 87) : Color.FromArgb(186, 91, 52);
        }

        public static Color AccentFore(bool dark)
        {
            return dark ? Color.FromArgb(59, 26, 14) : Color.White;
        }

        public static Color AccentHoverBack(bool dark)
        {
            return dark ? Color.FromArgb(224, 137, 99) : Color.FromArgb(201, 106, 66);
        }

        public static Color AccentPressedBack(bool dark)
        {
            return dark ? Color.FromArgb(201, 106, 66) : Color.FromArgb(168, 78, 43);
        }

        // --- 普通按钮悬浮/按下（阶段9 新增的舒适细节） ---

        public static Color ButtonHoverBack(bool dark)
        {
            return dark ? Color.FromArgb(59, 56, 51) : Color.FromArgb(244, 242, 236);
        }

        public static Color ButtonPressedBack(bool dark)
        {
            return dark ? Color.FromArgb(68, 64, 58) : Color.FromArgb(236, 233, 224);
        }

        // --- 选中 ---

        public static Color SelectionBack(bool dark)
        {
            return dark ? Color.FromArgb(94, 130, 176) : Color.FromArgb(86, 119, 158);
        }

        public static Color SelectionFore(bool dark)
        {
            return Color.White;
        }

        // --- 拖放高亮 ---

        public static Color DropTargetBack(bool dark)
        {
            return dark ? Color.FromArgb(94, 78, 32) : Color.FromArgb(247, 233, 166);
        }

        public static Color DropTargetFore(bool dark)
        {
            return dark ? Color.White : Color.FromArgb(58, 46, 15);
        }

        // --- 预览行语义底色（同色系浅底，配正文/深字） ---

        public static Color PreviewAltBack(bool dark)
        {
            return dark ? Color.FromArgb(44, 42, 37) : Color.FromArgb(247, 245, 239);
        }

        public static Color PreviewNeutralBack(bool dark)
        {
            return dark ? Color.FromArgb(49, 47, 42) : Color.FromArgb(241, 239, 233);
        }

        public static Color PreviewWarningBack(bool dark)
        {
            return dark ? Color.FromArgb(74, 61, 32) : Color.FromArgb(247, 237, 216);
        }

        public static Color PreviewErrorBack(bool dark)
        {
            return dark ? Color.FromArgb(74, 44, 38) : Color.FromArgb(247, 228, 222);
        }

        // --- 复选框 ---

        public static Color CheckAccentBack(bool dark)
        {
            return dark ? Color.FromArgb(108, 62, 42) : Color.FromArgb(244, 227, 219);
        }

        // --- 进度填充（DataGridViewProgressCell 经 ApplyPalette 取用） ---

        public static Color ProgressActiveFill(bool dark)
        {
            return dark ? Color.FromArgb(217, 119, 87) : Color.FromArgb(186, 91, 52);
        }

        public static Color ProgressCompletedFill(bool dark)
        {
            return dark ? Color.FromArgb(95, 162, 104) : Color.FromArgb(76, 138, 79);
        }

        // --- 应用 ---

        public static void ApplyForm(Form form, bool dark)
        {
            if (form == null)
            {
                return;
            }

            form.BackColor = WindowBack(dark);
            form.ForeColor = TextColor(dark);
            foreach (Control control in form.Controls)
            {
                ApplyControl(control, dark);
            }
        }

        public static void ApplyControl(Control control, bool dark)
        {
            if (control == null)
            {
                return;
            }

            string role = control.Tag as string;
            bool muted = StringComparer.OrdinalIgnoreCase.Equals(role, "Muted");
            bool error = StringComparer.OrdinalIgnoreCase.Equals(role, "Error");
            bool primary = StringComparer.OrdinalIgnoreCase.Equals(role, "Primary");

            if (control is ToolStrip)
            {
                ApplyToolStrip((ToolStrip)control, dark);
            }
            else if (control is Button)
            {
                Button button = (Button)control;
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = primary ? AccentBack(dark) : BorderColor(dark);
                button.FlatAppearance.MouseOverBackColor = primary ? AccentHoverBack(dark) : ButtonHoverBack(dark);
                button.FlatAppearance.MouseDownBackColor = primary ? AccentPressedBack(dark) : ButtonPressedBack(dark);
                button.BackColor = primary ? AccentBack(dark) : ControlBack(dark);
                button.ForeColor = primary ? AccentFore(dark) : TextColor(dark);
            }
            else if (control is TextBoxBase || control is NumericUpDown)
            {
                control.BackColor = ControlBack(dark);
                control.ForeColor = TextColor(dark);
            }
            else if (control is CheckBox)
            {
                ApplyCheckBox((CheckBox)control, dark);
            }
            else if (control is Label)
            {
                control.BackColor = ParentBack(control, dark);
                control.ForeColor = error ? ErrorText(dark) : (muted ? MutedText(dark) : TextColor(dark));
            }
            else if (control is DataGridView)
            {
                ApplyGrid((DataGridView)control, dark);
            }
            else if (control is ListView)
            {
                ListView listView = (ListView)control;
                listView.BackColor = ControlBack(dark);
                listView.ForeColor = TextColor(dark);
            }
            else if (control is Panel || control is FlowLayoutPanel || control is SplitterPanel || control is SplitContainer)
            {
                control.BackColor = PanelBack(dark);
                control.ForeColor = TextColor(dark);
            }
            else
            {
                control.BackColor = ParentBack(control, dark);
                control.ForeColor = TextColor(dark);
            }

            foreach (Control child in control.Controls)
            {
                ApplyControl(child, dark);
            }
        }

        private static void ApplyToolStrip(ToolStrip toolStrip, bool dark)
        {
            toolStrip.BackColor = PanelBack(dark);
            toolStrip.ForeColor = TextColor(dark);
            foreach (ToolStripItem item in toolStrip.Items)
            {
                ApplyToolStripItem(item, dark);
            }
        }

        private static void ApplyToolStripItem(ToolStripItem item, bool dark)
        {
            if (item == null)
            {
                return;
            }

            item.BackColor = PanelBack(dark);
            item.ForeColor = TextColor(dark);
            ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
            if (dropDown == null)
            {
                return;
            }

            dropDown.DropDown.BackColor = PanelBack(dark);
            dropDown.DropDown.ForeColor = TextColor(dark);
            foreach (ToolStripItem child in dropDown.DropDownItems)
            {
                ApplyToolStripItem(child, dark);
            }
        }

        private static void ApplyCheckBox(CheckBox checkBox, bool dark)
        {
            checkBox.UseVisualStyleBackColor = false;
            checkBox.FlatStyle = FlatStyle.Flat;
            checkBox.FlatAppearance.BorderColor = BorderColor(dark);
            checkBox.FlatAppearance.CheckedBackColor = CheckAccentBack(dark);
            checkBox.FlatAppearance.MouseOverBackColor = ButtonHoverBack(dark);
            checkBox.FlatAppearance.MouseDownBackColor = ButtonPressedBack(dark);
            checkBox.BackColor = ParentBack(checkBox, dark);
            checkBox.ForeColor = TextColor(dark);
        }

        public static void ApplyGrid(DataGridView grid, bool dark)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.BackgroundColor = WindowBack(dark);
            grid.GridColor = BorderColor(dark);
            grid.DefaultCellStyle.BackColor = ControlBack(dark);
            grid.DefaultCellStyle.ForeColor = TextColor(dark);
            grid.DefaultCellStyle.SelectionBackColor = SelectionBack(dark);
            grid.DefaultCellStyle.SelectionForeColor = SelectionFore(dark);
            grid.AlternatingRowsDefaultCellStyle.BackColor = PreviewAltBack(dark);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = TextColor(dark);
            grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderBack(dark);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextColor(dark);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderBack(dark);
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextColor(dark);
            grid.RowHeadersDefaultCellStyle.BackColor = HeaderBack(dark);
            grid.RowHeadersDefaultCellStyle.ForeColor = MutedText(dark);
            grid.RowHeadersDefaultCellStyle.SelectionBackColor = HeaderBack(dark);
            grid.RowHeadersDefaultCellStyle.SelectionForeColor = TextColor(dark);
            grid.RowsDefaultCellStyle.BackColor = ControlBack(dark);
            grid.RowsDefaultCellStyle.ForeColor = TextColor(dark);
        }

        private static Color ParentBack(Control control, bool dark)
        {
            return control.Parent == null ? WindowBack(dark) : control.Parent.BackColor;
        }
    }
}
