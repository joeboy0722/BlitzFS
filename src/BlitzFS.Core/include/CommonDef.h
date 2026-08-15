#pragma once
#include <cstdint>
#include <windows.h>

namespace BlitzFS {

// 緊湊檔案節點結構體 (48 Bytes 對齊)
// 設計為平鋪記憶體結構，最小化快取遺失 (Cache Miss)
#pragma pack(push, 8)
struct CompactNode {
    uint64_t fileId;           // 檔案唯一編號 (NTFS FRN 或 NT FileId)
    uint64_t parentId;         // 父目錄編號
    uint64_t fileSize;         // 檔案大小 (位元組)
    uint64_t lastWriteTime;    // 最後修改時間 (Windows FILETIME 格式)
    uint32_t attributes;       // Win32 檔案屬性旗標 (FILE_ATTRIBUTE_DIRECTORY 等)
    uint32_t nameOffset;       // 檔名在 StringPool 中的位元組偏移量
    uint16_t nameLength;       // 檔名字元數 (UTF-16 wchar_t 數量)
    uint16_t isDirectory : 1;  // 是否為資料夾 (1: 資料夾, 0: 檔案)
    uint16_t isHidden    : 1;  // 是否為隱藏項目
    uint16_t isSystem    : 1;  // 是否為系統檔案
    uint16_t isReadOnly  : 1;  // 是否唯讀
    uint16_t reserved    : 12; // 保留位元
};
#pragma pack(pop)

static_assert(sizeof(CompactNode) == 48, "CompactNode 必須精確對齊為 48 位元組以確保跨語言 ABI 穩定度與 Cache 效能");

// 傳輸進度結構體 (用於 Callback 即時回傳)
struct TransferProgressInfo {
    uint64_t totalBytes;         // 預計傳輸總位元組數
    uint64_t transferredBytes;   // 已完成傳輸位元組數
    uint32_t totalFiles;         // 總檔案數
    uint32_t processedFiles;     // 已處理檔案數
    double currentSpeedBps;      // 當前傳輸速率 (Bytes/s)
    const wchar_t* currentFileName; // 當前處理之檔案名稱
    uint32_t errorCode;          // 錯誤代碼 (0 代表正常)
};

// 掃描進度回呼函數指標
typedef void (*ScanProgressCallback)(uint32_t scannedCount, void* userData);

// 傳輸進度回呼函數指標
typedef void (*TransferProgressCallback)(const TransferProgressInfo* progress, void* userData);

// 檔案系統類型列舉
enum class FileSystemType : uint32_t {
    Unknown = 0,
    NTFS = 1,
    FAT32 = 2,
    exFAT = 3,
    ReFS = 4
};

} // namespace BlitzFS
