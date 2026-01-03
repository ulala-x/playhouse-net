#pragma once

#include "playhouse_socket.h"
#include "../asio/asio.connector.h"
#include <thread>
#include <atomic>
#include <chrono>
#include <memory>
#include <string>
#include <vector>
#include <mutex>

namespace playhouse {

// Forward declaration
class EchoClient;

// ========================================================================
// Message Type Configuration
// ========================================================================

/**
 * Message type information for traffic testing.
 * Each type has a specific size and pre-allocation count.
 */
struct MessageTypeInfo {
    size_t size;      ///< Message size in bytes
    size_t count;     ///< Number of pre-generated messages
};

/**
 * Message types for traffic testing (aligned with CGDK10).
 * Index 0-6 are used for normal testing.
 */
constexpr MessageTypeInfo MESSAGE_TYPES[] = {
    {8, 2 * 1024 * 1024},      // 0: 8B
    {64, 2 * 256 * 1024},      // 1: 64B
    {256, 2 * 64 * 1024},      // 2: 256B
    {1024, 2 * 16 * 1024},     // 3: 1KB
    {4 * 1024, 2 * 2 * 1024},  // 4: 4KB
    {16 * 1024, 2 * 1024},     // 5: 16KB
    {64 * 1024, 2 * 256},      // 6: 64KB
};

constexpr size_t MESSAGE_TYPE_COUNT = sizeof(MESSAGE_TYPES) / sizeof(MESSAGE_TYPES[0]);

// ========================================================================
// Echo Socket (Extends PlayHouseSocket with client-specific logic)
// ========================================================================

/**
 * Custom socket implementation for the echo client.
 * Overrides PlayHouseSocket's virtual methods to integrate with EchoClient.
 */
class EchoSocket : public PlayHouseSocket {
public:
    EchoSocket() : PlayHouseSocket(), m_client(nullptr) {}

    void set_client(EchoClient* client) { m_client = client; }

protected:
    virtual void on_connect() override;
    virtual void on_disconnect() noexcept override;
    virtual int on_message(const const_buffer& msg) override;

private:
    EchoClient* m_client;
};

// ========================================================================
// Echo Client
// ========================================================================

/**
 * PlayHouse Echo Benchmark Client
 *
 * Features:
 * - Connection test mode: Maintains connections within a specified range
 * - Traffic test mode: Continuously sends echo messages
 * - Relay echo mode: Immediately re-sends on receiving echo reply
 * - Multi-threaded: Background processing thread for auto tests
 * - Statistics: Real-time tracking of messages, connections, throughput
 *
 * Based on CGDK10 test_tcp_echo_client architecture.
 */
class EchoClient {
public:
    EchoClient();
    ~EchoClient();

    // ========================================================================
    // Server Configuration
    // ========================================================================

    /// Set server endpoint
    void set_endpoint(const std::string& host, int port);

    /// Get current endpoint
    std::string get_host() const { return m_host; }
    int get_port() const { return m_port; }

    /// Set base stage ID (used for assigning stage IDs to sockets)
    void set_base_stage_id(int64_t base_id) { m_base_stage_id = base_id; }

    // ========================================================================
    // Connection Test Mode
    // ========================================================================

    /// Toggle connection test mode
    void toggle_connect_test();

    /// Set connection range (min, max)
    void set_connect_range(int64_t min, int64_t max);

    /// Adjust connection range
    void add_connect_min(int64_t delta);
    void sub_connect_min(int64_t delta);
    void add_connect_max(int64_t delta);
    void sub_connect_max(int64_t delta);

    // ========================================================================
    // Traffic Test Mode
    // ========================================================================

    /// Toggle traffic test mode
    void toggle_traffic_test();

    /// Set message size index (0-6)
    void set_message_size_index(size_t index);

    /// Increase/decrease message size
    void increase_message_size();
    void decrease_message_size();

    /// Set times (number of messages to send per socket)
    void set_times(int64_t times);

    /// Adjust times
    void add_times(int64_t delta);
    void sub_times(int64_t delta);

    // ========================================================================
    // Relay Echo Mode
    // ========================================================================

