#include "RingBuffer.h"

namespace BlitzFS {

RingBuffer::RingBuffer(size_t slotCount, size_t slotSize)
    : m_slotCount(slotCount)
    , m_slotSize(slotSize)
{
    m_rawBuffers.resize(slotCount);
    m_slots.resize(slotCount);

    for (size_t i = 0; i < slotCount; ++i) {
        // 使用 VirtualAlloc 確保記憶體頁與磁區邊界對齊 (4KB/512B)
        m_rawBuffers[i] = reinterpret_cast<BYTE*>(
            VirtualAlloc(nullptr, slotSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE)
        );
        m_slots[i].data = m_rawBuffers[i];
        m_slots[i].validBytes = 0;
        m_slots[i].isEof = false;
    }
}

RingBuffer::~RingBuffer() {
    for (size_t i = 0; i < m_slotCount; ++i) {
        if (m_rawBuffers[i]) {
            VirtualFree(m_rawBuffers[i], 0, MEM_RELEASE);
            m_rawBuffers[i] = nullptr;
        }
    }
}

RingBuffer::Slot* RingBuffer::AcquireWriteSlot() {
    std::unique_lock<std::mutex> lock(m_mutex);
    m_cvWrite.wait(lock, [this]() {
        return m_occupiedCount < m_slotCount;
    });

    return &m_slots[m_writeIndex];
}

void RingBuffer::CommitWriteSlot(size_t validBytes, bool isEof) {
    std::unique_lock<std::mutex> lock(m_mutex);
    m_slots[m_writeIndex].validBytes = validBytes;
    m_slots[m_writeIndex].isEof = isEof;

    m_writeIndex = (m_writeIndex + 1) % m_slotCount;
    m_occupiedCount++;

    m_cvRead.notify_one();
}

RingBuffer::Slot* RingBuffer::AcquireReadSlot() {
    std::unique_lock<std::mutex> lock(m_mutex);
    m_cvRead.wait(lock, [this]() {
        return m_occupiedCount > 0;
    });

    return &m_slots[m_readIndex];
}

void RingBuffer::ReleaseReadSlot() {
    std::unique_lock<std::mutex> lock(m_mutex);
    m_readIndex = (m_readIndex + 1) % m_slotCount;
    m_occupiedCount--;

    m_cvWrite.notify_one();
}

void RingBuffer::Reset() {
    std::unique_lock<std::mutex> lock(m_mutex);
    m_writeIndex = 0;
    m_readIndex = 0;
    m_occupiedCount = 0;
    for (auto& slot : m_slots) {
        slot.validBytes = 0;
        slot.isEof = false;
    }
}

} // namespace BlitzFS
