using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlitzFS.Bridge;
using BlitzFS.UI.Services;

namespace BlitzFS.UI.ViewModels
{
    /// <summary>
    /// 檔案管理窗格 ViewModel (極速秒切、批次 UI 更新、支援所有磁碟與目錄)
    /// </summary>
    public class PaneViewModel : ViewModelBase
    {
        private readonly CoreEngineWrapper _engine;
        private char _currentDrive = 'D';
        private string _currentPath = "D:\\";
        private string _displayPath = "D:\\";
        private string _filterText = string.Empty;
        private FileItemViewModel? _selectedItem;
        private bool _isLoading;
        private ViewMode _viewMode = ViewMode.Details;
        private string _tabTitle = "D:\\";
        private SortField _currentSortField = SortField.Name;
        private SortDirection _currentSortDirection = SortDirection.Ascending;
        private bool _foldersFirst = true;

        private readonly List<FileItemViewModel> _allItems = new();
        private readonly Stack<string> _backPathStack = new();
        private readonly Stack<string> _forwardPathStack = new();

        public ObservableRangeCollection<FileItemViewModel> FilteredItems { get; } = new();

        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string TabTitle
        {
            get => _tabTitle;
            private set => SetProperty(ref _tabTitle, value);
        }

        public string DisplayPath
        {
            get => string.IsNullOrEmpty(_displayPath) ? CurrentPath : _displayPath;
            private set => SetProperty(ref _displayPath, value);
        }


        public ViewMode CurrentViewMode
        {
            get => _viewMode;
            set
            {
                if (SetProperty(ref _viewMode, value))
                {
                    OnPropertyChanged(nameof(IsDetailsView));
                    OnPropertyChanged(nameof(IsThumbnailView));
                    OnPropertyChanged(nameof(IsMediumIconsView));
                    OnPropertyChanged(nameof(IsLargeIconsView));
                }
            }
        }

        public bool IsDetailsView => CurrentViewMode == ViewMode.Details;
        public bool IsThumbnailView => CurrentViewMode != ViewMode.Details;
        public bool IsMediumIconsView => CurrentViewMode == ViewMode.MediumIcons;
        public bool IsLargeIconsView => CurrentViewMode == ViewMode.LargeIcons;

        #region 排序屬性

        public SortField CurrentSortField
        {
            get => _currentSortField;
            set
            {
                if (SetProperty(ref _currentSortField, value))
                {
                    NotifySortProperties();
                    ApplySort();
                }
            }
        }

        public SortDirection CurrentSortDirection
        {
            get => _currentSortDirection;
            set
            {
                if (SetProperty(ref _currentSortDirection, value))
                {
                    NotifySortProperties();
                    ApplySort();
                }
            }
        }

        public bool FoldersFirst
        {
            get => _foldersFirst;
            set
            {
                if (SetProperty(ref _foldersFirst, value))
                {
                    ApplySort();
                }
            }
        }

        public bool IsSortByName
        {
            get => CurrentSortField == SortField.Name;
            set { if (value) CurrentSortField = SortField.Name; }
        }

        public bool IsSortBySize
        {
            get => CurrentSortField == SortField.Size;
            set { if (value) CurrentSortField = SortField.Size; }
        }

        public bool IsSortByDate
        {
            get => CurrentSortField == SortField.ModifiedDate;
            set { if (value) CurrentSortField = SortField.ModifiedDate; }
        }

        public bool IsSortByType
        {
            get => CurrentSortField == SortField.Type;
            set { if (value) CurrentSortField = SortField.Type; }
        }

        public bool IsSortAscending
        {
            get => CurrentSortDirection == SortDirection.Ascending;
            set { if (value) CurrentSortDirection = SortDirection.Ascending; }
        }

        public bool IsSortDescending
        {
            get => CurrentSortDirection == SortDirection.Descending;
            set { if (value) CurrentSortDirection = SortDirection.Descending; }
        }

        private void NotifySortProperties()
        {
            OnPropertyChanged(nameof(IsSortByName));
            OnPropertyChanged(nameof(IsSortBySize));
            OnPropertyChanged(nameof(IsSortByDate));
            OnPropertyChanged(nameof(IsSortByType));
            OnPropertyChanged(nameof(IsSortAscending));
            OnPropertyChanged(nameof(IsSortDescending));
        }

        /// <summary>
        /// 切換或指定排序欄位 (若指定相同欄位則自動切換遞增/遞減)
        /// </summary>
        public void SortBy(SortField field, bool? toggleDirectionIfSame = true)
        {
            if (CurrentSortField == field && toggleDirectionIfSame == true)
            {
                CurrentSortDirection = CurrentSortDirection == SortDirection.Ascending
                    ? SortDirection.Descending
                    : SortDirection.Ascending;
            }
            else
            {
                CurrentSortField = field;
            }
        }

