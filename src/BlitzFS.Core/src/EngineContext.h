#pragma once
#include "../include/IEngine.h"
#include "FlatMemoryTree.h"
#include "MftScanner.h"
#include "DirectoryScanner.h"
#include "TransferEngine.h"
#include <memory>

namespace BlitzFS {

/**
 * @brief 核心引擎上下文實現
 */
class EngineContext : public IEngine {
public:
    EngineContext();
    ~EngineContext() override = default;

    bool ScanVolume(wchar_t driveLetter, ScanProgressCallback callback, void* userData) override;
    uint32_t GetNodeCount() const override;
    uint64_t GetRootFileId() const override;
    uint64_t FindNodeByPath(const wchar_t* path) const override;
    uint32_t GetChildren(uint64_t parentId, const uint32_t** outNodeIndices) const override;
    const CompactNode* GetNodeByIndex(uint32_t index) const override;
    const CompactNode* GetNodeById(uint64_t fileId) const override;
    const CompactNode* GetAllNodes(uint32_t* outCount) const override;
    const wchar_t* GetStringPoolPointer() const override;
    bool ResolvePath(uint64_t fileId, wchar_t* outPathBuffer, uint32_t maxLen) const override;

    bool StartTransfer(const std::wstring& srcPath, const std::wstring& dstPath, bool isMove, TransferProgressCallback callback, void* userData) override;
    void CancelTransfer() override;
    void PauseTransfer() override;
    void ResumeTransfer() override;

private:
    FlatMemoryTree m_tree;
    MftScanner m_mftScanner;
    DirectoryScanner m_dirScanner;
    TransferEngine m_transferEngine;
};

} // namespace BlitzFS
