using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace BlitzFS.UI.ViewModels
{
    /// <summary>
    /// 側邊欄導航項目 (快速存取 / 磁碟機 / 可攜式設備)
    /// </summary>
    public class SidebarItemViewModel : ViewModelBase
    {
        public string Title { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public string IconKey { get; init; } = "IconFolder";
        public Geometry? IconGeometry { get; init; }
        public bool IsDrive { get; init; }
        public bool CanEject { get; init; }
        public bool IsCustomQuickAccess { get; init; }
        public bool IsPortableDevice { get; init; }
        public string? DriveLetter { get; init; }

        public double TotalSpaceGB { get; init; }
        public double FreeSpaceGB { get; init; }
        public double UsedPercent { get; init; }
        public bool IsSpaceWarning => UsedPercent >= 90.0;

        public string SpaceSummary => IsDrive
            ? $"{FreeSpaceGB:0.#} GB 可用 / {TotalSpaceGB:0.#} GB"
            : (IsPortableDevice ? "便攜式設備" : string.Empty);

        private static Geometry? GetGeometry(string key)
        {
            try
            {
                return Application.Current?.TryFindResource(key) as Geometry;
            }
            catch
            {
                return null;
            }
        }

        public static SidebarItemViewModel CreateQuickAccess(string title, string path, string iconKey, bool isCustom = false)
        {
            return new SidebarItemViewModel
            {
                Title = title,
                Path = path,
                IconKey = iconKey,
                IconGeometry = GetGeometry(iconKey) ?? GetGeometry("IconFolder"),
                IsDrive = false,
                IsCustomQuickAccess = isCustom
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

            string label = "";
            try
            {
                if (drive.IsReady && !string.IsNullOrEmpty(drive.VolumeLabel))
                {
                    label = drive.VolumeLabel;
                }
            }
            catch {}

            if (string.IsNullOrEmpty(label))
            {
                label = drive.DriveType switch
                {
                    DriveType.Removable => "USB 磁碟",
                    DriveType.Network => "網路磁碟",
                    DriveType.CDRom => "光碟機",
                    _ => "本機磁碟"
                };
            }

            string cleanName = drive.Name.TrimEnd('\\');
            string title = $"{label} ({cleanName})";

            string iconKey = drive.DriveType switch
            {
                DriveType.Removable => "IconUsb",
                DriveType.Network => "IconNetwork",
                DriveType.CDRom => "IconCd",
                _ => "IconDrive"
            };

            return new SidebarItemViewModel
            {
                Title = title,
                Path = drive.RootDirectory.FullName,
                IconKey = iconKey,
                IconGeometry = GetGeometry(iconKey),
                IsDrive = true,
                CanEject = drive.DriveType == DriveType.Removable,
                DriveLetter = cleanName,
                TotalSpaceGB = totalGb,
                FreeSpaceGB = freeGb,
                UsedPercent = usedPercent
            };
        }

        public static SidebarItemViewModel CreatePortableDevice(string name, string parsingPath, string typeDescription)
        {
            return new SidebarItemViewModel
            {
                Title = name,
                Path = parsingPath,
                IconKey = "IconPhone",
                IconGeometry = GetGeometry("IconPhone"),
                IsDrive = false,
                IsPortableDevice = true,
                CanEject = false
            };
        }
    }
}
