using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SmartClip.Services;
using SmartClip.ViewModels;
using SmartClip.Views;
using Wpf.Ui.Controls;

namespace SmartClip
{
    public partial class MainWindow : FluentWindow
    {
        private readonly StorageService _storageService;
        private readonly ClipboardService _clipboardService;
        private readonly HotkeyService _hotkeyService;
        private readonly WindowPositionService _windowPositionService;
        private readonly PasteSimulatorService _pasteSimulator;
        private readonly TrayService _trayService;
        private readonly MainViewModel _viewModel;

        private Storyboard? _fadeOutStoryboard;
        private bool _isShowingDialog;
        private IntPtr _hwnd;

        // 全局鼠标钩子
        private IntPtr _mouseHookId = IntPtr.Zero;
        private LowLevelMouseProc? _mouseProc;

        // Win32 常量
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        // Win32 API
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimumWorkingSetSize, IntPtr maximumWorkingSetSize);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // 鼠标钩子相关
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        public MainWindow()
        {
            InitializeComponent();

            // 初始化服务
            _storageService = new StorageService();
            _clipboardService = new ClipboardService(_storageService);
            _hotkeyService = new HotkeyService();
            _windowPositionService = new WindowPositionService();
            _pasteSimulator = new PasteSimulatorService();

            // 初始化托盘服务
            _trayService = new TrayService(
                showWindowAction: () => Dispatcher.Invoke(() =>
                {
                    if (!IsVisible)
                    {
                        _pasteSimulator.SaveActiveWindow();
                        ShowWindow();
                    }
                }),
                exitAction: () => Dispatcher.Invoke(() =>
                {
                    System.Windows.Application.Current.Shutdown();
                })
            );
            _trayService.Initialize();

            // 初始化ViewModel
            _viewModel = new MainViewModel(_storageService, _clipboardService);
            DataContext = _viewModel;

            // 订阅热键事件
            _hotkeyService.WinVPressed += OnWinVPressed;

            // 缓存淡出动画并设置事件
            _fadeOutStoryboard = (Storyboard)FindResource("FadeOutStoryboard");
            _fadeOutStoryboard.Completed += FadeOut_Completed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var helper = new WindowInteropHelper(this);
            _hwnd = helper.Handle;

            // 设置给粘贴服务
            _pasteSimulator.SetOurWindow(_hwnd);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 开始监听剪贴板
            _clipboardService.StartListening(this);

            // 安装热键钩子
            _hotkeyService.Install();

            // 加载历史记录
            await _viewModel.LoadAsync();

            // 执行清理
            await _storageService.CleanupAsync();

            // 首次加载后隐藏窗口
            Hide();

            // 触发GC释放初始化内存
            TrimMemory();
        }

