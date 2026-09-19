using System;
using System.Runtime.InteropServices;

namespace DshInstaller
{
    /// <summary>需要的那几个 Win32 调用。</summary>
    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern int GetDpiForWindow(IntPtr hwnd);

        // ---- 去掉最大化按钮 ----
        // Windows App SDK 1.8 的 AppWindowTitleBar 没有 IsMaximizeButtonVisible,
        // 只能改窗口样式:去掉 WS_MAXIMIZEBOX,系统就不画那个按钮了。
        public const int GWL_STYLE = -16;
        public const int WS_MAXIMIZEBOX = 0x00010000;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_FRAMECHANGED = 0x0020;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        // ---- 拖拽窗口 ----
        // 不能用 SendMessage(WM_NCLBUTTONDOWN) 那套:它会进入系统的模态拖动循环,
        // 把 UI 线程堵住 —— WinUI3 下表现成"指针变忙、要再点一下才结束"。
        // 而且 WinUI3 的命中测试在内容岛上,顶层窗口收不到 WM_NCHITTEST。
        // 所以在拖拽区按下时自己算位移,用屏幕坐标(AppWindow.Move 也是物理像素)。
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        /// <summary>取当前鼠标的屏幕坐标(物理像素)。</summary>
        public static bool TryGetCursorPosition(out int x, out int y)
        {
            x = 0;
            y = 0;
            try
            {
                POINT point;
                if (!GetCursorPos(out point))
                {
                    return false;
                }

                x = point.X;
                y = point.Y;
                return true;
            }
            catch
            {
                return false;
            }
        }
        /// <summary>把这个窗口的最大化按钮拿掉(不是禁用,是不画)。</summary>
        public static void RemoveMaximizeButton(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            try
            {
                int style = GetWindowLong(hwnd, GWL_STYLE);
                bool hadMaximize = (style & WS_MAXIMIZEBOX) != 0;

                if (!hadMaximize)
                {
                    Diag("样式里本来就没有 WS_MAXIMIZEBOX,style=0x" + style.ToString("X8"));
                    return;
                }

                int newStyle = SetWindowLong(hwnd, GWL_STYLE, style & ~WS_MAXIMIZEBOX);
                bool applied = SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

                int check = GetWindowLong(hwnd, GWL_STYLE);
                Diag("去最大化按钮: 原=0x" + style.ToString("X8")
                    + " 改后=0x" + check.ToString("X8")
                    + " SetWindowLong 返回=" + newStyle.ToString("X8")
                    + " SetWindowPos=" + applied
                    + " 现在还有最大化位=" + ((check & WS_MAXIMIZEBOX) != 0));
            }
            catch (Exception exception)
            {
                Diag("去最大化按钮失败: " + exception.GetType().Name + " " + exception.Message);
            }
        }

        private static void Diag(string message)
        {
            try
            {
                string directory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekHarness");
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(directory, "installer-ui.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  [native] " + message + Environment.NewLine,
                    new System.Text.UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }
}
