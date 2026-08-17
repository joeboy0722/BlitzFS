using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlitzFS.Bridge;

namespace BlitzFS.UI.ViewModels
{
    /// <summary>
    /// 主視窗全域狀態 ViewModel (支援多分頁、雙窗格焦點感知、縮圖/清單切換、即時預覽與浮動傳輸)
    /// </summary>
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly CoreEngineWrapper _engine;
        private bool _isDualPaneMode = false;
        private bool _isPreviewPaneOpen = false;
        private PaneViewModel? _selectedTab;
        private PaneViewModel? _secondaryPane;
        private PaneViewModel? _activePane;
        private FileItemViewModel? _previewItem;
        private string _statusMessage = "就緒";

        public CoreEngineWrapper Engine => _engine;
        public SidebarViewModel Sidebar { get; }
        public ObservableCollection<PaneViewModel> Tabs { get; } = new();
        public TransferViewModel TransferHub { get; }

        public bool IsDualPaneMode
        {
            get => _isDualPaneMode;
            set
            {
                if (SetProperty(ref _isDualPaneMode, value))
                {
                    if (value && SecondaryPane == null)
                    {
                        SecondaryPane = new PaneViewModel(_engine, SelectedTab?.CurrentPath ?? "D:\\");
                        _ = SecondaryPane.RefreshAsync();
                        SecondaryPane.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName == nameof(PaneViewModel.SelectedItem) && ActivePane == SecondaryPane)
                            {
                                UpdatePreviewItem();
                            }
                        };
                    }
                }
            }
        }

        public bool IsPreviewPaneOpen
        {
            get => _isPreviewPaneOpen;
            set
            {
                if (SetProperty(ref _isPreviewPaneOpen, value))
                {
                    UpdatePreviewItem();
                }
            }
        }

        public PaneViewModel? SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (SetProperty(ref _selectedTab, value))
                {
                    foreach (var tab in Tabs)
                    {
                        tab.IsSelected = (tab == value);
                    }

                    if (ActivePane == null || ActivePane != SecondaryPane)
                    {
                        ActivePane = _selectedTab;
                    }
                    UpdatePreviewItem();
                    if (_selectedTab != null)
                    {
                        _selectedTab.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName == nameof(PaneViewModel.SelectedItem) && ActivePane == _selectedTab)
                            {
                                UpdatePreviewItem();
                            }
                        };
                    }
                }
            }
        }

        public PaneViewModel? SecondaryPane
        {
            get => _secondaryPane;
            set => SetProperty(ref _secondaryPane, value);
        }

        /// <summary>
        /// 當前獲得焦點的作用窗格 (左窗格或右窗格)
        /// </summary>
        public PaneViewModel? ActivePane
        {
            get => _activePane ?? SelectedTab;
            set
            {
                if (SetProperty(ref _activePane, value))
                {
                    OnPropertyChanged(nameof(IsPrimaryActive));
                    OnPropertyChanged(nameof(IsSecondaryActive));
                    UpdatePreviewItem();
                }
            }
        }

        public bool IsPrimaryActive => ActivePane == SelectedTab;
        public bool IsSecondaryActive => ActivePane == SecondaryPane;

        public FileItemViewModel? PreviewItem
        {
            get => _previewItem;
            set => SetProperty(ref _previewItem, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public MainViewModel()
        {
            _engine = new CoreEngineWrapper();
            Sidebar = new SidebarViewModel();
            TransferHub = new TransferViewModel(_engine);

            // 預設開啟第一個分頁
            var firstTab = new PaneViewModel(_engine, "D:\\");
            Tabs.Add(firstTab);
            SelectedTab = firstTab;
            ActivePane = firstTab;
        }

        public async Task InitializeAsync()
        {
            StatusMessage = "正在連線 C++20 零拷貝引擎與索引...";
            try
            {
                char initialDrive = 'D';
                await _engine.ScanVolumeAsync(initialDrive);
                StatusMessage = $"全盤索引建立完成！共載入 {_engine.TotalNodeCount:N0} 個節點";

                if (SelectedTab != null)
                {
                    await SelectedTab.RefreshAsync();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"索引載入完成 (Win32 FS 模式): {ex.Message}";
                if (SelectedTab != null)
                {
                    await SelectedTab.RefreshAsync();
                }
            }
        }

        /// <summary>
        /// 新增分頁標籤 (Ctrl+T)
        /// </summary>
        public PaneViewModel AddNewTab(string? path = null)
        {
            string targetPath = path ?? ActivePane?.CurrentPath ?? SelectedTab?.CurrentPath ?? "C:\\";
            var newTab = new PaneViewModel(_engine, targetPath);
            Tabs.Add(newTab);
            SelectedTab = newTab;
            ActivePane = newTab;
            _ = newTab.RefreshAsync();
            return newTab;
        }

        /// <summary>
        /// 關閉指定分頁標籤 (Ctrl+W)
        /// </summary>
        public void CloseTab(PaneViewModel tab)
        {
            if (Tabs.Count <= 1) return;

            int index = Tabs.IndexOf(tab);
            Tabs.Remove(tab);

            if (SelectedTab == tab)
            {
                int newIndex = Math.Clamp(index, 0, Tabs.Count - 1);
                SelectedTab = Tabs[newIndex];
                ActivePane = SelectedTab;
            }
        }

        /// <summary>
        /// 切換檢視模式 (清單 / 中縮圖 / 大縮圖)
        /// </summary>
        public void SetViewMode(ViewMode mode)
        {
            if (ActivePane != null)
            {
                ActivePane.CurrentViewMode = mode;
            }
            else if (SelectedTab != null)
            {
                SelectedTab.CurrentViewMode = mode;
            }
        }

        /// <summary>
        /// 切換右側即時預覽面板
        /// </summary>
        public void TogglePreviewPane()
        {
            IsPreviewPaneOpen = !IsPreviewPaneOpen;
        }

        /// <summary>
        /// 一鍵切換單/雙窗格模式 (F10)
        /// </summary>
        public void ToggleDualPane()
        {
            IsDualPaneMode = !IsDualPaneMode;
        }

        private void UpdatePreviewItem()
        {
            if (IsPreviewPaneOpen)
            {
                PreviewItem = ActivePane?.SelectedItem ?? SelectedTab?.SelectedItem;
            }
            else
            {
                PreviewItem = null;
            }
        }

        /// <summary>
        /// 將檔案非同步極速複製或移動到目標路徑 (自動識別本地極速 Zero-Copy 傳輸或跨裝置 MTP 傳輸)
        /// </summary>
        public async Task TransferFilesAsync(string sourcePath, string targetDirectory, bool isMove)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetDirectory)) return;

            string fileName = sourcePath.Contains('|') ? sourcePath.Split('|')[^1] : Path.GetFileName(sourcePath);

            TransferHub.IsTransferring = true;
            TransferHub.CurrentFileName = fileName;

            try
            {
                // 1. 若涉及手機/MTP 跨裝置傳輸
                if (Services.ShellFolderService.Instance.IsShellPath(sourcePath) ||
                    Services.ShellFolderService.Instance.IsShellPath(targetDirectory))
                {
                    bool success = await Services.ShellFolderService.Instance.TransferShellItemAsync(sourcePath, targetDirectory, isMove);
                    if (success)
                    {
                        StatusMessage = $"{(isMove ? "移動" : "複製")}完成: {fileName}";
                    }
                    else
                    {
                        StatusMessage = $"傳輸未完成或已取消: {fileName}";
                    }
                }
                else
                {
                    // 2. 純本地磁碟：走底層 C++/Rust Zero-Copy 極速引擎
                    string destinationPath = Path.Combine(targetDirectory, fileName);
                    var progress = new Progress<TransferProgressInfo>(info =>
                    {
                        TransferHub.UpdateProgress(info);
                    });

                    await _engine.StartTransferAsync(sourcePath, destinationPath, isMove, progress);
                    StatusMessage = $"{(isMove ? "移動" : "複製")}完成: {fileName}";
                }

                if (SelectedTab != null) await SelectedTab.RefreshAsync();
                if (SecondaryPane != null) await SecondaryPane.RefreshAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"傳輸錯誤: {ex.Message}";
            }
            finally
            {
                TransferHub.IsTransferring = false;
            }
        }


        public void Dispose()
        {
            _engine.Dispose();
        }
    }
}
