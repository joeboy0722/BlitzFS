using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlitzFS.UI.Services
{
    /// <summary>
    /// 系統 Shell 圖示非同步抓取與記憶體快取服務 (雙層延遲載入)
    /// </summary>
    public static class IconCacheService
    {
        private static readonly ConcurrentDictionary<string, ImageSource> _cache = new();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags
        );

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>
        /// 非同步獲取檔案副檔名或目錄對應的系統原生圖示
        /// </summary>
        public static async Task<ImageSource?> GetIconAsync(string extension, bool isDirectory)
        {
            string key = isDirectory ? "__DIR__" : (string.IsNullOrEmpty(extension) ? "__FILE__" : extension.ToLowerInvariant());

            if (_cache.TryGetValue(key, out var cachedIcon))
            {
                return cachedIcon;
            }

            return await Task.Run(() =>
            {
                var icon = FetchShellIcon(key, isDirectory);
                if (icon != null)
                {
                    _cache.TryAdd(key, icon);
                }
                return icon;
            });
        }

        private static ImageSource? FetchShellIcon(string extensionOrKey, bool isDirectory)
        {
            SHFILEINFO shinfo = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
            uint attr = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            string pseudoPath = isDirectory ? "folder" : ("file" + (extensionOrKey.StartsWith(".") ? extensionOrKey : "." + extensionOrKey));

            IntPtr res = SHGetFileInfo(pseudoPath, attr, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
            if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                try
                {
                    var imageSource = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions()
                    );
                    imageSource.Freeze(); // 凍結跨執行緒共享
                    return imageSource;
                }
                catch
                {
                    return null;
                }
                finally
                {
                    DestroyIcon(shinfo.hIcon);
                }
            }

            return null;
        }
    }
}
