using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlitzFS.Bridge;
using BlitzFS.UI.Services;

namespace BlitzFS.UI.ViewModels
{
    /// <summary>
    /// 單一檔案/資料夾項目之 ViewModel (支援圖示、高畫質多媒體縮圖與類型識別)
    /// </summary>
    public class FileItemViewModel : ViewModelBase
    {
        private ImageSource? _icon;
        private bool _iconLoaded;

        private ImageSource? _thumbnail;
        private bool _thumbnailLoaded;

        public ulong FileId { get; init; }
        public ulong ParentId { get; init; }
        public string Name { get; init; } = string.Empty;
        public ulong FileSize { get; init; }
        public DateTimeOffset ModifiedTime { get; init; }
        public bool IsDirectory { get; init; }
        public bool IsHidden { get; init; }
        public string FullPath { get; set; } = string.Empty;

        /// <summary>
        /// 檔案副檔名 (小寫，例如 .jpg, .mp4, .pdf)
        /// </summary>
        public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name).ToLowerInvariant();

        /// <summary>
        /// 檔案類型描述 (例如: 資料夾、PNG 圖片、TXT 檔案)
        /// </summary>
        public string TypeName
        {
            get
            {
                if (IsDirectory) return "資料夾";
                string ext = Extension;
                if (string.IsNullOrEmpty(ext)) return "檔案";
                return $"{ext.TrimStart('.').ToUpperInvariant()} 檔案";
            }
        }

        /// <summary>
        /// 是否為圖片檔案
        /// </summary>
        public bool IsImage => Extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp" or ".ico" or ".tiff" or ".svg" => true,
            _ => false
        };

        /// <summary>
        /// 是否為影片檔案
        /// </summary>
        public bool IsVideo => Extension switch
        {
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v" => true,
            _ => false
        };

        /// <summary>
        /// 是否為音訊檔案
        /// </summary>
        public bool IsAudio => Extension switch
        {
            ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg" or ".m4a" or ".wma" => true,
            _ => false
        };

        /// <summary>
        /// 輕量系統圖示 (16x16 / 32x32)
        /// </summary>
        public ImageSource? Icon
        {
            get
            {
                if (!_iconLoaded)
                {
                    _iconLoaded = true;
                    _ = LoadIconAsync();
                }
                return _icon;
            }
            private set => SetProperty(ref _icon, value);
        }

        /// <summary>
        /// 高畫質縮圖 (用於縮圖網格、照片影片瀏覽與預覽面板)
        /// </summary>
        public ImageSource? Thumbnail
        {
            get
            {
                if (!_thumbnailLoaded)
                {
                    _thumbnailLoaded = true;
                    _ = LoadThumbnailAsync();
                }
                return _thumbnail ?? Icon;
            }
            private set => SetProperty(ref _thumbnail, value);
        }

        public string FormattedSize => IsDirectory ? "<DIR>" : FormatBytes(FileSize);
        public string FormattedDate => ModifiedTime.Year < 1980 ? string.Empty : ModifiedTime.ToString("yyyy-MM-dd HH:mm:ss");

        public FileItemViewModel()

        {
        }

        public FileItemViewModel(in CompactNode node, string fileName, string fullPath)
        {
            FileId = node.FileId;
            ParentId = node.ParentId;
            Name = fileName;
            FileSize = node.FileSize;
            ModifiedTime = node.ModifiedTime;
            IsDirectory = node.IsDirectory;
            IsHidden = node.IsHidden;
            FullPath = fullPath;
        }

        private async Task LoadIconAsync()
        {
            var icon = await IconCacheService.GetIconAsync(Extension, IsDirectory);
            if (icon != null)
            {
                Icon = icon;
            }
        }

        private async Task LoadThumbnailAsync()
        {
            if (!IsDirectory && (IsImage || IsVideo) && File.Exists(FullPath))
            {
                var thumb = await ShellThumbnailService.GetThumbnailAsync(FullPath, 160);
                if (thumb != null)
                {
                    if (System.Windows.Application.Current != null)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            Thumbnail = thumb;
                        });
                    }
                    else
                    {
                        Thumbnail = thumb;
                    }
                    return;
                }
            }

            if (Icon != null)
            {
                Thumbnail = Icon;
            }
        }



        private static string FormatBytes(ulong bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dblBytes = bytes;
            while (dblBytes >= 1024.0 && i < 4)
            {
                dblBytes /= 1024.0;
                i++;
            }
            return $"{dblBytes:0.##} {suffixes[i]}";
        }
    }
}
