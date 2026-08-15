#include "TransferEngine.h"
#include "RingBuffer.h"
#include <windows.h>
#include <chrono>
#include <thread>
#include <cwctype>
#include <filesystem>

namespace BlitzFS {

namespace fs = std::filesystem;

TransferEngine::TransferEngine() {
}

TransferEngine::~TransferEngine() {
    Cancel();
}

bool TransferEngine::IsSameVolume(const std::wstring& path1, const std::wstring& path2) {
    if (path1.length() >= 2 && path2.length() >= 2) {
        if (path1[1] == L':' && path2[1] == L':') {
            return (towupper(path1[0]) == towupper(path2[0]));
        }
    }
    return false;
}

void TransferEngine::CollectTransferTasks(const std::wstring& srcPath, const std::wstring& dstPath, std::vector<FileTransferTask>& outTasks) {
    DWORD srcAttr = GetFileAttributesW(srcPath.c_str());
    if (srcAttr == INVALID_FILE_ATTRIBUTES) {
        return;
    }

    if (!(srcAttr & FILE_ATTRIBUTE_DIRECTORY)) {
        // 單一檔案
        WIN32_FILE_ATTRIBUTE_DATA fileInfo;
        uint64_t size = 0;
        if (GetFileAttributesExW(srcPath.c_str(), GetFileExInfoStandard, &fileInfo)) {
            ULARGE_INTEGER ulSize;
            ulSize.LowPart = fileInfo.nFileSizeLow;
            ulSize.HighPart = fileInfo.nFileSizeHigh;
            size = ulSize.QuadPart;
        }
        outTasks.push_back({ srcPath, dstPath, size, false });
        m_totalBytes += size;
        m_totalFiles++;
        return;
    }

    // 目錄走訪
    outTasks.push_back({ srcPath, dstPath, 0, true });

    std::wstring searchPattern = srcPath;
    if (searchPattern.back() != L'\\') {
        searchPattern.push_back(L'\\');
    }
    searchPattern.append(L"*");

    WIN32_FIND_DATAW findData;
    HANDLE hFind = FindFirstFileExW(
        searchPattern.c_str(),
        FindExInfoBasic,
        &findData,
        FindExSearchNameMatch,
        nullptr,
        FIND_FIRST_EX_LARGE_FETCH
    );

    if (hFind != INVALID_HANDLE_VALUE) {
        do {
            if (wcscmp(findData.cFileName, L".") == 0 || wcscmp(findData.cFileName, L"..") == 0) {
                continue;
            }

            std::wstring subSrc = srcPath;
            if (subSrc.back() != L'\\') subSrc.push_back(L'\\');
            subSrc.append(findData.cFileName);

            std::wstring subDst = dstPath;
            if (subDst.back() != L'\\') subDst.push_back(L'\\');
            subDst.append(findData.cFileName);

            if (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
                CollectTransferTasks(subSrc, subDst, outTasks);
            } else {
                ULARGE_INTEGER ulSize;
                ulSize.LowPart = findData.nFileSizeLow;
                ulSize.HighPart = findData.nFileSizeHigh;
                uint64_t size = ulSize.QuadPart;

                outTasks.push_back({ subSrc, subDst, size, false });
                m_totalBytes += size;
                m_totalFiles++;
            }

        } while (FindNextFileW(hFind, &findData));

        FindClose(hFind);
    }
}

bool TransferEngine::TransferSmallFile(const FileTransferTask& task) {
    if (m_isCancelled.load()) return false;

    // 檢查暫停
    if (m_isPaused.load()) {
        std::unique_lock<std::mutex> lock(m_pauseMutex);
        m_pauseCv.wait(lock, [this]() {
            return !m_isPaused.load() || m_isCancelled.load();
        });
    }

    if (m_isCancelled.load()) return false;

    BOOL ok = CopyFileW(task.srcFilePath.c_str(), task.dstFilePath.c_str(), FALSE);
    if (ok) {
        m_transferredBytes.fetch_add(task.fileSize);
        m_processedFiles.fetch_add(1);
        return true;
    }
    return false;
}

bool TransferEngine::TransferLargeFileDirectIO(const FileTransferTask& task) {
    // 啟用非快取 Direct I/O
    constexpr DWORD SLOT_SIZE = 4 * 1024 * 1024; // 4MB 對齊槽
    constexpr DWORD SLOT_COUNT = 4;              // 4 槽環形緩衝區 (共 16MB)

    HANDLE hSrc = CreateFileW(
        task.srcFilePath.c_str(),
        GENERIC_READ,
        FILE_SHARE_READ,
        nullptr,
        OPEN_EXISTING,
        FILE_FLAG_NO_BUFFERING | FILE_FLAG_SEQUENTIAL_SCAN,
        nullptr
    );

    if (hSrc == INVALID_HANDLE_VALUE) {
        return TransferSmallFile(task); // 若無法開啟 Direct I/O 則降級
    }

    HANDLE hDst = CreateFileW(
        task.dstFilePath.c_str(),
        GENERIC_WRITE,
        0,
        nullptr,
        CREATE_ALWAYS,
        FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH | FILE_FLAG_SEQUENTIAL_SCAN,
        nullptr
    );

    if (hDst == INVALID_HANDLE_VALUE) {
        CloseHandle(hSrc);
        return TransferSmallFile(task);
    }

    RingBuffer ringBuffer(SLOT_COUNT, SLOT_SIZE);
    std::atomic<bool> ioError{false};

    // 讀取執行緒 (生產者)
    std::thread readerThread([&]() {
        uint64_t remaining = task.fileSize;
        while (remaining > 0 && !m_isCancelled.load() && !ioError.load()) {
            if (m_isPaused.load()) {
                std::unique_lock<std::mutex> lock(m_pauseMutex);
                m_pauseCv.wait(lock, [this]() {
                    return !m_isPaused.load() || m_isCancelled.load();
                });
            }

            auto* slot = ringBuffer.AcquireWriteSlot();
            DWORD bytesToRead = (remaining > SLOT_SIZE) ? SLOT_SIZE : static_cast<DWORD>(remaining);
            // 非快取 I/O 必須以 4096 磁區邊界對齊讀取
            DWORD alignedBytesToRead = (bytesToRead + 4095) & ~4095;

            DWORD bytesRead = 0;
            BOOL readOk = ReadFile(hSrc, slot->data, alignedBytesToRead, &bytesRead, nullptr);
            if (!readOk) {
                ioError.store(true);
                ringBuffer.CommitWriteSlot(0, true);
                break;
            }

            size_t actualValidBytes = (bytesRead < bytesToRead) ? bytesRead : bytesToRead;
            remaining = (remaining > actualValidBytes) ? (remaining - actualValidBytes) : 0;
            bool isEof = (remaining == 0);

            ringBuffer.CommitWriteSlot(actualValidBytes, isEof);
            if (isEof) break;
        }
    });

    // 寫入執行緒 (消費者)
    std::thread writerThread([&]() {
        while (!m_isCancelled.load() && !ioError.load()) {
            auto* slot = ringBuffer.AcquireReadSlot();
            if (slot->validBytes > 0) {
                DWORD alignedBytesToWrite = static_cast<DWORD>((slot->validBytes + 4095) & ~4095);
                DWORD bytesWritten = 0;
                BOOL writeOk = WriteFile(hDst, slot->data, alignedBytesToWrite, &bytesWritten, nullptr);
                if (!writeOk) {
                    ioError.store(true);
                    ringBuffer.ReleaseReadSlot();
                    break;
                }
                m_transferredBytes.fetch_add(slot->validBytes);
            }

            bool isEof = slot->isEof;
            ringBuffer.ReleaseReadSlot();
            if (isEof) break;
        }
    });

    if (readerThread.joinable()) readerThread.join();
    if (writerThread.joinable()) writerThread.join();

    // 透過 SetEndOfFile 截斷 Direct I/O 對齊填補的多餘字節，精確符合原始大小
    LARGE_INTEGER targetSize;
    targetSize.QuadPart = task.fileSize;
    SetFilePointerEx(hDst, targetSize, nullptr, FILE_BEGIN);
    SetEndOfFile(hDst);

    CloseHandle(hSrc);
    CloseHandle(hDst);

    if (!ioError.load() && !m_isCancelled.load()) {
        m_processedFiles.fetch_add(1);
        return true;
    }
    return false;
}

bool TransferEngine::StartTransfer(
    const std::wstring& srcPath,
    const std::wstring& dstPath,
    bool isMove,
    TransferProgressCallback callback,
    void* userData
) {
    m_isRunning.store(true);
    m_isCancelled.store(false);
    m_isPaused.store(false);
    m_callback = callback;
    m_userData = userData;

    m_totalBytes = 0;
    m_transferredBytes.store(0);
    m_totalFiles = 0;
    m_processedFiles.store(0);

    auto startTime = std::chrono::steady_clock::now();

    // 1. 同磁區瞬時搬移判定 (純指標修改，不阻塞硬體 Flush)
    if (isMove && IsSameVolume(srcPath, dstPath)) {
        BOOL ok = MoveFileExW(srcPath.c_str(), dstPath.c_str(), MOVEFILE_REPLACE_EXISTING);
        if (ok) {
            if (m_callback) {
                TransferProgressInfo info{};
                info.totalBytes = 1;
                info.transferredBytes = 1;
                info.totalFiles = 1;
                info.processedFiles = 1;
                info.currentSpeedBps = 999999999.0;
                info.currentFileName = srcPath.c_str();
                info.errorCode = 0;
                m_callback(&info, m_userData);
            }
            m_isRunning.store(false);
            return true;
        }
    }

    // 2. 收集傳輸任務清單
    std::vector<FileTransferTask> tasks;
    CollectTransferTasks(srcPath, dstPath, tasks);

    if (tasks.empty()) {
        m_isRunning.store(false);
        return false;
    }

    // 先建立所有需要的目錄結構
    for (const auto& task : tasks) {
        if (task.isDirectory) {
            CreateDirectoryW(task.dstFilePath.c_str(), nullptr);
        }
    }

    // 傳輸檔案
    for (const auto& task : tasks) {
        if (m_isCancelled.load()) break;
        if (task.isDirectory) continue;

        if (m_callback) {
            auto now = std::chrono::steady_clock::now();
            double elapsedSec = std::chrono::duration<double>(now - startTime).count();
            double speed = (elapsedSec > 0.001) ? (static_cast<double>(m_transferredBytes.load()) / elapsedSec) : 0.0;

            TransferProgressInfo info{};
            info.totalBytes = m_totalBytes;
            info.transferredBytes = m_transferredBytes.load();
            info.totalFiles = m_totalFiles;
            info.processedFiles = m_processedFiles.load();
            info.currentSpeedBps = speed;
            info.currentFileName = task.srcFilePath.c_str();
            info.errorCode = 0;
            m_callback(&info, m_userData);
        }

        // 大於 64MB 使用 Direct I/O，否則使用標準快速複製
        if (task.fileSize >= 64 * 1024 * 1024) {
            TransferLargeFileDirectIO(task);
        } else {
            TransferSmallFile(task);
        }
    }

    // 若為搬移且無取消，反向刪除來源檔案與資料夾
    if (isMove && !m_isCancelled.load()) {
        for (auto it = tasks.rbegin(); it != tasks.rend(); ++it) {
            if (it->isDirectory) {
                RemoveDirectoryW(it->srcFilePath.c_str());
            } else {
                DeleteFileW(it->srcFilePath.c_str());
            }
        }
    }

    if (m_callback) {
        TransferProgressInfo info{};
        info.totalBytes = m_totalBytes;
        info.transferredBytes = m_transferredBytes.load();
        info.totalFiles = m_totalFiles;
        info.processedFiles = m_processedFiles.load();
        info.currentSpeedBps = 0.0;
        info.currentFileName = L"Completed";
        info.errorCode = m_isCancelled.load() ? ERROR_CANCELLED : 0;
        m_callback(&info, m_userData);
    }

    m_isRunning.store(false);
    return !m_isCancelled.load();
}

void TransferEngine::Cancel() {
    m_isCancelled.store(true);
    Resume(); // 若處於暫停則喚醒以便退出
}

void TransferEngine::Pause() {
    m_isPaused.store(true);
}

void TransferEngine::Resume() {
    m_isPaused.store(false);
    m_pauseCv.notify_all();
}

} // namespace BlitzFS
