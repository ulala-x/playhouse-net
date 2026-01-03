# PlayHouse Protocol Layer

C++ implementation of PlayHouse client-server protocol encoding/decoding.

## Protocol Specification

### Request Packet (Client → Server)
```
[Length: 4B LE]      - Message body size (excluding Length field)
[MsgIdLen: 1B]       - UTF-8 byte length of MsgId
[MsgId: N bytes]     - UTF-8 string
[MsgSeq: 2B LE]      - Sequence number
[StageId: 8B LE]     - Stage ID
[Payload: variable]  - Protobuf serialized data
```

### Response Packet (Server → Client)
```
[Length: 4B LE]      - Message body size (excluding Length field)
[MsgIdLen: 1B]       - UTF-8 byte length of MsgId
[MsgId: N bytes]     - UTF-8 string
[MsgSeq: 2B LE]      - Sequence number
[StageId: 8B LE]     - Stage ID
[ErrorCode: 2B LE]   - 0 = success
[OriginalSize: 4B LE] - 0 = uncompressed
[Payload: variable]  - Protobuf serialized data
```

**Note:** All multi-byte integers use Little-Endian byte order.

## API Usage

### Low-Level API (Codec)

```cpp
#include "protocol/playhouse_codec.h"

using namespace playhouse;

// Encoding a request
std::string msg_id = "EchoRequest";
uint16_t msg_seq = 1;
int64_t stage_id = 0;
std::vector<uint8_t> payload = {...};  // Protobuf serialized

auto encoded = Codec::encode_request(
    msg_id, msg_seq, stage_id,
    payload.data(), payload.size()
);

// Send encoded.data() over network (encoded.size() bytes)

// Decoding a response (after reading from network)
std::string decoded_msg_id;
uint16_t decoded_msg_seq;
int64_t decoded_stage_id;
uint16_t error_code;
std::vector<uint8_t> decoded_payload;

bool success = Codec::decode_response(
    response_data, response_size,
    decoded_msg_id, decoded_msg_seq, decoded_stage_id,
    error_code, decoded_payload
);

if (success && error_code == 0) {
    // Parse protobuf from decoded_payload
}
```

### High-Level API (Packet Wrapper)

```cpp
#include "protocol/packet.h"

using namespace playhouse;

// Create request packet
std::vector<uint8_t> payload = {...};  // Protobuf serialized
Packet request("EchoRequest", 1, 0, payload);

// Send packet
send(socket, request.data(), request.size(), 0);

// Parse response
auto response = Packet::parse_response(data, size);
if (response && response->is_success()) {
    std::cout << "Received: " << response->msg_id() << "\n";
    auto& payload = response->payload();
    // Deserialize protobuf from payload
}
```

## Integration with Protobuf

```cpp
#include "protocol/packet.h"
#include "echo.pb.h"  // Generated protobuf header

// Create protobuf message
EchoRequest echo_req;
echo_req.set_content("Hello");
echo_req.set_sequence(1);

// Serialize to payload
std::vector<uint8_t> payload(echo_req.ByteSizeLong());
echo_req.SerializeToArray(payload.data(), payload.size());

// Create packet
Packet request("EchoRequest", 1, 0, payload);

// ... send request ...

// Parse response
auto response = Packet::parse_response(data, size);
if (response && response->is_success()) {
    EchoResponse echo_resp;
    if (echo_resp.ParseFromArray(
        response->payload().data(),
        response->payload().size()))
    {
        std::cout << "Echo: " << echo_resp.content() << "\n";
    }
}
```

## Building

The protocol layer is built as a static library:

```bash
cd build
cmake ..
make protocol
```

## Testing

Run the test suite:

```bash
cd protocol
g++ -std=c++20 -O2 -o test_codec test_codec.cpp playhouse_codec.cpp
./test_codec
```

## Files

- **playhouse_codec.h/cpp**: Low-level protocol encoding/decoding
- **packet.h**: High-level packet wrapper (header-only)
- **test_codec.cpp**: Unit tests demonstrating usage

## Thread Safety

All encoding/decoding functions are stateless and thread-safe. You can safely call them from multiple threads without synchronization.

## Performance Considerations

- The Codec uses `std::vector<uint8_t>` for memory management
- For high-performance scenarios, consider pre-allocating buffers
- Little-endian operations are optimized for x86/x64 architectures
- No dynamic allocations in the encoding/decoding paths except for the output buffers
