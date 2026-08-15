#pragma once
#include "../include/CommonDef.h"
#include "FlatMemoryTree.h"
#include <windows.h>
#include <string>
#include <atomic>
#include <queue>
#include <mutex>
#include <condition_variable>
#include <thread>

namespace BlitzFS {

/**
 * @brief 通用多執行緒目錄掃描器 (適用於 FAT32 / exFAT 或權限受限之備援引擎)
 * 採用並發任務佇列與 Win32 FindFirstFileExW 高速 API 走訪目錄
 */
class DirectoryScanner {
public:
    DirectoryScanner();
    ~DirectoryScanner() = default;

    /**
     * @brief 執行多執行緒目錄樹走訪
     * @param driveLetter 磁碟代號 (如 L'C', L'D')
     * @param outTree 接收資料的平鋪記憶體樹
     * @param callback 掃描進度回呼
     * @param userData 使用者自訂指標
     * @return 成功傳回 true
     */
    bool Scan(wchar_t driveLetter, FlatMemoryTree& outTree, ScanProgressCallback callback, void* userData);

private:
    struct ScanTask {
        std::wstring directoryPath;
        uint64_t parentId;
    };

    void WorkerThread(FlatMemoryTree& outTree);

    std::atomic<uint64_t> m_nextIdGenerator{1};
    std::atomic<uint32_t> m_totalScanned{0};
    std::atomic<int> m_activeWorkers{0};
    std::atomic<bool> m_isDone{false};

    std::queue<ScanTask> m_taskQueue;
    std::mutex m_queueMutex;
    std::condition_variable m_cv;
};

} // namespace BlitzFS