        private void OnWinVPressed(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (IsVisible)
                {
                    HideWindow();
                }
                else
                {
                    // 在显示窗口之前保存当前活动窗口
                    _pasteSimulator.SaveActiveWindow();
                    ShowWindow();
                }
            });
        }

        private void ShowWindow()
        {
            // 刷新列表和序号
            _viewModel.RefreshItems();
            UpdateIndexDisplay();

            // 计算位置
            var position = _windowPositionService.GetOptimalWindowPosition(Width, Height);
            Left = position.X;
            Top = position.Y;

            // 重置透明度
            Opacity = 0;

            // 显示窗口
            Show();

            // 强制置顶
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

            // 激活窗口并获取焦点
            Activate();
            SetForegroundWindow(_hwnd);
            Focus();

            // 播放淡入动画
            var fadeIn = (Storyboard)FindResource("FadeInStoryboard");
            BeginStoryboard(fadeIn);

            // 安装全局鼠标钩子检测点击外部
            InstallMouseHook();
        }

        private void HideWindow()
        {
            if (!IsVisible) return;

            // 卸载鼠标钩子
            UninstallMouseHook();

            // 播放淡出动画
            BeginStoryboard(_fadeOutStoryboard);

            // 清空搜索
            _viewModel.SearchText = string.Empty;
        }

        private void FadeOut_Completed(object? sender, EventArgs e)
        {
            Hide();
            Opacity = 1;

            // 隐藏后释放内存
            TrimMemory();
        }

        #region 全局鼠标钩子 - 检测点击外部

        private void InstallMouseHook()
        {
            if (_mouseHookId != IntPtr.Zero) return;

            _mouseProc = MouseHookCallback;
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            using var module = process.MainModule;
            _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(module?.ModuleName ?? "SmartClip"), 0);
        }

        private void UninstallMouseHook()
        {
            if (_mouseHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_LBUTTONDOWN || wParam == (IntPtr)WM_RBUTTONDOWN))
            {
                if (_isShowingDialog)
                {
                    return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
                }

                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var mousePoint = new System.Windows.Point(hookStruct.pt.x, hookStruct.pt.y);

                // 获取窗口位置（转换为屏幕物理像素）
                var source = PresentationSource.FromVisual(this);
                if (source != null)
                {
                    var dpiX = source.CompositionTarget.TransformToDevice.M11;
                    var dpiY = source.CompositionTarget.TransformToDevice.M22;

                    var windowLeft = Left * dpiX;
                    var windowTop = Top * dpiY;
                    var windowRight = windowLeft + ActualWidth * dpiX;
                    var windowBottom = windowTop + ActualHeight * dpiY;

                    // 检查点击是否在窗口外部
                    if (mousePoint.X < windowLeft || mousePoint.X > windowRight ||
                        mousePoint.Y < windowTop || mousePoint.Y > windowBottom)
                    {
                        // 在UI线程隐藏窗口
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (IsVisible)
                            {
                                HideWindow();
                            }
                        }));
                    }
                }
            }

            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        #endregion

        /// <summary>
        /// 释放内存
        /// </summary>
        private void TrimMemory()
        {
            GC.Collect(2, GCCollectionMode.Optimized, false);
            GC.WaitForPendingFinalizers();

            try
            {
                SetProcessWorkingSetSize(
                    System.Diagnostics.Process.GetCurrentProcess().Handle,
                    (IntPtr)(-1),
                    (IntPtr)(-1));
            }
            catch { }
        }

        private void UpdateIndexDisplay()
        {
            var items = _viewModel.FilteredItems.Cast<ClipboardItemViewModel>().ToList();
            for (int i = 0; i < items.Count; i++)
            {
                items[i].IndexDisplay = i < 9 ? (i + 1).ToString() : "";
            }
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            // 由于 WS_EX_NOACTIVATE，此事件通常不触发
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 点击窗口内部时，手动激活窗口以接收键盘输入
            // 但由于返回 MA_NOACTIVATE，需要手动处理
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // 由于 WS_EX_NOACTIVATE，此事件通常不触发
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    HideWindow();
                    e.Handled = true;
                    break;

                case Key.Enter:
                    PasteSelected(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                    e.Handled = true;
                    break;

                case Key.Up:
                    _viewModel.SelectPrevious();
                    ClipboardList.ScrollIntoView(_viewModel.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.Down:
                    _viewModel.SelectNext();
                    ClipboardList.ScrollIntoView(_viewModel.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.Delete:
                    _ = _viewModel.DeleteSelectedAsync();
                    UpdateIndexDisplay();
                    e.Handled = true;
                    break;

                case Key.P when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                    _ = _viewModel.TogglePinSelectedAsync();
                    UpdateIndexDisplay();
                    e.Handled = true;
                    break;

                // 数字键 1-9 快速粘贴
                case Key.D1:
                case Key.D2:
                case Key.D3:
                case Key.D4:
                case Key.D5:
                case Key.D6:
                case Key.D7:
                case Key.D8:
                case Key.D9:
                    if (!SearchBox.IsFocused || string.IsNullOrEmpty(_viewModel.SearchText))
                    {
                        int index = e.Key - Key.D1;
                        PasteByIndex(index);
                        e.Handled = true;
                    }
                    break;

                case Key.NumPad1:
                case Key.NumPad2:
                case Key.NumPad3:
                case Key.NumPad4:
                case Key.NumPad5:
                case Key.NumPad6:
                case Key.NumPad7:
                case Key.NumPad8:
                case Key.NumPad9:
                    int numIndex = e.Key - Key.NumPad1;
                    PasteByIndex(numIndex);
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>
        /// 粘贴选中项（不关闭窗口）
        /// </summary>
        private void PasteSelected(bool plainTextOnly = false)
        {
            if (_viewModel.SelectedItem == null) return;

            // 1. 设置剪贴板内容
            _viewModel.PasteSelectedAsync(plainTextOnly);

            // 2. 模拟粘贴到目标窗口
            _pasteSimulator.SimulatePaste();

            // 3. 刷新列表
            _viewModel.RefreshItems();
            UpdateIndexDisplay();
        }

        /// <summary>
        /// 按索引粘贴（不关闭窗口）
        /// </summary>
        private void PasteByIndex(int index)
        {
            var items = _viewModel.FilteredItems.Cast<ClipboardItemViewModel>().ToList();
            if (index < 0 || index >= items.Count) return;

            // 1. 设置剪贴板内容
            _viewModel.PasteByIndexAsync(index);

            // 2. 模拟粘贴到目标窗口
            _pasteSimulator.SimulatePaste();

            // 3. 刷新列表
            _viewModel.RefreshItems();
            UpdateIndexDisplay();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 不再使用
        }

        /// <summary>
        /// 窗口鼠标按下事件 - 实现任意空白区域拖动
        /// </summary>
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 检查点击的元素是否是可交互控件
            var element = e.OriginalSource as DependencyObject;
            while (element != null)
            {
                if (element is System.Windows.Controls.Button ||
                    element is System.Windows.Controls.TextBox ||
                    element is System.Windows.Controls.ListBoxItem ||
                    element is Wpf.Ui.Controls.TextBox)
                {
                    return;
                }
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }

            // 空白区域，允许拖动
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void ClipboardList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            PasteSelected();
        }

        private void ItemMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is ClipboardItemViewModel item)
            {
                _viewModel.SelectedItem = item;
                PinMenuItem.Header = item.Model.IsPinned ? "取消置顶" : "置顶";
                ItemContextMenu.PlacementTarget = button;

                // 订阅菜单关闭事件
                ItemContextMenu.Closed -= ContextMenu_Closed;
                ItemContextMenu.Closed += ContextMenu_Closed;

                // 标记菜单已打开，防止鼠标钩子误关闭窗口
                _isShowingDialog = true;
                ItemContextMenu.IsOpen = true;
            }
        }

        private void ContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            // 延迟重置标志，确保菜单点击事件完成后再允许外部点击关闭
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isShowingDialog = false;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            PasteSelected();
        }

        private void PastePlainText_Click(object sender, RoutedEventArgs e)
        {
            PasteSelected(true);
        }

        private async void Pin_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.TogglePinSelectedAsync();
            UpdateIndexDisplay();
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.DeleteSelectedAsync();
            UpdateIndexDisplay();
        }

        private async void ClearUnpinned_Click(object sender, RoutedEventArgs e)
        {
            _isShowingDialog = true;

            try
            {
                var dialog = new ConfirmDialog(
                    "确认清除",
                    "确定要清除所有非置顶记录吗？此操作无法撤销。",
                    "清除");
                dialog.Owner = this;
                dialog.ShowDialog();

                if (dialog.IsConfirmed)
                {
                    await _viewModel.ClearUnpinnedAsync();
                    UpdateIndexDisplay();
                    _viewModel.ShowStatus("已清除");
                }
            }
            finally
            {
                _isShowingDialog = false;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            UninstallMouseHook();
            _trayService.Dispose();
            _hotkeyService.Dispose();
            _clipboardService.StopListening();
            base.OnClosed(e);
        }
    }

    // 转换器
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
