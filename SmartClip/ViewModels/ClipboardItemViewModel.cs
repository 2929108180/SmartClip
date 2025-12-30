using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartClip.Models;
using SmartClip.Services;

namespace SmartClip.ViewModels
{
    public class ClipboardItemViewModel : INotifyPropertyChanged
    {
        private readonly StorageService _storageService;

        // 性能优化：限制缩略图并发加载数量
        private static readonly SemaphoreSlim _thumbnailSemaphore = new(3, 3);

        public ClipboardItem Model { get; }

        public string Id => Model.Id;
        public ClipboardItemType Type => Model.Type;
        public string? TextContent => Model.TextContent;
        public string Preview => Model.Preview;
        public bool IsPinned => Model.IsPinned;
        public DateTime CopyTime => Model.CopyTime;
        public string? SourceApp => Model.SourceApp;
        public int UseCount => Model.UseCount;
        public string FileTypeDescription => Model.FileTypeDescription;
        public FileItemInfo[] FileItems => Model.FileItems;

        private ImageSource? _thumbnail;
        private bool _thumbnailLoaded;

        public ImageSource? Thumbnail
        {
            get
            {
                if (!_thumbnailLoaded && Type == ClipboardItemType.Image)
                {
                    LoadThumbnailAsync();
                }
                return _thumbnail;
            }
            private set
            {
                _thumbnail = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 显示图标（根据类型）
        /// </summary>
        public string TypeIcon
        {
            get
            {
                switch (Type)
                {
                    case ClipboardItemType.Text:
                        return "\uE8C1"; // 文本图标
                    case ClipboardItemType.RichText:
                        return "\uE943"; // 富文本图标
                    case ClipboardItemType.Image:
                        return "\uE91B"; // 图片图标
                    case ClipboardItemType.FileList:
                        // 根据文件类型返回不同图标
                        var items = FileItems;
                        if (items.Length == 1)
                            return items[0].IconCode;
                        return "\uE8B7"; // 多文件用文件夹图标
                    default:
                        return "\uE8A5";
                }
            }
        }

        /// <summary>
        /// 是否显示文件图标（文件类型时显示）
        /// </summary>
        public bool ShowFileIcon => Type == ClipboardItemType.FileList;

        /// <summary>
        /// 格式化的时间显示
        /// </summary>
        public string FormattedTime
        {
            get
            {
                var now = DateTime.Now;
                var diff = now - CopyTime;

                if (diff.TotalMinutes < 1)
                    return "刚刚";
                if (diff.TotalMinutes < 60)
                    return $"{(int)diff.TotalMinutes}分钟前";
                if (diff.TotalHours < 24)
                    return $"{(int)diff.TotalHours}小时前";
                if (diff.TotalDays < 7)
                    return $"{(int)diff.TotalDays}天前";

                return CopyTime.ToString("MM-dd HH:mm");
            }
        }

        /// <summary>
        /// 序号显示（用于快捷键提示）
        /// </summary>
        public string IndexDisplay { get; set; } = string.Empty;

        /// <summary>
        /// 置顶标记显示
        /// </summary>
        public string PinDisplay => IsPinned ? "\uE840" : string.Empty; // 📌图标

        public ClipboardItemViewModel(ClipboardItem model, StorageService storageService)
        {
            Model = model;
            _storageService = storageService;
        }

        /// <summary>
        /// 异步加载缩略图（带并发控制）
        /// </summary>
        private async void LoadThumbnailAsync()
        {
            _thumbnailLoaded = true;

            if (string.IsNullOrEmpty(Model.ImagePath)) return;

            try
            {
                // 等待获取信号量，限制并发
                await _thumbnailSemaphore.WaitAsync();

                try
                {
                    await System.Threading.Tasks.Task.Run(() =>
                    {
                        var imagePath = _storageService.GetImageFullPath(Model.ImagePath);
                        if (!File.Exists(imagePath)) return;

                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.UriSource = new Uri(imagePath);
                                bitmap.DecodePixelWidth = 80; // 缩略图宽度
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.EndInit();
                                bitmap.Freeze();

                                Thumbnail = bitmap;
                            }
                            catch { }
                        });
                    });
                }
                finally
                {
                    _thumbnailSemaphore.Release();
                }
            }
            catch { }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
