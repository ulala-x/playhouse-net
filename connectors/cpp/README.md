# PlayHouse C++ Connector

C++ Connector for PlayHouse real-time game server framework.

## Overview

- **Purpose**: C++ server E2E testing
- **Status**: Initial Implementation Complete
- **C++ Standard**: C++17
- **E2E Guide**: `doc/docker-e2e.md`

## Directory Structure

```
connectors/cpp/
├── include/playhouse/
│   ├── connector.hpp           # Main API
│   ├── packet.hpp              # Packet abstraction
│   ├── config.hpp              # Configuration
│   └── types.hpp               # Common types
├── src/
│   ├── connector.cpp
│   ├── client_network.cpp      # Networking core
│   ├── tcp_connection.cpp      # TCP transport
│   ├── packet.cpp
│   └── ring_buffer.cpp
├── tests/
│   └── connector_test.cpp      # Unit tests
├── CMakeLists.txt              # Native build
└── vcpkg.json                  # vcpkg package definition
```

## Technology Stack

| Component | Technology |
|-----------|------------|
| Language | C++17 |
| Build System | CMake 3.20+ |
| Networking | asio (standalone, no Boost) |
| Testing | Google Test + Google Mock |
| Package | vcpkg, Conan |

## API Design

```cpp
namespace playhouse {

struct ConnectorConfig {
    uint32_t send_buffer_size = 65536;      // 64KB
    uint32_t receive_buffer_size = 262144;  // 256KB
    uint32_t heartbeat_interval_ms = 10000; // 10s
    uint32_t request_timeout_ms = 30000;    // 30s
    bool enable_reconnect = false;
    uint32_t reconnect_interval_ms = 5000;
};

class Connector {
public:
    // Lifecycle
    void Init(const ConnectorConfig& config);
    std::future<bool> ConnectAsync(const std::string& host, uint16_t port);
    void Disconnect();
    bool IsConnected() const;

    // Messaging
    void Send(Packet packet);
    std::future<Packet> RequestAsync(Packet packet);
    void Request(Packet packet, std::function<void(Packet)> callback);

    // Authentication
    std::future<bool> AuthenticateAsync(const std::string& service_id,
                                        const std::string& account_id,
                                        const std::vector<uint8_t>& payload);

    // Thread integration
    void MainThreadAction();  // Call from main thread

    // Callbacks
    std::function<void()> OnConnect;
    std::function<void(Packet)> OnReceive;
    std::function<void(int code, std::string message)> OnError;
    std::function<void()> OnDisconnect;
};

} // namespace playhouse
```

## Protocol Format

### Request Packet
```
┌─────────────┬────────────┬─────────┬─────────┬─────────┬─────────┐
│ ContentSize │ MsgIdLen   │ MsgId   │ MsgSeq  │ StageId │ Payload │
│ (4 bytes)   │ (1 byte)   │ (N)     │ (2)     │ (8)     │ (...)   │
└─────────────┴────────────┴─────────┴─────────┴─────────┴─────────┘
```

### Response Packet
```
┌─────────────┬────────────┬─────────┬─────────┬─────────┬───────────┬──────────────┬─────────┐
│ ContentSize │ MsgIdLen   │ MsgId   │ MsgSeq  │ StageId │ ErrorCode │ OriginalSize │ Payload │
│ (4 bytes)   │ (1 byte)   │ (N)     │ (2)     │ (8)     │ (2)       │ (4)          │ (...)   │
└─────────────┴────────────┴─────────┴─────────┴─────────┴───────────┴──────────────┴─────────┘
```

- **Byte Order**: Little-endian
- **String Encoding**: UTF-8
- **MsgSeq**: 0 = Push (server-initiated), >0 = Request-Response correlation
- **OriginalSize**: >0 indicates LZ4 compressed payload

### Special Message IDs
| MsgId | Purpose |
|-------|---------|
| `@Heart@Beat@` | Heartbeat (keep-alive) |
| `@Debug@` | Debug messages |
| `@Timeout@` | Request timeout notification |

## Usage Example

```cpp
#include <playhouse/connector.hpp>
#include <iostream>

int main() {
    using namespace playhouse;

    // Configure
    ConnectorConfig config;
    config.heartbeat_interval_ms = 5000;
    config.request_timeout_ms = 10000;

    // Create connector
    Connector connector;
    connector.Init(config);

    // Set callbacks
    connector.OnConnect = []() {
        std::cout << "Connected!" << std::endl;
    };

    connector.OnReceive = [](Packet packet) {
        std::cout << "Received: " << packet.GetMsgId() << std::endl;
    };

    connector.OnError = [](int code, std::string message) {
        std::cerr << "Error " << code << ": " << message << std::endl;
    };

    connector.OnDisconnect = []() {
        std::cout << "Disconnected" << std::endl;
    };

    // Connect
    auto connect_future = connector.ConnectAsync("localhost", 34001);
    if (!connect_future.get()) {
        std::cerr << "Connection failed" << std::endl;
        return 1;
    }

    // Authenticate
    auto auth_future = connector.AuthenticateAsync("game", "user123", {});
    if (!auth_future.get()) {
        std::cerr << "Authentication failed" << std::endl;
        return 1;
    }

    // Send request
    EchoRequest request;
    request.set_content("Hello");
    request.set_sequence(1);

    Packet packet(request);
    auto response_future = connector.RequestAsync(std::move(packet));
    Packet response = response_future.get();

    if (response.GetErrorCode() == 0) {
        EchoReply reply;
        reply.ParseFromArray(response.GetPayload().data(), response.GetPayload().size());
        std::cout << "Echo reply: " << reply.content() << std::endl;
    }

    // Cleanup
    connector.Disconnect();
    return 0;
}
```

