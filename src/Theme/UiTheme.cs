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

        public static Color WindowBack(bool dark)
        {
            return dark ? Color.FromArgb(24, 26, 31) : Color.White;
        }

        public static Color PanelBack(bool dark)
        {
            return dark ? Color.FromArgb(32, 35, 42) : Color.FromArgb(246, 247, 249);
        }

        public static Color ControlBack(bool dark)
        {
            return dark ? Color.FromArgb(40, 44, 52) : Color.White;
        }

        public static Color HeaderBack(bool dark)
        {
            return dark ? Color.FromArgb(48, 53, 63) : Color.FromArgb(235, 239, 245);
        }

        public static Color TextColor(bool dark)
        {
            return dark ? Color.FromArgb(232, 236, 244) : Color.FromArgb(24, 31, 42);
        }

        public static Color MutedText(bool dark)
        {
            return dark ? Color.FromArgb(166, 174, 188) : Color.FromArgb(95, 105, 120);
        }

        public static Color BorderColor(bool dark)
        {
            return dark ? Color.FromArgb(78, 86, 100) : Color.FromArgb(206, 214, 224);
        }

        public static Color SelectionBack(bool dark)
        {
            return dark ? Color.FromArgb(60, 116, 220) : Color.FromArgb(35, 94, 190);
        }

        public static Color DropTargetBack(bool dark)
        {
            return dark ? Color.FromArgb(86, 72, 30) : Color.FromArgb(255, 242, 157);
        }

        public static Color DropTargetFore(bool dark)
        {
            return dark ? Color.White : Color.FromArgb(38, 32, 14);
        }

        public static Color PreviewAltBack(bool dark)
        {
            return dark ? Color.FromArgb(30, 40, 54) : Color.FromArgb(246, 250, 255);
        }

        public static Color PreviewNeutralBack(bool dark)
        {
            return dark ? Color.FromArgb(45, 48, 55) : Color.FromArgb(245, 245, 245);
        }

        public static Color PreviewWarningBack(bool dark)
        {
            return dark ? Color.FromArgb(92, 75, 28) : Color.FromArgb(255, 249, 196);
        }

        public static Color PreviewErrorBack(bool dark)
        {
            return dark ? Color.FromArgb(92, 42, 50) : Color.FromArgb(255, 235, 238);
        }

        public static Color ErrorText(bool dark)
        {
            return dark ? Color.FromArgb(255, 154, 154) : Color.FromArgb(150, 60, 50);
        }

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
                button.FlatAppearance.BorderColor = primary ? SelectionBack(dark) : BorderColor(dark);
                button.BackColor = primary ? SelectionBack(dark) : ControlBack(dark);
                button.ForeColor = primary ? Color.White : TextColor(dark);
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
            checkBox.FlatAppearance.CheckedBackColor = dark ? Color.FromArgb(58, 88, 144) : Color.FromArgb(222, 235, 255);
            checkBox.FlatAppearance.MouseOverBackColor = dark ? Color.FromArgb(48, 53, 63) : Color.FromArgb(238, 243, 250);
            checkBox.FlatAppearance.MouseDownBackColor = dark ? Color.FromArgb(58, 64, 76) : Color.FromArgb(226, 235, 247);
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
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.AlternatingRowsDefaultCellStyle.BackColor = dark ? Color.FromArgb(36, 41, 50) : Color.FromArgb(250, 252, 255);
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
