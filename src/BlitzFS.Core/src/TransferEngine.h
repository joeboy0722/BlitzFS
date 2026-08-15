#pragma once
#include "../include/CommonDef.h"
#include <string>
#include <vector>
#include <atomic>
#include <mutex>
#include <condition_variable>

namespace BlitzFS {

/**
 * @brief 高效能多工檔案搬移與傳輸引擎
 */
class TransferEngine {
public:
    TransferEngine();
    ~TransferEngine();

    /**
     * @brief 啟動檔案或目錄搬移/複製任務
     */
    bool StartTransfer(
        const std::wstring& srcPath,
        const std::wstring& dstPath,
        bool isMove,
        TransferProgressCallback callback,
        void* userData
    );

    void Cancel();
    void Pause();
    void Resume();

    bool IsRunning() const { return m_isRunning.load(); }

private:
    struct FileTransferTask {
        std::wstring srcFilePath;
        std::wstring dstFilePath;
        uint64_t fileSize;
        bool isDirectory;
    };

    bool IsSameVolume(const std::wstring& path1, const std::wstring& path2);
    void CollectTransferTasks(const std::wstring& srcPath, const std::wstring& dstPath, std::vector<FileTransferTask>& outTasks);
    bool TransferSmallFile(const FileTransferTask& task);
    bool TransferLargeFileDirectIO(const FileTransferTask& task);

    std::atomic<bool> m_isRunning{false};
    std::atomic<bool> m_isCancelled{false};
    std::atomic<bool> m_isPaused{false};

    std::mutex m_pauseMutex;
    std::condition_variable m_pauseCv;

    TransferProgressCallback m_callback{nullptr};
    void* m_userData{nullptr};

    uint64_t m_totalBytes{0};
    std::atomic<uint64_t> m_transferredBytes{0};
    uint32_t m_totalFiles{0};
    std::atomic<uint32_t> m_processedFiles{0};
};

} // namespace BlitzFS
