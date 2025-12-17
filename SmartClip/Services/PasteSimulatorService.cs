using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SmartClip.Services
{
    public class PasteSimulatorService
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(uint dwProcessId);

        private const uint ASFW_ANY = unchecked((uint)-1);

        private const byte VK_CONTROL = 0x11;
        private const byte VK_V = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private IntPtr _lastActiveWindow;
        private IntPtr _lastFocusedControl;
        private IntPtr _ourWindow;

        public void SaveActiveWindow()
        {
            _lastActiveWindow = GetForegroundWindow();

            // 同时保存焦点控件
            if (_lastActiveWindow != IntPtr.Zero)
            {
                uint targetThreadId = GetWindowThreadProcessId(_lastActiveWindow, out _);
                uint currentThreadId = GetCurrentThreadId();

                bool attached = false;
                if (targetThreadId != currentThreadId)
                {
                    attached = AttachThreadInput(currentThreadId, targetThreadId, true);
                }

                _lastFocusedControl = GetFocus();

                if (attached)
                {
                    AttachThreadInput(currentThreadId, targetThreadId, false);
                }
            }
        }

        public void SetOurWindow(IntPtr hwnd)
        {
            _ourWindow = hwnd;
        }

        /// <summary>
        /// 模拟粘贴到保存的目标窗口
        /// </summary>
        public void SimulatePaste()
        {
            if (_lastActiveWindow == IntPtr.Zero) return;

            // 允许任何进程设置前台窗口
            AllowSetForegroundWindow(ASFW_ANY);

            uint targetThreadId = GetWindowThreadProcessId(_lastActiveWindow, out uint targetProcessId);
            uint currentThreadId = GetCurrentThreadId();

            // 附加输入线程
            bool attached = false;
            if (targetThreadId != currentThreadId)
            {
                attached = AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            try
            {
                // 激活目标窗口
                SetForegroundWindow(_lastActiveWindow);
                BringWindowToTop(_lastActiveWindow);

                // 恢复焦点到之前的控件
                if (_lastFocusedControl != IntPtr.Zero)
                {
                    SetFocus(_lastFocusedControl);
                }

                // 等待窗口激活
                Thread.Sleep(30);

                // 发送 Ctrl+V
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                Thread.Sleep(10);
                keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                Thread.Sleep(10);
                keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                Thread.Sleep(10);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(currentThreadId, targetThreadId, false);
                }
            }
        }

        public Task SimulatePasteAsync()
        {
            SimulatePaste();
            return Task.CompletedTask;
        }
    }
}
