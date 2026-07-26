using System;
using System.Windows.Forms;

namespace VideoMaterialRenamer
{
    // 免责声明闸门的 UI 侧（阶段13a，自 DisclaimerManager 逐字迁入）：
    // 对话框展示与保存失败提示。确认记录的读写留在 Services。
    public static class DisclaimerGate
    {
        public static bool EnsureAccepted(IWin32Window owner, bool darkMode)
        {
            if (DisclaimerManager.IsAccepted())
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

            string error;
            if (!DisclaimerManager.TryRecordAcceptance(out error))
            {
                MessageBox.Show(owner, "无法保存免责协议确认记录，本次仍会继续运行。\r\n\r\n" + error, "保存确认记录失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return true;
        }
    }
}
