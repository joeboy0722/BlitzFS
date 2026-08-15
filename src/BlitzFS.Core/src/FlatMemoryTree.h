#pragma once
#include "../include/CommonDef.h"
#include <vector>
#include <unordered_map>
#include <string>
#include <shared_mutex>
#include <memory>

namespace BlitzFS {

/**
 * @brief 緊湊 UTF-16 檔名字串池
 * 使用連續記憶體儲存所有節點檔名，避免海量小字串動態記憶體配置帶來的碎片化
 */
class StringPool {
public:
    StringPool();
    ~StringPool() = default;

    void Reserve(size_t charCapacity);
    void Clear();

    // 將 UTF-16 檔名壓入字串池，傳回位元組偏移量 (byte offset)
    uint32_t AddString(const wchar_t* str, uint16_t length);

    // 取得字串池首指標
    const wchar_t* GetBuffer() const { return m_buffer.data(); }

    // 根據位元組偏移量取得字串指標
    const wchar_t* GetString(uint32_t byteOffset) const {
        return reinterpret_cast<const wchar_t*>(reinterpret_cast<const char*>(m_buffer.data()) + byteOffset);
    }

    size_t GetTotalBytes() const { return m_buffer.size() * sizeof(wchar_t); }

private:
    std::vector<wchar_t> m_buffer;
};

/**
 * @brief 連續平鋪式記憶體目錄樹架構
 * 節點儲存於連續記憶體陣列中，提供 O(1) 父子關係索引與秒級全盤路徑逆向追溯
 */
class FlatMemoryTree {
public:
    FlatMemoryTree();
    ~FlatMemoryTree() = default;

    void SetDriveLetter(wchar_t driveLetter);
    wchar_t GetDriveLetter() const { return m_driveLetter; }

    void Clear();
    void Reserve(size_t expectedNodeCount);

    // 批次插入節點 (直接填入資料結構)
    uint32_t AddNode(
        uint64_t fileId,
        uint64_t parentId,
        uint64_t fileSize,
        uint64_t lastWriteTime,
        uint32_t attributes,
        const wchar_t* fileName,
        uint16_t fileNameLength
    );

    // 掃描完成後呼叫，建構/優化父子關係索引
    void FinalizeIndexing();

    // 查詢介面 (O(1) 瞬時查詢)
    uint32_t GetNodeCount() const;
    uint32_t GetChildren(uint64_t parentId, const uint32_t** outNodeIndices) const;
    const CompactNode* GetNodeByIndex(uint32_t index) const;
    const CompactNode* GetNodeById(uint64_t fileId) const;
    const CompactNode* GetAllNodes(uint32_t* outCount) const;
    const wchar_t* GetStringPoolPointer() const;

    // 逆向路徑追溯
    bool ResolvePath(uint64_t fileId, wchar_t* outPathBuffer, uint32_t maxLen) const;

    // 根據路徑搜尋節點 ID
    uint64_t FindNodeByPath(const wchar_t* path) const;

    // 取得根目錄的 File ID (NTFS 通常為 0x0005000000000005)
    uint64_t GetRootFileId() const { return m_rootFileId; }
    void SetRootFileId(uint64_t rootId) { m_rootFileId = rootId; }

private:
    wchar_t m_driveLetter;
    uint64_t m_rootFileId;

    StringPool m_stringPool;
    std::vector<CompactNode> m_nodes;

    // 索引結構
    std::unordered_map<uint64_t, uint32_t> m_fileIdToIndex;
    std::unordered_map<uint64_t, std::vector<uint32_t>> m_parentToChildren;

    // 讀寫鎖保障執行緒安全
    mutable std::shared_mutex m_mutex;
};

} // namespace BlitzFS
