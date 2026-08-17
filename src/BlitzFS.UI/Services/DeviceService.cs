using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace BlitzFS.UI.Services
{
    /// <summary>
    /// 可攜式設備 (MTP / 手機 / 平板 / 相機) 資訊
    /// </summary>
    public class PortableDeviceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string ParsingPath { get; set; } = string.Empty;
        public string DeviceTypeDescription { get; set; } = "便攜式設備";
    }

    /// <summary>
    /// 硬體裝置、USB 安全彈出與便攜式設備管理服務
    /// </summary>
    public class DeviceService
    {
        private static readonly Lazy<DeviceService> _instance = new(() => new DeviceService());
        public static DeviceService Instance => _instance.Value;

        private DeviceService() {}

        /// <summary>
        /// 列舉系統中的便攜式設備 (手機、相機、WPD/MTP 設備)
        /// </summary>
        public List<PortableDeviceInfo> GetPortableDevices()
        {
            var results = new List<PortableDeviceInfo>();

            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return results;

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return results;

                // 17 = ssfDRIVES (This PC / 我的電腦)
                dynamic? myComputer = shell.NameSpace(17);
                if (myComputer == null) return results;

                dynamic items = myComputer.Items();
                int count = items.Count;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic item = items.Item(i);
                        string path = item.Path?.ToString() ?? "";
                        string name = item.Name?.ToString() ?? "";
                        string type = item.Type?.ToString() ?? "";

                        // 若非傳統磁碟機路徑 (如 C:\) 且為可攜式/MTP 設備或 GUID 路徑
                        if (!string.IsNullOrEmpty(name) && !IsTraditionalDrivePath(path))
                        {
                            results.Add(new PortableDeviceInfo
                            {
                                Name = name,
                                ParsingPath = path,
                                DeviceTypeDescription = string.IsNullOrEmpty(type) ? "便攜式設備" : type
                            });
                        }
                    }
                    catch {}
                }
            }
            catch {}

            return results;
        }

        /// <summary>
        /// 透過原生 Windows 介面開啟便攜式設備 (手機/相機)
        /// </summary>
        public bool OpenPortableDevice(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;

                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        dynamic? myComputer = shell.NameSpace(17);
                        if (myComputer != null)
                        {
                            dynamic items = myComputer.Items();
                            for (int i = 0; i < items.Count; i++)
                            {
                                dynamic item = items.Item(i);
                                if (item.Path?.ToString() == path)
                                {
                                    item.InvokeVerb("open");
                                    return true;
                                }
                            }
                        }
                    }
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTraditionalDrivePath(string path)

        {
            if (string.IsNullOrEmpty(path)) return false;
            // 傳統磁碟如 "C:\", "D:\"
            if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
                return true;
            return false;
        }

        /// <summary>
        /// 安全移除/彈出可移動磁碟機 (USB 隨身碟 / 外接磁碟)
        /// </summary>
        public (bool Success, string Message) EjectDrive(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter))
                return (false, "無效的磁碟機代號");

            string cleanLetter = driveLetter.Trim().TrimEnd('\\', ':').ToUpperInvariant();
            if (cleanLetter.Length != 1 || cleanLetter[0] < 'A' || cleanLetter[0] > 'Z')
                return (false, "無效的磁碟機路徑");

            string volumePath = $@"\\.\{cleanLetter}:";

            IntPtr handle = CreateFile(
                volumePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return (false, $"無法鎖定磁碟機 {cleanLetter}:，請確認是否有其他程式正在使用。");
            }

            try
            {
                uint bytesReturned;

                // 1. 嘗試鎖定磁碟
                DeviceIoControl(handle, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);

                // 2. 卸載磁碟
                DeviceIoControl(handle, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);

                // 3. 彈出媒體
                bool ejectSuccess = DeviceIoControl(handle, IOCTL_STORAGE_EJECT_MEDIA, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);

                if (ejectSuccess)
                {
                    return (true, $"磁碟機 ({cleanLetter}:) 已成功安全退出，現在可以拔除。");
                }
                else
                {
                    // 若 EJECT 失敗，嘗試解鎖並透過 Shell 退出
                    DeviceIoControl(handle, FSCTL_UNLOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);
                    return ShellEjectDrive(cleanLetter);
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static (bool Success, string Message) ShellEjectDrive(string cleanLetter)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        dynamic? myComputer = shell.NameSpace(17);
                        if (myComputer != null)
                        {
                            dynamic items = myComputer.Items();
                            for (int i = 0; i < items.Count; i++)
                            {
                                dynamic item = items.Item(i);
                                string path = item.Path?.ToString() ?? "";
                                if (path.StartsWith(cleanLetter + ":", StringComparison.OrdinalIgnoreCase))
                                {
                                    item.InvokeVerb("Eject");
                                    return (true, $"已送出磁碟機 ({cleanLetter}:) 退出請求。");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, $"退出失敗: {ex.Message}");
            }

            return (false, $"無法彈出磁碟機 ({cleanLetter}:)");
        }

        #region Win32 P/Invoke
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        private const uint FSCTL_LOCK_VOLUME = 0x00090018;
        private const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;
        private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
        private const uint IOCTL_STORAGE_EJECT_MEDIA = 0x002D4808;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
        #endregion
    }
}
