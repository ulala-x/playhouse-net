#ifndef PLAYHOUSE_PACKET_HPP
#define PLAYHOUSE_PACKET_HPP

#include "types.hpp"
#include <memory>
#include <string>
#include <vector>

namespace playhouse {

/// Packet class representing a PlayHouse message
/// Uses Pimpl pattern to hide implementation details
class Packet {
public:
    /// Construct a packet with message ID and payload
    Packet(std::string msg_id, Bytes payload);

    /// Move constructor
    Packet(Packet&& other) noexcept;

    /// Move assignment operator
    Packet& operator=(Packet&& other) noexcept;

    /// Destructor
    ~Packet();

    // Delete copy operations
    Packet(const Packet&) = delete;
    Packet& operator=(const Packet&) = delete;

    /// Get message ID
    const std::string& GetMsgId() const;

    /// Get message sequence number (0 = push, >0 = request/response)
    uint16_t GetMsgSeq() const;

    /// Get stage ID
    int64_t GetStageId() const;

    /// Get error code (0 = success)
    int16_t GetErrorCode() const;

    /// Get payload data
    const Bytes& GetPayload() const;

    /// Get original size (>0 indicates compressed payload)
    uint32_t GetOriginalSize() const;

    /// Set message sequence number (internal use)
    void SetMsgSeq(uint16_t msg_seq);

    /// Set stage ID (internal use)
    void SetStageId(int64_t stage_id);

    /// Set error code (internal use)
    void SetErrorCode(int16_t error_code);

    /// Set original size (internal use)
    void SetOriginalSize(uint32_t original_size);

    /// Create an empty packet with just a message ID
    static Packet Empty(const std::string& msg_id);

    /// Create a packet from raw bytes
    static Packet FromBytes(const std::string& msg_id, Bytes bytes);

private:
    class Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace playhouse

#endif // PLAYHOUSE_PACKET_HPP
