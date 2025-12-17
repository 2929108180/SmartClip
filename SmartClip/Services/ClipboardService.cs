using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SmartClip.Models;

namespace SmartClip.Services
{
    public class ClipboardService
    {
        private readonly StorageService _storageService;

        // 剪贴板消息
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private IntPtr _hwnd;
        private HwndSource? _hwndSource;
        private bool _isListening;

        // 用于防止自己设置剪贴板时触发
        private bool _isSettingClipboard;

        public event EventHandler<ClipboardItem>? ClipboardChanged;

        public ClipboardService(StorageService storageService)
        {
            _storageService = storageService;
        }

        /// <summary>
        /// 开始监听剪贴板
        /// </summary>
        public void StartListening(System.Windows.Window window)
        {
            if (_isListening) return;

            var helper = new WindowInteropHelper(window);
            _hwnd = helper.Handle;

            if (_hwnd == IntPtr.Zero)
            {
                helper.EnsureHandle();
                _hwnd = helper.Handle;
            }

            _hwndSource = HwndSource.FromHwnd(_hwnd);
            _hwndSource?.AddHook(WndProc);

            AddClipboardFormatListener(_hwnd);
            _isListening = true;
        }

        /// <summary>
        /// 停止监听剪贴板
        /// </summary>
        public void StopListening()
        {
            if (!_isListening) return;

            RemoveClipboardFormatListener(_hwnd);
            _hwndSource?.RemoveHook(WndProc);
            _isListening = false;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE && !_isSettingClipboard)
            {
                _ = ProcessClipboardAsync();
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 处理剪贴板内容
        /// </summary>
        private async Task ProcessClipboardAsync()
        {
            // 重试机制
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    await Task.Delay(50 * (retry + 1)); // 等待剪贴板释放

                    ClipboardItem? item = null;

                    // 在UI线程获取剪贴板数据
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        item = GetClipboardContent();
                    });

