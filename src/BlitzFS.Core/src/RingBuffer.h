#pragma once
#include <windows.h>
#include <cstdint>
#include <vector>
#include <mutex>
#include <condition_variable>

namespace BlitzFS {

/**
 * @brief 磁區對齊之環形記憶體緩衝區 (用於 Unbuffered Direct I/O)
 */
class RingBuffer {
public:
    RingBuffer(size_t slotCount, size_t slotSize);
    ~RingBuffer();

    // 禁止複製
    RingBuffer(const RingBuffer&) = delete;
    RingBuffer& operator=(const RingBuffer&) = delete;

    // 取得用於讀取寫入的對齊記憶體 Slot
    struct Slot {
        BYTE* data;
        size_t validBytes;
        bool isEof;
    };

    // 生產者取得可寫入的 Slot
    Slot* AcquireWriteSlot();
    // 生產者提交已填入資料的 Slot
    void CommitWriteSlot(size_t validBytes, bool isEof);

    // 消費者取得可讀取的 Slot
    Slot* AcquireReadSlot();
    // 消費者釋放已讀取的 Slot
    void ReleaseReadSlot();

    void Reset();

private:
    size_t m_slotCount;
    size_t m_slotSize;
    std::vector<BYTE*> m_rawBuffers;
    std::vector<Slot> m_slots;

    size_t m_writeIndex{0};
    size_t m_readIndex{0};
    size_t m_occupiedCount{0};

    std::mutex m_mutex;
    std::condition_variable m_cvWrite;
    std::condition_variable m_cvRead;
};

} // namespace BlitzFS
