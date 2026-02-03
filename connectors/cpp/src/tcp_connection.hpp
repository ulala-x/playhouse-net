#ifndef PLAYHOUSE_TCP_CONNECTION_HPP
#define PLAYHOUSE_TCP_CONNECTION_HPP

#include "connection.hpp"

#include <memory>
#include <mutex>
#include <thread>

namespace playhouse {
namespace internal {

/// TCP connection using asio
class TcpConnection : public IConnection {
public:
    explicit TcpConnection(bool use_ssl = false, bool skip_server_certificate_validation = false);
    ~TcpConnection();

    // Delete copy operations
    TcpConnection(const TcpConnection&) = delete;
    TcpConnection& operator=(const TcpConnection&) = delete;

    /// Connect to server asynchronously
    std::future<bool> ConnectAsync(const std::string& host, uint16_t port) override;

    /// Disconnect from server
    void Disconnect() override;

    /// Check if connected
    bool IsConnected() const override;

    /// Send data
    void Send(const uint8_t* data, size_t size) override;

    /// Set callback for received data
    void SetReceiveCallback(std::function<void(const uint8_t*, size_t)> callback) override;

    /// Set callback for disconnection
    void SetDisconnectCallback(std::function<void()> callback) override;

private:
    class Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace internal
} // namespace playhouse

#endif // PLAYHOUSE_TCP_CONNECTION_HPP
