#pragma once
#include "CommonDef.h"
#include <string>
#include <vector>

namespace BlitzFS {

/**
 * @brief 核心引擎介面定義
 */
class IEngine {
public:
    virtual ~IEngine() = default;

    virtual bool ScanVolume(wchar_t driveLetter, ScanProgressCallback callback, void* userData) = 0;
    virtual uint32_t GetNodeCount() const = 0;
    virtual uint64_t GetRootFileId() const = 0;
    virtual uint64_t FindNodeByPath(const wchar_t* path) const = 0;
    virtual uint32_t GetChildren(uint64_t parentId, const uint32_t** outNodeIndices) const = 0;
    virtual const CompactNode* GetNodeByIndex(uint32_t index) const = 0;
    virtual const CompactNode* GetNodeById(uint64_t fileId) const = 0;
    virtual const CompactNode* GetAllNodes(uint32_t* outCount) const = 0;
    virtual const wchar_t* GetStringPoolPointer() const = 0;
    virtual bool ResolvePath(uint64_t fileId, wchar_t* outPathBuffer, uint32_t maxLen) const = 0;

    virtual bool StartTransfer(const std::wstring& srcPath, const std::wstring& dstPath, bool isMove, TransferProgressCallback callback, void* userData) = 0;
    virtual void CancelTransfer() = 0;
    virtual void PauseTransfer() = 0;
    virtual void ResumeTransfer() = 0;
};

} // namespace BlitzFS
