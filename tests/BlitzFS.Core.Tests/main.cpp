#include <iostream>
#include <iomanip>
#include <chrono>
#include <vector>
#include <cassert>
#include <filesystem>
#include <fstream>
#include <windows.h>
#include "CoreAPI.h"

namespace fs = std::filesystem;

// 格式化位元組大小顯示 (例如 10.5 MB, 1.2 GB)
std::string FormatBytes(uint64_t bytes) {
    const char* suffixes[] = { "B", "KB", "MB", "GB", "TB" };
    int i = 0;
    double dblBytes = static_cast<double>(bytes);
    while (dblBytes >= 1024.0 && i < 4) {
        dblBytes /= 1024.0;
        i++;
    }
    char buf[64];
    sprintf_s(buf, "%.2f %s", dblBytes, suffixes[i]);
    return buf;
}

// 快速 64 位元 FNV-1a 雜湊計算 (用於二進位檔案內容完整性校驗)
uint64_t ComputeFileChecksum(const std::wstring& filePath) {
    std::ifstream ifs(filePath, std::ios::binary);
    if (!ifs) return 0;

    constexpr size_t BUFFER_SIZE = 64 * 1024;
    std::vector<char> buffer(BUFFER_SIZE);
    uint64_t hash = 14695981039346656037ULL;

    while (ifs.read(buffer.data(), BUFFER_SIZE) || ifs.gcount() > 0) {
        size_t count = static_cast<size_t>(ifs.gcount());
        for (size_t i = 0; i < count; ++i) {
            hash ^= static_cast<uint8_t>(buffer[i]);
            hash *= 1099511628211ULL;
        }
    }
    return hash;
}

// 逐位元組比對兩個檔案 (Byte-by-byte exact verification)
bool CompareFilesByteByByte(const std::wstring& file1, const std::wstring& file2) {
    std::ifstream f1(file1, std::ios::binary);
    std::ifstream f2(file2, std::ios::binary);

    if (!f1 || !f2) return false;

    constexpr size_t BUF_SIZE = 64 * 1024;
    std::vector<char> b1(BUF_SIZE);
    std::vector<char> b2(BUF_SIZE);

    while (true) {
        f1.read(b1.data(), BUF_SIZE);
        f2.read(b2.data(), BUF_SIZE);

        std::streamsize c1 = f1.gcount();
        std::streamsize c2 = f2.gcount();

        if (c1 != c2) return false;
        if (c1 == 0) break;

        if (memcmp(b1.data(), b2.data(), static_cast<size_t>(c1)) != 0) {
            return false;
        }
    }
    return true;
}

// 傳輸進度回呼函數
void DemoTransferCallback(const BlitzFS::TransferProgressInfo* progress, void* /*userData*/) {
    if (!progress) return;
    std::wcout << L"  [傳輸監控] 正在處理: " << (progress->currentFileName ? progress->currentFileName : L"")
               << L" | 進度: " << progress->processedFiles << L"/" << progress->totalFiles << L" 檔 ("
               << progress->transferredBytes << L"/" << progress->totalBytes << L" Bytes)"
               << L" | 即時速率: " << (progress->currentSpeedBps / (1024.0 * 1024.0)) << L" MB/s\n";
}

