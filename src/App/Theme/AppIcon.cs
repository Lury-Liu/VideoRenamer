using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VideoRenamer
{
    public static class AppIcon
    {
        private static Icon cachedIcon;

        public static Icon Get()
        {
            if (cachedIcon != null)
            {
                return cachedIcon;
            }

            foreach (string path in GetCandidatePaths())
            {
                try
                {
                    if (File.Exists(path))
                    {
                        cachedIcon = new Icon(path);
                        return cachedIcon;
                    }
                }
                catch
                {
                }
            }

            try
            {
                string executablePath = Application.ExecutablePath;
                if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
                {
                    cachedIcon = Icon.ExtractAssociatedIcon(executablePath);
                }
            }
            catch
            {
            }

            return cachedIcon;
        }

        public static void Apply(Form form)
        {
            Icon icon = Get();
            if (form != null && icon != null)
            {
                form.Icon = icon;
            }
        }

        private static IEnumerable<string> GetCandidatePaths()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentDir = Environment.CurrentDirectory;
            yield return Path.Combine(baseDir, "assets", "app.ico");
            yield return Path.Combine(baseDir, "app.ico");
            yield return Path.Combine(currentDir, "assets", "app.ico");
            yield return Path.Combine(currentDir, "app.ico");
        }
    }
}
