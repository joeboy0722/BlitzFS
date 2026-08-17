using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using BlitzFS.UI.Services;

namespace BlitzFS.UI.ViewModels
{
    /// <summary>
    /// 左側導航側邊欄 ViewModel (支援快速存取管理、USB 熱插拔與手機設備)
    /// </summary>
    public class SidebarViewModel : ViewModelBase
    {
        public ObservableCollection<SidebarItemViewModel> QuickAccessItems { get; } = new();
        public ObservableCollection<SidebarItemViewModel> FixedDrives { get; } = new();
        public ObservableCollection<SidebarItemViewModel> RemovableDrives { get; } = new();
        public ObservableCollection<SidebarItemViewModel> PortableDevices { get; } = new();
        public ObservableCollection<SidebarItemViewModel> NetworkDrives { get; } = new();

        public bool HasRemovableDrives => RemovableDrives.Count > 0;
        public bool HasPortableDevices => PortableDevices.Count > 0;
        public bool HasNetworkDrives => NetworkDrives.Count > 0;

        public event Action<string, bool>? NotificationRequested;

        public SidebarViewModel()
        {
            QuickAccessService.Instance.QuickAccessChanged += OnQuickAccessChanged;
            LoadQuickAccess();
            LoadDrivesAndDevices();
        }

        private void OnQuickAccessChanged()
        {
            Application.Current?.Dispatcher.InvokeAsync(LoadQuickAccess);
        }

        public void LoadQuickAccess()
        {
            QuickAccessItems.Clear();
            var entries = QuickAccessService.Instance.GetEntries();
            foreach (var entry in entries)
            {
                QuickAccessItems.Add(SidebarItemViewModel.CreateQuickAccess(
                    entry.Title,
                    entry.Path,
                    entry.IconKey,
                    entry.IsCustom
                ));
            }
        }

        public void LoadDrivesAndDevices()
        {
            try
            {
                FixedDrives.Clear();
                RemovableDrives.Clear();
                NetworkDrives.Clear();

                var drives = DriveInfo.GetDrives();
                foreach (var drive in drives)
                {
                    try
                    {
                        var item = SidebarItemViewModel.CreateDrive(drive);
                        switch (drive.DriveType)
                        {
                            case DriveType.Removable:
                                RemovableDrives.Add(item);
                                break;
                            case DriveType.Network:
                                NetworkDrives.Add(item);
                                break;
                            case DriveType.Fixed:
                            case DriveType.Ram:
                            default:
                                FixedDrives.Add(item);
                                break;
                        }
                    }
                    catch {}
                }

                NotifyDriveCollectionChanges();

                // 異步列舉手機/便攜式設備 (避免阻礙 UI)
                Task.Run(() =>
                {
                    var portables = DeviceService.Instance.GetPortableDevices();
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        PortableDevices.Clear();
                        foreach (var p in portables)
                        {
                            PortableDevices.Add(SidebarItemViewModel.CreatePortableDevice(p.Name, p.ParsingPath, p.DeviceTypeDescription));
                        }
                        OnPropertyChanged(nameof(HasPortableDevices));
                    });
                });
            }
            catch {}
        }

        private void NotifyDriveCollectionChanges()
        {
            OnPropertyChanged(nameof(HasRemovableDrives));
            OnPropertyChanged(nameof(HasNetworkDrives));
        }

        /// <summary>
        /// 釘選指定路徑至快速存取
        /// </summary>
        public bool PinToQuickAccess(string path, string? title = null)
        {
            bool success = QuickAccessService.Instance.PinPath(path, title);
            if (success)
            {
                NotificationRequested?.Invoke($"已將「{title ?? System.IO.Path.GetFileName(path)}」釘選到快速存取", true);
            }
            return success;
        }

        /// <summary>
        /// 從快速存取取消釘選
        /// </summary>
        public bool UnpinFromQuickAccess(string path)
        {
            bool success = QuickAccessService.Instance.UnpinPath(path);
            if (success)
            {
                NotificationRequested?.Invoke("已從快速存取取消釘選", true);
            }
            return success;
        }

        /// <summary>
        /// 安全彈出可移動磁碟
        /// </summary>
        public void EjectDrive(string? driveLetter)
        {
            if (string.IsNullOrEmpty(driveLetter)) return;

            Task.Run(() =>
            {
                var result = DeviceService.Instance.EjectDrive(driveLetter);
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    NotificationRequested?.Invoke(result.Message, result.Success);
                    LoadDrivesAndDevices();
                });
            });
        }
    }
}
