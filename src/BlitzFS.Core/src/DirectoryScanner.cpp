#include "DirectoryScanner.h"
#include <vector>
#include <chrono>

namespace BlitzFS {

DirectoryScanner::DirectoryScanner() {
}

bool DirectoryScanner::Scan(wchar_t driveLetter, FlatMemoryTree& outTree, ScanProgressCallback callback, void* userData) {
    outTree.Clear();
    outTree.SetDriveLetter(driveLetter);

    uint64_t rootId = 1;
    outTree.SetRootFileId(rootId);
    outTree.Reserve(100000);

    m_nextIdGenerator.store(2);
    m_totalScanned.store(0);
    m_activeWorkers.store(0);
    m_isDone.store(false);

    // 將根目錄加入樹中
    wchar_t rootName[4] = { driveLetter, L':', L'\\', L'\0' };
    outTree.AddNode(rootId, 0, 0, 0, FILE_ATTRIBUTE_DIRECTORY, rootName, 3);

    // 初始化任務佇列
    {
        std::lock_guard<std::mutex> lock(m_queueMutex);
        std::wstring rootPath;
        rootPath.push_back(driveLetter);
        rootPath.append(L":\\");
        m_taskQueue.push({ rootPath, rootId });
    }

    // 啟動多執行緒 Worker 池
    unsigned int numThreads = std::thread::hardware_concurrency();
    if (numThreads == 0) numThreads = 4;
    if (numThreads > 16) numThreads = 16; // 限制並發上限避免磁碟磁頭過度尋道

    std::vector<std::thread> workers;
    workers.reserve(numThreads);

    for (unsigned int i = 0; i < numThreads; ++i) {
        workers.emplace_back(&DirectoryScanner::WorkerThread, this, std::ref(outTree));
    }

    // 主執行緒進行進度監控
    while (!m_isDone.load()) {
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
        if (callback) {
            callback(m_totalScanned.load(), userData);
        }
    }

    // 等待所有 Worker 結束
    for (auto& t : workers) {
        if (t.joinable()) {
            t.join();
        }
    }

    if (callback) {
        callback(m_totalScanned.load(), userData);
    }

    outTree.FinalizeIndexing();
    return true;
}

void DirectoryScanner::WorkerThread(FlatMemoryTree& outTree) {
    while (true) {
        ScanTask currentTask;
        {
            std::unique_lock<std::mutex> lock(m_queueMutex);
            m_cv.wait(lock, [this]() {
                return !m_taskQueue.empty() || (m_activeWorkers.load() == 0 && m_taskQueue.empty());
            });

            if (m_taskQueue.empty()) {
                if (m_activeWorkers.load() == 0) {
                    m_isDone.store(true);
                    m_cv.notify_all();
                    return;
                }
                continue;
            }

            currentTask = m_taskQueue.front();
            m_taskQueue.pop();
            m_activeWorkers.fetch_add(1);
        }

        // 走訪該目錄
        std::wstring searchPattern = currentTask.directoryPath;
        if (searchPattern.back() != L'\\') {
            searchPattern.push_back(L'\\');
        }
        searchPattern.append(L"*");

        WIN32_FIND_DATAW findData;
        // 使用 FindExInfoBasic 與 FIND_FIRST_EX_LARGE_FETCH 加速 Win32 枚舉
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
                // 略過 "." 與 ".."
                if (wcscmp(findData.cFileName, L".") == 0 || wcscmp(findData.cFileName, L"..") == 0) {
                    continue;
                }

                uint64_t fileId = m_nextIdGenerator.fetch_add(1);
                ULARGE_INTEGER fileSize;
                fileSize.LowPart = findData.nFileSizeLow;
                fileSize.HighPart = findData.nFileSizeHigh;

                ULARGE_INTEGER fileTime;
                fileTime.LowPart = findData.ftLastWriteTime.dwLowDateTime;
                fileTime.HighPart = findData.ftLastWriteTime.dwHighDateTime;

                uint16_t nameLen = static_cast<uint16_t>(wcslen(findData.cFileName));

                // 寫入記憶體樹
                outTree.AddNode(
                    fileId,
                    currentTask.parentId,
                    fileSize.QuadPart,
                    fileTime.QuadPart,
                    findData.dwFileAttributes,
                    findData.cFileName,
                    nameLen
                );

                m_totalScanned.fetch_add(1);

                // 若為子目錄，將其推入任務佇列
                if (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
                    std::wstring subDirPath = currentTask.directoryPath;
                    if (subDirPath.back() != L'\\') {
                        subDirPath.push_back(L'\\');
                    }
                    subDirPath.append(findData.cFileName);

                    {
                        std::lock_guard<std::mutex> lock(m_queueMutex);
                        m_taskQueue.push({ subDirPath, fileId });
                    }
                    m_cv.notify_one();
                }

            } while (FindNextFileW(hFind, &findData));

            FindClose(hFind);
        }

        m_activeWorkers.fetch_sub(1);
        m_cv.notify_all();
    }
}

} // namespace BlitzFS
