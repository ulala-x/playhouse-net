#pragma once

#include "../asio/asio.h"
#include "../protocol/playhouse_codec.h"
#include "../protocol/packet.h"
#include <functional>
#include <atomic>
#include <vector>
#include <string>
#include <cstdint>
#include <chrono>

namespace playhouse {

/**
 * PlayHouse protocol socket implementation.
 * Extends CGDK10 asio::Nsocket_tcp to handle PlayHouse message framing and protocol.
 *
 * Features:
 * - Length-prefix based framing (4-byte little-endian)
 * - Automatic packet parsing and dispatching
 * - Request/Response tracking with sequence numbers
 * - Authentication support
 * - Relay echo mode for benchmarking
 */
class PlayHouseSocket : public asio::Nsocket_tcp {
public:
    PlayHouseSocket();
    virtual ~PlayHouseSocket();

    // ========================================================================
    // Callbacks
    // ========================================================================

    /// Called when connection is established
    std::function<void()> on_connect_callback;

    /// Called when connection is closed
    std::function<void()> on_disconnect_callback;

    /// Called when a message is received
    /// @param msg_id Message ID (e.g., "EchoReply")
    /// @param msg_seq Sequence number
    /// @param stage_id Stage ID
    /// @param error_code Error code (0 = success)
    /// @param payload Message payload
    std::function<void(const std::string& msg_id,
                       uint16_t msg_seq,
                       int64_t stage_id,
                       uint16_t error_code,
                       const std::vector<uint8_t>& payload)> on_message_callback;

    // ========================================================================
    // Stage Management
    // ========================================================================

    /// Set the target stage ID for messages
    void set_stage_id(int64_t stage_id) { m_stage_id = stage_id; }

    /// Get the current stage ID
    int64_t get_stage_id() const { return m_stage_id; }

    // ========================================================================
    // Authentication
    // ========================================================================

    /// Send authentication request
    /// @param client_version Client version string
    /// @return true if sent successfully
    bool send_authenticate(const std::string& client_version = "1.0.0");

    /// Check if authenticated
    bool is_authenticated() const { return m_authenticated; }

    // ========================================================================
    // Message Sending
    // ========================================================================

    /// Send echo request
    /// @param content Echo content
    /// @param client_timestamp Client timestamp for RTT measurement
    /// @return true if sent successfully
    bool send_echo_request(const std::string& content, int64_t client_timestamp);

    /// Send a generic message with protobuf payload
    /// @param msg_id Message ID
    /// @param payload Serialized protobuf message
    /// @param payload_size Payload size in bytes
    /// @return true if sent successfully
    bool send_message(const std::string& msg_id,
                     const uint8_t* payload,
                     size_t payload_size);

    /// Send a generic message with protobuf payload (vector overload)
    bool send_message(const std::string& msg_id,
                     const std::vector<uint8_t>& payload);

    // ========================================================================
    // Relay Echo Mode (for benchmarking)
    // ========================================================================

    /// Enable/disable relay echo mode (static for all sockets)
    /// When enabled, immediately re-send EchoRequest upon receiving EchoReply
    static bool s_enable_relay_echo;

    /// Get current timestamp in milliseconds
    static int64_t get_current_timestamp_ms();

protected:
    // ========================================================================
    // CGDK10 Socket Overrides
    // ========================================================================

    virtual void on_connect() override;
    virtual void on_disconnect() noexcept override;
    virtual int on_message(const const_buffer& msg) override;

    // ========================================================================
    // State Management (accessible to derived classes)
    // ========================================================================

    int64_t m_stage_id;                          ///< Target stage ID
    std::atomic<uint16_t> m_msg_seq;             ///< Message sequence counter
    bool m_authenticated;                         ///< Authentication status

    // Relay Echo state
    std::string m_last_echo_content;             ///< Last echo content for relay

    /// Generate next message sequence number
    uint16_t next_msg_seq() { return m_msg_seq.fetch_add(1); }

private:
    // ========================================================================
    // Packet Framing
    // ========================================================================

    std::vector<uint8_t> m_recv_buffer;          ///< Receive buffer for incomplete packets

    /// Parse PlayHouse packets from received data
    /// @param data Received data
    /// @param size Data size
    /// @return Number of bytes consumed
    int parse_playhouse_packets(const uint8_t* data, size_t size);

    /// Handle a single PlayHouse packet
    /// @param data Packet data (without length prefix)
    /// @param size Packet size
    void handle_packet(const uint8_t* data, size_t size);
};

} // namespace playhouse
