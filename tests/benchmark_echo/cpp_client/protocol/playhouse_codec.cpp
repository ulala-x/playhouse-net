#include "playhouse_codec.h"
#include <cstring>
#include <stdexcept>

namespace playhouse {

// ============================================================================
// Little-Endian Write Functions
// ============================================================================

void Codec::write_uint16_le(uint8_t* buf, uint16_t value) {
    buf[0] = static_cast<uint8_t>(value & 0xFF);
    buf[1] = static_cast<uint8_t>((value >> 8) & 0xFF);
}

void Codec::write_int32_le(uint8_t* buf, int32_t value) {
    buf[0] = static_cast<uint8_t>(value & 0xFF);
    buf[1] = static_cast<uint8_t>((value >> 8) & 0xFF);
    buf[2] = static_cast<uint8_t>((value >> 16) & 0xFF);
    buf[3] = static_cast<uint8_t>((value >> 24) & 0xFF);
}

void Codec::write_int64_le(uint8_t* buf, int64_t value) {
    buf[0] = static_cast<uint8_t>(value & 0xFF);
    buf[1] = static_cast<uint8_t>((value >> 8) & 0xFF);
    buf[2] = static_cast<uint8_t>((value >> 16) & 0xFF);
    buf[3] = static_cast<uint8_t>((value >> 24) & 0xFF);
    buf[4] = static_cast<uint8_t>((value >> 32) & 0xFF);
    buf[5] = static_cast<uint8_t>((value >> 40) & 0xFF);
    buf[6] = static_cast<uint8_t>((value >> 48) & 0xFF);
    buf[7] = static_cast<uint8_t>((value >> 56) & 0xFF);
}

// ============================================================================
// Little-Endian Read Functions
// ============================================================================

uint16_t Codec::read_uint16_le(const uint8_t* buf) {
    return static_cast<uint16_t>(buf[0]) |
           (static_cast<uint16_t>(buf[1]) << 8);
}

int32_t Codec::read_int32_le(const uint8_t* buf) {
    return static_cast<int32_t>(buf[0]) |
           (static_cast<int32_t>(buf[1]) << 8) |
           (static_cast<int32_t>(buf[2]) << 16) |
           (static_cast<int32_t>(buf[3]) << 24);
}

int64_t Codec::read_int64_le(const uint8_t* buf) {
    return static_cast<int64_t>(buf[0]) |
           (static_cast<int64_t>(buf[1]) << 8) |
           (static_cast<int64_t>(buf[2]) << 16) |
           (static_cast<int64_t>(buf[3]) << 24) |
           (static_cast<int64_t>(buf[4]) << 32) |
           (static_cast<int64_t>(buf[5]) << 40) |
           (static_cast<int64_t>(buf[6]) << 48) |
           (static_cast<int64_t>(buf[7]) << 56);
}

// ============================================================================
// Size Calculation Functions
// ============================================================================

size_t Codec::request_packet_size(size_t msg_id_len, size_t payload_size) {
    // Length(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload
    return 4 + 1 + msg_id_len + 2 + 8 + payload_size;
}

size_t Codec::request_header_size(size_t msg_id_len) {
    // MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8)
    return 1 + msg_id_len + 2 + 8;
}

// ============================================================================
// Request Encoding
// ============================================================================

std::vector<uint8_t> Codec::encode_request(
    const std::string& msg_id,
    uint16_t msg_seq,
    int64_t stage_id,
    const uint8_t* payload,
    size_t payload_size)
{
    if (msg_id.empty()) {
        throw std::invalid_argument("msg_id cannot be empty");
    }

    if (msg_id.size() > 255) {
        throw std::invalid_argument("msg_id too long (max 255 bytes)");
    }

    const size_t msg_id_len = msg_id.size();

    // Calculate body size (excluding Length field)
    // Body: MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload
    const int32_t body_size = static_cast<int32_t>(1 + msg_id_len + 2 + 8 + payload_size);

    // Total packet size: Length(4) + Body
    const size_t total_size = 4 + body_size;

    std::vector<uint8_t> packet(total_size);
    size_t offset = 0;

    // 1. Write Length (4 bytes, little-endian)
    write_int32_le(&packet[offset], body_size);
    offset += 4;

    // 2. Write MsgIdLen (1 byte)
    packet[offset++] = static_cast<uint8_t>(msg_id_len);

    // 3. Write MsgId (N bytes)
    std::memcpy(&packet[offset], msg_id.data(), msg_id_len);
    offset += msg_id_len;

    // 4. Write MsgSeq (2 bytes, little-endian)
    write_uint16_le(&packet[offset], msg_seq);
    offset += 2;

    // 5. Write StageId (8 bytes, little-endian)
    write_int64_le(&packet[offset], stage_id);
    offset += 8;

    // 6. Write Payload
    if (payload_size > 0 && payload != nullptr) {
        std::memcpy(&packet[offset], payload, payload_size);
        offset += payload_size;
    }

    return packet;
}

// ============================================================================
// Response Decoding
// ============================================================================

bool Codec::decode_response(
    const uint8_t* data,
    size_t size,
    std::string& msg_id,
    uint16_t& msg_seq,
    int64_t& stage_id,
    uint16_t& error_code,
    std::vector<uint8_t>& payload)
{
    // Clear output parameters
    msg_id.clear();
    msg_seq = 0;
    stage_id = 0;
    error_code = 0;
    payload.clear();

    // Minimum size check: MsgIdLen(1) + at least 1 char + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4)
    if (size < MIN_MESSAGE_SIZE + RESPONSE_HEADER_SIZE - REQUEST_HEADER_SIZE) {
        return false;
    }

    size_t offset = 0;

    // 1. Read MsgIdLen (1 byte)
    const uint8_t msg_id_len = data[offset++];

    // 2. Validate we have enough data
    // Need: msg_id_len + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) = msg_id_len + 16
    if (offset + msg_id_len + RESPONSE_HEADER_SIZE > size) {
        return false;
    }

    // 3. Read MsgId (N bytes, UTF-8)
    msg_id.assign(reinterpret_cast<const char*>(&data[offset]), msg_id_len);
    offset += msg_id_len;

    // 4. Read MsgSeq (2 bytes, little-endian)
    msg_seq = read_uint16_le(&data[offset]);
    offset += 2;

    // 5. Read StageId (8 bytes, little-endian)
    stage_id = read_int64_le(&data[offset]);
    offset += 8;

    // 6. Read ErrorCode (2 bytes, little-endian)
    error_code = read_uint16_le(&data[offset]);
    offset += 2;

    // 7. Read OriginalSize (4 bytes, little-endian)
    int32_t original_size = read_int32_le(&data[offset]);
    offset += 4;

    // 8. Read Payload (remaining bytes)
    const size_t payload_size = size - offset;
    if (payload_size > 0) {
        payload.resize(payload_size);
        std::memcpy(payload.data(), &data[offset], payload_size);
    }

    // Note: Decompression (if original_size > 0) is not implemented in this basic version
    // Clients can handle decompression separately if needed
    if (original_size > 0) {
        // TODO: Implement LZ4 decompression if needed
        // For now, we just store the compressed payload
    }

    return true;
}

} // namespace playhouse
