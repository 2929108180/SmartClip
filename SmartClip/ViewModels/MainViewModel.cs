using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using SmartClip.Models;
using SmartClip.Services;

namespace SmartClip.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly StorageService _storageService;
        private readonly ClipboardService _clipboardService;

        private ObservableCollection<ClipboardItemViewModel> _items = new();
        private ClipboardItemViewModel? _selectedItem;
        private string _searchText = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isStatusVisible;

        // 性能优化：ViewModel缓存
        private readonly Dictionary<string, ClipboardItemViewModel> _viewModelCache = new();

        // 性能优化：搜索防抖
        private CancellationTokenSource? _searchDebounceToken;
        private const int SearchDebounceMs = 150;

        public ObservableCollection<ClipboardItemViewModel> Items
        {
            get => _items;
            set { _items = value; OnPropertyChanged(); }
        }

        public ClipboardItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set { _selectedItem = value; OnPropertyChanged(); }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                FilterItemsDebounced();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public bool IsStatusVisible
        {
            get => _isStatusVisible;
            set { _isStatusVisible = value; OnPropertyChanged(); }
        }

        public ICollectionView FilteredItems { get; }

        public MainViewModel(StorageService storageService, ClipboardService clipboardService)
        {
            _storageService = storageService;
            _clipboardService = clipboardService;

            FilteredItems = CollectionViewSource.GetDefaultView(_items);
            FilteredItems.Filter = FilterPredicate;

            _clipboardService.ClipboardChanged += OnClipboardChanged;
        }

        /// <summary>
        /// 加载历史记录
        /// </summary>
        public async Task LoadAsync()
        {
            await _storageService.LoadAsync();
            RefreshItems();
        }

        /// <summary>
        /// 刷新列表（使用ViewModel缓存优化）
        /// </summary>
        public void RefreshItems()
        {
            var sortedItems = _storageService.GetSortedItems();
            var currentIds = new HashSet<string>(sortedItems.Select(i => i.Id));

            // 移除不再存在的缓存项
            var keysToRemove = _viewModelCache.Keys.Where(k => !currentIds.Contains(k)).ToList();
            foreach (var key in keysToRemove)
            {
                _viewModelCache.Remove(key);
            }

            // 构建新列表，复用已有的ViewModel
            var newItems = new List<ClipboardItemViewModel>(sortedItems.Count);
            foreach (var item in sortedItems)
            {
                if (!_viewModelCache.TryGetValue(item.Id, out var vm))
                {
                    vm = new ClipboardItemViewModel(item, _storageService);
                    _viewModelCache[item.Id] = vm;
                }
                newItems.Add(vm);
            }

            // 智能更新：只在必要时重建集合
            if (_items.Count != newItems.Count || !_items.SequenceEqual(newItems))
            {
                _items.Clear();
                foreach (var vm in newItems)
                {
                    _items.Add(vm);
                }
            }

            if (_items.Count > 0 && SelectedItem == null)
            {
                SelectedItem = _items[0];
            }
        }

        /// <summary>
        /// 剪贴板变化事件
        /// </summary>
        private void OnClipboardChanged(object? sender, ClipboardItem item)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                RefreshItems();
            });
        }

        /// <summary>
        /// 过滤条目（带防抖）
        /// </summary>
        private async void FilterItemsDebounced()
        {
            _searchDebounceToken?.Cancel();
            _searchDebounceToken = new CancellationTokenSource();

            try
            {
                await Task.Delay(SearchDebounceMs, _searchDebounceToken.Token);
                FilterItems();
            }
            catch (TaskCanceledException)
            {
                // 被取消，忽略
            }
        }

        /// <summary>
        /// 过滤条目
        /// </summary>
        private void FilterItems()
        {
            FilteredItems.Refresh();
        }

        /// <summary>
        /// 过滤谓词
        /// </summary>
        private bool FilterPredicate(object obj)
        {
            if (obj is not ClipboardItemViewModel item)
                return false;

            if (string.IsNullOrWhiteSpace(_searchText))
                return true;

            var search = _searchText.Trim();

            // 类型过滤
            if (search.StartsWith("/"))
            {
                var typeFilter = search[1..].ToLower();
                return typeFilter switch
                {
                    "img" or "image" => item.Type == ClipboardItemType.Image,
                    "file" or "files" => item.Type == ClipboardItemType.FileList,
                    "text" => item.Type == ClipboardItemType.Text,
                    "rich" => item.Type == ClipboardItemType.RichText,
                    _ => true
                };
            }

            // 模糊搜索
            var searchLower = search.ToLower();
            var previewLower = item.Preview?.ToLower() ?? string.Empty;
            var textLower = item.TextContent?.ToLower() ?? string.Empty;

            return previewLower.Contains(searchLower) || textLower.Contains(searchLower);
        }

        /// <summary>
        /// 粘贴选中项（快速版本，不等待IO）
        /// </summary>
        public void PasteSelectedAsync(bool plainTextOnly = false)
        {
            if (SelectedItem == null) return;

            // 同步设置剪贴板，不等待
            _clipboardService.SetClipboardContent(SelectedItem.Model, plainTextOnly);

            // 异步更新使用记录，不阻塞粘贴
            _ = _storageService.UpdateUsageAsync(SelectedItem.Id);
        }

        /// <summary>
        /// 按索引粘贴（1-9快捷键）
        /// </summary>
        public void PasteByIndexAsync(int index, bool plainTextOnly = false)
        {
            var visibleItems = FilteredItems.Cast<ClipboardItemViewModel>().ToList();
            if (index < 0 || index >= visibleItems.Count) return;

            var item = visibleItems[index];

            // 同步设置剪贴板，不等待
            _clipboardService.SetClipboardContent(item.Model, plainTextOnly);

            // 异步更新使用记录，不阻塞粘贴
            _ = _storageService.UpdateUsageAsync(item.Id);
        }

        /// <summary>
        /// 删除选中项
        /// </summary>
        public async Task DeleteSelectedAsync()
        {
            if (SelectedItem == null) return;

            var id = SelectedItem.Id;
            await _storageService.RemoveItemAsync(id);
            RefreshItems();
            ShowStatus("已删除");
        }

        /// <summary>
        /// 切换选中项置顶
        /// </summary>
        public async Task TogglePinSelectedAsync()
        {
            if (SelectedItem == null) return;

            var wasPinned = SelectedItem.Model.IsPinned;
            await _storageService.TogglePinAsync(SelectedItem.Id);
            RefreshItems();
            ShowStatus(wasPinned ? "已取消置顶" : "已置顶");
        }

        /// <summary>
        /// 清除所有非置顶
        /// </summary>
        public async Task ClearUnpinnedAsync()
        {
            await _storageService.ClearUnpinnedAsync();
            RefreshItems();
            ShowStatus("已清除非置顶记录");
        }

        /// <summary>
        /// 选择上一项
        /// </summary>
        public void SelectPrevious()
        {
            var visibleItems = FilteredItems.Cast<ClipboardItemViewModel>().ToList();
            if (visibleItems.Count == 0) return;

            var currentIndex = SelectedItem != null ? visibleItems.IndexOf(SelectedItem) : -1;
            var newIndex = currentIndex > 0 ? currentIndex - 1 : visibleItems.Count - 1;
            SelectedItem = visibleItems[newIndex];
        }

        /// <summary>
        /// 选择下一项
        /// </summary>
        public void SelectNext()
        {
            var visibleItems = FilteredItems.Cast<ClipboardItemViewModel>().ToList();
            if (visibleItems.Count == 0) return;

            var currentIndex = SelectedItem != null ? visibleItems.IndexOf(SelectedItem) : -1;
            var newIndex = currentIndex < visibleItems.Count - 1 ? currentIndex + 1 : 0;
            SelectedItem = visibleItems[newIndex];
        }

        /// <summary>
        /// 显示状态消息
        /// </summary>
        public async void ShowStatus(string message)
        {
            StatusMessage = message;
            IsStatusVisible = true;

            await Task.Delay(2000);
            IsStatusVisible = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