        #endregion

        public char CurrentDrive
        {
            get => _currentDrive;
            set
            {
                if (SetProperty(ref _currentDrive, value))
                {
                    _ = NavigateToPathAsync($"{value}:\\");
                }
            }
        }

        public string CurrentPath
        {
            get => _currentPath;
            private set
            {
                if (SetProperty(ref _currentPath, value))
                {
                    UpdateTabTitle(value);
                }
            }
        }

        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    ApplyFilter();
                }
            }
        }

        private readonly List<FileItemViewModel> _selectedItems = new();
        public IReadOnlyList<FileItemViewModel> SelectedItems => _selectedItems;

        public bool HasSelection => _selectedItems.Count > 0;

        public string SelectedSummary
        {
            get
            {
                if (_selectedItems.Count == 0) return string.Empty;
                ulong totalBytes = 0;
                foreach (var item in _selectedItems)
                {
                    if (!item.IsDirectory) totalBytes += item.FileSize;
                }
                return $"|  已選取 {_selectedItems.Count} 個項目 ({FormatBytes(totalBytes)})";
            }
        }

        public void UpdateSelection(IEnumerable<FileItemViewModel>? selectedList)
        {
            _selectedItems.Clear();
            if (selectedList != null)
            {
                _selectedItems.AddRange(selectedList);
            }
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedSummary));
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


        public List<string> GetSelectedPaths()
        {
            if (_selectedItems.Count > 0)
            {
                return _selectedItems.Select(x => x.FullPath).Where(p => !string.IsNullOrEmpty(p)).ToList();
            }
            if (SelectedItem != null && !string.IsNullOrEmpty(SelectedItem.FullPath))
            {
                return new List<string> { SelectedItem.FullPath };
            }
            return new List<string>();
        }

        public FileItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }


        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public bool CanGoBack => _backPathStack.Count > 0;
        public bool CanGoForward => _forwardPathStack.Count > 0;

        public PaneViewModel(CoreEngineWrapper engine, char initialDrive = 'D')
        {
            _engine = engine;
            _currentDrive = initialDrive;
            _currentPath = $"{initialDrive}:\\";
            _displayPath = _currentPath;
            UpdateTabTitle(_currentPath);
        }

        public PaneViewModel(CoreEngineWrapper engine, string initialPath)
        {
            _engine = engine;
            _currentPath = string.IsNullOrEmpty(initialPath) ? "C:\\" : initialPath;
            _displayPath = _currentPath;
            if (_currentPath.Length >= 2 && _currentPath[1] == ':')
            {
                _currentDrive = char.ToUpper(_currentPath[0]);
            }
            UpdateTabTitle(_currentPath);
        }

        private void UpdateTabTitle(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                TabTitle = "首頁";
                return;
            }

            if (BlitzFS.UI.Services.ShellFolderService.Instance.IsShellPath(path))
            {
                TabTitle = BlitzFS.UI.Services.ShellFolderService.Instance.GetFriendlyDisplayPath(path);
                return;
            }

            string trimmed = path.TrimEnd('\\', '/');
            if (trimmed.Length <= 2)
            {
                TabTitle = $"磁碟 ({trimmed})";
            }
            else
            {
                TabTitle = Path.GetFileName(trimmed);
                if (string.IsNullOrEmpty(TabTitle))
                {
                    TabTitle = trimmed;
                }
            }
        }

        /// <summary>
        /// 導航至任意 Windows 路徑 (支援快速存取、磁碟機與輸入路徑)
        /// </summary>
        public async Task NavigateToPathAsync(string path, bool recordHistory = true)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            string normalizedPath = path.Trim();
            if (normalizedPath.Length == 2 && normalizedPath[1] == ':')
            {
                normalizedPath += "\\";
            }

            if (recordHistory && !string.Equals(CurrentPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                _backPathStack.Push(CurrentPath);
                _forwardPathStack.Clear();
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoForward));
            }

            CurrentPath = normalizedPath;
            if (normalizedPath.Length >= 2 && normalizedPath[1] == ':')
            {
                _currentDrive = char.ToUpper(normalizedPath[0]);
            }

            await LoadPathItemsAsync(normalizedPath);
        }

        /// <summary>
        /// 載入指定路徑下的子項目 (採用 EnumerateFileSystemInfos 極速直讀與批次 UI 載入)
        /// </summary>
        private string? _currentShellParentPath;

        /// <summary>
        /// 載入指定路徑下的子項目 (支援傳統磁碟零額外 IO 直讀與手機/便攜設備原生枚舉)
        /// </summary>
        public async Task LoadPathItemsAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            IsLoading = true;
            SelectedItem = null; // 切換目錄時立即清除前次選中，防止預覽鎖定

            try
            {
                var list = new List<FileItemViewModel>();
                string? detectedTitle = null;
                string? detectedFriendlyPath = null;

                await Task.Run(() =>
                {
                    // 1. 判斷是否為手機 / 便攜式 Shell 虛擬目錄
                    if (BlitzFS.UI.Services.ShellFolderService.Instance.IsShellPath(path))
                    {
                        var (shellItems, title, friendlyPath, parent) = BlitzFS.UI.Services.ShellFolderService.Instance.EnumerateShellFolder(path);
                        list.AddRange(shellItems);
                        detectedTitle = title;
                        detectedFriendlyPath = friendlyPath;
                        _currentShellParentPath = parent;
                    }
                    // 2. 傳統 Windows 檔案路徑 (C:\, D:\, E:\...)
                    else if (Directory.Exists(path))
                    {
                        _currentShellParentPath = null;
                        detectedFriendlyPath = path;
                        try
                        {
                            var dirInfo = new DirectoryInfo(path);

                            // 使用 EnumerateFileSystemInfos 進行零額外 IO 快速枚舉
                            foreach (var fsi in dirInfo.EnumerateFileSystemInfos())
                            {
                                try
                                {
                                    bool isDir = (fsi.Attributes & FileAttributes.Directory) != 0;
                                    bool isHidden = (fsi.Attributes & FileAttributes.Hidden) != 0;
                                    if (isHidden) continue; // 略過隱藏檔案以加速

                                    ulong size = 0;
                                    if (!isDir && fsi is FileInfo fi)
                                    {
                                        size = (ulong)fi.Length;
                                    }

                                    CompactNode dummyNode = new CompactNode
                                    {
                                        BitFlags = isDir ? (ushort)1 : (ushort)0,
                                        LastWriteTime = (ulong)fsi.LastWriteTime.ToFileTime(),
                                        FileSize = size
                                    };

                                    list.Add(new FileItemViewModel(dummyNode, fsi.Name, fsi.FullName));
                                }
                                catch {}
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"存取目錄失敗: {ex.Message}");
                        }
                    }

                    // 依據當前排序設定 (資料夾優先、自然語言數字排序、欄位與方向) 進行排序
                    SortItemList(list);
                });

                if (!string.IsNullOrEmpty(detectedFriendlyPath))
                {
                    DisplayPath = detectedFriendlyPath;
                }
                else
                {
                    DisplayPath = path;
                }

                if (!string.IsNullOrEmpty(detectedTitle) && detectedTitle != path)
                {
                    TabTitle = detectedTitle;
                }

                if (System.Windows.Application.Current != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _allItems.Clear();
                        _allItems.AddRange(list);
                        ApplyFilter();
                    });
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 重新整理目前目錄
        /// </summary>
        public async Task RefreshAsync()
        {
            await LoadPathItemsAsync(CurrentPath);
        }

        /// <summary>
        /// 進入子目錄或開啟檔案
        /// </summary>
        public async Task OpenItemAsync(FileItemViewModel? item)
        {
            if (item == null) return;

            if (item.IsDirectory)
            {
                await NavigateToPathAsync(item.FullPath);
            }
            else
            {
                try
                {
                    if (File.Exists(item.FullPath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = item.FullPath,
                            UseShellExecute = true
                        });
                    }
                    else if (BlitzFS.UI.Services.ShellFolderService.Instance.IsShellPath(item.FullPath))
                    {
                        BlitzFS.UI.Services.ShellFolderService.Instance.OpenShellFile(item.FullPath);
                    }
                }
                catch {}
            }
        }

        /// <summary>
        /// 返回上一層目錄 (Navigate Up)
        /// </summary>
        public async Task NavigateUpAsync()
        {
            try
            {
                if (BlitzFS.UI.Services.ShellFolderService.Instance.IsShellPath(CurrentPath))
                {
                    if (!string.IsNullOrEmpty(_currentShellParentPath))
                    {
                        await NavigateToPathAsync(_currentShellParentPath);
                    }
                    else
                    {
                        await NavigateToPathAsync("C:\\");
                    }
                    return;
                }

                string current = CurrentPath.TrimEnd('\\', '/');
                if (current.Length <= 2)
                {
                    // 已經在根目錄，無需再向上
                    return;
                }

                DirectoryInfo? parent = Directory.GetParent(current);
                if (parent != null)
                {
                    await NavigateToPathAsync(parent.FullName);
                }
            }
            catch {}
        }


        /// <summary>
        /// 歷史上一頁
        /// </summary>
        public async Task GoBackAsync()
        {
            if (_backPathStack.Count > 0)
            {
                string targetPath = _backPathStack.Pop();
                _forwardPathStack.Push(CurrentPath);
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoForward));

                await NavigateToPathAsync(targetPath, recordHistory: false);
            }
        }

        /// <summary>
        /// 歷史下一頁
        /// </summary>
        public async Task GoForwardAsync()
        {
            if (_forwardPathStack.Count > 0)
            {
                string targetPath = _forwardPathStack.Pop();
                _backPathStack.Push(CurrentPath);
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoForward));

                await NavigateToPathAsync(targetPath, recordHistory: false);
            }
        }

        /// <summary>
        /// 對檔案清單依據當前排序規則進行排序 (支援 Windows 原生自然語言比較)
        /// </summary>
        public void SortItemList(List<FileItemViewModel> list)
        {
            list.Sort((a, b) =>
            {
                // 1. 資料夾優先置頂判斷
                if (FoldersFirst && a.IsDirectory != b.IsDirectory)
                {
                    return a.IsDirectory ? -1 : 1;
                }

                // 2. 依照指定欄位比較
                int result = 0;
                switch (CurrentSortField)
                {
                    case SortField.Name:
                        result = NaturalStringComparer.Instance.Compare(a.Name, b.Name);
                        break;
                    case SortField.Size:
                        result = a.FileSize.CompareTo(b.FileSize);
                        break;
                    case SortField.ModifiedDate:
                        result = a.ModifiedTime.CompareTo(b.ModifiedTime);
                        break;
                    case SortField.Type:
                        result = string.Compare(a.TypeName, b.TypeName, StringComparison.OrdinalIgnoreCase);
                        if (result == 0)
                        {
                            result = string.Compare(a.Extension, b.Extension, StringComparison.OrdinalIgnoreCase);
                        }
                        break;
                }

                // 3. 若值相同或未分出勝負，以名稱作為次要排序依據
                if (result == 0)
                {
                    result = NaturalStringComparer.Instance.Compare(a.Name, b.Name);
                }

                // 4. 方向處理 (遞減反轉)
                return CurrentSortDirection == SortDirection.Ascending ? result : -result;
            });
        }

        /// <summary>
        /// 重新套用排序並刷新篩選顯示
        /// </summary>
        public void ApplySort()
        {
            SortItemList(_allItems);
            ApplyFilter();
        }

        /// <summary>
        /// 0ms 批次即時篩選 (使用 ReplaceAll 觸發單次 Reset，0 重繪卡頓)
        /// </summary>
        private void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(_filterText))
            {
                FilteredItems.ReplaceAll(_allItems);
            }
            else
            {
                string search = _filterText.Trim();
                var filtered = _allItems.Where(item => item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
                FilteredItems.ReplaceAll(filtered);
            }
        }

        /// <summary>
        /// 新建資料夾
        /// </summary>
        public async Task CreateNewFolderAsync()
        {
            try
            {
                string baseName = "新資料夾";
                string targetPath = Path.Combine(CurrentPath, baseName);
                int count = 1;
                while (Directory.Exists(targetPath))
                {
                    count++;
                    targetPath = Path.Combine(CurrentPath, $"{baseName} ({count})");
                }

                Directory.CreateDirectory(targetPath);
                await RefreshAsync();
            }
            catch {}
        }

        /// <summary>
        /// 批次刪除選取的項目
        /// </summary>
        public async Task DeleteSelectedAsync()
        {
            var targets = _selectedItems.Count > 0
                ? _selectedItems.ToList()
                : (SelectedItem != null ? new List<FileItemViewModel> { SelectedItem } : new());

            if (targets.Count == 0) return;

            try
            {
                foreach (var item in targets)
                {
                    if (string.IsNullOrEmpty(item.FullPath)) continue;

                    try
                    {
                        if (item.IsDirectory)
                        {
                            if (Directory.Exists(item.FullPath))
                            {
                                Directory.Delete(item.FullPath, true);
                            }
                        }
                        else
                        {
                            if (File.Exists(item.FullPath))
                            {
                                File.Delete(item.FullPath);
                            }
                        }
                    }
                    catch {}
                }

                await RefreshAsync();
            }
            catch {}
        }

    }
}
