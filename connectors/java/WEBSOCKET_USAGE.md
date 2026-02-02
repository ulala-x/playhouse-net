# Java Connector - WebSocket Support

## Overview

The Java connector now supports both TCP and WebSocket protocols with optional SSL/TLS encryption.

## New Features

### 1. IConnection Interface
- Abstract interface for both TCP and WebSocket connections
- Located at: `com.playhouse.connector.internal.IConnection`

### 2. Enhanced ConnectorConfig
New configuration options:
- `useWebsocket`: Enable WebSocket protocol (default: false)
- `useSsl`: Enable SSL/TLS encryption (default: false)
- `webSocketPath`: WebSocket endpoint path (default: "/ws")
- `skipServerCertificateValidation`: Skip SSL cert validation for testing (default: false)
- `heartbeatTimeoutMs`: Heartbeat timeout (default: 30000ms)
- `connectionIdleTimeoutMs`: Connection idle timeout (default: 30000ms)

### 3. WsConnection Implementation
- Full WebSocket support using Netty
- Binary WebSocket frames for packet protocol
- SSL/TLS support (wss://)
- Automatic handshake handling

### 4. Enhanced TcpConnection
- Now implements IConnection interface
- Added SSL/TLS support
- Certificate validation options

### 5. ConnectorErrorCode Enum
Standardized error codes:
- `DISCONNECTED` (60201): Connection is disconnected
- `REQUEST_TIMEOUT` (60202): Request timeout
- `UNAUTHENTICATED` (60203): Not authenticated

### 6. Callback-based Authentication
New method: `authenticate(serviceId, accountId, payload, callback)`

## Usage Examples

### TCP Connection (Default)
```java
ConnectorConfig config = ConnectorConfig.builder()
    .requestTimeoutMs(10000)
    .build();

try (Connector connector = new Connector()) {
    connector.init(config);
    connector.connectAsync("localhost", 34001).join();
    // Use connector...
}
```

### TCP with SSL/TLS
```java
ConnectorConfig config = ConnectorConfig.builder()
    .useSsl(true)
    .skipServerCertificateValidation(true) // For testing only!
    .build();

try (Connector connector = new Connector()) {
    connector.init(config);
    connector.connectAsync("localhost", 34001).join();
    // Use connector...
}
```

### WebSocket Connection
```java
ConnectorConfig config = ConnectorConfig.builder()
    .useWebsocket(true)
    .webSocketPath("/ws")
    .build();

try (Connector connector = new Connector()) {
    connector.init(config);
    connector.connectAsync("localhost", 38080).join();
    // Use connector...
}
```

### WebSocket with SSL (wss://)
```java
ConnectorConfig config = ConnectorConfig.builder()
    .useWebsocket(true)
    .useSsl(true)
    .webSocketPath("/ws")
    .skipServerCertificateValidation(true) // For testing only!
    .build();

try (Connector connector = new Connector()) {
    connector.init(config);
    connector.connectAsync("secure.example.com", 443).join();
    // Use connector...
}
```

### Callback-based Authentication
```java
connector.authenticate("gameService", "user123", authData, success -> {
    if (success) {
        System.out.println("Authentication successful!");
    } else {
        System.out.println("Authentication failed!");
    }
});
```

### Using Error Codes
```java
try {
    Packet response = connector.requestAsync(packet).join();
    if (response.hasError()) {
        ConnectorErrorCode errorCode = ConnectorErrorCode.fromCode(response.getErrorCode());
        System.err.println("Error: " + errorCode);
    }
} catch (ConnectorException e) {
    System.err.println("Connector error: " + e.getMessage());
}
```

## Architecture

### Connection Selection
The `ClientNetwork` class automatically selects the appropriate connection type based on configuration:

```java
if (config.isUseWebsocket()) {
    connection = new WsConnection(config);
} else {
    connection = new TcpConnection(config);
}
```

### Protocol Compatibility
Both TCP and WebSocket use the same packet protocol:
- ContentSize(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Payload

WebSocket wraps packets in BinaryWebSocketFrame for transport.

## Implementation Details

### Key Classes
1. **IConnection**: Abstract connection interface
2. **TcpConnection**: TCP implementation with SSL support
3. **WsConnection**: WebSocket implementation with SSL support
4. **ClientNetwork**: Network layer that uses IConnection
5. **ConnectorErrorCode**: Standardized error codes

### SSL/TLS Support
Both TCP and WebSocket support SSL/TLS:
- Uses Netty's `SslContext` and `SslHandler`
- Optional certificate validation skip for testing
- Supports custom trust managers

### WebSocket Features
- Binary WebSocket frames (not text)
- Automatic handshake handling
- Graceful close with WebSocket close frame
- Ping/Pong support for keepalive
- Fragment handling

## Testing

Compile and test:
```bash
cd connectors/java
./gradlew test
```

## Security Notes

⚠️ **WARNING**: Never use `skipServerCertificateValidation(true)` in production!

This option is provided for testing purposes only. In production, always:
- Use proper SSL certificates
- Validate server certificates
- Use secure WebSocket (wss://) for sensitive data

## Compatibility

This implementation is compatible with:
- C# connector (PlayHouse.Connector)
- JavaScript connector (@playhouse/connector)
- Test server with WebSocket support

## Dependencies

Netty libraries (already included):
- netty-all: WebSocket client, SSL/TLS support
- netty-handler: SSL handlers
- netty-codec-http: HTTP/WebSocket codec

## Next Steps

For integration tests, see the planned test structure:
- Core tests: Connection, Authentication, Request-Response, etc.
- Advanced tests: Large payloads, Multiple connectors, Edge cases

Refer to `/home/ulalax/.claude/plans/dreamy-greeting-dewdrop.md` for the full testing plan.
