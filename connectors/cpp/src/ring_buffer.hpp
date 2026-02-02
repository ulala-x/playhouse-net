#ifndef PLAYHOUSE_RING_BUFFER_HPP
#define PLAYHOUSE_RING_BUFFER_HPP

#include <cstddef>
#include <cstdint>
#include <vector>

namespace playhouse {
namespace internal {

/// Ring buffer for efficient data buffering
/// Thread-safe when used with external synchronization
class RingBuffer {
public:
    /// Construct a ring buffer with specified capacity
    explicit RingBuffer(size_t capacity);

    /// Get current data count
    size_t GetCount() const;

    /// Get buffer capacity
    size_t GetCapacity() const;

    /// Get available free space
    size_t GetFreeSpace() const;

    /// Write data to the buffer
    void Write(const uint8_t* data, size_t size);

    /// Read data from the buffer (consumes data)
    void Read(uint8_t* dest, size_t size);

    /// Peek at data without consuming
    void Peek(uint8_t* dest, size_t size, size_t offset = 0) const;

    /// Peek a single byte at offset
    uint8_t PeekByte(size_t offset) const;

    /// Consume data without reading
    void Consume(size_t size);

    /// Clear all data
    void Clear();

    /// Get read pointer (for zero-copy read operations)
    const uint8_t* GetReadPtr() const;

    /// Get contiguous readable size
    size_t GetContiguousReadSize() const;

    /// Get write pointer (for zero-copy write operations)
    uint8_t* GetWritePtr();

    /// Get contiguous writable size
    size_t GetContiguousWriteSize() const;

    /// Advance write position after zero-copy write
    void Advance(size_t size);

private:
    std::vector<uint8_t> buffer_;
    size_t capacity_;
    size_t head_;  // Write position
    size_t tail_;  // Read position
    size_t count_; // Current data size
};

} // namespace internal
} // namespace playhouse

#endif // PLAYHOUSE_RING_BUFFER_HPP
