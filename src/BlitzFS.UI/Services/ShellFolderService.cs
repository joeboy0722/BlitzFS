using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlitzFS.Bridge;
using BlitzFS.UI.ViewModels;

namespace BlitzFS.UI.Services
{
    /// <summary>
    /// Windows Shell 虛擬命名空間與便攜式設備 (手機/相機/MTP) 原生檔案存取服務 (純記憶體處理，絕不寫入硬碟暫存)
    /// </summary>
    public class ShellFolderService
    {
        private static readonly Lazy<ShellFolderService> _instance = new(() => new ShellFolderService());
        public static ShellFolderService Instance => _instance.Value;

        private ShellFolderService() {}

        /// <summary>
        /// 判斷是否為 Windows Shell 虛擬命名空間路徑 (如手機、相機、虛擬資料夾)
        /// </summary>
        public bool IsShellPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            if (path.StartsWith("::", StringComparison.Ordinal) ||
                path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                path.Contains("::{") ||
                path.Contains('|'))
            {
                return true;
            }

            if (path.Length < 2 || path[1] != ':')
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 列舉 Shell 虛擬資料夾 (手機、內部儲存空間、子目錄) 下的檔案與資料夾 (純記憶體解析)
        /// </summary>
        public (List<FileItemViewModel> Items, string DisplayTitle, string DisplayPath, string? ParentPath) EnumerateShellFolder(string path)
        {
            var results = new List<FileItemViewModel>();
            string displayTitle = path;
            string displayPath = path;
            string? parentPath = null;

            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return (results, displayTitle, displayPath, parentPath);

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return (results, displayTitle, displayPath, parentPath);

                var parts = path.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return (results, displayTitle, displayPath, parentPath);

                string rootDevicePath = parts[0];
                dynamic? currentFolder = FindRootDeviceFolder(shell, rootDevicePath);
                if (currentFolder == null)
                {
                    currentFolder = shell.NameSpace(rootDevicePath);
                }

                if (currentFolder == null) return (results, displayTitle, displayPath, parentPath);

                string deviceName = "";
                try { deviceName = currentFolder.Title?.ToString() ?? "便攜式設備"; } catch {}
                if (string.IsNullOrWhiteSpace(deviceName)) deviceName = "便攜式設備";

                // 若包含子目錄層級 (如 "內部共用儲存空間" -> "DCIM")，依序向下深入
                for (int step = 1; step < parts.Length; step++)
                {
                    string targetName = parts[step];
                    dynamic subItems = currentFolder.Items();
                    int subCount = subItems.Count;
                    dynamic? nextFolder = null;

                    for (int j = 0; j < subCount; j++)
                    {
                        try
                        {
                            dynamic it = subItems.Item(j);
                            string itName = it.Name?.ToString() ?? "";
                            if (string.Equals(itName, targetName, StringComparison.OrdinalIgnoreCase))
                            {
                                nextFolder = it.GetFolder;
                                break;
                            }
                        }
                        catch {}
                    }

                    if (nextFolder != null)
                    {
                        currentFolder = nextFolder;
                    }
                    else
                    {
                        break;
                    }
                }

                // 計算友善顯示名稱與導航路徑
                if (parts.Length == 1)
                {
                    displayTitle = deviceName;
                    displayPath = deviceName;
                    parentPath = "C:\\";
                }
                else
                {
                    displayTitle = parts[^1];
                    displayPath = $"{deviceName} \\ {string.Join(" \\ ", parts.Skip(1))}";
                    parentPath = string.Join("|", parts.Take(parts.Length - 1));
                }

                // 列舉當前層級的所有子項目
                dynamic items = currentFolder.Items();
                int count = items.Count;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic item = items.Item(i);
                        string name = item.Name?.ToString() ?? "";
                        if (string.IsNullOrEmpty(name)) continue;

                        bool isFolder = item.IsFolder;
                        long size = 0;
                        try { size = (long)item.Size; } catch {}

                        // 從 Shell 屬性直接讀取 MTP 檔案大小
                        if (!isFolder && size <= 0)
                        {
                            try
                            {
                                var extSize = item.ExtendedProperty("System.Size");
                                if (extSize != null)
                                {
                                    size = Convert.ToInt64(extSize);
                                }
                            }
                            catch {}
                        }

                        DateTime modifyDate = DateTime.MinValue;
                        try
                        {
                            var rawDate = item.ModifyDate;
                            if (rawDate != null)
                            {
                                var parsed = Convert.ToDateTime(rawDate);
                                if (parsed.Year >= 1980)
                                {
                                    modifyDate = parsed;
                                }
                            }
                        }
                        catch {}

                        string itemHierarchyPath = $"{path}|{name}";

                        CompactNode node = new CompactNode
                        {
                            BitFlags = isFolder ? (ushort)1 : (ushort)0,
                            LastWriteTime = modifyDate > DateTime.MinValue ? (ulong)modifyDate.ToFileTime() : 0,
                            FileSize = (ulong)Math.Max(0, size)
                        };

                        var vm = new FileItemViewModel(node, name, itemHierarchyPath);
                        results.Add(vm);
                    }
                    catch {}
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Shell 枚舉失敗: {ex.Message}");
            }

