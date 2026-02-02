#include "ring_buffer.hpp"
#include <algorithm>
#include <cstring>
#include <stdexcept>

namespace playhouse {
namespace internal {

RingBuffer::RingBuffer(size_t capacity)
    : buffer_(capacity)
    , capacity_(capacity)
    , head_(0)
    , tail_(0)
    , count_(0)
{}

size_t RingBuffer::GetCount() const {
    return count_;
}

size_t RingBuffer::GetCapacity() const {
    return capacity_;
}

size_t RingBuffer::GetFreeSpace() const {
    return capacity_ - count_;
}

void RingBuffer::Write(const uint8_t* data, size_t size) {
    if (size > GetFreeSpace()) {
        throw std::runtime_error("Not enough space in ring buffer");
    }

    if (size == 0) {
        return;
    }

    // Calculate how much we can write contiguously
    size_t contiguous_space = capacity_ - head_;
    size_t first_chunk = std::min(size, contiguous_space);

    // Write first chunk
    std::memcpy(buffer_.data() + head_, data, first_chunk);
    head_ = (head_ + first_chunk) % capacity_;
    count_ += first_chunk;

    // Write second chunk if needed (wrap around)
    if (first_chunk < size) {
        size_t second_chunk = size - first_chunk;
        std::memcpy(buffer_.data() + head_, data + first_chunk, second_chunk);
        head_ = (head_ + second_chunk) % capacity_;
        count_ += second_chunk;
    }
}

void RingBuffer::Read(uint8_t* dest, size_t size) {
    if (size > count_) {
        throw std::runtime_error("Not enough data in ring buffer");
    }

    if (size == 0) {
        return;
    }

    // Calculate how much we can read contiguously
    size_t contiguous_data = capacity_ - tail_;
    size_t first_chunk = std::min(size, contiguous_data);

    // Read first chunk
    std::memcpy(dest, buffer_.data() + tail_, first_chunk);
    tail_ = (tail_ + first_chunk) % capacity_;
    count_ -= first_chunk;

    // Read second chunk if needed (wrap around)
    if (first_chunk < size) {
        size_t second_chunk = size - first_chunk;
        std::memcpy(dest + first_chunk, buffer_.data() + tail_, second_chunk);
        tail_ = (tail_ + second_chunk) % capacity_;
        count_ -= second_chunk;
    }
}

void RingBuffer::Peek(uint8_t* dest, size_t size, size_t offset) const {
    if (offset + size > count_) {
        throw std::runtime_error("Not enough data in ring buffer to peek");
    }

    if (size == 0) {
        return;
    }

    // Calculate read position
    size_t read_pos = (tail_ + offset) % capacity_;

    // Calculate how much we can read contiguously
    size_t contiguous_data = capacity_ - read_pos;
    size_t first_chunk = std::min(size, contiguous_data);

    // Read first chunk
    std::memcpy(dest, buffer_.data() + read_pos, first_chunk);

    // Read second chunk if needed (wrap around)
    if (first_chunk < size) {
        size_t second_chunk = size - first_chunk;
        std::memcpy(dest + first_chunk, buffer_.data(), second_chunk);
    }
}

uint8_t RingBuffer::PeekByte(size_t offset) const {
    if (offset >= count_) {
        throw std::runtime_error("Offset out of range");
    }

    size_t pos = (tail_ + offset) % capacity_;
    return buffer_[pos];
}

void RingBuffer::Consume(size_t size) {
    if (size > count_) {
        throw std::runtime_error("Cannot consume more data than available");
    }

    tail_ = (tail_ + size) % capacity_;
    count_ -= size;
}

void RingBuffer::Clear() {
    head_ = 0;
    tail_ = 0;
    count_ = 0;
}

const uint8_t* RingBuffer::GetReadPtr() const {
    return buffer_.data() + tail_;
}

size_t RingBuffer::GetContiguousReadSize() const {
    if (count_ == 0) {
        return 0;
    }

    // When buffer is full (count_ == capacity_), head_ == tail_
    // In this case, data wraps around and we return from tail to end
    if (count_ == capacity_) {
        return capacity_ - tail_;
    }

    if (tail_ < head_) {
        // Data is contiguous
        return head_ - tail_;
    } else {
        // Data wraps around
        return capacity_ - tail_;
    }
}

uint8_t* RingBuffer::GetWritePtr() {
    return buffer_.data() + head_;
}

size_t RingBuffer::GetContiguousWriteSize() const {
    size_t free_space = GetFreeSpace();
    if (free_space == 0) {
        return 0;
    }

    if (head_ >= tail_ || count_ == 0) {
        // Can write from head to end or to tail
        size_t to_end = capacity_ - head_;
        if (count_ == 0) {
            return to_end;
        }
        return std::min(to_end, free_space);
    } else {
        // Can write from head to tail
        return tail_ - head_;
    }
}

void RingBuffer::Advance(size_t size) {
    if (size > GetFreeSpace()) {
        throw std::runtime_error("Cannot advance more than free space");
    }

    head_ = (head_ + size) % capacity_;
    count_ += size;
}

} // namespace internal
} // namespace playhouse
