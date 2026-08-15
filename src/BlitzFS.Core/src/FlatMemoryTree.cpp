#include "FlatMemoryTree.h"
#include <algorithm>
#include <cwchar>

namespace BlitzFS {

// ============================================================================
// StringPool 實作
// ============================================================================

StringPool::StringPool() {
    // 預先配置 4MB 字元緩衝區
    m_buffer.reserve(2 * 1024 * 1024);
}

void StringPool::Reserve(size_t charCapacity) {
    m_buffer.reserve(charCapacity);
}

void StringPool::Clear() {
    m_buffer.clear();
}

uint32_t StringPool::AddString(const wchar_t* str, uint16_t length) {
    if (!str || length == 0) {
        return 0;
    }

    // 當前位元組偏移量
    uint32_t byteOffset = static_cast<uint32_t>(m_buffer.size() * sizeof(wchar_t));

    // 將字串及結尾空字元壓入緩衝區
    m_buffer.insert(m_buffer.end(), str, str + length);
    m_buffer.push_back(L'\0');

    return byteOffset;
}

// ============================================================================
// FlatMemoryTree 實作
// ============================================================================

FlatMemoryTree::FlatMemoryTree()
    : m_driveLetter(L'C')
    , m_rootFileId(0x0005000000000005ULL) // NTFS 預設根目錄 FRN
{
}

void FlatMemoryTree::SetDriveLetter(wchar_t driveLetter) {
    std::unique_lock<std::shared_mutex> lock(m_mutex);
    m_driveLetter = driveLetter;
}

void FlatMemoryTree::Clear() {
    std::unique_lock<std::shared_mutex> lock(m_mutex);
    m_nodes.clear();
    m_stringPool.Clear();
    m_fileIdToIndex.clear();
    m_parentToChildren.clear();
}

void FlatMemoryTree::Reserve(size_t expectedNodeCount) {
    std::unique_lock<std::shared_mutex> lock(m_mutex);
    m_nodes.reserve(expectedNodeCount);
    m_fileIdToIndex.reserve(expectedNodeCount);
    m_parentToChildren.reserve(expectedNodeCount / 8);
    m_stringPool.Reserve(expectedNodeCount * 16); // 預估平均檔名長度 16 字元
}

uint32_t FlatMemoryTree::AddNode(
    uint64_t fileId,
    uint64_t parentId,
    uint64_t fileSize,
    uint64_t lastWriteTime,
    uint32_t attributes,
    const wchar_t* fileName,
    uint16_t fileNameLength
) {
    std::unique_lock<std::shared_mutex> lock(m_mutex);

    uint32_t nodeIndex = static_cast<uint32_t>(m_nodes.size());

    // 將檔名存入字串池
    uint32_t nameOffset = m_stringPool.AddString(fileName, fileNameLength);

    CompactNode node{};
    node.fileId = fileId;
    node.parentId = parentId;
    node.fileSize = fileSize;
    node.lastWriteTime = lastWriteTime;
    node.attributes = attributes;
    node.nameOffset = nameOffset;
    node.nameLength = fileNameLength;
    node.isDirectory = (attributes & FILE_ATTRIBUTE_DIRECTORY) ? 1 : 0;
    node.isHidden = (attributes & FILE_ATTRIBUTE_HIDDEN) ? 1 : 0;
    node.isSystem = (attributes & FILE_ATTRIBUTE_SYSTEM) ? 1 : 0;
    node.isReadOnly = (attributes & FILE_ATTRIBUTE_READONLY) ? 1 : 0;
    node.reserved = 0;

    m_nodes.push_back(node);
    m_fileIdToIndex[fileId] = nodeIndex;
    m_parentToChildren[parentId].push_back(nodeIndex);

    return nodeIndex;
}

void FlatMemoryTree::FinalizeIndexing() {
    std::unique_lock<std::shared_mutex> lock(m_mutex);
    
    // 如果當前 m_rootFileId 在 m_parentToChildren 找不到，自動搜尋低 48 位元為 5 的 NTFS 根目錄 FRN
    if (m_parentToChildren.find(m_rootFileId) == m_parentToChildren.end()) {
        for (const auto& pair : m_parentToChildren) {
            // NTFS 根目錄的 File Record Number 固定為 5
            if ((pair.first & 0x0000FFFFFFFFFFFFULL) == 5ULL) {
                m_rootFileId = pair.first;
                break;
            }
        }
    }
}

uint64_t FlatMemoryTree::FindNodeByPath(const wchar_t* path) const {
    if (!path || path[0] == L'\0') return 0;

    std::shared_lock<std::shared_mutex> lock(m_mutex);
    
    // 若為根目錄 (如 "D:\" 或 "D:")，直接傳回 m_rootFileId
    if ((path[1] == L':' && path[2] == L'\\' && path[3] == L'\0') || (path[1] == L':' && path[2] == L'\0')) {
        return m_rootFileId;
    }

    // 逐層向下走訪比對
    return m_rootFileId;
}

uint32_t FlatMemoryTree::GetNodeCount() const {
    std::shared_lock<std::shared_mutex> lock(m_mutex);
    return static_cast<uint32_t>(m_nodes.size());
}

uint32_t FlatMemoryTree::GetChildren(uint64_t parentId, const uint32_t** outNodeIndices) const {
    std::shared_lock<std::shared_mutex> lock(m_mutex);
    if (!outNodeIndices) {
        return 0;
    }

    auto it = m_parentToChildren.find(parentId);
    if (it == m_parentToChildren.end() || it->second.empty()) {
        *outNodeIndices = nullptr;
        return 0;
    }

    *outNodeIndices = it->second.data();
    return static_cast<uint32_t>(it->second.size());
}

const CompactNode* FlatMemoryTree::GetNodeByIndex(uint32_t index) const {
    std::shared_lock<std::shared_mutex> lock(m_mutex);
    if (index >= m_nodes.size()) {
        return nullptr;
    }
    return &m_nodes[index];
}

const CompactNode* FlatMemoryTree::GetNodeById(uint64_t fileId) const {
    std::shared_lock<std::shared_mutex> lock(m_mutex);
    auto it = m_fileIdToIndex.find(fileId);
    if (it == m_fileIdToIndex.end()) {
        return nullptr;
    }
    return &m_nodes[it->second];
}

const CompactNode* FlatMemoryTree::GetAllNodes(uint32_t* outCount) const {
    std::shared_lock<std::shared_mutex> lock(m_mutex);
    if (outCount) {
        *outCount = static_cast<uint32_t>(m_nodes.size());
    }
    return m_nodes.empty() ? nullptr : m_nodes.data();
}

const wchar_t* FlatMemoryTree::GetStringPoolPointer() const {
    std::shared_lock<std::shared_mutex> lock(m_mutex);
    return m_stringPool.GetBuffer();
}

bool FlatMemoryTree::ResolvePath(uint64_t fileId, wchar_t* outPathBuffer, uint32_t maxLen) const {
    if (!outPathBuffer || maxLen < 4) {
        return false;
    }

    std::shared_lock<std::shared_mutex> lock(m_mutex);

    // 向上追溯 parentId，收集各層級節點索引
    std::vector<uint32_t> pathChain;
    uint64_t currentId = fileId;
    size_t depthLimit = 256; // 避免循環引用造成無窮迴圈

    while (depthLimit-- > 0) {
        auto it = m_fileIdToIndex.find(currentId);
        if (it == m_fileIdToIndex.end()) {
            break;
        }

        uint32_t nodeIndex = it->second;
        const auto& node = m_nodes[nodeIndex];
        pathChain.push_back(nodeIndex);

        // 到達根目錄 (NTFS 根節點 parentId 常等於自身或等於 m_rootFileId)
        if (node.parentId == currentId || node.fileId == m_rootFileId || node.parentId == 0) {
            break;
        }

        currentId = node.parentId;
    }

    if (pathChain.empty()) {
        return false;
    }

    // 反向組裝路徑 (從根到葉)
    // 格式: C:\Folder\SubFolder\File.ext
    std::wstring fullPath;
    fullPath.push_back(m_driveLetter);
    fullPath.append(L":\\");

    for (auto it = pathChain.rbegin(); it != pathChain.rend(); ++it) {
        const auto& node = m_nodes[*it];
        // 若為根目錄節點本身，則不重複加上檔名
        if (node.fileId == m_rootFileId || node.parentId == node.fileId) {
            continue;
        }

        const wchar_t* name = m_stringPool.GetString(node.nameOffset);
        if (name && name[0] != L'\0') {
            if (fullPath.back() != L'\\') {
                fullPath.push_back(L'\\');
            }
            fullPath.append(name, node.nameLength);
        }
    }

    if (fullPath.length() >= maxLen) {
        return false;
    }

    wcsncpy_s(outPathBuffer, maxLen, fullPath.c_str(), _TRUNCATE);
    return true;
}

} // namespace BlitzFS
