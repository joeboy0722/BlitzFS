using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace BlitzFS.UI.Services
{
    /// <summary>
    /// Windows 原生 Shell 縮圖與多媒體影像解碼服務 (支援相片真實內容、影片畫面與各類檔案)
    /// </summary>
    public static class ShellThumbnailService
    {
        private static readonly ConcurrentDictionary<string, BitmapSource> _thumbnailCache = new();
        private const int MaxCacheEntries = 3000;

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(
                [In, MarshalAs(UnmanagedType.Struct)] SIZE size,
                [In] SIIGBF flags,
                [Out] out IntPtr phbm
            );
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;

            public SIZE(int cx, int cy)
            {
                this.cx = cx;
                this.cy = cy;
            }
        }

        [Flags]
        private enum SIIGBF
        {
            SIIGBF_RESIZETOFIT = 0x00000000,
            SIIGBF_BIGGERSIZEOK = 0x00000001,
            SIIGBF_MEMORYONLY = 0x00000002,
            SIIGBF_ICONONLY = 0x00000004,
            SIIGBF_THUMBNAILONLY = 0x00000008,
            SIIGBF_INCACHEONLY = 0x00000010,
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            [In] IntPtr pbc,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv
        );

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        private static readonly Guid IShellItemImageFactoryGuid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        /// <summary>
        /// 非同步提取指定路徑的檔案真實縮圖 (相片極速原生解碼 / 影片真實畫面提取)
        /// </summary>
        public static async Task<BitmapSource?> GetThumbnailAsync(string fullPath, int targetSize = 160)
        {
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                return null;
            }

            string cacheKey = $"{fullPath}_{targetSize}";
            if (_thumbnailCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            return await Task.Run(() =>
            {
                BitmapSource? bmp = null;
                string ext = Path.GetExtension(fullPath).ToLowerInvariant();

                // 1. 照片格式：優先使用 WPF 原生極速解碼
                if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp" or ".gif" or ".ico")
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(fullPath);
                        bi.DecodePixelWidth = targetSize;
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bi.EndInit();
                        bi.Freeze();
                        bmp = bi;
                    }
                    catch
                    {
                        bmp = null;
                    }
                }

                // 2. 影片或非圖片格式：使用 Windows Shell 縮圖工廠嚴格提取影片影格
                if (bmp == null)
                {
                    bmp = ExtractShellThumbnail(fullPath, targetSize);
                }

                if (bmp != null)
                {
                    if (_thumbnailCache.Count > MaxCacheEntries)
                    {
                        _thumbnailCache.Clear();
                    }
                    _thumbnailCache.TryAdd(cacheKey, bmp);
                }

                return bmp;
            });
        }

        private static BitmapSource? ExtractShellThumbnail(string path, int targetSize)
        {
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, IShellItemImageFactoryGuid, out var factory);
                if (factory == null) return null;

                var size = new SIZE(targetSize, targetSize);

                // 優先使用 SIIGBF_THUMBNAILONLY 嚴格模式，強制提取影片/相片真實縮圖畫面（避免提取到播放器圖示）
                int hr = factory.GetImage(size, SIIGBF.SIIGBF_THUMBNAILONLY | SIIGBF.SIIGBF_BIGGERSIZEOK, out IntPtr hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero)
                {
                    // 若失敗，重試 RESIZETOFIT
                    hr = factory.GetImage(size, SIIGBF.SIIGBF_RESIZETOFIT, out hBitmap);
                }

                if (hr == 0 && hBitmap != IntPtr.Zero)
                {
                    try
                    {
                        var source = Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions()
                        );
                        source.Freeze();
                        return source;
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }
            catch {}

            return null;
        }
    }
}
