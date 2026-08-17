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
            if (ViewModel == null) return;

            var element = e.OriginalSource as DependencyObject;
            var container = FindVisualAncestor<ListBoxItem>(element);
            if (container?.DataContext is FileItemViewModel item)
            {
                await ViewModel.OpenItemAsync(item);
            }
        }


        private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel == null) return;
            if (sender is ListBox listBox)
            {
                var selected = listBox.SelectedItems.OfType<FileItemViewModel>().ToList();
                ViewModel.UpdateSelection(selected);
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
                await ViewModel.DeleteSelectedAsync();
                e.Handled = true;
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
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
            {
                if (DetailsListView.IsVisible)
                {
                    DetailsListView.SelectAll();
                }
                else if (ThumbnailListBox.IsVisible)
                {
                    ThumbnailListBox.SelectAll();
                }
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
            var paths = ViewModel?.GetSelectedPaths();
            if (paths != null && paths.Count > 0)
            {
                Services.AppClipboardService.SetCopy(paths);
            }
        }

        private void OnContextCutClick(object sender, RoutedEventArgs e)
        {
            var paths = ViewModel?.GetSelectedPaths();
            if (paths != null && paths.Count > 0)
            {
                Services.AppClipboardService.SetCut(paths);
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

                    string srcDir = GetParentDirectory(src);
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

        private static string GetParentDirectory(string path)
        {
            if (path.Contains('|'))
            {
                var parts = path.Split('|', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 1 ? string.Join("|", parts.Take(parts.Length - 1)) : string.Empty;
            }
            try
            {
                return Path.GetDirectoryName(path) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
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

        private void OnItemContextMenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            var pinItem = menu.FindName("MenuPinToQuickAccess") as MenuItem;
            if (pinItem == null) return;

            if (ViewModel?.SelectedItem != null && ViewModel.SelectedItem.IsDirectory)
            {
                pinItem.Visibility = Visibility.Visible;
                bool isPinned = BlitzFS.UI.Services.QuickAccessService.Instance.IsPinned(ViewModel.SelectedItem.FullPath);
                pinItem.Header = isPinned ? "從快速存取取消釘選" : "釘選到快速存取";
            }
            else
            {
                pinItem.Visibility = Visibility.Collapsed;
            }
        }

        private void OnContextPinToQuickAccessClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.SelectedItem != null && ViewModel.SelectedItem.IsDirectory)
            {
                string path = ViewModel.SelectedItem.FullPath;
                var service = BlitzFS.UI.Services.QuickAccessService.Instance;
                if (service.IsPinned(path))
                {
                    service.UnpinPath(path);
                }
                else
                {
                    service.PinPath(path, ViewModel.SelectedItem.Name);
                }
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

        #region 拖曳支援 (Drag and Drop 與多選保護)

        private FileItemViewModel? _pendingSingleSelectionItem = null;
        private bool _dragStartedOnScrollBar = false;

        private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
            _pendingSingleSelectionItem = null;
            _dragStartedOnScrollBar = false;

            var element = e.OriginalSource as DependencyObject;

            // 1. 若點擊在 ScrollBar、Thumb 或 GridViewColumnHeader 上，絕對禁止啟動檔案拖曳
            if (FindVisualAncestor<System.Windows.Controls.Primitives.ScrollBar>(element) != null ||
                FindVisualAncestor<System.Windows.Controls.Primitives.Thumb>(element) != null ||
                FindVisualAncestor<GridViewColumnHeader>(element) != null)
            {
                _dragStartedOnScrollBar = true;
                return;
            }

            if (sender is ListBox listBox)
            {
                var container = FindVisualAncestor<ListBoxItem>(element);

                // 2. 若點擊在空白處 (不是任何 ListBoxItem) -> 清空所有選取！
                if (container == null)
                {
                    listBox.UnselectAll();
                    listBox.SelectedItem = null;
                    ViewModel?.UpdateSelection(null);
                    return;
                }

                // 3. 若點擊在已選取的項目上 (且沒有按下 Ctrl 或 Shift) -> 延遲單選判定以允許整組多選拖曳！
                if (Keyboard.Modifiers == ModifierKeys.None &&
                    container.DataContext is FileItemViewModel item &&
                    listBox.SelectedItems.Contains(item) &&
                    listBox.SelectedItems.Count > 1)
                {
                    _pendingSingleSelectionItem = item;
                    e.Handled = true;
                    container.Focus();
                }
            }
        }

        private void OnListPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_pendingSingleSelectionItem != null)
            {
                if (sender is ListBox listBox && !_isDragging)
                {
                    listBox.SelectedItem = _pendingSingleSelectionItem;
                    ViewModel?.UpdateSelection(new[] { _pendingSingleSelectionItem });
                }
                _pendingSingleSelectionItem = null;
            }
        }

        private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void OnListMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragStartedOnScrollBar) return;

            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging && ViewModel != null)
            {
                var paths = ViewModel.GetSelectedPaths();
                if (paths.Count == 0 && ViewModel.SelectedItem != null)
                {
                    paths.Add(ViewModel.SelectedItem.FullPath);
                }

                if (paths.Count > 0)
                {
                    Point currentPos = e.GetPosition(null);
                    Vector diff = _dragStartPoint - currentPos;

                    if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                    {
                        _isDragging = true;
                        _pendingSingleSelectionItem = null;
                        try
                        {
                            var dataObject = new DataObject(DataFormats.FileDrop, paths.ToArray());
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
                        if (Services.ShellFolderService.Instance.IsShellPath(files[0]) ||
                            Services.ShellFolderService.Instance.IsShellPath(ViewModel.CurrentPath))
                        {
                            e.Effects = DragDropEffects.Copy;
                        }
                        else if (files[0].Length > 0 && ViewModel.CurrentPath.Length > 0)
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

                        string srcDir = GetParentDirectory(src);
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

        #region 排序事件處理

        private void OnBackgroundContextMenuOpened(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || sender is not ContextMenu menu) return;

            if (menu.FindName("CtxSortName") is MenuItem miName) miName.IsChecked = ViewModel.IsSortByName;
            if (menu.FindName("CtxSortDate") is MenuItem miDate) miDate.IsChecked = ViewModel.IsSortByDate;
            if (menu.FindName("CtxSortSize") is MenuItem miSize) miSize.IsChecked = ViewModel.IsSortBySize;
            if (menu.FindName("CtxSortType") is MenuItem miType) miType.IsChecked = ViewModel.IsSortByType;

            if (menu.FindName("CtxSortAsc") is MenuItem miAsc) miAsc.IsChecked = ViewModel.IsSortAscending;
            if (menu.FindName("CtxSortDesc") is MenuItem miDesc) miDesc.IsChecked = ViewModel.IsSortDescending;

            if (menu.FindName("CtxFoldersFirst") is MenuItem miFolders) miFolders.IsChecked = ViewModel.FoldersFirst;
        }

        private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            if (e.OriginalSource is GridViewColumnHeader header && header.Tag is string tagStr)
            {
                if (Enum.TryParse<SortField>(tagStr, out var field))
                {
                    ViewModel.SortBy(field);
                }
            }
        }

        private void OnSortByNameClick(object sender, RoutedEventArgs e)
        {
            ViewModel?.SortBy(SortField.Name, toggleDirectionIfSame: false);
        }

        private void OnSortByDateClick(object sender, RoutedEventArgs e)
        {
            ViewModel?.SortBy(SortField.ModifiedDate, toggleDirectionIfSame: false);
        }

        private void OnSortBySizeClick(object sender, RoutedEventArgs e)
        {
            ViewModel?.SortBy(SortField.Size, toggleDirectionIfSame: false);
        }

        private void OnSortByTypeClick(object sender, RoutedEventArgs e)
        {
            ViewModel?.SortBy(SortField.Type, toggleDirectionIfSame: false);
        }

        private void OnSortAscendingClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.CurrentSortDirection = SortDirection.Ascending;
            }
        }

        private void OnSortDescendingClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.CurrentSortDirection = SortDirection.Descending;
            }
        }

        private void OnToggleFoldersFirstClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.FoldersFirst = !ViewModel.FoldersFirst;
            }
        }

        #endregion
    }
}
