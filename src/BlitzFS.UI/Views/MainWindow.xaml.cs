using System;
using System.Windows;
using System.Windows.Input;
using BlitzFS.UI.ViewModels;

namespace BlitzFS.UI.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            DataContext = ViewModel;
        }

        public MainWindow(string initialPath) : this()
        {
            if (ViewModel.SelectedTab != null)
            {
                _ = ViewModel.SelectedTab.NavigateToPathAsync(initialPath);
            }
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            StateChanged += (s, args) =>
            {
                IconMaximizePath.Data = (WindowState == WindowState.Maximized)
                    ? (System.Windows.Media.Geometry)FindResource("IconWinRestore")
                    : (System.Windows.Media.Geometry)FindResource("IconWinMaximize");
            };

            await ViewModel.InitializeAsync();
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnTabItemClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PaneViewModel tab)
            {
                ViewModel.SelectedTab = tab;
            }
        }

        private void OnAddTabClick(object sender, RoutedEventArgs e)
        {
            ViewModel.AddNewTab();
        }

        private void OnCloseTabClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PaneViewModel tab)
            {
                ViewModel.CloseTab(tab);
            }
        }

        private void OnNewWindowClick(object sender, RoutedEventArgs e)
        {
            var newWindow = new MainWindow(ViewModel.SelectedTab?.CurrentPath ?? "C:\\");
            newWindow.Show();
        }

        private async void OnSidebarPathSelected(object? sender, string targetPath)
        {
            var targetPane = ViewModel.ActivePane ?? ViewModel.SelectedTab;
            if (targetPane != null)
            {
                await targetPane.NavigateToPathAsync(targetPath);
            }
        }

        private void OnPrimaryPaneFocused(object sender, RoutedEventArgs e)
        {
            ViewModel.ActivePane = ViewModel.SelectedTab;
        }

        private void OnSecondaryPaneFocused(object sender, RoutedEventArgs e)
        {
            ViewModel.ActivePane = ViewModel.SecondaryPane;
        }

        private async void OnNewFolderClick(object sender, RoutedEventArgs e)
        {
            var targetPane = ViewModel.ActivePane ?? ViewModel.SelectedTab;
            if (targetPane != null)
            {
                await targetPane.CreateNewFolderAsync();
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var targetPane = ViewModel.ActivePane ?? ViewModel.SelectedTab;
            if (targetPane != null)
            {
                await targetPane.DeleteSelectedAsync();
            }
        }

        private void OnViewDetailsClick(object sender, RoutedEventArgs e)
        {
            ViewModel.SetViewMode(ViewMode.Details);
        }

        private void OnViewMediumIconsClick(object sender, RoutedEventArgs e)
        {
            ViewModel.SetViewMode(ViewMode.MediumIcons);
        }

        private void OnViewLargeIconsClick(object sender, RoutedEventArgs e)
        {
            ViewModel.SetViewMode(ViewMode.LargeIcons);
        }

        private void OnToggleDualPaneClick(object sender, RoutedEventArgs e)
        {
            ViewModel.ToggleDualPane();
        }

        private void OnTogglePreviewPaneClick(object sender, RoutedEventArgs e)
        {
            ViewModel.TogglePreviewPane();
        }

        private void OnQuickLookRequested(object? sender, FileItemViewModel item)
        {
            var ql = new QuickLookWindow(item);
            ql.Owner = this;
            ql.ShowDialog();
        }

        private async void OnFileDropped(object? sender, (string SourcePath, string TargetDir, bool ForceCopy, bool ForceMove) args)
        {
            bool isMove = false;
            if (args.ForceCopy)
            {
                isMove = false;
            }
            else if (args.ForceMove)
            {
                isMove = true;
            }
            else
            {
                // Windows 標準規範：同磁碟機移動、跨磁碟機複製
                char srcDrive = char.ToUpperInvariant(args.SourcePath[0]);
                char dstDrive = char.ToUpperInvariant(args.TargetDir[0]);
                isMove = (srcDrive == dstDrive);
            }

            await ViewModel.TransferFilesAsync(args.SourcePath, args.TargetDir, isMove);
        }

        #region 工具列排序選單處理

        private void OnSortButtonClick(object sender, RoutedEventArgs e)
        {
            var targetPane = ViewModel.ActivePane ?? ViewModel.SelectedTab;
            if (targetPane == null) return;

            // 更新選單勾選狀態
            MenuSortName.IsChecked = targetPane.IsSortByName;
            MenuSortDate.IsChecked = targetPane.IsSortByDate;
            MenuSortSize.IsChecked = targetPane.IsSortBySize;
            MenuSortType.IsChecked = targetPane.IsSortByType;

            MenuSortAsc.IsChecked = targetPane.IsSortAscending;
            MenuSortDesc.IsChecked = targetPane.IsSortDescending;

            MenuFoldersFirst.IsChecked = targetPane.FoldersFirst;

            if (BtnSortMenu.ContextMenu != null)
            {
                BtnSortMenu.ContextMenu.PlacementTarget = BtnSortMenu;
                BtnSortMenu.ContextMenu.IsOpen = true;
            }
        }

        private void OnSortByNameClick(object sender, RoutedEventArgs e)
        {
            var targetPane = ViewModel.ActivePane ?? ViewModel.SelectedTab;
            targetPane?.SortBy(SortField.Name, toggleDirectionIfSame: false);
        }

        private void OnSortByDateClick(object sender, RoutedEventArgs e)
        {
            var targetPane = ViewModel.ActivePane ?? ViewModel.SelectedTab;
            targetPane?.SortBy(SortField.ModifiedDate, toggleDirectionIfSame: false);
        }

        private void OnSortBySizeClick(object sender, RoutedEventArgs e)
        {
            var targetPane = ViewModel.ActivePane ?? ViewModel.SelectedTab;
            targetPane?.SortBy(SortField.Size, toggleDirectionIfSame: false);
        }

        private void OnSortByTypeClick(object sender, RoutedEventArgs e)
        {
            var targetPane = ViewModel.ActivePane ?? ViewModel.SelectedTab;
            targetPane?.SortBy(SortField.Type, toggleDirectionIfSame: false);
        }

        private void OnSortAscendingClick(object sender, RoutedEventArgs e)
        {
            var targetPane = ViewModel.ActivePane ?? ViewModel.SelectedTab;
            if (targetPane != null)
            {
                targetPane.CurrentSortDirection = SortDirection.Ascending;
            }
        }

        private void OnSortDescendingClick(object sender, RoutedEventArgs e)
        {
            var targetPane = ViewModel.ActivePane ?? ViewModel.SelectedTab;
            if (targetPane != null)
            {
                targetPane.CurrentSortDirection = SortDirection.Descending;
            }
        }

        private void OnToggleFoldersFirstClick(object sender, RoutedEventArgs e)
        {
            var targetPane = ViewModel.ActivePane ?? ViewModel.SelectedTab;
            if (targetPane != null)
            {
                targetPane.FoldersFirst = !targetPane.FoldersFirst;
            }
        }

        #endregion

        private void OnMainWindowKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+T: 新增分頁
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T)
            {
                ViewModel.AddNewTab();
                e.Handled = true;
            }
            // Ctrl+W: 關閉分頁
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W)
            {
                if (ViewModel.SelectedTab != null)
                {
                    ViewModel.CloseTab(ViewModel.SelectedTab);
                    e.Handled = true;
                }
            }
            // Ctrl+N: 開啟新視窗
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
            {
                OnNewWindowClick(sender, e);
                e.Handled = true;
            }
            // Ctrl+Shift+N: 新建資料夾
            else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.N)
            {
                OnNewFolderClick(sender, e);
                e.Handled = true;
            }
            // F10: 切換雙窗格
            else if (e.Key == Key.F10)
            {
                ViewModel.ToggleDualPane();
                e.Handled = true;
            }
        }
    }
}
