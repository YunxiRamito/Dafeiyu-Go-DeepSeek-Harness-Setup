using System;
using System.Runtime.InteropServices;

namespace DshInstaller
{
    /// <summary>
    /// 接管窗口过程,把整条非客户区(标题栏 + 那三个系统按钮)彻底抹掉。
    ///
    /// 为什么非得这么干:只要窗口还有非客户区,系统就会在那儿画最小化/最大化/关闭。
    /// 前面试过两条路都不干净:
    ///   1. 去掉 WS_MAXIMIZEBOX —— Win10 1809 上没用,照样画一个"禁用态"灰按钮;
    ///   2. 把按钮前景设透明 —— 禁用态用系统内部色压不住,而且透明不等于不可点,
    ///      自绘按钮一旦和系统槽位错位就会点到那个灰方块上变死区。
    /// 把非客户区归零之后,系统没有绘制目标,三个按钮自然不存在。
    ///
    /// 副作用与补救:
    ///   - 圆角/投影会丢(那是 DWM 给"有边框窗口"画的)。这里用
    ///     DwmExtendFrameIntoClientArea 把边框要回来一像素,再给 Win11 打开圆角属性。
    ///   - 拖拽没了。改用 WM_NCHITTEST 返回 HTCAPTION 兜。
    /// </summary>
    internal static class WindowChrome
    {
        private const int GWLP_WNDPROC = -4;
        private const int GWL_STYLE = -16;

        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_MINIMIZEBOX = 0x00020000;

        private const uint WM_NCCALCSIZE = 0x0083;
        private const uint WM_NCHITTEST = 0x0084;
        private const uint WM_NCACTIVATE = 0x0086;

        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;

        /// <summary>DwmSetWindowAttribute:窗口圆角偏好。</summary>
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        /// <summary>DWMWCP_ROUND。</summary>
        private const int DWMWCP_ROUND = 2;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        // 必须用静态字段保住委托引用:被 GC 回收后窗口过程就指向野地址,直接崩。
        private static WndProcDelegate _handler;
        private static IntPtr _previous;

        private static int _dragHeight;
        private static int _buttonWidth;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS margins);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        public static void Attach(IntPtr hwnd, int dragHeight, int buttonWidth)
        {
            if (hwnd == IntPtr.Zero || _handler != null)
            {
                return;
            }

            _dragHeight = dragHeight;
            _buttonWidth = buttonWidth;

            RestoreFrameForShadow(hwnd);

            Diag("Attach: 拖拽高=" + dragHeight + " 按钮宽=" + buttonWidth);

            _handler = Handler;
            IntPtr pointer = Marshal.GetFunctionPointerForDelegate(_handler);
            if (IntPtr.Size == 8)
            {
                _previous = SetWindowLongPtr64(hwnd, GWLP_WNDPROC, pointer);
            }
            else
            {
                _previous = new IntPtr(SetWindowLong32(hwnd, GWLP_WNDPROC, pointer.ToInt32()));
            }

            Diag("子类化: 原proc=" + _previous.ToString("X") + " 新proc=" + pointer.ToString("X"));
        }

        /// <summary>
        /// 把 WS_THICKFRAME 之类的框加回窗口样式。
        ///
        /// 看着矛盾(我们明明要把边框抹掉),但 DWM 只给"有边框的窗口"画投影和圆角;
        /// 光靠 WM_NCCALCSIZE 归零会让窗口变成硬边没投影。所以留着框、但把非客户区归零,
        /// 视觉上还是无边框,同时又拿回了 DWM 的投影和圆角。
        /// 拖拽/缩放由 WM_NCHITTEST 控制,不会因为这个框变成可拉伸。
        /// </summary>
        private static void RestoreFrameForShadow(IntPtr hwnd)
        {
            try
            {
                int style = GetWindowLong(hwnd, GWL_STYLE);
                int wanted = style | WS_THICKFRAME | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
                if (wanted != style)
                {
                    if (IntPtr.Size == 8)
                    {
                        SetWindowLongPtr64(hwnd, GWL_STYLE, new IntPtr(wanted));
                    }
                    else
                    {
                        SetWindowLong32(hwnd, GWL_STYLE, wanted);
                    }
                }

                // 让 DWM 在四边各画一像素的"框" —— 投影和圆角就是从这儿来的
                MARGINS margins = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);

                // Win11:显式要求圆角。Win10 上这个属性会被忽略(系统本身没有圆角),
                // 调不成功也无害。
                int preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch
            {
            }
        }

        /// <summary>窗口尺寸或缩放变化后,更新拖拽区的判定范围。</summary>
        public static void UpdateDragArea(int dragHeight, int buttonWidth)
        {
            _dragHeight = dragHeight;
            _buttonWidth = buttonWidth;
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
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  [chrome] " + message + Environment.NewLine,
                    new System.Text.UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static IntPtr Handler(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            switch (message)
            {
                case WM_NCCALCSIZE:
                    // wParam != 0 表示要重算客户区。返回 0 且不改 lParam 里的矩形,
                    // 客户区就等于整个窗口 —— 非客户区归零,系统没地方画标题栏和按钮。
                    if (wParam != IntPtr.Zero)
                    {
                        return IntPtr.Zero;
                    }

                    break;

                case WM_NCACTIVATE:
                    return new IntPtr(1);

                // 注意:这里**不能**用 WM_NCHITTEST 返回 HTCAPTION 来做拖拽。
                // WinUI3 的命中测试跑在内容岛(ContentIsland)上,顶层窗口根本收不到这条消息
                // (实测:子类化成功、鼠标划过标题栏,一条 WM_NCHITTEST 都没有)。
                // 拖拽改由 NativeMethods.BeginDragMove 在 PointerPressed 里主动触发。
            }

            return CallWindowProc(_previous, hWnd, message, wParam, lParam);
        }
    }
}
