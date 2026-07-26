using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace VideoRenamer
{
    // 每个进程只选定一张启动图标。资源加载、落盘和 Shell 刷新集中在这里，
    // 让 AppIcon 继续只是窗体图标的轻量门面。
    internal static class StartupIconManager
    {
        private const int IconCount = 9;
        private const string ResourcePrefix = "VideoRenamer.StartupIcons.";
        private static readonly object SyncRoot = new object();
        private static Icon sessionIcon;
        private static Image sessionPreviewImage;
        private static bool initialized;

        public static void InitializeForApplication()
        {
            string changedIconPath = "";
            lock (SyncRoot)
            {
                if (initialized)
                {
                    return;
                }

                int selectedIndex = StartupIconRotation.GetNextIndex(StartupIconStateStore.ReadLastIndex(), IconCount);
                byte[] iconBytes = LoadIconBytes(selectedIndex);
                Icon icon = CreateIcon(iconBytes);
                if (icon == null)
                {
                    return;
                }

                sessionIcon = icon;
                sessionPreviewImage = StartupIconPreview.ExtractLargestPngLayer(iconBytes);
                changedIconPath = TryDeployIcon(iconBytes);
                StartupIconStateStore.TryWriteLastIndex(selectedIndex);
                initialized = true;
            }

            IconFileChangeNotifier.TryNotifyChanged(changedIconPath);
        }

        public static Icon GetSessionIcon()
        {
            lock (SyncRoot)
            {
                return sessionIcon;
            }
        }

        public static Image GetSessionPreviewImage()
        {
            lock (SyncRoot)
            {
                return sessionPreviewImage == null ? null : new Bitmap(sessionPreviewImage);
            }
        }

        private static Icon CreateIcon(byte[] iconBytes)
        {
            if (iconBytes == null || iconBytes.Length == 0)
            {
                return null;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream(iconBytes, false))
                using (Icon icon = new Icon(stream))
                {
                    return (Icon)icon.Clone();
                }
            }
            catch
            {
                return null;
            }
        }

        private static byte[] LoadIconBytes(int index)
        {
            string resourceName = ResourcePrefix + GetIconFileName(index);
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream resource = assembly.GetManifestResourceStream(resourceName))
            {
                if (resource != null)
                {
                    return ReadAllBytes(resource);
                }
            }

            foreach (string path in GetLocalIconPaths(index))
            {
                try
                {
                    if (File.Exists(path))
                    {
                        return File.ReadAllBytes(path);
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static string TryDeployIcon(byte[] iconBytes)
        {
            if (iconBytes == null || iconBytes.Length == 0)
            {
                return "";
            }

            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    AppInfo.Name,
                    "startup-icons");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "current.ico");
                File.WriteAllBytes(path, iconBytes);
                return path;
            }
            catch (Exception ex)
            {
                AppLog.Write("startup-icon", "部署快捷方式图标失败", ex);
                return "";
            }
        }

        private static string GetIconFileName(int index)
        {
            return (index + 1).ToString("00") + ".ico";
        }

        private static byte[] ReadAllBytes(Stream input)
        {
            using (MemoryStream output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
            }
        }

        private static string[] GetLocalIconPaths(int index)
        {
            string fileName = GetIconFileName(index);
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string currentDirectory = Environment.CurrentDirectory;
            return new string[]
            {
                Path.Combine(baseDirectory, "assets", "startup-icons", fileName),
                Path.Combine(currentDirectory, "assets", "startup-icons", fileName)
            };
        }
    }
}