                    if (item != null)
                    {
                        // 如果是图片，在后台线程处理保存
                        if (item.Type == ClipboardItemType.Image && item.ImageData != null)
                        {
                            await Task.Run(async () =>
                            {
                                var hash = StorageService.ComputeHash(item.ImageData);
                                var fileName = await _storageService.SaveImageAsync(item.ImageData);
                                item.ContentHash = hash;
                                item.ImagePath = fileName;
                                item.ImageData = null; // 清理临时数据
                            });
                        }

                        await _storageService.AddItemAsync(item);

                        // 在UI线程触发事件
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            ClipboardChanged?.Invoke(this, item);
                        });
                    }
                    break;
                }
                catch (Exception)
                {
                    if (retry == 2) break;
                }
            }
        }

        /// <summary>
        /// 获取剪贴板内容
        /// </summary>
        private ClipboardItem? GetClipboardContent()
        {
            try
            {
                var sourceApp = GetSourceAppName();

                // 优先检测文件列表
                if (System.Windows.Clipboard.ContainsFileDropList())
                {
                    var files = System.Windows.Clipboard.GetFileDropList();
                    var filePaths = new string[files.Count];
                    files.CopyTo(filePaths, 0);

                    var hash = StorageService.ComputeHash(string.Join("|", filePaths));
                    var item = new ClipboardItem
                    {
                        Type = ClipboardItemType.FileList,
                        FilePaths = filePaths,
                        ContentHash = hash,
                        SourceApp = sourceApp
                    };
                    item.GeneratePreview();
                    return item;
                }

                // 检测图片 - 只获取数据，不在UI线程处理保存
                if (System.Windows.Clipboard.ContainsImage())
                {
                    var image = System.Windows.Clipboard.GetImage();
                    if (image != null)
                    {
                        var imageData = BitmapSourceToBytes(image);
                        var item = new ClipboardItem
                        {
                            Type = ClipboardItemType.Image,
                            ImageData = imageData, // 临时存储，稍后在后台线程处理
                            SourceApp = sourceApp
                        };
                        item.GeneratePreview();
                        return item;
                    }
                }

                // 检测富文本
                string? htmlContent = null;
                string? rtfContent = null;
                string? textContent = null;

                if (System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.Html))
                {
                    htmlContent = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.Html);
                }

                if (System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.Rtf))
                {
                    rtfContent = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.Rtf);
                }

                if (System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.UnicodeText))
                {
                    textContent = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText);
                }
                else if (System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.Text))
                {
                    textContent = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.Text);
                }

                if (!string.IsNullOrEmpty(textContent))
                {
                    // 只对纯文本去除多余空格（富文本保持原样）
                    var isRichText = !string.IsNullOrEmpty(htmlContent) || !string.IsNullOrEmpty(rtfContent);
                    var processedText = isRichText ? textContent : NormalizeSpaces(textContent);

                    if (string.IsNullOrWhiteSpace(processedText))
                        return null;

                    var hash = StorageService.ComputeHash(processedText);

                    return new ClipboardItem
                    {
                        Type = isRichText ? ClipboardItemType.RichText : ClipboardItemType.Text,
                        TextContent = processedText,
                        HtmlContent = htmlContent,
                        RtfContent = rtfContent,
                        ContentHash = hash,
                        SourceApp = sourceApp
                    };
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 规范化空格（去除多余空格）
        /// </summary>
        private string NormalizeSpaces(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var sb = new StringBuilder();
            bool lastWasSpace = false;

            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c) && c != '\n' && c != '\r')
                {
                    if (!lastWasSpace)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// 设置剪贴板内容（快速版本）
        /// </summary>
        public bool SetClipboardContent(ClipboardItem item, bool plainTextOnly = false)
        {
            _isSettingClipboard = true;

            try
            {
                var dataObject = new System.Windows.DataObject();

                switch (item.Type)
                {
                    case ClipboardItemType.Text:
                        dataObject.SetData(System.Windows.DataFormats.UnicodeText, item.TextContent ?? string.Empty);
                        break;

                    case ClipboardItemType.RichText:
                        if (plainTextOnly)
                        {
                            dataObject.SetData(System.Windows.DataFormats.UnicodeText, item.TextContent ?? string.Empty);
                        }
                        else
                        {
                            dataObject.SetData(System.Windows.DataFormats.UnicodeText, item.TextContent ?? string.Empty);
                            if (!string.IsNullOrEmpty(item.HtmlContent))
                                dataObject.SetData(System.Windows.DataFormats.Html, item.HtmlContent);
                            if (!string.IsNullOrEmpty(item.RtfContent))
                                dataObject.SetData(System.Windows.DataFormats.Rtf, item.RtfContent);
                        }
                        break;

                    case ClipboardItemType.Image:
                        if (!string.IsNullOrEmpty(item.ImagePath))
                        {
                            var imagePath = _storageService.GetImageFullPath(item.ImagePath);
                            if (File.Exists(imagePath))
                            {
                                // 添加位图格式（用于粘贴到图像编辑器）
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.UriSource = new Uri(imagePath);
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.EndInit();
                                bitmap.Freeze();
                                dataObject.SetData(System.Windows.DataFormats.Bitmap, bitmap);

                                // 添加文件格式（用于粘贴到文件夹/桌面）
                                var fileCollection = new System.Collections.Specialized.StringCollection();
                                fileCollection.Add(imagePath);
                                dataObject.SetFileDropList(fileCollection);

                                // 添加文本格式（用于粘贴到不支持图片/文件的输入框，回退为路径）
                                dataObject.SetData(System.Windows.DataFormats.UnicodeText, imagePath);
                            }
                        }
                        break;

                    case ClipboardItemType.FileList:
                        if (item.FilePaths != null && item.FilePaths.Length > 0)
                        {
                            var fileCollection = new System.Collections.Specialized.StringCollection();
                            fileCollection.AddRange(item.FilePaths);
                            dataObject.SetFileDropList(fileCollection);

                            // 添加文本格式（用于粘贴到不支持文件的输入框，回退为路径）
                            var pathsText = string.Join(Environment.NewLine, item.FilePaths);
                            dataObject.SetData(System.Windows.DataFormats.UnicodeText, pathsText);
                        }
                        break;
                }

                // 直接设置剪贴板（不持久化，速度更快）
                System.Windows.Clipboard.SetDataObject(dataObject, false);

                // 延迟重置标志，避免触发自己的监听
                System.Threading.Tasks.Task.Delay(100).ContinueWith(_ => _isSettingClipboard = false);
                return true;
            }
            catch
            {
                _isSettingClipboard = false;
                return false;
            }
        }

        /// <summary>
        /// 获取来源应用程序名称
        /// </summary>
        private string? GetSourceAppName()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                GetWindowThreadProcessId(hwnd, out uint processId);
                var process = Process.GetProcessById((int)processId);
                return process.ProcessName;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// BitmapSource转字节数组
        /// </summary>
        private byte[] BitmapSourceToBytes(BitmapSource bitmapSource)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
    }
}
