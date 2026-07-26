using System;
using System.Collections.Generic;
using System.Drawing;

namespace VideoRenamer
{
    // 缩略图 LRU 缓存：字典 + 触达顺序链表 + 上限裁剪，图像生命周期归本类所有
    // （加入即接管，替换/淘汰/释放时 Dispose）。仅限 UI 线程访问
    // （与原实现相同，不加锁）。
    //
    // retainedImageProvider（修复评估发现的既有缺陷）：被淘汰的图像若恰好
    // 正被详情面板展示（借用式引用），Dispose 后重绘会触发 GDI+ 异常。
    // 淘汰/替换前与“当前展示图像”做 ReferenceEquals 保护——命中时只移出
    // 缓存、不 Dispose（宁可极罕见地漏掉一张图的释放，也不崩溃）。
    public sealed class ThumbnailCache : IDisposable
    {
        private readonly int limit;
        private readonly Func<Image> retainedImageProvider;
        private readonly Dictionary<string, Image> images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> order = new LinkedList<string>();
        private readonly Dictionary<string, LinkedListNode<string>> nodes = new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase);

        public ThumbnailCache(int limit)
            : this(limit, null)
        {
        }

        public ThumbnailCache(int limit, Func<Image> retainedImageProvider)
        {
            this.limit = Math.Max(1, limit);
            this.retainedImageProvider = retainedImageProvider;
        }

        private void DisposeUnlessRetained(Image image)
        {
            if (image == null)
            {
                return;
            }

            if (retainedImageProvider != null && object.ReferenceEquals(retainedImageProvider(), image))
            {
                return;
            }

            image.Dispose();
        }

        public bool TryGet(string path, out Image image)
        {
            image = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (!images.TryGetValue(path, out image))
            {
                return false;
            }

            Touch(path);
            return image != null;
        }

        public void Add(string path, Image image)
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
            if (images.TryGetValue(path, out existing) && !object.ReferenceEquals(existing, image))
            {
                DisposeUnlessRetained(existing);
            }

            images[path] = image;
            Touch(path);
            Trim();
        }

        // 供阶段7 的“重命名/覆盖导出完成后按路径失效”使用。
        public void Remove(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            LinkedListNode<string> node;
            if (nodes.TryGetValue(path, out node))
            {
                order.Remove(node);
                nodes.Remove(path);
            }

            Image image;
            if (images.TryGetValue(path, out image))
            {
                images.Remove(path);
                DisposeUnlessRetained(image);
            }
        }

        private void Touch(string path)
        {
            LinkedListNode<string> node;
            if (!nodes.TryGetValue(path, out node))
            {
                node = order.AddLast(path);
                nodes[path] = node;
                return;
            }

            order.Remove(node);
            order.AddLast(node);
        }

        private void Trim()
        {
            while (images.Count > limit && order.First != null)
            {
                string path = order.First.Value;
                order.RemoveFirst();
                nodes.Remove(path);

                Image image;
                if (images.TryGetValue(path, out image))
                {
                    images.Remove(path);
                    DisposeUnlessRetained(image);
                }
            }
        }

        public void Dispose()
        {
            foreach (Image image in images.Values)
            {
                if (image != null)
                {
                    image.Dispose();
                }
            }

            images.Clear();
            order.Clear();
            nodes.Clear();
        }
    }
}
