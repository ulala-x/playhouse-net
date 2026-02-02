#ifndef PLAYHOUSE_CONNECTOR_HPP
#define PLAYHOUSE_CONNECTOR_HPP

#include "config.hpp"
#include "packet.hpp"
#include "types.hpp"

#include <functional>
#include <future>
#include <memory>
#include <string>

// ASIO headers - support both standalone and Boost.Asio
#if defined(ASIO_STANDALONE) || !defined(BOOST_ASIO_HPP)
    #include <asio/awaitable.hpp>
#else
    #include <boost/asio/awaitable.hpp>
    namespace asio = boost::asio;
#endif

namespace playhouse {

/// Main connector class for PlayHouse client
/// Provides asynchronous networking with callback-based or future-based APIs
class Connector {
public:
    /// Constructor
    Connector();

    /// Destructor
    ~Connector();

    // Delete copy operations
    Connector(const Connector&) = delete;
    Connector& operator=(const Connector&) = delete;

    /// Initialize the connector with configuration
    void Init(const ConnectorConfig& config);

    /// Connect to the server using C++20 coroutine
    /// @param host Server hostname or IP address
    /// @param port Server port number
    /// @return Awaitable that resolves to true on success
    asio::awaitable<bool> Connect(const std::string& host, uint16_t port);

    /// Disconnect from the server
    void Disconnect();

    /// Check if connected to the server
    bool IsConnected() const;

    /// Send a message without expecting a response (fire-and-forget)
    /// @param packet Packet to send (will be moved)
    void Send(Packet packet);

    /// Send a request and receive response using C++20 coroutine
    /// @param packet Request packet (will be moved)
    /// @return Awaitable that resolves to the response packet
    asio::awaitable<Packet> Request(Packet packet);

    /// Send a request with callback for response (callback overload)
    /// @param packet Request packet (will be moved)
    /// @param callback Callback function to handle response
    void Request(Packet packet, std::function<void(Packet)> callback);

    /// Authenticate with the server using C++20 coroutine
    /// @param authPacket Authentication packet
    /// @return Awaitable that resolves to true on success
    asio::awaitable<bool> Authenticate(Packet authPacket);

    /// Authenticate with callback (callback overload)
    /// @param authPacket Authentication packet
    /// @param callback Callback function to handle authentication result
    void Authenticate(Packet authPacket, std::function<void(bool)> callback);

    // Legacy std::future-based APIs (deprecated but kept for compatibility)
    /// @deprecated Use Connect() coroutine version instead
    std::future<bool> ConnectAsync(const std::string& host, uint16_t port);

    /// @deprecated Use Request() coroutine version instead
    std::future<Packet> RequestAsync(Packet packet);

    /// @deprecated Use Authenticate() coroutine version instead
    std::future<bool> AuthenticateAsync(
        const std::string& service_id,
        const std::string& account_id,
        const Bytes& payload);

    /// Process callbacks on the main thread
    /// Call this regularly from your main loop (e.g., game thread)
    void MainThreadAction();

    /// Callback fired when connection is established
    std::function<void()> OnConnect;

    /// Callback fired when a packet is received
    std::function<void(Packet)> OnReceive;

    /// Callback fired when an error occurs
    std::function<void(int code, std::string message)> OnError;

    /// Callback fired when disconnected from server
    std::function<void()> OnDisconnect;

private:
    class Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace playhouse

#endif // PLAYHOUSE_CONNECTOR_HPP
