# BlitzFS.Core (C++20) 核心引擎開發解析與 API 使用手冊

本手冊詳細記錄了 **BlitzFS.Core (C++20)** 底層極速核心引擎的架構設計、核心資料結構、C-ABI 導出 API 使用指南、實作範例代碼、編譯構建流程以及效能實測數據。

---

## 目錄

1. [專案模組架構與目錄結構](#1-專案模組架構與目錄結構)
2. [核心資料結構詳解](#2-核心資料結構詳解)
3. [C-ABI 導出 API 清單與使用說明](#3-c-abi-導出-api-清單與使用說明)
4. [完整程式碼範例 (C++ & C# P/Invoke)](#4-完整程式碼範例)
5. [傳輸引擎與資料完整性機制](#5-傳輸引擎與資料完整性機制)
6. [效能與二進位內容實測數據](#6-效能與二進位內容實測數據)
7. [編譯與建置指南](#7-編譯與建置指南)
8. [注意事項與常見問題](#8-注意事項與常見問題)

---

## 1. 專案模組架構與目錄結構

BlitzFS.Core 採用純 C++20 開發，透過標準 C-ABI 介面（`extern "C" __declspec(dllexport)`）將底層能力封裝為獨立的 `BlitzFS.Core.dll`，支援 C#、C++、Rust 等多種語言進行零拷貝（Zero-copy）指標調用。

```
d:/BlitzFS/
├── CMakeLists.txt                         # 頂層 CMake 構建腳本 (C++20, /O2, /AVX2)
├── src/
│   └── BlitzFS.Core/
│       ├── CMakeLists.txt                 # Core DLL 編譯腳本
│       ├── include/
│       │   ├── CommonDef.h                # 48 Bytes 對齊之 CompactNode 與基礎型別
│       │   ├── CoreAPI.h                  # 外部匯出 C-ABI 介面宣告
│       │   └── IEngine.h                  # 內部核心引擎介面定義
│       └── src/
│           ├── CoreAPI.cpp                # C-ABI 導出實作與例外防護層
│           ├── EngineContext.h/.cpp       # 引擎上下文調度中心
│           ├── FlatMemoryTree.h/.cpp      # 連續記憶體平坦目錄樹與 StringPool
│           ├── MftScanner.h/.cpp          # NTFS FSCTL_ENUM_USN_DATA 極速掃描器
│           ├── DirectoryScanner.h/.cpp    # FAT32/exFAT 穩健多執行緒並發掃描器
│           ├── TransferEngine.h/.cpp      # 同磁區瞬時搬移與跨磁區 Direct I/O 管線
│           └── RingBuffer.h/.cpp          # 4KB 扇區對齊之環形記憶體緩衝區
└── tests/
    └── BlitzFS.Core.Tests/
        ├── CMakeLists.txt                 # 測試專案 CMake 腳本
        └── main.cpp                       # 自動化單元測試、沙盒展示與內容校驗
```

---

## 2. 核心資料結構詳解

定義於 [`CommonDef.h`](file:///d:/BlitzFS/src/BlitzFS.Core/include/CommonDef.h)：

### 2.1 緊湊平鋪檔案節點 (`CompactNode`，精確 48 Bytes)
採用緊湊位元組對齊設計，100 萬個檔案常駐記憶體僅約 48MB，大幅提升 CPU 快取行（Cache-line）命中率：

```cpp
#pragma pack(push, 8)
struct CompactNode {
    uint64_t fileId;           // 檔案唯一編號 (NTFS FRN 或 NT FileId)
    uint64_t parentId;         // 父目錄編號 (根目錄為 0x0005000000000005 或 0)
    uint64_t fileSize;         // 檔案大小 (位元組)
    uint64_t lastWriteTime;    // 最後修改時間 (Windows FILETIME 格式)
    uint32_t attributes;       // Win32 檔案屬性旗標 (FILE_ATTRIBUTE_DIRECTORY 等)
    uint32_t nameOffset;       // 檔名在 StringPool 中的位元組偏移量 (byte offset)
    uint16_t nameLength;       // 檔名字元數 (UTF-16 wchar_t 數量)
    uint16_t isDirectory : 1;  // 是否為資料夾 (1: 目錄, 0: 檔案)
    uint16_t isHidden    : 1;  // 是否為隱藏項目
    uint16_t isSystem    : 1;  // 是否為系統檔案
    uint16_t isReadOnly  : 1;  // 是否唯讀
    uint16_t reserved    : 12; // 保留位元
};
#pragma pack(pop)
```

### 2.2 檔名字串池 (`StringPool`)
將全盤所有 UTF-16 檔名集中儲存在大區塊連續記憶體中，徹底杜絕為每個檔案做 `std::wstring` 小物件記憶體配置所造成的碎片化與記憶體開銷。
* 外部取得檔名方式：`const wchar_t* name = (const wchar_t*)((const char*)stringPoolPtr + node->nameOffset);`

### 2.3 傳輸進度狀態 (`TransferProgressInfo`)
```cpp
struct TransferProgressInfo {
    uint64_t totalBytes;            // 預計傳輸總位元組數
    uint64_t transferredBytes;      // 已完成傳輸位元組數
    uint32_t totalFiles;            // 總檔案數
    uint32_t processedFiles;        // 已處理檔案數
    double currentSpeedBps;         // 當前傳輸速率 (Bytes/s)
    const wchar_t* currentFileName; // 當前處理之檔案名稱
    uint32_t errorCode;             // 錯誤代碼 (0 代表正常)
};
```

---

## 3. C-ABI 導出 API 清單與使用說明

宣告於 [`CoreAPI.h`](file:///d:/BlitzFS/src/BlitzFS.Core/include/CoreAPI.h)：

| API 函數名稱 | 功能說明 | 參數說明 | 傳回值 |
| :--- | :--- | :--- | :--- |
| `BlitzFS_CreateEngine()` | 建立核心引擎實例 | 無 | 引擎控制代碼 `void*` (若失敗傳回 `nullptr`) |
| `BlitzFS_DestroyEngine(handle)` | 銷毀引擎實例並釋放記憶體 | `handle`: 引擎控制代碼 | `void` |
| `BlitzFS_ScanVolume(handle, drive, cb, user)` | 掃描指定磁碟中繼資料並建立記憶體樹 | `drive`: 磁碟代號 (如 `L'C'` / `L'D'`)<br>`cb`: 進度回呼<br>`user`: 自訂指標 | 成功傳回 `true`，失敗傳回 `false` |
| `BlitzFS_GetNodeCount(handle)` | 取得記憶體樹中索引的節點總數 | `handle`: 引擎控制代碼 | 節點總數 (`uint32_t`) |
| `BlitzFS_GetChildren(handle, parentId, outIndices)` | **O(1) 瞬時查詢** 指定目錄下的子項目索引陣列 | `parentId`: 父目錄編號<br>`outIndices`: 輸出 `const uint32_t**` | 子項目總數 (`uint32_t`) |
| `BlitzFS_GetNodeByIndex(handle, index)` | 根據平鋪索引取得 `CompactNode*` (零拷貝) | `index`: 節點平鋪索引 | `const CompactNode*` |
| `BlitzFS_GetNodeById(handle, fileId)` | 根據 File ID 取得 `CompactNode*` | `fileId`: 檔案或目錄編號 | `const CompactNode*` |
| `BlitzFS_GetStringPoolPointer(handle)` | 取得檔名字串池首位址 | `handle`: 引擎控制代碼 | `const wchar_t*` |
| `BlitzFS_ResolvePath(handle, fileId, buf, maxLen)` | 逆向追溯重組完整實體路徑 | `fileId`: 目標檔案 ID<br>`buf`: 輸出字串緩衝區<br>`maxLen`: 最大長度 | 成功傳回 `true` |
| `BlitzFS_StartTransfer(handle, src, dst, isMove, cb, user)` | 啟動檔案/目錄傳輸 (同磁區瞬時/跨磁區管線) | `src`: 來源路徑<br>`dst`: 目標路徑<br>`isMove`: `true` 為搬移，`false` 為複製<br>`cb`: 進度回呼 | 成功傳回 `true` |
| `BlitzFS_CancelTransfer(handle)` | 取消當前傳輸任務 | `handle`: 引擎控制代碼 | `void` |
| `BlitzFS_PauseTransfer(handle)` | 暫停傳輸任務 | `handle`: 引擎控制代碼 | `void` |
| `BlitzFS_ResumeTransfer(handle)` | 恢復暫停中之傳輸任務 | `handle`: 引擎控制代碼 | `void` |

---

## 4. 完整程式碼範例

### 4.1 C++ 原生調用範例

```cpp
#include "CoreAPI.h"
#include <iostream>

// 進度回呼
void OnProgress(const BlitzFS::TransferProgressInfo* info, void* /*user*/) {
    if (info) {
        std::wcout << L"正在處理: " << info->currentFileName 
                   << L" | 速率: " << (info->currentSpeedBps / (1024 * 1024)) << L" MB/s\n";
    }
}

int main() {
    // 1. 建立引擎
    void* engine = BlitzFS_CreateEngine();
    if (!engine) return -1;

    // 2. 掃描 D 槽
    std::cout << "正在掃描 D 槽...\n";
    BlitzFS_ScanVolume(engine, L'D', nullptr, nullptr);
    std::cout << "索引建立完成，總節點數: " << BlitzFS_GetNodeCount(engine) << "\n";

    // 3. O(1) 瞬時獲取根目錄下的所有子項目
    const uint32_t* childIndices = nullptr;
    uint32_t count = BlitzFS_GetChildren(engine, 0x0005000000000005ULL, &childIndices);
    const wchar_t* stringPool = BlitzFS_GetStringPoolPointer(engine);

    for (uint32_t i = 0; i < count; ++i) {
        const auto* node = BlitzFS_GetNodeByIndex(engine, childIndices[i]);
        if (node) {
            const wchar_t* name = (const wchar_t*)((const char*)stringPool + node->nameOffset);
            wchar_t fullPath[MAX_PATH];
            BlitzFS_ResolvePath(engine, node->fileId, fullPath, MAX_PATH);

            std::wcout << (node->isDirectory ? L"[目錄] " : L"[檔案] ") 
                       << name << L" -> " << fullPath << L"\n";
        }
    }

    // 4. 執行傳輸 (例如複製資料夾)
    BlitzFS_StartTransfer(engine, L"D:\\SourceFolder", L"D:\\BackupFolder", false, OnProgress, nullptr);

    // 5. 釋放引擎
    BlitzFS_DestroyEngine(engine);
    return 0;
}
```

### 4.2 C# (.NET 8) P/Invoke 調用範例

```csharp
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct CompactNode
{
    public ulong FileId;
    public ulong ParentId;
    public ulong FileSize;
    public ulong LastWriteTime;
    public uint Attributes;
    public uint NameOffset;
    public ushort NameLength;
    public ushort BitFlags; // isDirectory = BitFlags & 0x1
}

public static class NativeMethods
{
    private const string DllName = "BlitzFS.Core.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr BlitzFS_CreateEngine();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void BlitzFS_DestroyEngine(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool BlitzFS_ScanVolume(IntPtr handle, char driveLetter, IntPtr callback, IntPtr userData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint BlitzFS_GetNodeCount(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint BlitzFS_GetChildren(IntPtr handle, ulong parentId, out IntPtr outNodeIndices);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr BlitzFS_GetNodeByIndex(IntPtr handle, uint nodeIndex);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr BlitzFS_GetStringPoolPointer(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern bool BlitzFS_ResolvePath(IntPtr handle, ulong fileId, [Out] char[] outPathBuffer, uint maxLen);
}
```

---

## 5. 傳輸引擎與資料完整性機制

BlitzFS 傳輸引擎包含三大核心防護與加速機制：

```
                              ┌─────────────────────────────┐
                              │  BlitzFS_StartTransfer      │
                              └──────────────┬──────────────┘
                                             │
                       ┌─────────────────────┴─────────────────────┐
                       ▼                                           ▼
             【 同磁區判定 (isMove) 】                    【 跨磁區傳輸管線 】
             - MoveFileExW O(1)                        - 遞迴建構目錄結構
             - 瞬時更新 NTFS 指標                      - 小檔案: 多執行緒並發
             - 物理扇區不變，內容 100% 原始            - 大檔案 (>=64MB): Direct I/O
                                                         (4KB 對齊 + 16MB RingBuffer)
                                                       - SetEndOfFile 精確截斷
```

1. **同磁區瞬時搬移**：
   - 呼叫 `MoveFileExW(src, dst, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)`。
   - 僅修改 NTFS 目錄樹階層指標，萬筆檔案在 **0.01 秒內完成**，原始扇區內容完全無損。
2. **跨磁區超大檔案 Direct / Unbuffered I/O**：
   - 使用 `FILE_FLAG_NO_BUFFERING | FILE_FLAG_SEQUENTIAL_SCAN` 繞過 Windows 檔案快取。
   - 搭配 4KB 扇區對齊的 16MB 環形雙緩衝區（`RingBuffer`），讀取與寫入平行管線化。
   - 最後以 `SetFilePointerEx` 與 `SetEndOfFile` 精確截斷至實際 Byte 數，確保非 4KB 倍數的大檔資料 100% 完整。
3. **錯誤自動略過與狀態控制**：
   - 遇鎖定（`ERROR_SHARING_VIOLATION`）或權限不足（`ERROR_ACCESS_DENIED`）記錄於日誌並跳過，整體任務不中斷。
   - 支援隨時 `Pause`、`Resume` 與 `Cancel`。

---

## 6. 效能與二進位內容實測數據

執行環境：Windows 10 / MSVC v143 (Release `/O2 /AVX2`) / 實體 D: 槽測試

```text
======================================================================
        BlitzFS 核心引擎功能測試與檔案二進位內容完整性嚴格校驗        
======================================================================
[步驟 0] 原始檔案建立完成，已記錄初始校驗碼：
   - 文檔檔案校驗碼 (Doc Checksum)  : 0x15a04f9880d0684e
   - 二進位圖片校驗碼 (Pic Checksum): 0xe1e59b90089a2325
   - 大檔案校驗碼 (Large Checksum)  : 0x60065b007537e2fe (5,244,217 位元組，非 4KB 對齊)

【功能 1】極速掃描與中繼資料索引 (Scan & Metadata Indexing)
-> 掃描狀態: 成功
-> 總索引檔案與目錄數: 1,225,141 筆
-> 掃描耗時: 2080.44 ms (約 2.08 秒)

【功能 2】O(1) 瞬時目錄子項目檢索 (GetChildren)
-> 根目錄直接子項目數量: 69 個項目
-> O(1) 檢索耗時: 1.4 微秒 (0.0014 ms)

【功能 3】檔案複製 +【二進位內容 100% 完整性嚴格校驗】
-> 複製結果: 成功，耗時: 4.85 ms
-> 原始 Checksum: 0x15a04f9880d0684e | 複製 Checksum: 0x15a04f9880d0684e
-> 【二進位 Byte-by-Byte 比對】: 【100% 逐字元完全吻合！無任何資料損毀】

【功能 4】檔案移動 +【移動後內容 Checksum 完整性驗證】
-> 移動結果: 成功，耗時: 9.53 ms
-> 移動前 Checksum: 0xe1e59b90089a2325 | 移動後 Checksum: 0xe1e59b90089a2325
-> 【內容校驗結果】: 【雜湊 100% 吻合！內容毫髮無損】

【功能 5】大檔 Direct I/O 傳輸 +【尾部邊界與二進位逐位元組嚴格比對】
-> 傳輸非扇區倍數大檔案 (5.00 MB)...
-> 大檔傳輸結果: 成功，耗時: 2.01 ms
-> 原始 Checksum: 0x60065b007537e2fe | 傳輸 Checksum: 0x60065b007537e2fe
-> 【二進位 Byte-by-Byte 比對】: 【全檔 5,244,217 位元組完全吻合！尾部截斷 100% 正確！】

【測試後清理】清理獨立沙盒目錄...
-> 沙盒目錄已安全清理完畢！
======================================================================
       所有功能二進位內容完整性（Byte-by-byte Check）驗證全部通過！   
======================================================================
```

---

## 7. 編譯與建置指南

### 7.1 環境需求
* **作業系統**：Windows 10 / 11 (x64)
* **編譯工具**：Visual Studio 2022 (MSVC v143+) / C++20 標準支援
* **構建系統**：CMake >= 3.24

### 7.2 一鍵建置指令 (PowerShell / CMD)

```powershell
# 1. 進入專案根目錄
cd d:\BlitzFS

# 2. 透過 CMake 生成 Visual Studio 2022 x64 專案
cmake -B build -S . -G "Visual Studio 17 2022" -A x64

# 3. 編譯 Release 最佳化版本
cmake --build build --config Release

# 4. 執行全自動測試與基準效能驗證
.\build\bin\Release\BlitzFS.Core.Tests.exe
```

產出二進位檔案位置：
* **DLL 動態函式庫**：`d:\BlitzFS\build\bin\Release\BlitzFS.Core.dll`
* **LIB 導出符號庫**：`d:\BlitzFS\build\lib\Release\BlitzFS.Core.lib`
* **測試程式**：`d:\BlitzFS\build\bin\Release\BlitzFS.Core.Tests.exe`

---

## 8. 注意事項與常見問題

1. **系統管理員權限 (Administrator Rights)**：
   - 掃描 NTFS 磁區的 MFT / USN Journal（`FSCTL_ENUM_USN_DATA`）需要系統管理員權限。
   - 若在無管理員權限環境或 FAT32/exFAT 隨身碟上執行，引擎會自動無縫降級至多執行緒 `DirectoryScanner`，確保 100% 可用性。
2. **生命週期管理**：
   - 每次呼叫 `BlitzFS_CreateEngine()` 產生的指標，務必在程式結束時呼叫 `BlitzFS_DestroyEngine(handle)` 釋放記憶體。
3. **字串編碼**：
   - 所有傳入與輸出的路徑與檔名字串均使用 Windows 原生 **UTF-16 (`wchar_t`)**。
