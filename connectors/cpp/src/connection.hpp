#ifndef PLAYHOUSE_CONNECTION_HPP
#define PLAYHOUSE_CONNECTION_HPP

#include <cstddef>
#include <cstdint>
#include <functional>
#include <future>
#include <string>

namespace playhouse {
namespace internal {

/// Common connection interface for different transports (TCP / WebSocket)
class IConnection {
public:
    virtual ~IConnection() = default;

    virtual std::future<bool> ConnectAsync(const std::string& host, uint16_t port) = 0;
    virtual void Disconnect() = 0;
    virtual bool IsConnected() const = 0;
    virtual void Send(const uint8_t* data, size_t size) = 0;
    virtual void SetReceiveCallback(std::function<void(const uint8_t*, size_t)> callback) = 0;
    virtual void SetDisconnectCallback(std::function<void()> callback) = 0;
};

} // namespace internal
} // namespace playhouse

#endif // PLAYHOUSE_CONNECTION_HPP
