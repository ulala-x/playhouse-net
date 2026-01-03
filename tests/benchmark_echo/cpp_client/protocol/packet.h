#pragma once

#include "playhouse_codec.h"
#include <memory>
#include <string>
#include <vector>
#include <cstdint>

namespace playhouse {

/**
 * Packet wrapper class for convenient request/response handling.
 * Provides high-level API for creating and parsing PlayHouse protocol packets.
 */
class Packet {
public:
    /**
     * Creates a request packet from a Protobuf message.
     *
     * @param msg_id Message ID (e.g., "EchoRequest")
     * @param msg_seq Sequence number for request/response matching
     * @param stage_id Target stage ID (0 for session-level messages)
     * @param payload Serialized Protobuf message
     * @param payload_size Payload size in bytes
     */
    Packet(const std::string& msg_id, uint16_t msg_seq, int64_t stage_id,
           const uint8_t* payload, size_t payload_size)
        : msg_id_(msg_id)
        , msg_seq_(msg_seq)
        , stage_id_(stage_id)
        , error_code_(0)
    {
        // Encode as request packet
        encoded_data_ = Codec::encode_request(msg_id, msg_seq, stage_id, payload, payload_size);
    }

    /**
     * Creates a request packet from a Protobuf message (vector overload).
     */
    Packet(const std::string& msg_id, uint16_t msg_seq, int64_t stage_id,
           const std::vector<uint8_t>& payload)
        : Packet(msg_id, msg_seq, stage_id, payload.data(), payload.size())
    {
    }

    /**
     * Creates an empty request packet (no payload).
     */
    Packet(const std::string& msg_id, uint16_t msg_seq, int64_t stage_id)
        : Packet(msg_id, msg_seq, stage_id, nullptr, 0)
    {
    }

    /**
     * Parses a response packet from raw data (without length prefix).
     *
     * @param data Response packet data
     * @param size Data size
     * @return Parsed packet, or nullptr on parse error
     */
    static std::unique_ptr<Packet> parse_response(const uint8_t* data, size_t size) {
        std::string msg_id;
        uint16_t msg_seq;
        int64_t stage_id;
        uint16_t error_code;
        std::vector<uint8_t> payload;

        if (!Codec::decode_response(data, size, msg_id, msg_seq, stage_id, error_code, payload)) {
            return nullptr;
        }

        auto packet = std::unique_ptr<Packet>(new Packet());
        packet->msg_id_ = std::move(msg_id);
        packet->msg_seq_ = msg_seq;
        packet->stage_id_ = stage_id;
        packet->error_code_ = error_code;
        packet->payload_ = std::move(payload);

        return packet;
    }

    // Getters
    const std::string& msg_id() const { return msg_id_; }
    uint16_t msg_seq() const { return msg_seq_; }
    int64_t stage_id() const { return stage_id_; }
    uint16_t error_code() const { return error_code_; }
    const std::vector<uint8_t>& payload() const { return payload_; }

    /**
     * Returns the encoded packet data ready to send (for requests).
     * Includes the length prefix.
     */
    const std::vector<uint8_t>& encoded_data() const { return encoded_data_; }

    /**
     * Returns pointer to encoded data (for sending).
     */
    const uint8_t* data() const { return encoded_data_.data(); }

    /**
     * Returns encoded packet size.
     */
    size_t size() const { return encoded_data_.size(); }

    /**
     * Checks if the packet represents a successful response.
     */
    bool is_success() const { return error_code_ == 0; }

    /**
     * Checks if this is an error response.
     */
    bool has_error() const { return error_code_ != 0; }

private:
    // Private default constructor for parse_response
    Packet() = default;

    std::string msg_id_;
    uint16_t msg_seq_;
    int64_t stage_id_;
    uint16_t error_code_;
    std::vector<uint8_t> payload_;
    std::vector<uint8_t> encoded_data_;  // For request packets
};

} // namespace playhouse
