#pragma once

#include <cstdint>
#include <cstring>
#include <vector>

namespace playhouse {
namespace internal {

// Little-endian conversion utilities
// These functions provide portable, explicit endianness conversion
// for network protocol handling without relying on platform-specific macros

inline uint16_t ReadUInt16LE(const uint8_t* data) {
    return static_cast<uint16_t>(data[0]) |
           (static_cast<uint16_t>(data[1]) << 8);
}

inline uint32_t ReadUInt32LE(const uint8_t* data) {
    return static_cast<uint32_t>(data[0]) |
           (static_cast<uint32_t>(data[1]) << 8) |
           (static_cast<uint32_t>(data[2]) << 16) |
           (static_cast<uint32_t>(data[3]) << 24);
}

inline int16_t ReadInt16LE(const uint8_t* data) {
    uint16_t uvalue = ReadUInt16LE(data);
    return static_cast<int16_t>(uvalue);
}

inline int64_t ReadInt64LE(const uint8_t* data) {
    uint64_t value = 0;
    for (int i = 0; i < 8; ++i) {
        value |= static_cast<uint64_t>(data[i]) << (i * 8);
    }
    return static_cast<int64_t>(value);
}

inline void WriteUInt16LE(uint8_t* data, uint16_t value) {
    data[0] = static_cast<uint8_t>(value & 0xFF);
    data[1] = static_cast<uint8_t>((value >> 8) & 0xFF);
}

inline void WriteUInt32LE(uint8_t* data, uint32_t value) {
    data[0] = static_cast<uint8_t>(value & 0xFF);
    data[1] = static_cast<uint8_t>((value >> 8) & 0xFF);
    data[2] = static_cast<uint8_t>((value >> 16) & 0xFF);
    data[3] = static_cast<uint8_t>((value >> 24) & 0xFF);
}

inline void WriteInt64LE(uint8_t* data, int64_t value) {
    uint64_t uvalue = static_cast<uint64_t>(value);
    for (int i = 0; i < 8; ++i) {
        data[i] = static_cast<uint8_t>((uvalue >> (i * 8)) & 0xFF);
    }
}

// Variants that push to byte vector for convenience
inline void WriteUInt8(std::vector<uint8_t>& buffer, uint8_t value) {
    buffer.push_back(value);
}

inline void WriteUInt16LE(std::vector<uint8_t>& buffer, uint16_t value) {
    buffer.push_back(static_cast<uint8_t>(value & 0xFF));
    buffer.push_back(static_cast<uint8_t>((value >> 8) & 0xFF));
}

inline void WriteUInt32LE(std::vector<uint8_t>& buffer, uint32_t value) {
    buffer.push_back(static_cast<uint8_t>(value & 0xFF));
    buffer.push_back(static_cast<uint8_t>((value >> 8) & 0xFF));
    buffer.push_back(static_cast<uint8_t>((value >> 16) & 0xFF));
    buffer.push_back(static_cast<uint8_t>((value >> 24) & 0xFF));
}

inline void WriteInt64LE(std::vector<uint8_t>& buffer, int64_t value) {
    uint64_t uvalue = static_cast<uint64_t>(value);
    buffer.push_back(static_cast<uint8_t>(uvalue & 0xFF));
    buffer.push_back(static_cast<uint8_t>((uvalue >> 8) & 0xFF));
    buffer.push_back(static_cast<uint8_t>((uvalue >> 16) & 0xFF));
    buffer.push_back(static_cast<uint8_t>((uvalue >> 24) & 0xFF));
    buffer.push_back(static_cast<uint8_t>((uvalue >> 32) & 0xFF));
    buffer.push_back(static_cast<uint8_t>((uvalue >> 40) & 0xFF));
    buffer.push_back(static_cast<uint8_t>((uvalue >> 48) & 0xFF));
    buffer.push_back(static_cast<uint8_t>((uvalue >> 56) & 0xFF));
}

} // namespace internal
} // namespace playhouse
