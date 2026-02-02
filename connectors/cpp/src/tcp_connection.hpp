#ifndef PLAYHOUSE_TCP_CONNECTION_HPP
#define PLAYHOUSE_TCP_CONNECTION_HPP

#include <cstddef>
#include <cstdint>
#include <functional>
#include <future>
#include <memory>
#include <string>
#include <thread>
#include <mutex>

namespace playhouse {
namespace internal {

/// TCP connection using asio
class TcpConnection {
public:
    TcpConnection();
    ~TcpConnection();

    // Delete copy operations
    TcpConnection(const TcpConnection&) = delete;
    TcpConnection& operator=(const TcpConnection&) = delete;

    /// Connect to server asynchronously
    std::future<bool> ConnectAsync(const std::string& host, uint16_t port);

    /// Disconnect from server
    void Disconnect();

    /// Check if connected
    bool IsConnected() const;

    /// Send data
    void Send(const uint8_t* data, size_t size);

    /// Set callback for received data
    void SetReceiveCallback(std::function<void(const uint8_t*, size_t)> callback);

    /// Set callback for disconnection
    void SetDisconnectCallback(std::function<void()> callback);

private:
    class Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace internal
} // namespace playhouse

#endif // PLAYHOUSE_TCP_CONNECTION_HPP
