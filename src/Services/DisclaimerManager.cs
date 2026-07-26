using System;
using System.IO;
using System.Text;

namespace VideoMaterialRenamer
{
    // 免责声明确认记录的存储（阶段13a：对话框编排移至 App/Presenters/
    // DisclaimerGate，本类只剩纯读写，不再触碰 WinForms）。
    // 记录文件格式为既有行为：AcceptedV1|UTC刻度|版本。
    public static class DisclaimerManager
    {
        private const string AcceptanceVersion = "DisclaimerAcceptedV1";
        private static readonly string AcceptancePath = Path.Combine(AppInfo.AppDataDirectory, "disclaimer.accepted");

        public static bool IsAccepted()
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

        public static bool TryRecordAcceptance(out string error)
        {
            error = "";
            try
            {
                Directory.CreateDirectory(AppInfo.AppDataDirectory);
                string text = AcceptanceVersion + "|" + DateTime.UtcNow.Ticks.ToString() + "|" + AppInfo.Version;
                File.WriteAllText(AcceptancePath, text, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLog.Write("disclaimer", "保存免责确认记录失败", ex);
                return false;
            }
        }
    }
}
