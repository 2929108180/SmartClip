using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartClip.Models;

namespace SmartClip.Services
{
    public class StorageService
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartClip");

        private static readonly string HistoryFilePath = Path.Combine(AppDataPath, "History.json");
        private static readonly string CachePath = Path.Combine(AppDataPath, "Cache");
        private static readonly string BackupPath = Path.Combine(AppDataPath, "Backup");

        private readonly SemaphoreSlim _saveLock = new(1, 1);

        private List<ClipboardItem> _items = new();

        // 性能优化：排序结果缓存
        private List<ClipboardItem>? _sortedItemsCache;
        private bool _sortCacheInvalid = true;

        // 配置常量
        private const int MaxItems = 300;
        private const int MaxDays = 30;

        public IReadOnlyList<ClipboardItem> Items => _items.AsReadOnly();

        /// <summary>
        /// 使排序缓存失效
        /// </summary>
        private void InvalidateSortCache()
        {
            _sortCacheInvalid = true;
            _sortedItemsCache = null;
        }

        public StorageService()
        {
            EnsureDirectories();
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(AppDataPath);
            Directory.CreateDirectory(CachePath);
            Directory.CreateDirectory(BackupPath);
        }

        /// <summary>
        /// 加载历史记录
        /// </summary>
        public async Task LoadAsync()
        {
            try
            {
                if (!File.Exists(HistoryFilePath))
                {
                    _items = new List<ClipboardItem>();
                    InvalidateSortCache();
                    return;
                }

                var json = await File.ReadAllTextAsync(HistoryFilePath);
                _items = JsonSerializer.Deserialize<List<ClipboardItem>>(json) ?? new List<ClipboardItem>();
                InvalidateSortCache();
            }
            catch (JsonException)
            {
                // JSON损坏，备份并重置
                await BackupCorruptedFileAsync();
                _items = new List<ClipboardItem>();
                InvalidateSortCache();
            }
            catch (Exception)
            {
                _items = new List<ClipboardItem>();
                InvalidateSortCache();
            }
        }

        /// <summary>
        /// 保存历史记录
        /// </summary>
        public async Task SaveAsync()
        {
            await _saveLock.WaitAsync();
            try
            {
                var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(HistoryFilePath, json);
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// 添加剪贴板项
        /// </summary>
        public async Task<bool> AddItemAsync(ClipboardItem item)
        {
            // 检查重复
            var existing = _items.FirstOrDefault(i => i.ContentHash == item.ContentHash);
            if (existing != null)
            {
                // 更新时间戳和使用次数
                existing.CopyTime = DateTime.Now;
                existing.UseCount++;
                existing.LastUsedTime = DateTime.Now;
                InvalidateSortCache();
                await SaveAsync();
                return false; // 表示是重复项
            }

            item.GeneratePreview();
            _items.Insert(0, item);
            InvalidateSortCache();
            await SaveAsync();
            return true;
        }

        /// <summary>
        /// 删除剪贴板项
        /// </summary>
        public async Task RemoveItemAsync(string id)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item == null) return;

            // 删除关联的图片文件
            if (!string.IsNullOrEmpty(item.ImagePath))
            {
                var imagePath = Path.Combine(CachePath, item.ImagePath);
                if (File.Exists(imagePath))
                {
                    try
                    {
                        File.Delete(imagePath);
                    }
                    catch { }
                }
            }

            _items.Remove(item);
            InvalidateSortCache();
            await SaveAsync();
        }

