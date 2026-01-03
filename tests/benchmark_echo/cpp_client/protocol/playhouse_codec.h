#pragma once

#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

namespace playhouse {

/**
 * PlayHouse protocol encoder/decoder for client-server communication.
 *
 * Request Packet Format (Client -> Server):
 *   [Length: 4B LE] - Message body size (excluding this 4-byte field)
 *   [MsgIdLen: 1B] - UTF-8 byte length of MsgId
 *   [MsgId: N bytes] - UTF-8 string
 *   [MsgSeq: 2B LE] - Sequence number
 *   [StageId: 8B LE] - Stage ID
 *   [Payload: variable] - Protobuf serialized data
 *
 * Response Packet Format (Server -> Client):
 *   [Length: 4B LE] - Message body size (excluding this 4-byte field)
 *   [MsgIdLen: 1B]
 *   [MsgId: N bytes]
 *   [MsgSeq: 2B LE]
 *   [StageId: 8B LE]
 *   [ErrorCode: 2B LE] - 0 = success
 *   [OriginalSize: 4B LE] - 0 = uncompressed
 *   [Payload: variable]
 */
class Codec {
public:
    /// Minimum message size: MsgIdLen(1) + MsgSeq(2) + StageId(8) = 11 bytes
    static constexpr size_t MIN_MESSAGE_SIZE = 11;

    /// Response header size after MsgId: MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) = 16 bytes
    static constexpr size_t RESPONSE_HEADER_SIZE = 16;

    /// Request header size after MsgId: MsgSeq(2) + StageId(8) = 10 bytes
    static constexpr size_t REQUEST_HEADER_SIZE = 10;

    /**
     * Encodes a request packet with length prefix.
     * Returns complete packet: [Length(4)][MsgIdLen(1)][MsgId(N)][MsgSeq(2)][StageId(8)][Payload]
     *
     * @param msg_id Message ID (UTF-8 string)
     * @param msg_seq Sequence number
     * @param stage_id Stage ID
     * @param payload Protobuf serialized payload
     * @param payload_size Payload size in bytes
     * @return Complete encoded packet ready to send
     */
    static std::vector<uint8_t> encode_request(
        const std::string& msg_id,
        uint16_t msg_seq,
        int64_t stage_id,
        const uint8_t* payload,
        size_t payload_size);

    /**
     * Decodes a response packet (data after the length prefix).
     * Input: [MsgIdLen(1)][MsgId(N)][MsgSeq(2)][StageId(8)][ErrorCode(2)][OriginalSize(4)][Payload]
     *
     * @param data Packet data (without length prefix)
     * @param size Data size
     * @param[out] msg_id Message ID
     * @param[out] msg_seq Sequence number
     * @param[out] stage_id Stage ID
     * @param[out] error_code Error code (0 = success)
     * @param[out] payload Payload data (copy of the data)
     * @return true on success, false on parse error
     */
    static bool decode_response(
        const uint8_t* data,
        size_t size,
        std::string& msg_id,
        uint16_t& msg_seq,
        int64_t& stage_id,
        uint16_t& error_code,
        std::vector<uint8_t>& payload);

    /**
     * Calculates the total request packet size including length prefix.
     *
     * @param msg_id_len Length of message ID in bytes
     * @param payload_size Payload size in bytes
     * @return Total packet size: Length(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload
     */
    static size_t request_packet_size(size_t msg_id_len, size_t payload_size);

    /**
     * Calculates the request header size (excluding payload).
     *
     * @param msg_id_len Length of message ID in bytes
     * @return Header size: MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8)
     */
    static size_t request_header_size(size_t msg_id_len);

    // Little-endian helper functions (public for testing)
    static void write_uint16_le(uint8_t* buf, uint16_t value);
    static void write_int32_le(uint8_t* buf, int32_t value);
    static void write_int64_le(uint8_t* buf, int64_t value);

    static uint16_t read_uint16_le(const uint8_t* buf);
    static int32_t read_int32_le(const uint8_t* buf);
    static int64_t read_int64_le(const uint8_t* buf);
};

} // namespace playhouse
