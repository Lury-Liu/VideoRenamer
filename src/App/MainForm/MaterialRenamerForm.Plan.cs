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
    public partial class MaterialRenamerForm
    {
        // 当前命名计划：字段与唯一写入点同驻本分部（阶段8a——状态单一所有者）。
        // 其余分部只读；两条刷新路径（整表/增量）都经 ReplaceCurrentPlan 换内容。
        private readonly List<RenamePlan> currentPlan = new List<RenamePlan>();

        private void ReplaceCurrentPlan(List<RenamePlan> plan)
        {
            currentPlan.Clear();
            if (plan != null)
            {
                currentPlan.AddRange(plan);
            }
        }

        private int GetDefaultScene()
        {
            return numScene == null ? 1 : Math.Max(1, (int)numScene.Value);
        }

        // 命名设置快照的唯一读取点：构造期（BuildUi→ApplyTheme→RenderAll）
        // 各控件可能尚未创建，逐项判空保持与原分散读取相同的兜底语义。
        private NamingSettings ReadNamingSettings()
        {
            return new NamingSettings
            {
                Episode = numEpisode == null ? 1 : (int)numEpisode.Value,
                DefaultScene = GetDefaultScene(),
                KeepExtensionCase = chkKeepExtension != null && chkKeepExtension.Checked,
                Export1080p = IsExport1080pEnabled(),
                ExportWatermark = IsExportWatermarkEnabled(),
                UseRowScene = IsRowSceneEnabled()
            };
        }

        private bool IsRowSceneEnabled()
        {
            return chkRowScene != null && chkRowScene.Checked;
        }

        // 导出设置读取（原先误放在 Theme 分部文件里，被 Preview/Rename/Ui
        // 跨分部调用的隐藏接缝；命名设置的家在这里）。
        private bool IsExport1080pEnabled()
        {
            return chkExport1080p != null && chkExport1080p.Checked;
        }

        private bool IsExportWatermarkEnabled()
        {
            return IsExport1080pEnabled() && chkExportWatermark != null && chkExportWatermark.Checked;
        }

        private void InitializeRowScenesFromDefaultIfNeeded()
        {
            if (rowSceneModeInitialized)
            {
                return;
            }

            int defaultScene = GetDefaultScene();
            foreach (ShotRow row in rows)
            {
                row.Scene = defaultScene;
            }

            rowSceneModeInitialized = true;
        }

        // 阶段10h：列头去掉字母前缀（与设计稿一致）后，提示文案直接用列名。
        private string GetMainColumnDisplayName()
        {
            return "「主要素材」";
        }

        private string GetBackupColumnDisplayName()
        {
            return "「备用素材」";
        }

        private static List<string> GetVideoFilePaths(string[] paths)
        {
            List<string> files = new List<string>();
            if (paths == null)
            {
                return files;
            }

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (File.Exists(path))
                {
                    string extension = Path.GetExtension(path);
                    if (extension != null && VideoExtensions.Contains(extension))
                    {
                        files.Add(Path.GetFullPath(path));
                    }
                    continue;
                }

                if (Directory.Exists(path))
                {
                    foreach (string file in Directory.GetFiles(path))
                    {
                        string extension = Path.GetExtension(file);
                        if (extension != null && VideoExtensions.Contains(extension))
                        {
                            files.Add(Path.GetFullPath(file));
                        }
                    }
                }
            }

            files.Sort(new NaturalPathComparer());
            return files;
        }

        private HashSet<string> GetAllFileKeys()
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ShotRow row in rows)
            {
                foreach (string file in row.MainFiles)
                {
                    keys.Add(file);
                }
                foreach (string file in row.BackupFiles)
                {
                    keys.Add(file);
                }
            }
            return keys;
        }
    }
}
