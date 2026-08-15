#include "../include/CoreAPI.h"
#include "EngineContext.h"
#include <memory>

using namespace BlitzFS;

BLITZFS_API void* BlitzFS_CreateEngine() {
    try {
        auto* engine = new EngineContext();
        return static_cast<void*>(engine);
    } catch (...) {
        return nullptr;
    }
}

BLITZFS_API void BlitzFS_DestroyEngine(void* engineHandle) {
    if (engineHandle) {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        delete engine;
    }
}

BLITZFS_API bool BlitzFS_ScanVolume(void* engineHandle, wchar_t driveLetter, ScanProgressCallback callback, void* userData) {
    if (!engineHandle) return false;
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        return engine->ScanVolume(driveLetter, callback, userData);
    } catch (...) {
        return false;
    }
}

BLITZFS_API uint32_t BlitzFS_GetNodeCount(void* engineHandle) {
    if (!engineHandle) return 0;
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        return engine->GetNodeCount();
    } catch (...) {
        return 0;
    }
}

BLITZFS_API uint64_t BlitzFS_GetRootFileId(void* engineHandle) {
    if (!engineHandle) return 0;
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        return engine->GetRootFileId();
    } catch (...) {
        return 0;
    }
}

BLITZFS_API uint64_t BlitzFS_FindNodeByPath(void* engineHandle, const wchar_t* path) {
    if (!engineHandle || !path) return 0;
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        return engine->FindNodeByPath(path);
    } catch (...) {
        return 0;
    }
}

BLITZFS_API uint32_t BlitzFS_GetChildren(void* engineHandle, uint64_t parentId, const uint32_t** outNodeIndices) {
    if (!engineHandle || !outNodeIndices) return 0;
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        return engine->GetChildren(parentId, outNodeIndices);
    } catch (...) {
        *outNodeIndices = nullptr;
        return 0;
    }
}

BLITZFS_API const CompactNode* BlitzFS_GetNodeByIndex(void* engineHandle, uint32_t nodeIndex) {
    if (!engineHandle) return nullptr;
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        return engine->GetNodeByIndex(nodeIndex);
    } catch (...) {
        return nullptr;
    }
}

BLITZFS_API const CompactNode* BlitzFS_GetNodeById(void* engineHandle, uint64_t fileId) {
    if (!engineHandle) return nullptr;
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        return engine->GetNodeById(fileId);
    } catch (...) {
        return nullptr;
    }
}

BLITZFS_API const CompactNode* BlitzFS_GetAllNodes(void* engineHandle, uint32_t* outCount) {
    if (!engineHandle) {
        if (outCount) *outCount = 0;
        return nullptr;
    }
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        return engine->GetAllNodes(outCount);
    } catch (...) {
        if (outCount) *outCount = 0;
        return nullptr;
    }
}

BLITZFS_API const wchar_t* BlitzFS_GetStringPoolPointer(void* engineHandle) {
    if (!engineHandle) return nullptr;
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        return engine->GetStringPoolPointer();
    } catch (...) {
        return nullptr;
    }
}

BLITZFS_API bool BlitzFS_ResolvePath(void* engineHandle, uint64_t fileId, wchar_t* outPathBuffer, uint32_t maxLen) {
    if (!engineHandle || !outPathBuffer || maxLen == 0) return false;
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        return engine->ResolvePath(fileId, outPathBuffer, maxLen);
    } catch (...) {
        return false;
    }
}

BLITZFS_API bool BlitzFS_StartTransfer(
    void* engineHandle,
    const wchar_t* srcPath,
    const wchar_t* dstPath,
    bool isMove,
    TransferProgressCallback callback,
    void* userData
) {
    if (!engineHandle || !srcPath || !dstPath) return false;
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        return engine->StartTransfer(srcPath, dstPath, isMove, callback, userData);
    } catch (...) {
        return false;
    }
}

BLITZFS_API void BlitzFS_CancelTransfer(void* engineHandle) {
    if (!engineHandle) return;
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        engine->CancelTransfer();
    } catch (...) {}
}

BLITZFS_API void BlitzFS_PauseTransfer(void* engineHandle) {
    if (!engineHandle) return;
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        engine->PauseTransfer();
    } catch (...) {}
}

BLITZFS_API void BlitzFS_ResumeTransfer(void* engineHandle) {
    if (!engineHandle) return;
    try {
        auto* engine = static_cast<EngineContext*>(engineHandle);
        engine->ResumeTransfer();
    } catch (...) {}
}
