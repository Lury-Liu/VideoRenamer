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

namespace VideoRenamer
{
    public partial class MaterialRenamerForm
    {

        private void ToggleTheme()
        {
            darkMode = !darkMode;
            ApplyTheme();
            RefreshPreview();
        }

        private void UpdateWatermarkOptionState()
        {
            if (chkExportWatermark != null)
            {
                if (!IsExport1080pEnabled() && chkExportWatermark.Checked)
                {
                    chkExportWatermark.Checked = false;
                }
                chkExportWatermark.Enabled = true;
                UiTheme.ApplyControl(chkExportWatermark, darkMode);
            }
        }

        private void ApplyTheme()
        {
            UpdateRenameButtonText();
            UpdateWatermarkOptionState();
            if (btnTheme != null)
            {
                btnTheme.Text = "护眼模式";
            }

            UiTheme.ApplyForm(this, darkMode);
            ApplyGridNumberColumnStyles();
            if (detailTitleLabel != null && (string.IsNullOrWhiteSpace(detailTitleLabel.Text) || detailTitleLabel.Text == "未选择素材"))
            {
                ShowNoMaterialDetails();
            }
            ReapplyDragHighlight();
        }

        // 颜色本体收拢进 UiTheme（阶段9a）；这里只按当前主题取值。
        private Color GetSceneColumnTextColor()
        {
            return UiTheme.SceneNumberText(darkMode);
        }

        private Color GetShotColumnTextColor()
        {
            return UiTheme.TextColor(darkMode);
        }
    }
}
