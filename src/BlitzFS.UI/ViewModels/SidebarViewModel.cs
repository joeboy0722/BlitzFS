using System;
using System.Collections.ObjectModel;
using System.IO;

namespace BlitzFS.UI.ViewModels
{
    /// <summary>
    /// 左側導航側邊欄 ViewModel
    /// </summary>
    public class SidebarViewModel : ViewModelBase
    {
        public ObservableCollection<SidebarItemViewModel> QuickAccessItems { get; } = new();
        public ObservableCollection<SidebarItemViewModel> DriveItems { get; } = new();

        public SidebarViewModel()
        {
            LoadQuickAccess();
            LoadDrives();
        }

        private void LoadQuickAccess()
        {
            QuickAccessItems.Clear();

            // 桌面
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop))
                QuickAccessItems.Add(SidebarItemViewModel.CreateQuickAccess("桌面", desktop, "IconDesktop"));

            // 下載
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads))
                QuickAccessItems.Add(SidebarItemViewModel.CreateQuickAccess("下載", downloads, "IconDownloads"));

            // 文件
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docs) && Directory.Exists(docs))
                QuickAccessItems.Add(SidebarItemViewModel.CreateQuickAccess("文件", docs, "IconDocuments"));

            // 圖片
            string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (!string.IsNullOrEmpty(pics) && Directory.Exists(pics))
                QuickAccessItems.Add(SidebarItemViewModel.CreateQuickAccess("圖片", pics, "IconPictures"));

            // 影片
            string vids = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (!string.IsNullOrEmpty(vids) && Directory.Exists(vids))
                QuickAccessItems.Add(SidebarItemViewModel.CreateQuickAccess("影片", vids, "IconVideos"));
        }

        public void LoadDrives()
        {
            DriveItems.Clear();
            try
            {
                var drives = DriveInfo.GetDrives();
                foreach (var drive in drives)
                {
                    if (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable)
                    {
                        DriveItems.Add(SidebarItemViewModel.CreateDrive(drive));
                    }
                }
            }
            catch {}
        }
    }
}