        /// <summary>
        /// 切换置顶状态
        /// </summary>
        public async Task TogglePinAsync(string id)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item == null) return;

            item.IsPinned = !item.IsPinned;
            item.PinnedTime = item.IsPinned ? DateTime.Now : null;
            InvalidateSortCache();
            await SaveAsync();
        }

        /// <summary>
        /// 更新使用次数
        /// </summary>
        public async Task UpdateUsageAsync(string id)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item == null) return;

            item.UseCount++;
            item.LastUsedTime = DateTime.Now;
            item.CopyTime = DateTime.Now; // 更新复制时间，使粘贴的项目移动到顶部
            InvalidateSortCache();
            await SaveAsync();
        }

        /// <summary>
        /// 清除所有非置顶记录
        /// </summary>
        public async Task ClearUnpinnedAsync()
        {
            var unpinnedItems = _items.Where(i => !i.IsPinned).ToList();

            // 删除关联的图片文件
            foreach (var item in unpinnedItems)
            {
                if (!string.IsNullOrEmpty(item.ImagePath))
                {
                    var imagePath = Path.Combine(CachePath, item.ImagePath);
                    if (File.Exists(imagePath))
                    {
                        try
                        {
                            File.Delete(imagePath);
                        }
                        catch { }
                    }
                }
            }

            _items.RemoveAll(i => !i.IsPinned);
            InvalidateSortCache();
            await SaveAsync();
        }

        /// <summary>
        /// 执行数据清理
        /// </summary>
        public async Task CleanupAsync()
        {
            var now = DateTime.Now;
            var cutoffDate = now.AddDays(-MaxDays);

            // 删除超过30天的非置顶记录
            var expiredItems = _items.Where(i => !i.IsPinned && i.CopyTime < cutoffDate).ToList();
            foreach (var item in expiredItems)
            {
                await RemoveItemAsync(item.Id);
            }

            // 如果仍超过300条，删除最旧的非置顶记录
            while (_items.Count > MaxItems)
            {
                var oldestUnpinned = _items.Where(i => !i.IsPinned)
                    .OrderBy(i => i.CopyTime)
                    .FirstOrDefault();

                if (oldestUnpinned != null)
                {
                    await RemoveItemAsync(oldestUnpinned.Id);
                }
                else
                {
                    break; // 全是置顶的，无法再删除
                }
            }

            // 清理孤儿文件
            await CleanupOrphanFilesAsync();
        }

        /// <summary>
        /// 清理孤儿文件（在JSON中没有引用的图片）
        /// </summary>
        private async Task CleanupOrphanFilesAsync()
        {
            await Task.Run(() =>
            {
                if (!Directory.Exists(CachePath)) return;

                var referencedFiles = _items
                    .Where(i => !string.IsNullOrEmpty(i.ImagePath))
                    .Select(i => i.ImagePath!)
                    .ToHashSet();

                var cacheFiles = Directory.GetFiles(CachePath);
                foreach (var file in cacheFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (!referencedFiles.Contains(fileName))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch { }
                    }
                }
            });
        }

        /// <summary>
        /// 保存图片到缓存
        /// </summary>
        public async Task<string> SaveImageAsync(byte[] imageData)
        {
            var hash = ComputeHash(imageData);
            var fileName = $"{hash}.png";
            var filePath = Path.Combine(CachePath, fileName);

            if (!File.Exists(filePath))
            {
                await File.WriteAllBytesAsync(filePath, imageData);
            }

            return fileName;
        }

        /// <summary>
        /// 获取图片完整路径
        /// </summary>
        public string GetImageFullPath(string imagePath)
        {
            return Path.Combine(CachePath, imagePath);
        }

        /// <summary>
        /// 计算内容哈希值
        /// </summary>
        public static string ComputeHash(string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            return ComputeHash(bytes);
        }

        public static string ComputeHash(byte[] data)
        {
            var hashBytes = SHA256.HashData(data);
            return Convert.ToHexString(hashBytes)[..16]; // 只取前16位
        }

        /// <summary>
        /// 备份损坏的文件
        /// </summary>
        private async Task BackupCorruptedFileAsync()
        {
            if (!File.Exists(HistoryFilePath)) return;

            var backupFileName = $"History_corrupted_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var backupFilePath = Path.Combine(BackupPath, backupFileName);

            try
            {
                await Task.Run(() => File.Copy(HistoryFilePath, backupFilePath, true));
            }
            catch { }
        }

        /// <summary>
        /// 获取排序后的列表（带缓存）
        /// 排序优先级：置顶区 > 最新复制区 > 最近使用区 > 高频区 > 历史区
        /// </summary>
        public List<ClipboardItem> GetSortedItems()
        {
            // 使用缓存避免重复计算
            if (!_sortCacheInvalid && _sortedItemsCache != null)
            {
                return _sortedItemsCache;
            }

            var now = DateTime.Now;
            var newCopyMinutes = 5; // 最新复制区：5分钟内复制的
            var recentMinutes = 30; // 最近使用区：30分钟内使用过
            var recentDays = 7; // 高频区统计周期
            var frequentThreshold = 3; // 高频阈值

            // Level 1: 置顶区 - 手动置顶的内容
            var pinned = _items.Where(i => i.IsPinned)
                .OrderByDescending(i => i.PinnedTime)
                .ToList();

            var pinnedIds = pinned.Select(i => i.Id).ToHashSet();

            // Level 2: 最新复制区 - 5分钟内复制的（排除置顶）
            var newlyCopied = _items.Where(i =>
                !pinnedIds.Contains(i.Id) &&
                i.CopyTime > now.AddMinutes(-newCopyMinutes))
                .OrderByDescending(i => i.CopyTime)
                .ToList();

            var newlyCopiedIds = newlyCopied.Select(i => i.Id).ToHashSet();

            // Level 3: 最近使用区 - 30分钟内粘贴使用过的（排除置顶和最新复制）
            var recentlyUsed = _items.Where(i =>
                !pinnedIds.Contains(i.Id) &&
                !newlyCopiedIds.Contains(i.Id) &&
                i.UseCount > 0 &&
                i.LastUsedTime > now.AddMinutes(-recentMinutes))
                .OrderByDescending(i => i.LastUsedTime)
                .ToList();

            var recentlyUsedIds = recentlyUsed.Select(i => i.Id).ToHashSet();

            // Level 4: 高频区 - 7天内使用≥3次的（排除前面的）
            var frequent = _items.Where(i =>
                !pinnedIds.Contains(i.Id) &&
                !newlyCopiedIds.Contains(i.Id) &&
                !recentlyUsedIds.Contains(i.Id) &&
                i.LastUsedTime > now.AddDays(-recentDays) &&
                i.UseCount >= frequentThreshold)
                .OrderByDescending(i => i.UseCount)
                .ThenByDescending(i => i.LastUsedTime)
                .ToList();

            var frequentIds = frequent.Select(i => i.Id).ToHashSet();

            // Level 5: 历史区 - 其他所有记录按复制时间排序
            var history = _items.Where(i =>
                !pinnedIds.Contains(i.Id) &&
                !newlyCopiedIds.Contains(i.Id) &&
                !recentlyUsedIds.Contains(i.Id) &&
                !frequentIds.Contains(i.Id))
                .OrderByDescending(i => i.CopyTime)
                .ToList();

            _sortedItemsCache = pinned.Concat(newlyCopied).Concat(recentlyUsed).Concat(frequent).Concat(history).ToList();
            _sortCacheInvalid = false;

            return _sortedItemsCache;
        }
    }
}
