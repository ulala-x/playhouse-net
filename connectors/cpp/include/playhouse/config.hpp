#ifndef PLAYHOUSE_CONFIG_HPP
#define PLAYHOUSE_CONFIG_HPP

#include <cstdint>
#include <string>

namespace playhouse {

/// Configuration for the Connector
struct ConnectorConfig {
    /// Send buffer size in bytes (default: 64KB)
    uint32_t send_buffer_size = 65536;

    /// Receive buffer size in bytes (default: 256KB)
    uint32_t receive_buffer_size = 262144;

    /// Heartbeat interval in milliseconds (default: 10s)
    uint32_t heartbeat_interval_ms = 10000;

    /// Request timeout in milliseconds (default: 30s)
    uint32_t request_timeout_ms = 30000;

    /// Enable automatic reconnection (default: false)
    bool enable_reconnect = false;

    /// Reconnect interval in milliseconds (default: 5s)
    uint32_t reconnect_interval_ms = 5000;

    /// Maximum reconnection attempts (0 = unlimited)
    uint32_t max_reconnect_attempts = 0;

    /// Use WebSocket transport instead of TCP (default: false)
    bool use_websocket = false;

    /// Use SSL/TLS encryption (TCP uses TLS, WebSocket uses WSS)
    bool use_ssl = false;

    /// Skip server certificate validation (self-signed test certs)
    bool skip_server_certificate_validation = false;

    /// WebSocket path for handshake (default: "/ws")
    std::string websocket_path = "/ws";
};

} // namespace playhouse

#endif // PLAYHOUSE_CONFIG_HPP
