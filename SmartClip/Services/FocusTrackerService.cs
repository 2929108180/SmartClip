using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SmartClip.Services
{
    /// <summary>
    /// 焦点追踪服务 - 使用 WinEventHook 实时追踪前台窗口变化
    /// </summary>
    public class FocusTrackerService : IDisposable
    {
        // Win32 API
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        private WinEventDelegate? _hookDelegate;
        private IntPtr _hookId;
        private IntPtr _ourWindowHandle;

        /// <summary>
        /// 最后一个有效的目标窗口句柄（排除我们自己的窗口）
        /// </summary>
        public IntPtr LastTargetWindowHandle { get; private set; }

        /// <summary>
        /// 最后一个有效的焦点控件句柄
        /// </summary>
        public IntPtr LastFocusedControlHandle { get; private set; }

        /// <summary>
        /// 开始追踪前台窗口变化
        /// </summary>
        public void StartTracking(IntPtr ourWindowHandle)
        {
            _ourWindowHandle = ourWindowHandle;

            // 保持委托引用，防止被GC回收
            _hookDelegate = new WinEventDelegate(WinEventProc);

            // 设置事件钩子监听前台窗口变化
            _hookId = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND,
                EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _hookDelegate,
                0,
                0,
                WINEVENT_OUTOFCONTEXT);

            // 立即获取当前前台窗口作为初始值
            var currentForeground = GetForegroundWindow();
            if (currentForeground != IntPtr.Zero && currentForeground != _ourWindowHandle)
            {
                LastTargetWindowHandle = currentForeground;
                CaptureCurrentFocus(currentForeground);
            }
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // 只有当新激活的窗口不是我们自己的剪贴板窗口时，才更新目标
            if (hwnd != IntPtr.Zero && hwnd != _ourWindowHandle)
            {
                LastTargetWindowHandle = hwnd;
                CaptureCurrentFocus(hwnd);
                Debug.WriteLine($"[FocusTracker] 目标窗口已更新: {hwnd}");
            }
        }

        /// <summary>
        /// 捕获当前焦点控件
        /// </summary>
        private void CaptureCurrentFocus(IntPtr targetWindow)
        {
            uint targetThreadId = GetWindowThreadProcessId(targetWindow, out _);
            uint currentThreadId = GetCurrentThreadId();

            bool attached = false;
            if (targetThreadId != currentThreadId)
            {
                attached = AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            LastFocusedControlHandle = GetFocus();

            if (attached)
            {
                AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }

        /// <summary>
        /// 手动更新目标窗口（在打开剪贴板时调用）
        /// </summary>
        public void CaptureCurrentTarget()
        {
            var foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero && foreground != _ourWindowHandle)
            {
                LastTargetWindowHandle = foreground;
                CaptureCurrentFocus(foreground);
            }
        }

        /// <summary>
        /// 停止追踪
        /// </summary>
        public void StopTracking()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWinEvent(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            StopTracking();
        }
    }
}
