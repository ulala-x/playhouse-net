# PlayHouse Echo Client - Build Instructions

## Prerequisites

### System Requirements
- Linux (Ubuntu 20.04+ or similar)
- CMake 3.20 or higher
- GCC 13+ or Clang 14+ (C++20 support required)

### Required Dependencies

#### 1. Protobuf
```bash
sudo apt-get update
sudo apt-get install -y libprotobuf-dev protobuf-compiler
```

Verify installation:
```bash
protoc --version  # Should show libprotoc 3.x or higher
```

#### 2. Boost (Optional)
Boost ASIO headers are header-only, but the system library improves build times.

```bash
sudo apt-get install -y libboost-dev libboost-system-dev
```

If Boost is not available, the build will proceed using system headers.

#### 3. Threads (Usually pre-installed)
```bash
sudo apt-get install -y build-essential
```

## Build Steps

### 1. Navigate to Project Directory
```bash
cd /home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_echo/cpp_client
```

### 2. Create Build Directory
```bash
mkdir -p build
cd build
```

### 3. Configure with CMake
```bash
cmake ..
```

Expected output:
```
-- The CXX compiler identification is GNU 13.3.0
-- Build type: Release
-- C++ standard: 20
-- Boost version: 1.xx.x (or "not found")
-- Configuring done
-- Generating done
```

### 4. Build
```bash
make -j$(nproc)
```

This will compile:
- ASIO layer (asio_layer static library)
- Protocol layer (protocol static library)
- Echo proto (echo_proto static library from .proto files)
- Util layer (util static library)
- Main executable (echo_client)

### 5. Verify Build
```bash
ls -lh echo_client
```

You should see the executable:
```
-rwxr-xr-x 1 user user 2.5M Jan 3 18:00 echo_client
```

## Running the Client

### Basic Usage
```bash
./echo_client
```

### Change Server Endpoint
1. Press `ENTER` key
2. Input address (e.g., `127.0.0.1` or leave empty to keep current)
3. Input port (e.g., `16110` or leave empty to keep current)

Default endpoint: `127.0.0.1:16110`

## Troubleshooting

### CMake Configuration Errors

#### "Could NOT find Protobuf"
```bash
# Install protobuf development packages
sudo apt-get install libprotobuf-dev protobuf-compiler

# If using a custom installation, set PKG_CONFIG_PATH
export PKG_CONFIG_PATH=/usr/local/lib/pkgconfig:$PKG_CONFIG_PATH
```

#### "Could NOT find Boost"
This is optional. The build should proceed with:
```
-- Boost not found via CMake, will use system headers
```

If you see errors, install Boost:
```bash
sudo apt-get install libboost-dev
```

### Compilation Errors

#### "error: 'std::xxx' has not been declared"
Your compiler may not support C++20. Update GCC/Clang:
```bash
sudo apt-get install g++-13
export CXX=g++-13
```

#### "undefined reference to `google::protobuf::xxx'"
Protobuf library not linked. Check that libprotobuf is installed:
```bash
ldconfig -p | grep protobuf
```

### Link Errors

#### "undefined reference to `pthread_xxx'"
Pthread library missing:
```bash
sudo apt-get install build-essential
```

#### "cannot find -lboost_system"
CMake is trying to link Boost but it's not installed:
```bash
sudo apt-get install libboost-system-dev
```

Or edit `CMakeLists.txt` to disable Boost linking.

## Build Variants

### Debug Build
```bash
cd build
cmake -DCMAKE_BUILD_TYPE=Debug ..
make -j$(nproc)
```

Debug flags: `-g -O0 -Wall -Wextra`

### Release Build (Default)
```bash
cd build
cmake -DCMAKE_BUILD_TYPE=Release ..
make -j$(nproc)
```

Release flags: `-O3 -march=native -DNDEBUG`

### Clean Build
```bash
cd build
make clean
# or
rm -rf *
cmake ..
make -j$(nproc)
```

## Cross-Platform Notes

### Windows (MinGW/MSYS2)
Install MSYS2 and required packages:
```bash
pacman -S mingw-w64-x86_64-gcc
pacman -S mingw-w64-x86_64-cmake
pacman -S mingw-w64-x86_64-protobuf
pacman -S mingw-w64-x86_64-boost
```

Then build as above.

### macOS
Install dependencies with Homebrew:
```bash
brew install cmake protobuf boost
```

Then build as above.

## Testing the Build

### Quick Test
```bash
# Terminal 1: Start PlayHouse server
cd /home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_echo
dotnet run

# Terminal 2: Start C++ client
cd /home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_echo/cpp_client/build
./echo_client

# In client:
# Press '1' to create 1 connection
# Press 'u' to send 1 message immediately
# Check that [send] and [receive] counters increment
```

### Full Benchmark Test
```bash
# In client:
# Press '3' to create 100 connections
# Press SPACE to toggle traffic test ON
# Observe messages/s statistics
# Press 'j' to increase message size
# Press 'a' to increase times (messages per batch)
# Press '/' to toggle relay echo ON (maximum throughput)
```

## Performance Tips

### 1. Use Release Build
Debug builds are 10-100x slower due to assertions and lack of optimization.

### 2. CPU Affinity
For consistent benchmarking:
```bash
taskset -c 0-3 ./echo_client  # Pin to CPUs 0-3
```

### 3. Disable Frequency Scaling
```bash
# Set CPU governor to performance
sudo cpupower frequency-set -g performance
```

### 4. Network Tuning
For localhost testing, this is usually unnecessary. For network testing:
```bash
# Increase socket buffers
sudo sysctl -w net.core.rmem_max=8388608
sudo sysctl -w net.core.wmem_max=8388608
```

## File Layout After Build

```
cpp_client/
├── build/
│   ├── echo_client              # Main executable
│   ├── libasio_layer.a          # ASIO wrapper
│   ├── libprotocol.a            # PlayHouse protocol
│   ├── libecho_proto.a          # Protobuf messages
│   ├── libutil.a                # Utilities
│   └── proto/
│       ├── echo.pb.h            # Generated protobuf header
│       └── echo.pb.cc           # Generated protobuf source
├── client/
│   ├── echo_client.h            # Client implementation
│   ├── echo_client.cpp
│   ├── main.cpp                 # Entry point
│   ├── playhouse_socket.h       # Socket wrapper
│   └── playhouse_socket.cpp
├── asio/                        # CGDK10 ASIO layer
├── protocol/                    # PlayHouse codec
├── proto/                       # Proto definitions
└── CMakeLists.txt
```

## Getting Help

If you encounter issues:

1. Check CMake output for specific errors
2. Verify all dependencies are installed
3. Check compiler version: `g++ --version` (should be 13+)
4. Check CMake version: `cmake --version` (should be 3.20+)
5. Review build logs in `build/CMakeFiles/`

For questions specific to PlayHouse integration, refer to:
- `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_echo/cpp_client/client/PHASE4_SUMMARY.md`
- `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_echo/cpp_client/client/README.md`
