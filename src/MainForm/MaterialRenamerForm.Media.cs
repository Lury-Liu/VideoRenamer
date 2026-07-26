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
    public partial class MaterialRenamerForm : IUiDispatcher
    {
        // IUiDispatcher 实现：语义与 QueueOnUi 完全一致（含 false=未投递 契约）。
        public bool Post(Action action)
        {
            if (action == null)
            {
                return false;
            }
            return QueueOnUi(delegate { action(); });
        }

        private bool QueueOnUi(MethodInvoker action)
        {
            if (action == null || IsDisposed || !IsHandleCreated)
            {
                return false;
            }

            try
            {
                BeginInvoke(action);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void StartStaBackground(ThreadStart action)
        {
            if (action == null)
            {
                return;
            }

            Thread thread = new Thread(action);
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private VideoFileInfo GetFastVideoInfo(string path)
        {
            VideoFileInfo info = new VideoFileInfo();
            info.Path = path ?? "";
            info.FileName = string.IsNullOrWhiteSpace(path) ? "" : Path.GetFileName(path);
            info.SizeText = "";
            info.ResolutionText = "读取中";
            info.ModifiedText = "";
            info.Exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);

            if (!info.Exists)
            {
                info.ResolutionText = "未知";
                return info;
            }

            try
            {
                FileInfo file = new FileInfo(path);
                info.SizeText = VideoMetadataReader.FormatBytes(file.Length);
                info.ModifiedText = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
            }

            return info;
        }

        private void QueueVideoInfoLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || videoInfoCache.ContainsKey(path) || pendingVideoInfoLoads.Contains(path))
            {
                return;
            }

            pendingVideoInfoLoads.Add(path);
            StartStaBackground(delegate
            {
                VideoFileInfo info = VideoMetadataReader.Read(path);
                QueueOnUi(delegate
                {
                    pendingVideoInfoLoads.Remove(path);
                    videoInfoCache[path] = info;
                    UpdatePreviewListInfoSummary(path, info);
                    if (IsCurrentDetailPath(path))
                    {
                        RenderVideoInfo(info, currentDetailNewName, currentDetailContext);
                    }
                });
            });
        }

        private bool TryGetThumbnailFromCache(string path, out Image image)
        {
            image = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (!thumbnailCache.TryGetValue(path, out image))
            {
                return false;
            }

            TouchThumbnailCacheNode(path);
            return image != null;
        }

        private void TouchThumbnailCacheNode(string path)
        {
            LinkedListNode<string> node;
            if (!thumbnailCacheNodes.TryGetValue(path, out node))
            {
                node = thumbnailCacheOrder.AddLast(path);
                thumbnailCacheNodes[path] = node;
                return;
            }

            thumbnailCacheOrder.Remove(node);
            thumbnailCacheOrder.AddLast(node);
        }

        private void AddThumbnailToCache(string path, Image image)
        {
            if (string.IsNullOrWhiteSpace(path) || image == null)
            {
                if (image != null)
                {
                    image.Dispose();
                }
                return;
            }

            Image existing;
            if (thumbnailCache.TryGetValue(path, out existing) && !object.ReferenceEquals(existing, image))
            {
                existing.Dispose();
            }

            thumbnailCache[path] = image;
            TouchThumbnailCacheNode(path);
            TrimThumbnailCache();
        }

        private void TrimThumbnailCache()
        {
            while (thumbnailCache.Count > ThumbnailCacheLimit && thumbnailCacheOrder.First != null)
            {
                string path = thumbnailCacheOrder.First.Value;
                thumbnailCacheOrder.RemoveFirst();
                thumbnailCacheNodes.Remove(path);

                Image image;
                if (thumbnailCache.TryGetValue(path, out image))
                {
                    thumbnailCache.Remove(path);
                    if (image != null)
                    {
                        image.Dispose();
                    }
                }
            }
        }

        private void QueueThumbnailLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || pendingThumbnailLoads.Contains(path))
            {
                return;
            }

            Image cached;
            if (TryGetThumbnailFromCache(path, out cached))
            {
                return;
            }

            pendingThumbnailLoads.Add(path);
            StartStaBackground(delegate
            {
                Image image = VideoThumbnailProvider.GetThumbnail(path, new Size(300, 166));
                bool queued = QueueOnUi(delegate
                {
                    pendingThumbnailLoads.Remove(path);
                    if (image != null)
                    {
                        AddThumbnailToCache(path, image);
                    }

                    if (IsCurrentDetailPath(path))
                    {
                        if (image != null)
                        {
                            SetDetailImage(image, false);
                        }
                        else
                        {
                            SetDetailImage(CreatePlaceholderImage("无缩略图"), true);
                        }
                    }
                });

                if (!queued && image != null)
                {
                    image.Dispose();
                }
            });
        }

        private Image CreatePlaceholderImage(string text)
        {
            Bitmap bitmap = new Bitmap(300, 166);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(UiTheme.ControlBack(darkMode));
                using (Pen border = new Pen(UiTheme.BorderColor(darkMode)))
                {
                    graphics.DrawRectangle(border, 0, 0, bitmap.Width - 1, bitmap.Height - 1);
                }
                using (Brush brush = new SolidBrush(UiTheme.MutedText(darkMode)))
                using (Font font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold))
                {
                    SizeF size = graphics.MeasureString(text, font);
                    graphics.DrawString(text, font, brush, (bitmap.Width - size.Width) / 2, (bitmap.Height - size.Height) / 2);
                }
            }
            return bitmap;
        }

        private void SetDetailImage(Image image, bool ownsImage)
        {
            if (thumbnailBox == null)
            {
                if (ownsImage && image != null)
                {
                    image.Dispose();
                }
                return;
            }

            if (ownedDetailImage != null && !object.ReferenceEquals(ownedDetailImage, image))
            {
                ownedDetailImage.Dispose();
            }

            thumbnailBox.Image = image;
            ownedDetailImage = ownsImage ? image : null;
        }

        private void QueueFrameStripLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }
            if (StringComparer.OrdinalIgnoreCase.Equals(path, frameStripPath) && frameStrip.Count > 0)
            {
                return;
            }

            int version = ++frameStripVersion;
            frameStripPath = path;
            ClearFrameStrip();

            StartStaBackground(delegate
            {
                string ffmpegPath = FfmpegLocator.Resolve();
                List<Image> frames = VideoFrameStripProvider.Extract(ffmpegPath, path, 16);
                QueueOnUi(delegate
                {
                    if (version != frameStripVersion)
                    {
                        foreach (Image img in frames)
                        {
                            if (img != null) { img.Dispose(); }
                        }
                        return;
                    }
                    frameStrip = frames;
                });
            });
        }

        private void ShowFrameAtRatio(double ratio)
        {
            if (frameStrip == null || frameStrip.Count == 0)
            {
                return;
            }
            int index = (int)Math.Round(ratio * (frameStrip.Count - 1));
            index = Math.Max(0, Math.Min(frameStrip.Count - 1, index));
            Image frame = frameStrip[index];
            if (frame != null)
            {
                SetDetailImage(frame, false);
            }
        }

        private void RestoreStaticThumbnail()
        {
            Image cached;
            if (TryGetThumbnailFromCache(currentDetailPath, out cached) && cached != null)
            {
                SetDetailImage(cached, false);
            }
        }

        private void ClearFrameStrip()
        {
            if (frameStrip != null)
            {
                foreach (Image img in frameStrip)
                {
                    if (img != null) { img.Dispose(); }
                }
                frameStrip.Clear();
            }
        }
    }
}
