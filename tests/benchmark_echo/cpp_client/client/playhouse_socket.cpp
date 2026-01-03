#include "playhouse_socket.h"
#include <iostream>
#include <cstring>
#include <chrono>

namespace playhouse {

// Static member initialization
bool PlayHouseSocket::s_enable_relay_echo = false;

PlayHouseSocket::PlayHouseSocket()
    : asio::Nsocket_tcp()
    , m_stage_id(0)
    , m_msg_seq(0)
    , m_authenticated(false)
{
    // Reserve initial buffer capacity
    m_recv_buffer.reserve(8192);
}

PlayHouseSocket::~PlayHouseSocket()
{
    // Base class handles cleanup
}

// ============================================================================
// Socket Event Handlers
// ============================================================================

void PlayHouseSocket::on_connect()
{
#ifdef DEBUG_LOGGING
    std::cout << "[PlayHouseSocket] on_connect() called" << std::endl;
#endif

    // Reset state
    m_authenticated = false;
    m_recv_buffer.clear();
    m_last_echo_content.clear();

    // Invoke user callback
    if (on_connect_callback) {
        on_connect_callback();
    }
}

void PlayHouseSocket::on_disconnect() noexcept
{
    // Invoke user callback
    if (on_disconnect_callback) {
        on_disconnect_callback();
    }
}

int PlayHouseSocket::on_message(const const_buffer& msg)
{
    // CGDK10 asio socket already handles length-prefix framing
    // msg contains: [Length: 4B][Message Body: N bytes]
    // We need to parse the PlayHouse protocol from the message body

    const uint8_t* data = static_cast<const uint8_t*>(msg.data());
    size_t size = msg.size();

    // Validate minimum size (Length field)
    if (size < 4) {
        std::cerr << "PlayHouseSocket::on_message - Invalid message size: " << size << std::endl;
        return 0;
    }

    // Read length prefix (little-endian)
    uint32_t body_length = Codec::read_int32_le(data);

    // Validate that we have the complete message
    if (size < 4 + body_length) {
        std::cerr << "PlayHouseSocket::on_message - Incomplete message: expected "
                  << (4 + body_length) << " bytes, got " << size << std::endl;
        return 0;
    }

    // Skip the length prefix and process the message body
    const uint8_t* body_data = data + 4;
    handle_packet(body_data, body_length);

    // Return total bytes consumed
    return static_cast<int>(4 + body_length);
}

// ============================================================================
// Packet Handling
// ============================================================================

void PlayHouseSocket::handle_packet(const uint8_t* data, size_t size)
{
    // Decode the response packet
    std::string msg_id;
    uint16_t msg_seq;
    int64_t stage_id;
    uint16_t error_code;
    std::vector<uint8_t> payload;

    if (!Codec::decode_response(data, size, msg_id, msg_seq, stage_id, error_code, payload)) {
        std::cerr << "[PlayHouseSocket] Failed to decode response (size: " << size << ")" << std::endl;
        return;
    }

#ifdef DEBUG_LOGGING
    std::cout << "[PlayHouseSocket] Received message: " << msg_id
              << ", seq=" << msg_seq
              << ", stage_id=" << stage_id
              << ", error_code=" << error_code
              << ", payload_size=" << payload.size() << std::endl;
#endif

    // Handle authentication response
    if (msg_id == "AuthenticateReply") {
        m_authenticated = true;
#ifdef DEBUG_LOGGING
        std::cout << "[PlayHouseSocket] Authentication successful" << std::endl;
#endif
    }

    // Relay echo mode: auto-respond to EchoReply
    if (s_enable_relay_echo && msg_id == "EchoReply") {
        // Immediately send another EchoRequest with the same content
        if (!m_last_echo_content.empty()) {
            send_echo_request(m_last_echo_content, get_current_timestamp_ms());
        }
    }

    // Invoke user callback
    if (on_message_callback) {
        on_message_callback(msg_id, msg_seq, stage_id, error_code, payload);
    }
}

// ============================================================================
// Authentication
// ============================================================================

bool PlayHouseSocket::send_authenticate(const std::string& client_version)
{
#ifdef DEBUG_LOGGING
    std::cout << "[PlayHouseSocket] Sending authentication request (version: "
              << client_version << ")" << std::endl;
#endif

    // For now, we'll create the protobuf message manually as serialized bytes
    // In a real implementation, you would use the generated protobuf code
    // AuthenticateRequest { client_version = 1; }

    // Simple protobuf encoding for string field 1
    // Tag: (field_number << 3) | wire_type
    // field_number = 1, wire_type = 2 (length-delimited)
    // Tag = (1 << 3) | 2 = 10 (0x0A)

    std::vector<uint8_t> payload;
    payload.push_back(0x0A);  // Tag: field 1, wire type 2 (length-delimited)
    payload.push_back(static_cast<uint8_t>(client_version.size()));  // Length
    payload.insert(payload.end(), client_version.begin(), client_version.end());  // String data

    bool result = send_message("AuthenticateRequest", payload.data(), payload.size());
#ifdef DEBUG_LOGGING
    std::cout << "[PlayHouseSocket] Authentication request sent: " << (result ? "success" : "failed") << std::endl;
#endif
    return result;
}

// ============================================================================
// Message Sending
// ============================================================================

bool PlayHouseSocket::send_echo_request(const std::string& content, int64_t client_timestamp)
{
    // Save content for relay echo mode
    m_last_echo_content = content;

    // Encode EchoRequest protobuf message manually
    // message EchoRequest {
    //     string content = 1;
    //     int64 client_timestamp = 2;
    // }

    std::vector<uint8_t> payload;

    // Field 1: content (string, wire type 2)
    payload.push_back(0x0A);  // Tag: (1 << 3) | 2
    payload.push_back(static_cast<uint8_t>(content.size()));
    payload.insert(payload.end(), content.begin(), content.end());

    // Field 2: client_timestamp (int64, wire type 0)
    payload.push_back(0x10);  // Tag: (2 << 3) | 0

    // Encode varint (simple case for positive numbers)
    uint64_t ts = static_cast<uint64_t>(client_timestamp);
    while (ts >= 0x80) {
        payload.push_back(static_cast<uint8_t>((ts & 0x7F) | 0x80));
        ts >>= 7;
    }
    payload.push_back(static_cast<uint8_t>(ts));

    return send_message("EchoRequest", payload.data(), payload.size());
}

bool PlayHouseSocket::send_message(const std::string& msg_id,
                                   const uint8_t* payload,
                                   size_t payload_size)
{
    // Encode the request packet
    uint16_t seq = next_msg_seq();
    std::vector<uint8_t> encoded = Codec::encode_request(
        msg_id,
        seq,
        m_stage_id,
        payload,
        payload_size
    );

    // Send via CGDK10 socket
    const_buffer buffer(encoded.data(), encoded.size());
    return this->send(buffer);
}

bool PlayHouseSocket::send_message(const std::string& msg_id,
                                   const std::vector<uint8_t>& payload)
{
    return send_message(msg_id, payload.data(), payload.size());
}

// ============================================================================
// Utility Functions
// ============================================================================

int64_t PlayHouseSocket::get_current_timestamp_ms()
{
    using namespace std::chrono;
    auto now = system_clock::now();
    auto duration = now.time_since_epoch();
    return duration_cast<milliseconds>(duration).count();
}

} // namespace playhouse
