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
    public static class DisclaimerManager
    {
        private const string AcceptanceVersion = "DisclaimerAcceptedV1";
        private static readonly string AcceptancePath = Path.Combine(AppInfo.AppDataDirectory, "disclaimer.accepted");

        public static bool EnsureAccepted(IWin32Window owner, bool darkMode)
        {
            if (IsAccepted())
            {
                return true;
            }

            using (DisclaimerDialog dialog = new DisclaimerDialog(darkMode))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK)
                {
                    return false;
                }
            }

            try
            {
                Directory.CreateDirectory(AppInfo.AppDataDirectory);
                string text = AcceptanceVersion + "|" + DateTime.UtcNow.Ticks.ToString() + "|" + AppInfo.Version;
                File.WriteAllText(AcceptancePath, text, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "无法保存免责协议确认记录，本次仍会继续运行。\r\n\r\n" + ex.Message, "保存确认记录失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return true;
        }

        private static bool IsAccepted()
        {
            try
            {
                if (!File.Exists(AcceptancePath))
                {
                    return false;
                }

                string text = File.ReadAllText(AcceptancePath, Encoding.UTF8).Trim();
                return text.StartsWith(AcceptanceVersion + "|", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }
}
