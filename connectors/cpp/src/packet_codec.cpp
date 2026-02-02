#include "packet_codec.hpp"
#include "endian_utils.hpp"
#include "playhouse/types.hpp"
#include <cstring>
#include <stdexcept>

namespace playhouse {
namespace internal {

Bytes PacketCodec::EncodeRequest(const Packet& packet) {
    Bytes buffer;
    const auto& msg_id = packet.GetMsgId();
    const auto& payload = packet.GetPayload();

    if (msg_id.length() > protocol::MAX_MSG_ID_LENGTH) {
        throw std::invalid_argument("Message ID too long");
    }

    // Calculate content size: MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload(...)
    uint32_t content_size = 1 + static_cast<uint32_t>(msg_id.length()) + 2 + 8 + static_cast<uint32_t>(payload.size());

    // Reserve space for better performance
    buffer.reserve(4 + content_size);

    // Write ContentSize (4 bytes, little-endian)
    WriteUInt32LE(buffer, content_size);

    // Write MsgIdLen (1 byte)
    WriteUInt8(buffer, static_cast<uint8_t>(msg_id.length()));

    // Write MsgId (N bytes, UTF-8)
    buffer.insert(buffer.end(), msg_id.begin(), msg_id.end());

    // Write MsgSeq (2 bytes, little-endian)
    WriteUInt16LE(buffer, packet.GetMsgSeq());

    // Write StageId (8 bytes, little-endian)
    WriteInt64LE(buffer, packet.GetStageId());

    // Write Payload
    buffer.insert(buffer.end(), payload.begin(), payload.end());

    return buffer;
}

Packet PacketCodec::DecodeResponse(const uint8_t* data, size_t size) {
    if (size < protocol::MIN_HEADER_SIZE) {
        throw std::runtime_error("Packet too small");
    }

    size_t offset = 4;  // Skip ContentSize (already read)

    // Read MsgIdLen (1 byte)
    uint8_t msg_id_len = data[offset++];

    if (msg_id_len == 0 || msg_id_len > protocol::MAX_MSG_ID_LENGTH) {
        throw std::runtime_error("Invalid message ID length");
    }

    // Read MsgId (N bytes)
    if (offset + msg_id_len > size) {
        throw std::runtime_error("Incomplete message ID");
    }
    std::string msg_id(reinterpret_cast<const char*>(data + offset), msg_id_len);
    offset += msg_id_len;

    // Read MsgSeq (2 bytes)
    if (offset + 2 > size) {
        throw std::runtime_error("Incomplete MsgSeq");
    }
    uint16_t msg_seq = ReadUInt16LE(data + offset);
    offset += 2;

    // Read StageId (8 bytes)
    if (offset + 8 > size) {
        throw std::runtime_error("Incomplete StageId");
    }
    int64_t stage_id = ReadInt64LE(data + offset);
    offset += 8;

    // Read ErrorCode (2 bytes)
    if (offset + 2 > size) {
        throw std::runtime_error("Incomplete ErrorCode");
    }
    int16_t error_code = ReadInt16LE(data + offset);
    offset += 2;

    // Read OriginalSize (4 bytes)
    if (offset + 4 > size) {
        throw std::runtime_error("Incomplete OriginalSize");
    }
    uint32_t original_size = ReadUInt32LE(data + offset);
    offset += 4;

    // Read Payload (remaining bytes)
    Bytes payload;
    if (offset < size) {
        payload.assign(data + offset, data + size);
    }

    // Create packet
    Packet packet(std::move(msg_id), std::move(payload));
    packet.SetMsgSeq(msg_seq);
    packet.SetStageId(stage_id);
    packet.SetErrorCode(error_code);
    packet.SetOriginalSize(original_size);

    return packet;
}

} // namespace internal
} // namespace playhouse
