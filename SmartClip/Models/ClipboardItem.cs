using System;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace SmartClip.Models
{
    public enum ClipboardItemType
    {
        Text,
        RichText,
        Image,
        FileList
    }

    /// <summary>
    /// 文件项信息（用于文件列表预览）
    /// </summary>
    public class FileItemInfo
    {
        public string FullPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }

        /// <summary>
        /// 获取文件类型描述
        /// </summary>
        public string TypeDescription
        {
            get
            {
                if (IsDirectory) return "文件夹";

                return Extension.ToLower() switch
                {
                    ".txt" => "文本文件",
                    ".doc" or ".docx" => "Word 文档",
                    ".xls" or ".xlsx" => "Excel 表格",
                    ".ppt" or ".pptx" => "PowerPoint",
                    ".pdf" => "PDF 文档",
                    ".zip" or ".rar" or ".7z" => "压缩文件",
                    ".exe" => "应用程序",
                    ".dll" => "DLL 文件",
                    ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "图片",
                    ".mp3" or ".wav" or ".flac" or ".aac" => "音频",
                    ".mp4" or ".avi" or ".mkv" or ".mov" => "视频",
                    ".html" or ".htm" => "网页",
                    ".css" => "样式表",
                    ".js" => "JavaScript",
                    ".json" => "JSON",
                    ".xml" => "XML",
                    ".cs" => "C# 源码",
                    ".py" => "Python",
                    ".java" => "Java",
                    ".cpp" or ".c" or ".h" => "C/C++",
                    ".md" => "Markdown",
                    ".sql" => "SQL",
                    ".iso" => "镜像文件",
                    ".lnk" => "快捷方式",
                    _ => string.IsNullOrEmpty(Extension) ? "文件" : $"{Extension.TrimStart('.')} 文件"
                };
            }
        }

        /// <summary>
        /// 获取文件图标代码（Segoe MDL2 Assets）
        /// </summary>
        public string IconCode
        {
            get
            {
                if (IsDirectory) return "\uE8B7"; // 文件夹

                return Extension.ToLower() switch
                {
                    ".txt" or ".md" => "\uE8A5", // 文本
                    ".doc" or ".docx" => "\uE8A5", // Word
                    ".xls" or ".xlsx" => "\uE80A", // Excel
                    ".ppt" or ".pptx" => "\uE8A5", // PPT
                    ".pdf" => "\uE8A5", // PDF
                    ".zip" or ".rar" or ".7z" => "\uE8B7", // 压缩
                    ".exe" => "\uE756", // 应用
                    ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "\uE91B", // 图片
                    ".mp3" or ".wav" or ".flac" or ".aac" => "\uE8D6", // 音频
                    ".mp4" or ".avi" or ".mkv" or ".mov" => "\uE714", // 视频
                    ".html" or ".htm" or ".css" or ".js" => "\uE943", // 代码
                    ".lnk" => "\uE71B", // 快捷方式
                    _ => "\uE8A5" // 默认文件
                };
            }
        }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        public string FormattedSize
        {
            get
            {
                if (IsDirectory) return "";
                if (Size < 1024) return $"{Size} B";
                if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
                if (Size < 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024):F1} MB";
                return $"{Size / (1024.0 * 1024 * 1024):F1} GB";
            }
        }

        public static FileItemInfo FromPath(string path)
        {
            var info = new FileItemInfo { FullPath = path };

            try
            {
                if (Directory.Exists(path))
                {
                    info.IsDirectory = true;
                    info.FileName = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(info.FileName))
                        info.FileName = path; // 根目录
                }
                else if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    info.FileName = fileInfo.Name;
                    info.Extension = fileInfo.Extension;
                    info.Size = fileInfo.Length;
                }
                else
                {
                    info.FileName = Path.GetFileName(path);
                    info.Extension = Path.GetExtension(path);
                }
            }
            catch
            {
                info.FileName = Path.GetFileName(path);
            }

            return info;
        }
    }

    public class ClipboardItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public ClipboardItemType Type { get; set; }

        /// <summary>
        /// 文本内容（纯文本/富文本的文本部分）
        /// </summary>
        public string? TextContent { get; set; }

        /// <summary>
        /// 富文本HTML内容
        /// </summary>
        public string? HtmlContent { get; set; }

        /// <summary>
        /// 富文本RTF内容
        /// </summary>
        public string? RtfContent { get; set; }

        /// <summary>
        /// 图片文件路径（相对于Cache目录）
        /// </summary>
        public string? ImagePath { get; set; }

        /// <summary>
        /// 图片临时数据（不序列化，仅用于处理过程）
        /// </summary>
        [JsonIgnore]
        public byte[]? ImageData { get; set; }

        /// <summary>
        /// 文件列表路径
        /// </summary>
        public string[]? FilePaths { get; set; }

        /// <summary>
        /// 内容哈希值（用于去重）
        /// </summary>
        public string ContentHash { get; set; } = string.Empty;

        /// <summary>
        /// 显示预览（最多30字符）
        /// </summary>
        public string Preview { get; set; } = string.Empty;

        /// <summary>
        /// 文件类型描述（用于文件列表）
        /// </summary>
        public string FileTypeDescription { get; set; } = string.Empty;

        /// <summary>
        /// 是否置顶
        /// </summary>
        public bool IsPinned { get; set; }

        /// <summary>
        /// 置顶时间
        /// </summary>
        public DateTime? PinnedTime { get; set; }

        /// <summary>
        /// 复制时间
        /// </summary>
        public DateTime CopyTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 使用次数（用于高频区排序）
        /// </summary>
        public int UseCount { get; set; }

        /// <summary>
        /// 最后使用时间
        /// </summary>
        public DateTime LastUsedTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 来源应用程序名称
        /// </summary>
        public string? SourceApp { get; set; }

        /// <summary>
        /// 来源应用程序图标路径
        /// </summary>
        public string? SourceAppIconPath { get; set; }

        /// <summary>
        /// 获取文件项信息列表
        /// </summary>
        [JsonIgnore]
        public FileItemInfo[] FileItems => FilePaths?.Select(FileItemInfo.FromPath).ToArray() ?? Array.Empty<FileItemInfo>();

        /// <summary>
        /// 生成预览文本
        /// </summary>
        public void GeneratePreview()
        {
            string? content;

            switch (Type)
            {
                case ClipboardItemType.Text:
                case ClipboardItemType.RichText:
                    content = TextContent;
                    FileTypeDescription = Type == ClipboardItemType.RichText ? "富文本" : "文本";
                    break;

                case ClipboardItemType.Image:
                    content = "[图片]";
                    FileTypeDescription = "图片";
                    break;

                case ClipboardItemType.FileList:
                    var fileItems = FileItems;
                    if (fileItems.Length == 0)
                    {
                        content = "[文件]";
                        FileTypeDescription = "文件";
                    }
                    else if (fileItems.Length == 1)
                    {
                        var item = fileItems[0];
                        content = item.FileName;
                        FileTypeDescription = item.TypeDescription;
                    }
                    else
                    {
                        // 多个文件
                        var dirCount = fileItems.Count(f => f.IsDirectory);
                        var fileCount = fileItems.Length - dirCount;

                        var parts = new System.Collections.Generic.List<string>();
                        if (dirCount > 0) parts.Add($"{dirCount}个文件夹");
                        if (fileCount > 0) parts.Add($"{fileCount}个文件");

                        FileTypeDescription = string.Join(" + ", parts);
                        content = fileItems[0].FileName + (fileItems.Length > 1 ? $" 等{fileItems.Length}项" : "");
                    }
                    break;

                default:
                    content = string.Empty;
                    FileTypeDescription = "";
                    break;
            }

            if (string.IsNullOrEmpty(content))
            {
                Preview = string.Empty;
                return;
            }

            // 处理换行，将换行替换为空格
            content = content.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

            // 最多50个字符
            Preview = content.Length > 50 ? content[..50] + "..." : content;
        }
    }
}
