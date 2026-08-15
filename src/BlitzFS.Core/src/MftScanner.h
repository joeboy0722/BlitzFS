#pragma once
#include "../include/CommonDef.h"
#include "FlatMemoryTree.h"
#include <windows.h>
#include <winioctl.h>
#include <string>
#include <functional>

namespace BlitzFS {

/**
 * @brief NTFS 極速 MFT / USN Journal 掃描器
 * 透過直接向磁區發送 FSCTL_ENUM_USN_DATA，批次取得底層檔案系統紀錄
 */
class MftScanner {
public:
    MftScanner();
    ~MftScanner() = default;

    /**
     * @brief 執行 NTFS 磁區全盤掃描
     * @param driveLetter 磁碟代號 (如 L'C', L'D')
     * @param outTree 接收資料的平鋪記憶體樹
     * @param callback 掃描進度回呼
     * @param userData 使用者自訂指標
     * @return 成功傳回 true，失敗傳回 false
     */
    bool Scan(wchar_t driveLetter, FlatMemoryTree& outTree, ScanProgressCallback callback, void* userData);

private:
    HANDLE OpenVolumeHandle(wchar_t driveLetter);
    bool QueryUsnJournal(HANDLE hVolume, USN_JOURNAL_DATA_V0& outUsnData);
};

} // namespace BlitzFS
