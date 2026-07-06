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
    public static class VideoThumbnailProvider
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [Flags]
        private enum SIIGBF
        {
            ResizeToFit = 0x00000000,
            BiggerSizeOk = 0x00000001,
            MemoryOnly = 0x00000002,
            IconOnly = 0x00000004,
            ThumbnailOnly = 0x00000008,
            InCacheOnly = 0x00000010,
            CropToSquare = 0x00000020,
            WideThumbnails = 0x00000040,
            IconBackground = 0x00000080,
            ScaleUp = 0x00000100
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public static Image GetThumbnail(string path, Size size)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            Image shellImage = TryGetShellThumbnail(path, size);
            if (shellImage != null)
            {
                return shellImage;
            }

            return TryGetAssociatedIcon(path, size);
        }

        private static Image TryGetShellThumbnail(string path, Size size)
        {
            IShellItemImageFactory factory = null;
            IntPtr bitmapHandle = IntPtr.Zero;
            try
            {
                Guid factoryId = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref factoryId, out factory);
                SIZE shellSize = new SIZE { cx = Math.Max(64, size.Width), cy = Math.Max(64, size.Height) };
                int result = factory.GetImage(shellSize, SIIGBF.ThumbnailOnly | SIIGBF.BiggerSizeOk | SIIGBF.ResizeToFit, out bitmapHandle);
                if (result != 0 || bitmapHandle == IntPtr.Zero)
                {
                    return null;
                }

                using (Bitmap bitmap = Image.FromHbitmap(bitmapHandle))
                {
                    return new Bitmap(bitmap);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (bitmapHandle != IntPtr.Zero)
                {
                    DeleteObject(bitmapHandle);
                }
                if (factory != null && Marshal.IsComObject(factory))
                {
                    Marshal.FinalReleaseComObject(factory);
                }
            }
        }

        private static Image TryGetAssociatedIcon(string path, Size size)
        {
            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null)
                    {
                        return null;
                    }

                    Bitmap bitmap = new Bitmap(Math.Max(64, size.Width), Math.Max(64, size.Height));
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.Transparent);
                        int iconSize = Math.Min(64, Math.Min(bitmap.Width, bitmap.Height) - 16);
                        Rectangle target = new Rectangle((bitmap.Width - iconSize) / 2, (bitmap.Height - iconSize) / 2, iconSize, iconSize);
                        graphics.DrawIcon(icon, target);
                    }
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
