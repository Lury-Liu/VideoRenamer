using System.Windows.Forms;

namespace VideoRenamer
{
    // 双缓冲控件子类：DataGridView/ListView 默认不开双缓冲，导出进度列
    // 逐格重绘与预览整表刷新会产生可见闪烁（评估确认全仓库无任何
    // DoubleBuffered 设置）。
    public class DoubleBufferedGridView : DataGridView
    {
        public DoubleBufferedGridView()
        {
            DoubleBuffered = true;
        }
    }

    public class DoubleBufferedListView : ListView
    {
        public DoubleBufferedListView()
        {
            DoubleBuffered = true;
        }
    }
}
