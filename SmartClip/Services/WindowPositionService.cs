using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SmartClip.Services
{
    public class WindowPositionService
    {
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool GetCaretPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // 存储检测到的系统窗口区域
        private RECT? _systemWindowRect;

        /// <summary>
        /// 计算窗口位置（直接跟随鼠标）
        /// </summary>
        public System.Windows.Point GetOptimalWindowPosition(double windowWidth, double windowHeight)
        {
            // 直接使用鼠标位置
            var mousePos = GetMousePosition();
            return AdjustForScreenBounds(mousePos, windowWidth, windowHeight);
        }

        /// <summary>
        /// 检测是否存在系统特权窗口（搜索框、开始菜单等）
        /// </summary>
        private RECT? DetectSystemWindow()
        {
            RECT? result = null;

            // 检测 Windows 11/10 搜索框和开始菜单
            string[] systemClassNames = new[]
            {
                "Windows.UI.Core.CoreWindow",      // 搜索框、开始菜单
                "XamlExplorerHostIslandWindow",    // Windows 11 搜索面板
                "SearchPane",                       // 搜索面板
                "Shell_TrayWnd"                     // 任务栏（不需要避让，但可以检测）
            };

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                StringBuilder className = new StringBuilder(256);
                GetClassName(hWnd, className, 256);
                string classNameStr = className.ToString();

                // 检查是否是系统窗口
                foreach (var sysClass in systemClassNames)
                {
                    if (classNameStr.Contains(sysClass) && sysClass != "Shell_TrayWnd")
                    {
                        if (GetWindowRect(hWnd, out RECT rect))
                        {
                            // 检查窗口是否有实际大小（排除隐藏的窗口）
                            int width = rect.Right - rect.Left;
                            int height = rect.Bottom - rect.Top;

                            if (width > 100 && height > 100)
                            {
                                result = rect;
                                return false; // 停止枚举
                            }
                        }
                    }
                }

                return true; // 继续枚举
            }, IntPtr.Zero);

            return result;
        }

        /// <summary>
        /// 检查当前前台窗口是否是系统特权窗口
        /// </summary>
        public bool IsSystemWindowActive()
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;

            StringBuilder className = new StringBuilder(256);
            GetClassName(foreground, className, 256);
            string classNameStr = className.ToString();

            return classNameStr.Contains("Windows.UI.Core.CoreWindow") ||
                   classNameStr.Contains("XamlExplorerHostIslandWindow") ||
                   classNameStr.Contains("SearchPane") ||
                   classNameStr.Contains("SearchHost");
        }

        /// <summary>
        /// 获取文本光标位置
        /// </summary>
        private System.Windows.Point? GetCaretPosition()
        {
            try
            {
                var foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero) return null;

                GetWindowThreadProcessId(foregroundWindow, out _);
                uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);
                uint currentThreadId = GetCurrentThreadId();

                // 附加到前台窗口的线程
                bool attached = AttachThreadInput(currentThreadId, foregroundThreadId, true);
                if (!attached) return null;

                try
                {
                    var focusWindow = GetFocus();
                    if (focusWindow == IntPtr.Zero) return null;

                    if (GetCaretPos(out POINT caretPoint))
                    {
                        ClientToScreen(focusWindow, ref caretPoint);

                        double dpiScale = GetDpiScale(caretPoint);
                        return new System.Windows.Point(caretPoint.X / dpiScale, caretPoint.Y / dpiScale + 20); // 稍微偏下
                    }
                }
                finally
                {
                    AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// 获取鼠标位置
        /// </summary>
        private System.Windows.Point GetMousePosition()
        {
            GetCursorPos(out POINT point);
            double dpiScale = GetDpiScale(point);
            return new System.Windows.Point(point.X / dpiScale, point.Y / dpiScale);
        }

        /// <summary>
        /// 获取DPI缩放比例
        /// </summary>
        private double GetDpiScale(POINT point)
        {
            try
            {
                var monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
                GetDpiForMonitor(monitor, 0, out uint dpiX, out _);
                return dpiX / 96.0;
            }
            catch
            {
                return 1.0;
            }
        }

        /// <summary>
        /// 调整位置以确保在屏幕内
        /// </summary>
        private System.Windows.Point AdjustForScreenBounds(System.Windows.Point point, double windowWidth, double windowHeight)
        {
            // 获取鼠标所在的屏幕
            GetCursorPos(out POINT cursorPoint);
            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(cursorPoint.X, cursorPoint.Y));

            double dpiScale = GetDpiScale(cursorPoint);

            // 屏幕工作区域（排除任务栏）
            var workArea = screen.WorkingArea;
            double left = workArea.Left / dpiScale;
            double top = workArea.Top / dpiScale;
            double right = workArea.Right / dpiScale;
            double bottom = workArea.Bottom / dpiScale;

            double x = point.X;
            double y = point.Y;

            // 确保窗口不超出屏幕右边界
            if (x + windowWidth > right)
            {
                x = right - windowWidth - 10;
            }

            // 确保窗口不超出屏幕下边界
            if (y + windowHeight > bottom)
            {
                y = bottom - windowHeight - 10;
            }

            // 确保窗口不超出屏幕左边界
            if (x < left)
            {
                x = left + 10;
            }

            // 确保窗口不超出屏幕上边界
            if (y < top)
            {
                y = top + 10;
            }

            return new System.Windows.Point(x, y);
        }
    }
}
