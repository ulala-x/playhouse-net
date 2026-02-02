#ifndef PLAYHOUSE_CLIENT_NETWORK_HPP
#define PLAYHOUSE_CLIENT_NETWORK_HPP

#include "ring_buffer.hpp"
#include "tcp_connection.hpp"
#include "playhouse/config.hpp"
#include "playhouse/packet.hpp"

#include <functional>
#include <future>
#include <memory>
#include <queue>
#include <string>

namespace playhouse {
namespace internal {

/// Client network layer handling packet transmission and reception
class ClientNetwork {
public:
    explicit ClientNetwork(const ConnectorConfig& config);
    ~ClientNetwork();

    // Delete copy operations
    ClientNetwork(const ClientNetwork&) = delete;
    ClientNetwork& operator=(const ClientNetwork&) = delete;

    /// Connect to server
    std::future<bool> ConnectAsync(const std::string& host, uint16_t port);

    /// Disconnect from server
    void Disconnect();

    /// Check connection status
    bool IsConnected() const;

    /// Check authentication status
    bool IsAuthenticated() const;

    /// Send a packet (no response expected)
    void Send(Packet packet, int64_t stage_id);

    /// Send a request with callback
    void Request(Packet packet, std::function<void(Packet)> callback, int64_t stage_id);

    /// Send a request asynchronously
    std::future<Packet> RequestAsync(Packet packet, int64_t stage_id);

    /// Authenticate with callback
    void Authenticate(Packet packet, std::function<void(Packet)> callback, int64_t stage_id);

    /// Authenticate asynchronously
    std::future<Packet> AuthenticateAsync(Packet packet, int64_t stage_id);

    /// Process callbacks on main thread
    void MainThreadAction();

    /// Set callbacks
    void SetOnConnect(std::function<void(bool)> callback);
    void SetOnReceive(std::function<void(Packet)> callback);
    void SetOnError(std::function<void(int, std::string)> callback);
    void SetOnDisconnect(std::function<void()> callback);

private:
    class Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace internal
} // namespace playhouse

#endif // PLAYHOUSE_CLIENT_NETWORK_HPP
