#ifndef PLAYHOUSE_CLIENT_NETWORK_HPP
#define PLAYHOUSE_CLIENT_NETWORK_HPP

#include "ring_buffer.hpp"
#include "connection.hpp"
#include "playhouse/config.hpp"
#include "playhouse/packet.hpp"

#include <functional>
#include <future>
#include <memory>
#include <queue>
#include <string>

// ASIO headers - support both standalone and Boost.Asio
#if defined(ASIO_STANDALONE) || !defined(BOOST_ASIO_HPP)
    #include <asio/awaitable.hpp>
#else
    #include <boost/asio/awaitable.hpp>
    namespace asio = boost::asio;
#endif

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

    /// Connect to server using C++20 coroutine
    asio::awaitable<bool> Connect(const std::string& host, uint16_t port);

    /// Disconnect from server
    void Disconnect();

    /// Check connection status
    bool IsConnected() const;

    /// Check authentication status
    bool IsAuthenticated() const;

    /// Send a packet (no response expected)
    void Send(Packet packet, int64_t stage_id);

    /// Send a request using C++20 coroutine
    asio::awaitable<Packet> Request(Packet packet, int64_t stage_id);

    /// Send a request with callback (callback overload)
    void Request(Packet packet, std::function<void(Packet)> callback, int64_t stage_id);

    /// Authenticate using C++20 coroutine
    asio::awaitable<Packet> Authenticate(Packet packet, int64_t stage_id);

    /// Authenticate with callback (callback overload)
    void Authenticate(Packet packet, std::function<void(Packet)> callback, int64_t stage_id);

    // Legacy std::future-based APIs (deprecated but kept for compatibility)
    /// @deprecated Use Connect() coroutine version instead
    std::future<bool> ConnectAsync(const std::string& host, uint16_t port);

    /// @deprecated Use Request() coroutine version instead
    std::future<Packet> RequestAsync(Packet packet, int64_t stage_id);

    /// @deprecated Use Authenticate() coroutine version instead
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
