using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using BlitzFS.UI.ViewModels;

namespace BlitzFS.UI.Views
{
    public partial class PreviewPaneView : UserControl
    {
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(FileItemViewModel),
                typeof(PreviewPaneView),
                new PropertyMetadata(null, OnSelectedItemChanged));

        public FileItemViewModel? SelectedItem
        {
            get => (FileItemViewModel?)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        private bool _isPlaying = false;
        private string? _currentVideoPath = null;

        public PreviewPaneView()
        {
            InitializeComponent();
        }

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PreviewPaneView view)
            {
                view.UpdateContent(e.NewValue as FileItemViewModel);
            }
        }

        private void UpdateContent(FileItemViewModel? item)
        {
            // 切換項目時立即秒級關閉先前影片資源，絕不阻塞 UI 執行緒
            StopVideo();

            if (item == null)
            {
                EmptyHintTextBlock.Visibility = Visibility.Visible;
                PreviewScrollViewer.Visibility = Visibility.Collapsed;
                TextPreviewBorder.Visibility = Visibility.Collapsed;
                return;
            }

            EmptyHintTextBlock.Visibility = Visibility.Collapsed;
            PreviewScrollViewer.Visibility = Visibility.Visible;

            // 1. 影片格式：先顯示首格高清縮圖與中央播放按鈕 (0 負擔、0 卡頓)
            if (item.IsVideo && File.Exists(item.FullPath))
            {
                _currentVideoPath = item.FullPath;
                StaticPreviewImage.Visibility = Visibility.Visible;
                BtnCenterPlay.Visibility = Visibility.Visible;
                VideoPlayer.Visibility = Visibility.Collapsed;
                VideoControlsOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                _currentVideoPath = null;
                StaticPreviewImage.Visibility = Visibility.Visible;
                BtnCenterPlay.Visibility = Visibility.Collapsed;
                VideoPlayer.Visibility = Visibility.Collapsed;
                VideoControlsOverlay.Visibility = Visibility.Collapsed;
            }

            // 2. 文字檔案內容預覽
            if (!item.IsDirectory && !string.IsNullOrEmpty(item.FullPath) && File.Exists(item.FullPath))
            {
                string ext = item.Extension;
                if (ext is ".txt" or ".log" or ".json" or ".xml" or ".md" or ".cs" or ".cpp" or ".h" or ".c" or ".py" or ".html" or ".css" or ".xaml" or ".ini" or ".yaml" or ".yml")
                {
                    try
                    {
                        using var stream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(stream, Encoding.UTF8, true);
                        char[] buffer = new char[2048];
                        int read = reader.Read(buffer, 0, buffer.Length);
                        TextPreviewContent.Text = new string(buffer, 0, read);
                        TextPreviewBorder.Visibility = Visibility.Visible;
                        return;
                    }
                    catch {}
                }
            }

            TextPreviewBorder.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 點擊中央按鈕啟動動態播放
        /// </summary>
        private void OnCenterPlayClick(object sender, RoutedEventArgs e)
        {
            StartVideoPlayback();
        }

        private void StartVideoPlayback()
        {
            if (string.IsNullOrEmpty(_currentVideoPath) || !File.Exists(_currentVideoPath)) return;

            try
            {
                StaticPreviewImage.Visibility = Visibility.Collapsed;
                BtnCenterPlay.Visibility = Visibility.Collapsed;
                VideoPlayer.Visibility = Visibility.Visible;
                VideoControlsOverlay.Visibility = Visibility.Visible;

                VideoPlayer.Source = new Uri(_currentVideoPath);
                VideoPlayer.Play();
                _isPlaying = true;
                TxtPlayPauseIcon.Text = "⏸";
            }
            catch
            {
                StopVideo();
            }
        }

        private void StopVideo()
        {
            try
            {
                _isPlaying = false;
                VideoPlayer.Close();
                VideoPlayer.Source = null;
                VideoPlayer.Visibility = Visibility.Collapsed;
                VideoControlsOverlay.Visibility = Visibility.Collapsed;
                if (!string.IsNullOrEmpty(_currentVideoPath))
                {
                    BtnCenterPlay.Visibility = Visibility.Visible;
                    StaticPreviewImage.Visibility = Visibility.Visible;
                }
            }
            catch {}
        }

        private void OnStopVideoClick(object sender, RoutedEventArgs e)
        {
            StopVideo();
        }

        private void OnPlayPauseClick(object sender, RoutedEventArgs e)
        {
            if (_isPlaying)
            {
                VideoPlayer.Pause();
                _isPlaying = false;
                TxtPlayPauseIcon.Text = "▶";
            }
            else
            {
                VideoPlayer.Play();
                _isPlaying = true;
                TxtPlayPauseIcon.Text = "⏸";
            }
        }

        private void OnMuteToggleClick(object sender, RoutedEventArgs e)
        {
            VideoPlayer.IsMuted = !VideoPlayer.IsMuted;
            TxtMuteIcon.Text = VideoPlayer.IsMuted ? "🔇" : "🔊";
        }

        private void OnVideoMediaEnded(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Position = TimeSpan.Zero;
            VideoPlayer.Play();
        }
    }
}
