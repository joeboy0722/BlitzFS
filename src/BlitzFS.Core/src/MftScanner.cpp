#include "MftScanner.h"
#include <vector>
#include <iostream>

namespace BlitzFS {

MftScanner::MftScanner() {
}

HANDLE MftScanner::OpenVolumeHandle(wchar_t driveLetter) {
    wchar_t volumePath[8];
    swprintf_s(volumePath, L"\\\\.\\%c:", driveLetter);

    // 開啟磁區原始控制代碼 (需要管理員權限以支援 FSCTL_ENUM_USN_DATA)
    HANDLE hVolume = CreateFileW(
        volumePath,
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr,
        OPEN_EXISTING,
        0,
        nullptr
    );

    if (hVolume == INVALID_HANDLE_VALUE) {
        // 降級以唯讀方式重試
        hVolume = CreateFileW(
            volumePath,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr,
            OPEN_EXISTING,
            0,
            nullptr
        );
    }

    return hVolume;
}

bool MftScanner::QueryUsnJournal(HANDLE hVolume, USN_JOURNAL_DATA_V0& outUsnData) {
    DWORD bytesReturned = 0;
    BOOL success = DeviceIoControl(
        hVolume,
        FSCTL_QUERY_USN_JOURNAL,
        nullptr,
        0,
        &outUsnData,
        sizeof(outUsnData),
        &bytesReturned,
        nullptr
    );

    return (success && bytesReturned > 0);
}

bool MftScanner::Scan(wchar_t driveLetter, FlatMemoryTree& outTree, ScanProgressCallback callback, void* userData) {
    HANDLE hVolume = OpenVolumeHandle(driveLetter);
    if (hVolume == INVALID_HANDLE_VALUE) {
        return false;
    }

    USN_JOURNAL_DATA_V0 usnJournalData{};
    if (!QueryUsnJournal(hVolume, usnJournalData)) {
        CloseHandle(hVolume);
        return false;
    }

    outTree.Clear();
    outTree.SetDriveLetter(driveLetter);
    outTree.SetRootFileId(0x0005000000000005ULL); // NTFS 根目錄固定 FRN
    outTree.Reserve(200000); // 預先分配空間以減少配置開銷

    // 設定枚舉參數
    MFT_ENUM_DATA_V0 mftEnumData{};
    mftEnumData.StartFileReferenceNumber = 0;
    mftEnumData.LowUsn = 0;
    mftEnumData.HighUsn = usnJournalData.NextUsn;

    // 分配 1MB 的大容量批次讀取緩衝區
    constexpr DWORD BUFFER_SIZE = 1024 * 1024;
    std::vector<BYTE> buffer(BUFFER_SIZE);

    DWORD bytesReturned = 0;
    uint32_t totalScanned = 0;
    uint32_t callbackCounter = 0;

    while (true) {
        BOOL success = DeviceIoControl(
            hVolume,
            FSCTL_ENUM_USN_DATA,
            &mftEnumData,
            sizeof(mftEnumData),
            buffer.data(),
            BUFFER_SIZE,
            &bytesReturned,
            nullptr
        );

        if (!success || bytesReturned <= sizeof(USN)) {
            break;
        }

        // 緩衝區前 8 位元組為下一批次枚舉的起始 USN
        USN nextUsn = *reinterpret_cast<USN*>(buffer.data());
        mftEnumData.StartFileReferenceNumber = nextUsn;

        // 走訪緩衝區中的所有 USN_RECORD 項目
        BYTE* recordCursor = buffer.data() + sizeof(USN);
        BYTE* bufferEnd = buffer.data() + bytesReturned;

        while (recordCursor < bufferEnd) {
            auto* record = reinterpret_cast<USN_RECORD_V2*>(recordCursor);
            if (record->RecordLength == 0) {
                break;
            }

            // 提取檔案資訊
            uint64_t fileId = record->FileReferenceNumber;
            uint64_t parentId = record->ParentFileReferenceNumber;
            uint32_t attributes = record->FileAttributes;
            uint16_t nameLengthChars = static_cast<uint16_t>(record->FileNameLength / sizeof(wchar_t));
            const wchar_t* fileName = reinterpret_cast<const wchar_t*>(reinterpret_cast<BYTE*>(record) + record->FileNameOffset);

            // 加入至記憶體樹中 (零字串拼裝)
            outTree.AddNode(
                fileId,
                parentId,
                0, // USN 記錄不一定帶即時大小，若需要可後續非同步補充
                record->TimeStamp.QuadPart,
                attributes,
                fileName,
                nameLengthChars
            );

            totalScanned++;
            callbackCounter++;

            // 移動至下一筆記錄
            recordCursor += record->RecordLength;
        }

        // 每累積 10,000 筆觸發一次回呼，避免過度頻繁打斷掃描
        if (callback && callbackCounter >= 10000) {
            callback(totalScanned, userData);
            callbackCounter = 0;
        }
    }

    if (callback) {
        callback(totalScanned, userData);
    }

    outTree.FinalizeIndexing();
    CloseHandle(hVolume);
    return true;
}

} // namespace BlitzFS
