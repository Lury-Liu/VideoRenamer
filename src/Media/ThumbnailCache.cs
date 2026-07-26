using System;
using System.Collections.Generic;
using System.Drawing;

namespace VideoMaterialRenamer
{
    // 缩略图 LRU 缓存：字典 + 触达顺序链表 + 上限裁剪，图像生命周期归本类所有
    // （加入即接管，替换/淘汰/释放时 Dispose）。语义与原窗体内三字段实现
    // 逐行为一致。仅限 UI 线程访问（与原实现相同，不加锁）。
    //
    // 已知遗留（评估阶段发现，修复排在阶段7）：被淘汰的图像若恰好正被
    // 详情面板展示（借用式引用），淘汰 Dispose 后重绘会触发 GDI+ 异常；
    // 修复方案是淘汰前与“当前展示图像”做 ReferenceEquals 保护。
    public sealed class ThumbnailCache : IDisposable
    {
        private readonly int limit;
        private readonly Dictionary<string, Image> images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> order = new LinkedList<string>();
        private readonly Dictionary<string, LinkedListNode<string>> nodes = new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase);

        public ThumbnailCache(int limit)
        {
            this.limit = Math.Max(1, limit);
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
                existing.Dispose();
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
                if (image != null)
                {
                    image.Dispose();
                }
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
                    if (image != null)
                    {
                        image.Dispose();
                    }
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
