using System;
using System.Windows.Forms;

namespace VideoMaterialRenamer
{
    // 授权闸门的 UI 侧（阶段13a，自 LicenseManager 逐字迁入）：激活对话框
    // 编排与续期提醒弹窗。验证与 DPAPI 存储留在 Services（LicenseManager/
    // LicenseValidator），自此 Services 不再触碰 WinForms。
    public static class LicenseGate
    {
        private const int RenewalReminderDays = 3;

        public static bool EnsureLicensed(IWin32Window owner)
        {
            LicenseInfo ignored;
            return EnsureLicensed(owner, out ignored);
        }

        public static bool EnsureLicensed(IWin32Window owner, out LicenseInfo activeInfo)
        {
            activeInfo = null;
            LicenseInfo info;
            string error;
            if (LicenseManager.ValidateStoredLicense(out info, out error))
            {
                activeInfo = info;
                ShowRenewalReminder(owner, info);
                return true;
            }

            using (LicenseDialog dialog = new LicenseDialog(LicenseManager.GetMachineCode(), error))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK)
                {
                    return false;
                }

                if (LicenseManager.ValidateStoredLicense(out activeInfo, out error))
                {
                    ShowRenewalReminder(owner, activeInfo);
                    return true;
                }

                MessageBox.Show(owner, error, "授权状态异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private static void ShowRenewalReminder(IWin32Window owner, LicenseInfo info)
        {
            double daysLeftExact = (info.ExpiresUtc - DateTime.UtcNow).TotalDays;
            if (daysLeftExact <= RenewalReminderDays)
            {
                int daysLeft = LicenseManager.GetRemainingDays(info);
                MessageBox.Show(
                    owner,
                    "授权将在 " + daysLeft + " 天后到期，请及时获取新的密钥续期。",
                    "授权即将到期",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
    }
}