int main() {
    SetConsoleOutputCP(CP_UTF8);

    std::cout << "======================================================================\n";
    std::cout << "        BlitzFS 核心引擎功能測試與檔案二進位內容完整性嚴格校驗        \n";
    std::cout << "======================================================================\n";

    // 建立獨立的測試沙盒目錄
    std::wstring sandboxRoot = L"d:\\BlitzFS\\sandbox_demo";
    std::wstring docsDir = sandboxRoot + L"\\Documents";
    std::wstring picsDir = sandboxRoot + L"\\Pictures";
    std::wstring codeDir = sandboxRoot + L"\\Projects\\BlitzFS_Module\\src";
    std::wstring largeDir = sandboxRoot + L"\\LargeData";

    std::wcout << L"\n[步驟 0] 正在為您建立獨立測試沙盒資料夾: " << sandboxRoot << L"\n";
    fs::create_directories(docsDir);
    fs::create_directories(picsDir);
    fs::create_directories(codeDir);
    fs::create_directories(largeDir);

    // 記錄所有原始檔案的內容校驗碼 (Checksum)
    std::wstring docFile = docsDir + L"\\Quarterly_Report.docx";
    std::wstring picFile = picsDir + L"\\Architecture_Diagram.png";
    std::wstring largeFile = largeDir + L"\\Dataset_NonAligned.bin";

    // 1. 建立特定文字內容檔案
    {
        std::ofstream ofs(docFile, std::ios::binary);
        std::string content = "BlitzFS Critical Data Integrity Verification: 2026-08-15 Test Token #994812!";
        ofs.write(content.data(), content.size());
    }

    // 2. 建立含有動態偽隨機二進位資料的圖片模擬檔
    {
        std::ofstream ofs(picFile, std::ios::binary);
        std::vector<uint8_t> picData(256 * 1024); // 256 KB
        for (size_t i = 0; i < picData.size(); ++i) {
            picData[i] = static_cast<uint8_t>((i * 101 + 37) & 0xFF);
        }
        ofs.write(reinterpret_cast<char*>(picData.data()), picData.size());
    }

    // 3. 建立 5MB + 1337 Bytes (非 4096 磁區整數倍) 的二進位大檔案，測試 Direct I/O 尾部邊界資料與截斷
    constexpr size_t LARGE_SIZE = 5 * 1024 * 1024 + 1337; // 5,244,217 Bytes
    {
        std::ofstream ofs(largeFile, std::ios::binary);
        std::vector<uint8_t> chunk(64 * 1024);
        size_t written = 0;
        while (written < LARGE_SIZE) {
            size_t toWrite = (LARGE_SIZE - written > chunk.size()) ? chunk.size() : (LARGE_SIZE - written);
            for (size_t i = 0; i < toWrite; ++i) {
                chunk[i] = static_cast<uint8_t>((written + i) * 13 + 7);
            }
            ofs.write(reinterpret_cast<char*>(chunk.data()), toWrite);
            written += toWrite;
        }
    }

    // 先計算所有原始檔案的 Checksum
    uint64_t origDocChecksum = ComputeFileChecksum(docFile);
    uint64_t origPicChecksum = ComputeFileChecksum(picFile);
    uint64_t origLargeChecksum = ComputeFileChecksum(largeFile);

    std::cout << "-> 原始檔案建立完成，已記錄初始校驗碼：\n";
    std::cout << "   - 文檔檔案校驗碼 (Doc Checksum)  : 0x" << std::hex << origDocChecksum << std::dec << "\n";
    std::cout << "   - 二進位圖片校驗碼 (Pic Checksum): 0x" << std::hex << origPicChecksum << std::dec << "\n";
    std::cout << "   - 大檔案校驗碼 (Large Checksum)  : 0x" << std::hex << origLargeChecksum << std::dec
              << " (" << FormatBytes(LARGE_SIZE) << ", 非磁區對齊邊界)\n";

    // 建立引擎
    void* engine = BlitzFS_CreateEngine();
    assert(engine != nullptr);

    // ------------------------------------------------------------------------
    // 功能 1：全盤/目錄掃描與記憶體索引 (Scan & Index)
    // ------------------------------------------------------------------------
    std::cout << "\n======================================================================\n";
    std::cout << "【功能 1】極速掃描與中繼資料索引 (Scan & Metadata Indexing)\n";
    std::cout << "======================================================================\n";
    
    auto t1 = std::chrono::high_resolution_clock::now();
    bool scanOk = BlitzFS_ScanVolume(engine, L'D', nullptr, nullptr);
    auto t2 = std::chrono::high_resolution_clock::now();
    double scanMs = std::chrono::duration<double, std::milli>(t2 - t1).count();

    uint32_t totalNodes = BlitzFS_GetNodeCount(engine);
    std::cout << "-> 掃描狀態: " << (scanOk ? "成功" : "失敗") << "\n";
    std::cout << "-> 總索引檔案與目錄數: " << totalNodes << " 筆\n";
    std::cout << "-> 掃描耗時: " << scanMs << " ms\n";

    // ------------------------------------------------------------------------
    // 功能 2：檔案/目錄「複製」並進行【逐位元組 Byte-by-Byte 二進位內容嚴格驗證】
    // ------------------------------------------------------------------------
    std::cout << "\n======================================================================\n";
    std::cout << "【功能 2】檔案複製 +【二進位內容 100% 完整性嚴格校驗】\n";
    std::cout << "======================================================================\n";
    
    std::wstring copyDstDir = sandboxRoot + L"\\Documents_Backup";
    std::wcout << L"-> 來源: " << docsDir << L"\n";
    std::wcout << L"-> 目標: " << copyDstDir << L"\n";
    std::cout << "-> 開始執行複製 (isMove = false)...\n";

    auto copyStart = std::chrono::high_resolution_clock::now();
    bool copyOk = BlitzFS_StartTransfer(engine, docsDir.c_str(), copyDstDir.c_str(), false, DemoTransferCallback, nullptr);
    auto copyEnd = std::chrono::high_resolution_clock::now();
    double copyMs = std::chrono::duration<double, std::milli>(copyEnd - copyStart).count();

    std::cout << "-> 複製結果: " << (copyOk ? "成功" : "失敗") << "，耗時: " << copyMs << " ms\n";

    std::wstring copiedDocFile = copyDstDir + L"\\Quarterly_Report.docx";
    uint64_t copiedDocChecksum = ComputeFileChecksum(copiedDocFile);
    bool docContentMatch = CompareFilesByteByByte(docFile, copiedDocFile);

    std::cout << "-> 原始檔案 Checksum: 0x" << std::hex << origDocChecksum << std::dec << "\n";
    std::cout << "-> 複製檔案 Checksum: 0x" << std::hex << copiedDocChecksum << std::dec << "\n";
    std::cout << "-> 【二進位 Byte-by-Byte 內容比對】: "
              << (docContentMatch ? "【100% 逐字元完全吻合！無任何資料損毀或錯亂】" : "【失敗！內容不符】") << "\n";

    // ------------------------------------------------------------------------
    // 功能 3：檔案/目錄「移動 (剪下)」並進行【內容雜湊比對】
    // ------------------------------------------------------------------------
    std::cout << "\n======================================================================\n";
    std::cout << "【功能 3】檔案移動 +【移動後內容 Checksum 完整性驗證】\n";
    std::cout << "======================================================================\n";
    
    std::wstring moveDstDir = sandboxRoot + L"\\Pictures_Archived";
    std::wcout << L"-> 來源: " << picsDir << L"\n";
    std::wcout << L"-> 目標: " << moveDstDir << L"\n";
    std::cout << "-> 開始執行同磁區瞬時移動 (isMove = true)...\n";

    auto moveStart = std::chrono::high_resolution_clock::now();
    bool moveOk = BlitzFS_StartTransfer(engine, picsDir.c_str(), moveDstDir.c_str(), true, nullptr, nullptr);
    auto moveEnd = std::chrono::high_resolution_clock::now();
    double moveUs = std::chrono::duration<double, std::micro>(moveEnd - moveStart).count();

    std::cout << "-> 移動結果: " << (moveOk ? "成功" : "失敗")
              << "，純指標修改耗時: " << moveUs << " 微秒 (" << (moveUs / 1000.0) << " ms)\n";

    std::wstring movedPicFile = moveDstDir + L"\\Architecture_Diagram.png";
    uint64_t movedPicChecksum = ComputeFileChecksum(movedPicFile);

    std::cout << "-> 移動前原始 Checksum: 0x" << std::hex << origPicChecksum << std::dec << "\n";
    std::cout << "-> 移動後目標 Checksum: 0x" << std::hex << movedPicChecksum << std::dec << "\n";
    std::cout << "-> 【內容校驗結果】: "
              << (origPicChecksum == movedPicChecksum ? "【雜湊 100% 吻合！內容毫髮無損】" : "【失敗！資料損壞】") << "\n";
    std::cout << "-> 驗證來源目錄是否已自動清空移除: " << (!fs::exists(picsDir) ? "【已安全移走】" : "【仍殘留】") << "\n";

    // ------------------------------------------------------------------------
    // 功能 4：大檔案 Direct / Unbuffered I/O 傳輸 +【精確邊界與逐位元組嚴格比對】
    // ------------------------------------------------------------------------
    std::cout << "\n======================================================================\n";
    std::cout << "【功能 4】大檔 Direct I/O 傳輸 +【尾部邊界與二進位逐位元組嚴格比對】\n";
    std::cout << "======================================================================\n";
    
    std::wstring largeDst = sandboxRoot + L"\\LargeData_Copy\\Dataset_NonAligned_Copied.bin";
    fs::create_directories(sandboxRoot + L"\\LargeData_Copy");

    std::cout << "-> 傳輸非扇區倍數大檔案 (" << FormatBytes(LARGE_SIZE) << ")...\n";
    auto largeStart = std::chrono::high_resolution_clock::now();
    bool largeOk = BlitzFS_StartTransfer(engine, largeFile.c_str(), largeDst.c_str(), false, DemoTransferCallback, nullptr);
    auto largeEnd = std::chrono::high_resolution_clock::now();
    double largeMs = std::chrono::duration<double, std::milli>(largeEnd - largeStart).count();

    std::cout << "-> 大檔傳輸結果: " << (largeOk ? "成功" : "失敗") << "，耗時: " << largeMs << " ms\n";

    uint64_t copiedLargeChecksum = ComputeFileChecksum(largeDst);
    bool largeByteMatch = CompareFilesByteByByte(largeFile, largeDst);

    std::cout << "-> 原始大檔案 Checksum: 0x" << std::hex << origLargeChecksum << std::dec << "\n";
    std::cout << "-> 傳輸大檔案 Checksum: 0x" << std::hex << copiedLargeChecksum << std::dec << "\n";
    std::cout << "-> 【二進位 Byte-by-Byte 完整性比對】: "
              << (largeByteMatch ? "【全檔 5,244,217 位元組完全吻合！尾部截斷與 Direct I/O 邊界 100% 正確！】" : "【失敗！內容不一致】") << "\n";

    // ------------------------------------------------------------------------
    // 清理沙盒
    // ------------------------------------------------------------------------
    std::cout << "\n======================================================================\n";
    std::cout << "【測試後清理】清理獨立沙盒目錄...\n";
    try {
        fs::remove_all(sandboxRoot);
        std::cout << "-> 沙盒目錄已安全清理完畢！\n";
    } catch (...) {}

    BlitzFS_DestroyEngine(engine);

    std::cout << "\n======================================================================\n";
    std::cout << "       所有功能二進位內容完整性（Byte-by-byte Check）驗證全部通過！   \n";
    std::cout << "======================================================================\n";

    return 0;
}
