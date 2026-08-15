using System;
using System.IO;

namespace BlitzFS.UI.ViewModels
{
    /// <summary>
    /// 側邊欄導航項目 (快速存取 / 磁碟機)
    /// </summary>
    public class SidebarItemViewModel : ViewModelBase
    {
        public string Title { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public string IconKey { get; init; } = "IconFolder";
        public bool IsDrive { get; init; }

        public double TotalSpaceGB { get; init; }
        public double FreeSpaceGB { get; init; }
        public double UsedPercent { get; init; }
        public string SpaceSummary => IsDrive ? $"{FreeSpaceGB:0.#} GB 可用 / {TotalSpaceGB:0.#} GB" : string.Empty;

        public static SidebarItemViewModel CreateQuickAccess(string title, string path, string iconKey)
        {
            return new SidebarItemViewModel
            {
                Title = title,
                Path = path,
                IconKey = iconKey,
                IsDrive = false
            };
        }

        public static SidebarItemViewModel CreateDrive(DriveInfo drive)
        {
            double totalGb = 0;
            double freeGb = 0;
            double usedPercent = 0;

            try
            {
                if (drive.IsReady)
                {
                    totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
                    freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                    usedPercent = totalGb > 0 ? ((totalGb - freeGb) / totalGb) * 100.0 : 0;
                }
            }
            catch {}

            string label = string.IsNullOrEmpty(drive.VolumeLabel) ? "本機磁碟" : drive.VolumeLabel;
            string title = $"{label} ({drive.Name.TrimEnd('\\')})";

            return new SidebarItemViewModel
            {
                Title = title,
                Path = drive.RootDirectory.FullName,
                IconKey = "IconDrive",
                IsDrive = true,
                TotalSpaceGB = totalGb,
                FreeSpaceGB = freeGb,
                UsedPercent = usedPercent
            };
        }
    }
}
