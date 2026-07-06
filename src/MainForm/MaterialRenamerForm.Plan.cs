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

        private int GetDefaultScene()
        {
            return numScene == null ? 1 : Math.Max(1, (int)numScene.Value);
        }

        private bool IsRowSceneEnabled()
        {
            return chkRowScene != null && chkRowScene.Checked;
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

        private string GetMainColumnDisplayName()
        {
            return IsRowSceneEnabled() ? "C「主要素材」" : "B「主要素材」";
        }

        private string GetBackupColumnDisplayName()
        {
            return IsRowSceneEnabled() ? "D「备用素材」" : "C「备用素材」";
        }

        private string GetMainColumnLetter()
        {
            return IsRowSceneEnabled() ? "C" : "B";
        }

        private string GetBackupColumnLetter()
        {
            return IsRowSceneEnabled() ? "D" : "C";
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
