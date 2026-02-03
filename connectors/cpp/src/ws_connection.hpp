#ifndef PLAYHOUSE_WS_CONNECTION_HPP
#define PLAYHOUSE_WS_CONNECTION_HPP

#include "connection.hpp"

#include <memory>
#include <mutex>
#include <string>
#include <thread>

namespace playhouse {
namespace internal {

/// WebSocket connection using Boost.Beast
class WsConnection : public IConnection {
public:
    explicit WsConnection(std::string websocket_path);
    ~WsConnection();

    // Delete copy operations
    WsConnection(const WsConnection&) = delete;
    WsConnection& operator=(const WsConnection&) = delete;

    std::future<bool> ConnectAsync(const std::string& host, uint16_t port) override;
    void Disconnect() override;
    bool IsConnected() const override;
    void Send(const uint8_t* data, size_t size) override;
    void SetReceiveCallback(std::function<void(const uint8_t*, size_t)> callback) override;
    void SetDisconnectCallback(std::function<void()> callback) override;

private:
    class Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace internal
} // namespace playhouse

#endif // PLAYHOUSE_WS_CONNECTION_HPP
