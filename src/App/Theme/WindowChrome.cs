using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VideoMaterialRenamer
{
    // DWM 沉浸式深色标题栏（阶段9c）：护眼模式下 OS 标题栏一并调暗。
    // Windows 10 1903+ 用属性 20，1809 回退属性 19；不支持的系统静默
    // 保持默认（纯视觉能力，绝不影响功能）。句柄未创建时挂一次性事件，
    // 等句柄就绪再设置；主题切换时句柄已存在，走直达路径。
    public static class WindowChrome
    {
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmwaUseImmersiveDarkMode = 20;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void ApplyImmersiveDarkMode(Form form, bool dark)
        {
            if (form == null)
            {
                return;
            }

            if (form.IsHandleCreated)
            {
                SetImmersiveDarkMode(form.Handle, dark);
                return;
            }

            EventHandler onceOnCreated = null;
            onceOnCreated = delegate
            {
                form.HandleCreated -= onceOnCreated;
                SetImmersiveDarkMode(form.Handle, dark);
            };
            form.HandleCreated += onceOnCreated;
        }

        private static void SetImmersiveDarkMode(IntPtr handle, bool dark)
        {
            try
            {
                int value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, 4) != 0)
                {
                    DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref value, 4);
                }
            }
            catch
            {
            }
        }
    }
}