    /// Toggle relay echo mode (static for all sockets)
    void toggle_relay_echo();

    // ========================================================================
    // Connection Management
    // ========================================================================

    /// Request new connections
    void request_connect(int64_t count);

    /// Request disconnections
    void request_disconnect(int64_t count);

    /// Disconnect all connections
    void request_disconnect_all();

    // ========================================================================
    // Message Sending
    // ========================================================================

    /// Request immediate send (bypasses traffic test mode)
    void request_send_immediately(int64_t count);

    // ========================================================================
    // Lifecycle
    // ========================================================================

    /// Start the client
    void start();

    /// Stop the client
    void stop();

    // ========================================================================
    // Statistics
    // ========================================================================

    /// Get current connection count
    int64_t get_connection_count() const;

    /// Get total message counts
    int64_t get_send_count() const;
    int64_t get_recv_count() const;

    /// Get message rates (messages/second)
    double get_send_rate() const;
    double get_recv_rate() const;

    /// Get byte rates (bytes/second)
    double get_send_bytes_rate() const;
    double get_recv_bytes_rate() const;

    // ========================================================================
    // State Query
    // ========================================================================

    bool is_connect_test_enabled() const { return m_enable_connect_test; }
    bool is_traffic_test_enabled() const { return m_enable_traffic_test; }
    bool is_relay_echo_enabled() const { return PlayHouseSocket::s_enable_relay_echo; }
    size_t get_message_size_index() const { return m_message_size_index; }
    int64_t get_times() const { return m_times; }
    int64_t get_connect_min() const { return m_connect_min; }
    int64_t get_connect_max() const { return m_connect_max; }

private:
    // ========================================================================
    // Configuration
    // ========================================================================

    std::string m_host;
    int m_port;
    int64_t m_base_stage_id;

    // ========================================================================
    // Test Modes
    // ========================================================================

    // Connection test
    std::atomic<bool> m_enable_connect_test{false};
    int64_t m_connect_min;
    int64_t m_connect_max;

    // Traffic test
    std::atomic<bool> m_enable_traffic_test{false};
    size_t m_message_size_index;
    int64_t m_times;

    // ========================================================================
    // Connector
    // ========================================================================

    std::shared_ptr<asio::connector<EchoSocket>> m_connector;
    std::atomic<int64_t> m_next_stage_id{0};

    // ========================================================================
    // Statistics
    // ========================================================================

    std::atomic<int64_t> m_send_count{0};
    std::atomic<int64_t> m_recv_count{0};
    std::atomic<int64_t> m_send_bytes{0};
    std::atomic<int64_t> m_recv_bytes{0};

    // Rate calculation
    mutable std::mutex m_stats_mutex;
    int64_t m_last_send_count;
    int64_t m_last_recv_count;
    int64_t m_last_send_bytes;
    int64_t m_last_recv_bytes;
    std::chrono::steady_clock::time_point m_last_stats_time;

    // ========================================================================
    // Processing Thread
    // ========================================================================

    std::atomic<bool> m_running{false};
    std::unique_ptr<std::thread> m_process_thread;

    // ========================================================================
    // Internal Methods
    // ========================================================================

    /// Background processing loop
    void process_loop();

    /// Process connection test logic
    void process_connect_test();

    /// Process traffic test logic
    void process_traffic_test();

    /// Generate echo content with random data
    void generate_echo_content(size_t size, std::string& out);

    /// Send messages to all connected sockets
    void send_to_all_sockets(size_t message_size, int64_t count);

    // ========================================================================
    // Socket Event Handlers (called by EchoSocket)
    // ========================================================================

    /// Called when a socket connects
    void on_socket_connect(EchoSocket* socket);

    /// Called when a socket disconnects
    void on_socket_disconnect(EchoSocket* socket);

    /// Called when a socket receives a message
    void on_socket_message(EchoSocket* socket,
                          const std::string& msg_id,
                          uint16_t msg_seq,
                          int64_t stage_id,
                          uint16_t error_code,
                          const std::vector<uint8_t>& payload);

    // EchoSocket needs access to event handlers
    friend class EchoSocket;
};

} // namespace playhouse