            return (results, displayTitle, displayPath, parentPath);
        }

        /// <summary>
        /// 取得友善的路徑顯示名稱
        /// </summary>
        public string GetFriendlyDisplayPath(string path)
        {
            if (!IsShellPath(path)) return path;

            var parts = path.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return path;

            if (parts.Length == 1)
            {
                try
                {
                    Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                    if (shellType != null)
                    {
                        dynamic? shell = Activator.CreateInstance(shellType);
                        if (shell != null)
                        {
                            dynamic? folder = FindRootDeviceFolder(shell, parts[0]);
                            if (folder != null)
                            {
                                return folder.Title?.ToString() ?? "便攜式設備";
                            }
                        }
                    }
                }
                catch {}
                return "便攜式設備";
            }

            return string.Join(" \\ ", parts.Skip(1));
        }

        private dynamic? FindRootDeviceFolder(dynamic shell, string path)
        {
            try
            {
                dynamic? myComputer = shell.NameSpace(17);
                if (myComputer == null) return null;

                dynamic items = myComputer.Items();
                for (int i = 0; i < items.Count; i++)
                {
                    dynamic item = items.Item(i);
                    string itPath = item.Path?.ToString() ?? "";
                    if (string.Equals(itPath, path, StringComparison.OrdinalIgnoreCase) ||
                        itPath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                    {
                        return item.GetFolder;
                    }
                }
            }
            catch {}
            return null;
        }

        /// <summary>
        /// 開啟 Shell 檔案 (透過原生關聯程式以串流開啟，不寫入任何暫存檔案)
        /// </summary>
        public bool OpenShellFile(string fullPath)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return false;

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return false;

                var parts = fullPath.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return false;

                string fileName = parts[^1];

                string rootDevicePath = parts[0];
                dynamic? currentFolder = FindRootDeviceFolder(shell, rootDevicePath);
                if (currentFolder == null) currentFolder = shell.NameSpace(rootDevicePath);

                if (currentFolder == null) return false;

                for (int step = 1; step < parts.Length - 1; step++)
                {
                    string targetName = parts[step];
                    dynamic subItems = currentFolder.Items();
                    int subCount = subItems.Count;
                    dynamic? nextFolder = null;

                    for (int j = 0; j < subCount; j++)
                    {
                        dynamic it = subItems.Item(j);
                        if (string.Equals(it.Name?.ToString(), targetName, StringComparison.OrdinalIgnoreCase))
                        {
                            nextFolder = it.GetFolder;
                            break;
                        }
                    }

                    if (nextFolder != null)
                        currentFolder = nextFolder;
                    else
                        return false;
                }

                dynamic? targetItem = currentFolder.ParseName(fileName);
                if (targetItem != null)
                {
                    targetItem.InvokeVerb("open");
                    return true;
                }
            }
            catch {}

            return false;
        }

        /// <summary>
        /// 透過 Windows Shell 執行跨裝置傳輸 (例如 手機 -> 電腦、電腦 -> 手機、手機 -> 手機)
        /// 純背景執行，不開啟任何外部視窗
        /// </summary>
        public async Task<bool> TransferShellItemAsync(string sourcePath, string targetDirectory, bool isMove)
        {
            return await Task.Run(() =>
            {
                try
                {
                    Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                    if (shellType == null) return false;

                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell == null) return false;

                    dynamic? destFolder = null;

                    // 1. 取得目標資料夾
                    if (IsShellPath(targetDirectory))
                    {
                        var targetParts = targetDirectory.Split('|', StringSplitOptions.RemoveEmptyEntries);
                        if (targetParts.Length == 0) return false;

                        string rootDevicePath = targetParts[0];
                        destFolder = FindRootDeviceFolder(shell, rootDevicePath);
                        if (destFolder == null) destFolder = shell.NameSpace(rootDevicePath);
                        if (destFolder == null) return false;

                        for (int step = 1; step < targetParts.Length; step++)
                        {
                            string targetName = targetParts[step];
                            dynamic subItems = destFolder.Items();
                            int subCount = subItems.Count;
                            dynamic? nextFolder = null;

                            for (int j = 0; j < subCount; j++)
                            {
                                dynamic it = subItems.Item(j);
                                if (string.Equals(it.Name?.ToString(), targetName, StringComparison.OrdinalIgnoreCase))
                                {
                                    nextFolder = it.GetFolder;
                                    break;
                                }
                            }

                            if (nextFolder != null)
                                destFolder = nextFolder;
                            else
                                return false;
                        }
                    }
                    else
                    {
                        if (!Directory.Exists(targetDirectory))
                        {
                            Directory.CreateDirectory(targetDirectory);
                        }
                        destFolder = shell.NameSpace(targetDirectory);
                    }

                    if (destFolder == null) return false;

                    // 2. 取得來源物件並執行傳輸
                    dynamic? sourceItem = null;

                    if (IsShellPath(sourcePath))
                    {
                        var srcParts = sourcePath.Split('|', StringSplitOptions.RemoveEmptyEntries);
                        if (srcParts.Length < 2) return false;

                        string fileName = srcParts[^1];
                        string rootDevicePath = srcParts[0];
                        dynamic? currentFolder = FindRootDeviceFolder(shell, rootDevicePath);
                        if (currentFolder == null) currentFolder = shell.NameSpace(rootDevicePath);
                        if (currentFolder == null) return false;

                        for (int step = 1; step < srcParts.Length - 1; step++)
                        {
                            string targetName = srcParts[step];
                            dynamic subItems = currentFolder.Items();
                            int subCount = subItems.Count;
                            dynamic? nextFolder = null;

                            for (int j = 0; j < subCount; j++)
                            {
                                dynamic it = subItems.Item(j);
                                if (string.Equals(it.Name?.ToString(), targetName, StringComparison.OrdinalIgnoreCase))
                                {
                                    nextFolder = it.GetFolder;
                                    break;
                                }
                            }

                            if (nextFolder != null)
                                currentFolder = nextFolder;
                            else
                                return false;
                        }

                        sourceItem = currentFolder.ParseName(fileName);
                        if (sourceItem == null)
                        {
                            dynamic allItems = currentFolder.Items();
                            for (int i = 0; i < allItems.Count; i++)
                            {
                                dynamic it = allItems.Item(i);
                                if (string.Equals(it.Name?.ToString(), fileName, StringComparison.OrdinalIgnoreCase))
                                {
                                    sourceItem = it;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        // 本地檔案/資料夾
                        string? dirName = Path.GetDirectoryName(sourcePath);
                        if (!string.IsNullOrEmpty(dirName))
                        {
                            dynamic? srcDirFolder = shell.NameSpace(dirName);
                            if (srcDirFolder != null)
                            {
                                sourceItem = srcDirFolder.ParseName(Path.GetFileName(sourcePath));
                            }
                        }
                    }

                    if (sourceItem != null)
                    {
                        if (isMove)
                        {
                            try { destFolder.MoveHere(sourceItem, 16); }
                            catch { destFolder.MoveHere(sourceItem); }
                        }
                        else
                        {
                            try { destFolder.CopyHere(sourceItem, 16); }
                            catch { destFolder.CopyHere(sourceItem); }
                        }
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Shell 傳輸失敗: {ex.Message}");
                }

                return false;
            });
        }
    }
}


