using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using BlitzFS.UI.ViewModels;

namespace BlitzFS.UI.Views
{
    /// <summary>
    /// QuickLookWindow.xaml 的互動邏輯 (支援相片、影片動態播放與程式碼快速預覽)
    /// </summary>
    public partial class QuickLookWindow : Window
    {
        public QuickLookWindow()
        {
            InitializeComponent();
        }

        public QuickLookWindow(FileItemViewModel item) : this()
        {
            LoadFile(item);
        }

        public void LoadFile(FileItemViewModel item)
        {
            TxtFileName.Text = item.Name;
            TxtFilePath.Text = item.FullPath;
            TxtFileSize.Text = item.FormattedSize;

            if (!File.Exists(item.FullPath))
            {
                if (BlitzFS.UI.Services.ShellFolderService.Instance.IsShellPath(item.FullPath))
                {
                    TxtPreviewContent.Text = "便攜式設備 (手機) 檔案。\n\n按 [Enter] 或按兩下即可直接開啟。";
                    ScrollText.Visibility = Visibility.Visible;
                    return;
                }

                TxtPreviewContent.Text = "檔案不存在或無法存取。";
                ScrollText.Visibility = Visibility.Visible;
                return;
            }

            string localPath = item.FullPath;


            string ext = item.Extension;

            // 1. 圖片格式
            if (item.IsImage)
            {
                try
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(localPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    ImgPreview.Source = bitmap;
                    ImgPreview.Visibility = Visibility.Visible;
                    VideoPreview.Visibility = Visibility.Collapsed;
                    ScrollText.Visibility = Visibility.Collapsed;
                    return;
                }
                catch {}
            }

            // 2. 影片格式 (支援即時播放)
            if (item.IsVideo)
            {
                try
                {
                    ImgPreview.Visibility = Visibility.Collapsed;
                    ScrollText.Visibility = Visibility.Collapsed;
                    VideoPreview.Visibility = Visibility.Visible;

                    VideoPreview.Source = new Uri(localPath);
                    VideoPreview.Play();
                    return;
                }
                catch {}
            }

            // 3. 文字/程式碼預覽
            ImgPreview.Visibility = Visibility.Collapsed;
            VideoPreview.Visibility = Visibility.Collapsed;
            ScrollText.Visibility = Visibility.Visible;

            try
            {
                using var reader = new StreamReader(localPath);
                char[] buffer = new char[64 * 1024];
                int read = reader.Read(buffer, 0, buffer.Length);
                TxtPreviewContent.Text = new string(buffer, 0, read);
            }
            catch (Exception ex)
            {
                TxtPreviewContent.Text = $"無法讀取文字內容: {ex.Message}";
            }
        }


        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                VideoPreview.Stop();
                VideoPreview.Source = null;
            }
            catch {}
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space || e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }
    }
}