## Build Instructions

### Native Build (CMake)

```bash
mkdir build && cd build
cmake -DCMAKE_BUILD_TYPE=Release ..
cmake --build .
```

### CMakeLists.txt Example

```cmake
cmake_minimum_required(VERSION 3.20)
project(my_game_client)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Find dependencies
find_package(playhouse-connector CONFIG REQUIRED)
find_package(protobuf CONFIG REQUIRED)

add_executable(my_game_client main.cpp)
target_link_libraries(my_game_client PRIVATE
    playhouse::connector
    protobuf::libprotobuf
)
```

### vcpkg Integration

```bash
# Install
vcpkg install playhouse-connector

# Use in CMakeLists.txt
find_package(playhouse-connector CONFIG REQUIRED)
target_link_libraries(myapp PRIVATE playhouse::connector)
```

### Conan Integration

```python
# conanfile.py
from conan import ConanFile

class MyGameClient(ConanFile):
    requires = "playhouse-connector/1.0.0"
    generators = "CMakeDeps", "CMakeToolchain"
```

## LZ4 Compression

When `OriginalSize > 0` in response packets, the payload is LZ4 compressed:

```cpp
#include <lz4.h>

void DecompressPayload(const Packet& packet) {
    if (packet.GetOriginalSize() > 0) {
        std::vector<uint8_t> decompressed(packet.GetOriginalSize());
        int result = LZ4_decompress_safe(
            reinterpret_cast<const char*>(packet.GetPayload().data()),
            reinterpret_cast<char*>(decompressed.data()),
            packet.GetPayload().size(),
            packet.GetOriginalSize()
        );
        if (result < 0) {
            throw std::runtime_error("LZ4 decompression failed");
        }
        // Use decompressed data...
    }
}
```

**Recommended LZ4 Version**: 1.9.4+ (vcpkg: `lz4`)

## TLS/SSL Support

TLS support is planned for future releases. Current workaround:

- Use stunnel or nginx as TLS termination proxy
- Configure proxy to forward decrypted traffic to PlayHouse server

## Advanced: C++20 Coroutines

For high-performance scenarios, consider using `asio::awaitable` (C++20):

```cpp
// Requires C++20 and asio with coroutine support
asio::awaitable<void> ConnectAndAuthenticate(Connector& connector) {
    co_await connector.ConnectAwaitable("localhost", 34001);
    co_await connector.AuthenticateAwaitable("game", "user123", {});
}
```

## Development Tasks

| Phase | Tasks |
|-------|-------|
| Core | Project setup, protocol parsing, packet encode/decode |
| Network | TCP connection, async I/O (asio), request-response correlation |
| Reliability | Heartbeat, reconnection logic, error handling |
| Testing | Unit tests, E2E tests with C++ server |
| Release | vcpkg/Conan packaging, documentation |

## Distribution Channels

| Channel | Target |
|---------|--------|
| vcpkg | Native C++ developers |
| Conan | CMake projects |
| GitHub Releases | Source + prebuilt binaries |

## Memory Management

- RAII pattern for all resources
- `std::unique_ptr` for exclusive ownership
- `std::shared_ptr` for shared callbacks
- Thread-safe callback queue with lock-free design where possible

## Thread Safety

- Single I/O thread for networking (asio io_context)
- Callback queue for game thread delivery
- `MainThreadAction()` must be called from game thread only
- All public methods are thread-safe

## Error Codes

| Code | Description |
|------|-------------|
| 0 | Success |
| 1001 | Connection failed |
| 1002 | Connection timeout |
| 1003 | Connection closed |
| 2001 | Request timeout |
| 2002 | Invalid response |
| 3001 | Authentication failed |

## Troubleshooting

### Connection Fails

```cpp
// Enable verbose logging
#define PLAYHOUSE_LOG_LEVEL 3  // 0=None, 1=Error, 2=Warn, 3=Info, 4=Debug
```

Check network configuration:
- Verify host and port are correct
- Ensure firewall allows outgoing TCP connections
- Check server is running and accessible

### Request Timeout

```cpp
// Increase timeout
config.request_timeout_ms = 60000;  // 60 seconds
```

### Callbacks Not Firing

Ensure `MainThreadAction()` is called regularly:

```cpp
// In main loop
while (running) {
    connector.MainThreadAction();
    // ... other work
}
```

### Memory Leaks

Use RAII patterns and smart pointers:

```cpp
// Use unique_ptr for exclusive ownership
auto connector = std::make_unique<Connector>();

// Packets are automatically cleaned up with RAII
{
    Packet packet(request);
    connector->Send(std::move(packet));
}  // packet resources released here
```

### Linker Errors (asio)

Ensure asio is properly linked:

```cmake
# Use standalone asio (no Boost)
target_compile_definitions(myapp PRIVATE ASIO_STANDALONE)
```

## References

- [C# Connector](../csharp/) - Reference implementation
- [Protocol Spec](../../docs/architecture/protocol-spec.md)

## License

Apache 2.0 with Commons Clause
