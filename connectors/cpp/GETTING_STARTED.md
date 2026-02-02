# Getting Started with PlayHouse C++ Connector

This guide will help you get started with the PlayHouse C++ Connector.

## Prerequisites

- CMake 3.20 or higher
- C++17 compatible compiler (GCC 7+, Clang 5+, MSVC 2017+)
- asio library (standalone, no Boost required)

## Quick Start

### 1. Install Dependencies

#### Using vcpkg (Recommended)

```bash
vcpkg install asio
vcpkg install gtest  # Optional, for tests
```

#### Using system package manager

**Ubuntu/Debian:**
```bash
sudo apt-get install libasio-dev
```

**macOS:**
```bash
brew install asio
```

### 2. Build the Library

```bash
cd connectors/cpp
./build.sh Release
```

To build with tests:
```bash
./build.sh Release ON
```

### 3. Basic Usage

```cpp
#include <playhouse/connector.hpp>
#include <iostream>

int main() {
    using namespace playhouse;

    // Configure
    ConnectorConfig config;
    config.heartbeat_interval_ms = 5000;

    // Create connector
    Connector connector;
    connector.Init(config);

    // Set callbacks
    connector.OnConnect = []() {
        std::cout << "Connected!" << std::endl;
    };

    // Connect
    auto future = connector.ConnectAsync("localhost", 34001);
    if (future.get()) {
        std::cout << "Connection successful" << std::endl;
    }

    // Main loop
    while (connector.IsConnected()) {
        connector.MainThreadAction();  // Process callbacks
        // ... your game logic
    }

    connector.Disconnect();
    return 0;
}
```

### 4. Link Against the Library

**CMakeLists.txt:**
```cmake
cmake_minimum_required(VERSION 3.20)
project(my_game)

set(CMAKE_CXX_STANDARD 17)

# Find PlayHouse Connector
find_package(playhouse-connector CONFIG REQUIRED)

add_executable(my_game main.cpp)
target_link_libraries(my_game PRIVATE playhouse::connector)
```

## Key Concepts

### 1. Initialization

Always call `Init()` before using the connector:

```cpp
ConnectorConfig config;
config.send_buffer_size = 65536;
config.receive_buffer_size = 262144;

Connector connector;
connector.Init(config);
```

### 2. Callbacks

Set callbacks before connecting:

```cpp
connector.OnConnect = []() { /* ... */ };
connector.OnReceive = [](Packet packet) { /* ... */ };
connector.OnError = [](int code, std::string message) { /* ... */ };
connector.OnDisconnect = []() { /* ... */ };
```

### 3. Main Thread Processing

**Important:** Call `MainThreadAction()` regularly from your main thread:

```cpp
// In your main loop
while (running) {
    connector.MainThreadAction();
    // ... other work
}
```

This ensures callbacks are executed on the main thread, which is essential for:
- Thread safety
- Game engine integration (Unity, Unreal)
- UI updates

### 4. Sending Messages

**One-way message (no response):**
```cpp
Bytes payload = {0x01, 0x02, 0x03};
Packet packet = Packet::FromBytes("PlayerMove", std::move(payload));
connector.Send(std::move(packet));
```

**Request-Response (async/await style):**
```cpp
Packet request = Packet::FromBytes("GetPlayerInfo", payload);
auto future = connector.RequestAsync(std::move(request));
Packet response = future.get();

if (response.GetErrorCode() == 0) {
    // Success
}
```

**Request-Response (callback style):**
```cpp
Packet request = Packet::FromBytes("GetPlayerInfo", payload);
connector.Request(std::move(request), [](Packet response) {
    // Handle response
});
```

## Common Patterns

### Pattern 1: Connection with Retry

```cpp
bool TryConnect(Connector& connector, int max_attempts = 3) {
    for (int i = 0; i < max_attempts; ++i) {
        auto future = connector.ConnectAsync("localhost", 34001);

        if (future.wait_for(std::chrono::seconds(5)) == std::future_status::timeout) {
            std::cerr << "Connection timeout, retrying..." << std::endl;
            continue;
        }

        if (future.get()) {
            return true;
        }

        std::this_thread::sleep_for(std::chrono::seconds(2));
    }
    return false;
}
```

### Pattern 2: Authentication Flow

```cpp
std::future<bool> Authenticate(Connector& connector) {
    Bytes auth_payload = /* prepare auth data */;
    return connector.AuthenticateAsync("game", "user123", auth_payload);
}

// Usage
if (Authenticate(connector).get()) {
    std::cout << "Authenticated successfully" << std::endl;
}
```

### Pattern 3: Request Queue

```cpp
class RequestQueue {
    std::queue<std::function<void()>> requests_;
    Connector& connector_;

public:
    void AddRequest(Packet packet, std::function<void(Packet)> callback) {
        requests_.push([this, p = std::move(packet), cb = std::move(callback)]() mutable {
            connector_.Request(std::move(p), std::move(cb));
        });
    }

    void ProcessNext() {
        if (!requests_.empty()) {
            requests_.front()();
            requests_.pop();
        }
    }
};
```

## Error Handling

### Check Error Codes

```cpp
connector.OnError = [](int code, std::string message) {
    switch (code) {
        case playhouse::error_code::CONNECTION_FAILED:
            // Handle connection failure
            break;
        case playhouse::error_code::REQUEST_TIMEOUT:
            // Handle timeout
            break;
        default:
            std::cerr << "Error: " << message << std::endl;
    }
};
```

### Check Response Error Codes

```cpp
Packet response = future.get();
if (response.GetErrorCode() != 0) {
    std::cerr << "Request failed with code: " << response.GetErrorCode() << std::endl;
}
```

## Performance Tips

1. **Reuse Connectors**: Create once, use multiple times
2. **Pre-allocate Buffers**: Configure buffer sizes appropriately
3. **Batch Operations**: Group multiple sends when possible
4. **Move Semantics**: Always use `std::move()` for packets
5. **Callback Queue**: Process callbacks efficiently in `MainThreadAction()`

## Thread Safety

- All public methods are thread-safe
- Callbacks are executed on the main thread (via `MainThreadAction()`)
- Don't block in callbacks (offload heavy work to background threads)

## Debugging

Enable verbose logging (compile-time option):

```cpp
#define PLAYHOUSE_LOG_LEVEL 4  // 0=None, 1=Error, 2=Warn, 3=Info, 4=Debug
```

## Next Steps

- See [README.md](README.md) for complete API reference
- Check [example.cpp](example.cpp) for more usage examples
- Read [Protocol Format](README.md#protocol-format) for packet structure details

## Troubleshooting

**Q: Callbacks not firing?**
A: Make sure you call `MainThreadAction()` regularly from your main loop.

**Q: Connection fails immediately?**
A: Check if the server is running and the host/port are correct.

**Q: Memory leaks?**
A: Always use `std::move()` for packets and don't store raw pointers to internal data.

**Q: Linker errors with asio?**
A: Make sure you define `ASIO_STANDALONE` before including headers.
