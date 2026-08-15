using System;
using System.Runtime.InteropServices;

namespace BlitzFS.Bridge
{
    /// <summary>
    /// 緊湊檔案節點結構體 (48 Bytes 精確對齊)
    /// 與 C++ BlitzFS::CompactNode 完全映射，支援零拷貝指標直讀
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct CompactNode
    {
        public ulong FileId;           // 檔案唯一編號 (NTFS FRN 或 NT FileId)
        public ulong ParentId;         // 父目錄編號
        public ulong FileSize;         // 檔案大小 (Bytes)
        public ulong LastWriteTime;    // 最後修改時間 (FILETIME)
        public uint Attributes;        // Win32 檔案屬性
        public uint NameOffset;        // 檔名在 StringPool 中的位元組偏移
        public ushort NameLength;      // 檔名字元數 (UTF-16)
        public ushort BitFlags;        // 位元旗標 (isDirectory = BitFlags & 0x1, isHidden = (BitFlags >> 1) & 0x1)

        /// <summary>
        /// 是否為目錄
        /// </summary>
        public bool IsDirectory => (BitFlags & 0x1) != 0;

        /// <summary>
        /// 是否為隱藏項目
        /// </summary>
        public bool IsHidden => (BitFlags & 0x2) != 0;

        /// <summary>
        /// 是否為系統檔案
        /// </summary>
        public bool IsSystem => (BitFlags & 0x4) != 0;

        /// <summary>
        /// 是否唯讀
        /// </summary>
        public bool IsReadOnly => (BitFlags & 0x8) != 0;

        /// <summary>
        /// 將 Windows FILETIME 轉換為 DateTimeOffset
        /// </summary>
        public DateTimeOffset ModifiedTime => DateTimeOffset.FromFileTime((long)LastWriteTime);
    }

    /// <summary>
    /// 傳輸進度資訊結構體
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TransferProgressInfo
    {
        public ulong TotalBytes;
        public ulong TransferredBytes;
        public uint TotalFiles;
        public uint ProcessedFiles;
        public double CurrentSpeedBps;
        public IntPtr CurrentFileNamePtr;
        public uint ErrorCode;

        public string? CurrentFileName => CurrentFileNamePtr != IntPtr.Zero ? Marshal.PtrToStringUni(CurrentFileNamePtr) : null;
        public double ProgressPercentage => TotalBytes > 0 ? (double)TransferredBytes / TotalBytes * 100.0 : 0.0;
        public double SpeedMBps => CurrentSpeedBps / (1024.0 * 1024.0);
    }

    /// <summary>
    /// 掃描進度回呼委派
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ScanProgressCallback(uint scannedCount, IntPtr userData);

    /// <summary>
    /// 傳輸進度回呼委派
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void TransferProgressCallback(in TransferProgressInfo progress, IntPtr userData);
}
