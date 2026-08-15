using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BlitzFS.UI.ViewModels;

namespace BlitzFS.UI.Views
{
    public partial class FilePaneView : UserControl
    {
        public event EventHandler<FileItemViewModel>? QuickLookRequested;
        public event EventHandler<(string SourcePath, string TargetDir, bool ForceCopy, bool ForceMove)>? FileDropped;

        private Point _dragStartPoint;
        private bool _isDragging;

        private PaneViewModel? ViewModel => DataContext as PaneViewModel;

        public FilePaneView()
        {
            InitializeComponent();
        }

        #region 導航與按鍵處理 (含滑鼠側鍵 4、5)

        private async void OnFilePaneMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null) return;

            // 滑鼠第 4 鍵 (XButton1): 返回上一頁 (Back)
            if (e.ChangedButton == MouseButton.XButton1)
            {
                if (ViewModel.CanGoBack)
                {
                    await ViewModel.GoBackAsync();
                    e.Handled = true;
                }
            }
            // 滑鼠第 5 鍵 (XButton2): 前往下一頁 (Forward)
            else if (e.ChangedButton == MouseButton.XButton2)
            {
                if (ViewModel.CanGoForward)
                {
                    await ViewModel.GoForwardAsync();
                    e.Handled = true;
                }
            }
        }

        private async void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) await ViewModel.GoBackAsync();
        }

        private async void OnForwardClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) await ViewModel.GoForwardAsync();
        }

        private async void OnUpClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) await ViewModel.NavigateUpAsync();
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) await ViewModel.RefreshAsync();
        }

        private async void OnPathTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ViewModel != null)
            {
                string targetPath = PathTextBox.Text.Trim();
                if (Directory.Exists(targetPath))
                {
                    await ViewModel.NavigateToPathAsync(targetPath);
                }
            }
        }

        private async void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel?.SelectedItem != null)
            {
                await ViewModel.OpenItemAsync(ViewModel.SelectedItem);
            }
        }

        private async void OnListViewKeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel == null) return;

            if (e.Key == Key.Enter)
            {
                if (ViewModel.SelectedItem != null)
                {
                    await ViewModel.OpenItemAsync(ViewModel.SelectedItem);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Back)
            {
                await ViewModel.NavigateUpAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Space)
            {
                if (ViewModel.SelectedItem != null)
                {
                    QuickLookRequested?.Invoke(this, ViewModel.SelectedItem);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Delete)
            {
                if (ViewModel.SelectedItem != null)
                {
                    await ViewModel.DeleteSelectedAsync();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.F2)
            {
                OnContextRenameClick(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                await ViewModel.RefreshAsync();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
            {
                OnContextCopyClick(sender, e);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.X)
            {
                OnContextCutClick(sender, e);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
            {
                OnContextPasteClick(sender, e);
                e.Handled = true;
            }
        }

        #endregion

        #region 右鍵快顯功能表 (Context Menu Handlers)

        private async void OnContextOpenClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedItem != null)
            {
                await ViewModel.OpenItemAsync(ViewModel.SelectedItem);
            }
        }

        private void OnContextQuickLookClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedItem != null)
            {
                QuickLookRequested?.Invoke(this, ViewModel.SelectedItem);
            }
        }

        private void OnContextCopyClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedItem != null && !string.IsNullOrEmpty(ViewModel.SelectedItem.FullPath))
            {
                Services.AppClipboardService.SetCopy(new[] { ViewModel.SelectedItem.FullPath });
            }
        }

        private void OnContextCutClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedItem != null && !string.IsNullOrEmpty(ViewModel.SelectedItem.FullPath))
            {
                Services.AppClipboardService.SetCut(new[] { ViewModel.SelectedItem.FullPath });
            }
        }

        private async void OnContextPasteClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            var (files, isMove) = Services.AppClipboardService.GetClipboardFiles();
            if (files.Count > 0)
            {
                string targetDir = ViewModel.CurrentPath;
                foreach (var src in files)
                {
                    if (string.IsNullOrEmpty(src)) continue;

                    string? srcDir = Path.GetDirectoryName(src);
                    if (!string.IsNullOrEmpty(srcDir) &&
                        string.Equals(srcDir.TrimEnd('\\', '/'), targetDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 若剪下操作 (isMove = true)，無論同硬碟還是跨硬碟均為移動；若複製操作則為複製
                    FileDropped?.Invoke(this, (src, targetDir, !isMove, isMove));
                }

                Services.AppClipboardService.ClearCutStateAfterPaste();
                await ViewModel.RefreshAsync();
            }
        }

        private async void OnContextRenameClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedItem == null) return;

            string oldPath = ViewModel.SelectedItem.FullPath;
            string oldName = ViewModel.SelectedItem.Name;

            // 簡易彈出重新命名提示輸入
            string newName = Microsoft.VisualBasic.Interaction.InputBox("請輸入新檔案名稱:", "重新命名", oldName);
            if (!string.IsNullOrWhiteSpace(newName) && newName != oldName)
            {
                try
                {
                    string dir = Path.GetDirectoryName(oldPath) ?? ViewModel.CurrentPath;
                    string newPath = Path.Combine(dir, newName);

                    if (ViewModel.SelectedItem.IsDirectory)
                    {
                        Directory.Move(oldPath, newPath);
                    }
                    else
                    {
                        File.Move(oldPath, newPath);
                    }
                    await ViewModel.RefreshAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"重新命名失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void OnContextDeleteClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedItem != null)
            {
                var result = MessageBox.Show($"確定要刪除「{ViewModel.SelectedItem.Name}」嗎？", "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    await ViewModel.DeleteSelectedAsync();
                }
            }
        }

        private void OnContextCopyFullPathClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedItem != null && !string.IsNullOrEmpty(ViewModel.SelectedItem.FullPath))
            {
                try
                {
                    Clipboard.SetText(ViewModel.SelectedItem.FullPath);
                }
                catch {}
            }
        }

        private void OnContextShowInExplorerClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedItem != null && !string.IsNullOrEmpty(ViewModel.SelectedItem.FullPath))
            {
                try
                {
                    Process.Start("explorer.exe", $"/select,\"{ViewModel.SelectedItem.FullPath}\"");
                }
                catch {}
            }
        }

        private async void OnContextNewFolderClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                await ViewModel.CreateNewFolderAsync();
            }
        }

        #endregion

        #region 拖曳支援 (Drag and Drop)

        private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
        }

        private void OnListMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging && ViewModel?.SelectedItem != null)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    try
                    {
                        var dataObject = new DataObject(DataFormats.FileDrop, new[] { ViewModel.SelectedItem.FullPath });
                        DragDrop.DoDragDrop(this, dataObject, DragDropEffects.Copy | DragDropEffects.Move);
                    }
                    catch {}
                    finally
                    {
                        _isDragging = false;
                    }
                }
            }
        }

        private void OnFilePaneDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && ViewModel != null)
            {
                bool isCtrl = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
                bool isShift = (e.KeyStates & DragDropKeyStates.ShiftKey) != 0;

                if (isCtrl)
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else if (isShift)
                {
                    e.Effects = DragDropEffects.Move;
                }
                else
                {
                    string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null && files.Length > 0)
                    {
                        char srcDrive = char.ToUpperInvariant(files[0][0]);
                        char dstDrive = char.ToUpperInvariant(ViewModel.CurrentPath[0]);
                        e.Effects = (srcDrive == dstDrive) ? DragDropEffects.Move : DragDropEffects.Copy;
                    }
                    else
                    {
                        e.Effects = DragDropEffects.Copy;
                    }
                }
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void OnFilePaneDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && ViewModel != null)
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    string targetDir = ViewModel.CurrentPath;
                    bool isCtrl = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
                    bool isShift = (e.KeyStates & DragDropKeyStates.ShiftKey) != 0;

                    foreach (var src in files)
                    {
                        if (string.IsNullOrEmpty(src)) continue;

                        string? srcDir = Path.GetDirectoryName(src);
                        if (!string.IsNullOrEmpty(srcDir) &&
                            string.Equals(srcDir.TrimEnd('\\', '/'), targetDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        FileDropped?.Invoke(this, (src, targetDir, isCtrl, isShift));
                    }
                }
            }
        }

        #endregion
    }
}
