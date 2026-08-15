#include "EngineContext.h"
#include <iostream>

namespace BlitzFS {

EngineContext::EngineContext() {
}

bool EngineContext::ScanVolume(wchar_t driveLetter, ScanProgressCallback callback, void* userData) {
    // 1. 優先嘗試極速 NTFS USN Journal 掃描 (MFT 批次讀取)
    bool mftOk = m_mftScanner.Scan(driveLetter, m_tree, callback, userData);
    if (mftOk && m_tree.GetNodeCount() > 0) {
        return true;
    }

    // 2. 若 MFT 掃描失敗 (例如 FAT32 / exFAT 隨身碟或非管理員權限)，自動無縫降級至多執行緒並發掃描器
    return m_dirScanner.Scan(driveLetter, m_tree, callback, userData);
}

uint32_t EngineContext::GetNodeCount() const {
    return m_tree.GetNodeCount();
}

uint64_t EngineContext::GetRootFileId() const {
    return m_tree.GetRootFileId();
}

uint64_t EngineContext::FindNodeByPath(const wchar_t* path) const {
    return m_tree.FindNodeByPath(path);
}

uint32_t EngineContext::GetChildren(uint64_t parentId, const uint32_t** outNodeIndices) const {
    return m_tree.GetChildren(parentId, outNodeIndices);
}

const CompactNode* EngineContext::GetNodeByIndex(uint32_t index) const {
    return m_tree.GetNodeByIndex(index);
}

const CompactNode* EngineContext::GetNodeById(uint64_t fileId) const {
    return m_tree.GetNodeById(fileId);
}

const CompactNode* EngineContext::GetAllNodes(uint32_t* outCount) const {
    return m_tree.GetAllNodes(outCount);
}

const wchar_t* EngineContext::GetStringPoolPointer() const {
    return m_tree.GetStringPoolPointer();
}

bool EngineContext::ResolvePath(uint64_t fileId, wchar_t* outPathBuffer, uint32_t maxLen) const {
    return m_tree.ResolvePath(fileId, outPathBuffer, maxLen);
}

bool EngineContext::StartTransfer(
    const std::wstring& srcPath,
    const std::wstring& dstPath,
    bool isMove,
    TransferProgressCallback callback,
    void* userData
) {
    return m_transferEngine.StartTransfer(srcPath, dstPath, isMove, callback, userData);
}

void EngineContext::CancelTransfer() {
    m_transferEngine.Cancel();
}

void EngineContext::PauseTransfer() {
    m_transferEngine.Pause();
}

void EngineContext::ResumeTransfer() {
    m_transferEngine.Resume();
}

} // namespace BlitzFS
