#pragma once
#include "CommonDef.h"

#ifdef BLITZFS_CORE_EXPORTS
#define BLITZFS_API extern "C" __declspec(dllexport)
#else
#define BLITZFS_API extern "C" __declspec(dllimport)
#endif

// ============================================================================
// BlitzFS.Core C-ABI 導出介面 (支援 C# P/Invoke, C/C++, Rust 等零拷貝調用)
// ============================================================================

/**
 * @brief 建立核心引擎實例
 * @return 引擎不透明控制代碼 (void*)
 */
BLITZFS_API void* BlitzFS_CreateEngine();

/**
 * @brief 銷毀核心引擎實例並釋放相關記憶體
 * @param engineHandle 引擎控制代碼
 */
BLITZFS_API void  BlitzFS_DestroyEngine(void* engineHandle);

/**
 * @brief 掃描指定磁碟代號的所有檔案中繼資料並建立記憶體樹
 * @param engineHandle 引擎控制代碼
 * @param driveLetter 磁碟代號 (例如 L'C', L'D')
 * @param callback 掃描進度回呼函數 (可為 nullptr)
 * @param userData 使用者自訂指標
 * @return 掃描成功傳回 true，失敗傳回 false
 */
BLITZFS_API bool  BlitzFS_ScanVolume(void* engineHandle, wchar_t driveLetter, BlitzFS::ScanProgressCallback callback, void* userData);

/**
 * @brief 取得當前記憶體樹中索引的總節點數量
 * @param engineHandle 引擎控制代碼
 * @return 節點總數
 */
BLITZFS_API uint32_t BlitzFS_GetNodeCount(void* engineHandle);

/**
 * @brief 取得磁碟根目錄的 File ID
 * @param engineHandle 引擎控制代碼
 * @return 根目錄編號
 */
BLITZFS_API uint64_t BlitzFS_GetRootFileId(void* engineHandle);

/**
 * @brief 根據路徑尋找對應的節點 File ID
 * @param engineHandle 引擎控制代碼
 * @param path 檔案或資料夾完整路徑 (如 L"D:\\")
 * @return 節點 File ID，若找不到則傳回 0
 */
BLITZFS_API uint64_t BlitzFS_FindNodeByPath(void* engineHandle, const wchar_t* path);

/**
 * @brief 取得指定父目錄下的所有子節點索引指標陣列 (O(1) 瞬時查詢)
 * @param engineHandle 引擎控制代碼
 * @param parentId 父目錄編號 (根目錄 NTFS 為 0x0005000000000005 或 0)
 * @param outNodeIndices 傳回指向連續 uint32_t 節點索引陣列的指標
 * @return 子項目總數量
 */
BLITZFS_API uint32_t BlitzFS_GetChildren(void* engineHandle, uint64_t parentId, const uint32_t** outNodeIndices);

/**
 * @brief 根據節點平鋪索引取得 CompactNode 唯讀指標 (零拷貝)
 * @param engineHandle 引擎控制代碼
 * @param nodeIndex 節點在平鋪陣列中的索引
 * @return 指向 CompactNode 的指標，若索引無效則傳回 nullptr
 */
BLITZFS_API const BlitzFS::CompactNode* BlitzFS_GetNodeByIndex(void* engineHandle, uint32_t nodeIndex);

/**
 * @brief 根據 File ID 取得 CompactNode 唯讀指標
 * @param engineHandle 引擎控制代碼
 * @param fileId 檔案或目錄唯一編號
 * @return 指向 CompactNode 的指標，若不存在則傳回 nullptr
 */
BLITZFS_API const BlitzFS::CompactNode* BlitzFS_GetNodeById(void* engineHandle, uint64_t fileId);

/**
 * @brief 取得平鋪記憶體樹中所有節點的連續陣列首指標 (零拷貝直讀)
 * @param engineHandle 引擎控制代碼
 * @param outCount 輸出節點總數
 * @return 指向 CompactNode 連續記憶體陣列首地址
 */
BLITZFS_API const BlitzFS::CompactNode* BlitzFS_GetAllNodes(void* engineHandle, uint32_t* outCount);

/**
 * @brief 取得全域檔名字串池 (StringPool) 的首指標
 * @param engineHandle 引擎控制代碼
 * @return UTF-16 wchar_t 緩衝區指標
 */
BLITZFS_API const wchar_t* BlitzFS_GetStringPoolPointer(void* engineHandle);

/**
 * @brief 根據 File ID 向上追溯並重組完整絕對路徑
 * @param engineHandle 引擎控制代碼
 * @param fileId 目標檔案或資料夾編號
 * @param outPathBuffer 輸出完整路徑之字串緩衝區
 * @param maxLen 緩衝區最大長度 (字元數)
 * @return 重組成功傳回 true，失敗傳回 false
 */
BLITZFS_API bool  BlitzFS_ResolvePath(void* engineHandle, uint64_t fileId, wchar_t* outPathBuffer, uint32_t maxLen);

/**
 * @brief 啟動檔案搬移或複製任務 (多工管線/同磁區瞬時/跨磁區並發)
 * @param engineHandle 引擎控制代碼
 * @param srcPath 來源路徑 (檔案或目錄)
 * @param dstPath 目標路徑 (檔案或目錄)
 * @param isMove true 代表搬移 (剪下)，false 代表複製
 * @param callback 傳輸進度回呼函數 (可為 nullptr)
 * @param userData 使用者自訂指標
 * @return 啟動成功傳回 true
 */
BLITZFS_API bool  BlitzFS_StartTransfer(void* engineHandle, const wchar_t* srcPath, const wchar_t* dstPath, bool isMove, BlitzFS::TransferProgressCallback callback, void* userData);

/**
 * @brief 取消當前正在進行的傳輸任務
 * @param engineHandle 引擎控制代碼
 */
BLITZFS_API void  BlitzFS_CancelTransfer(void* engineHandle);

/**
 * @brief 暫停當前正在進行的傳輸任務
 * @param engineHandle 引擎控制代碼
 */
BLITZFS_API void  BlitzFS_PauseTransfer(void* engineHandle);

/**
 * @brief 恢復暫停中的傳輸任務
 * @param engineHandle 引擎控制代碼
 */
BLITZFS_API void  BlitzFS_ResumeTransfer(void* engineHandle);
