# PlayHouse Java Connector

Java Connector for PlayHouse real-time game server framework.

## Overview

- **Purpose**: Java server E2E testing
- **Status**: Planned
- **JDK**: 17+ (Virtual Threads optional)

## Directory Structure

```
connectors/java/
├── src/main/java/com/playhouse/connector/
│   ├── Connector.java          # Main API
│   ├── Packet.java             # Packet abstraction
│   ├── ConnectorConfig.java    # Configuration
│   ├── internal/
│   │   ├── ClientNetwork.java  # Networking core
│   │   ├── TcpConnection.java  # NIO-based TCP
│   │   └── PacketCodec.java    # Encode/decode
│   └── callback/
│       └── ConnectorCallback.java
├── src/test/java/
├── build.gradle.kts            # Gradle Kotlin DSL
└── settings.gradle.kts
```

## Technology Stack

| Component | Technology |
|-----------|------------|
| Language | Java 17+ |
| Build | Gradle 8.x (Kotlin DSL) |
| Networking | Java NIO (AsynchronousSocketChannel) |
| Async | CompletableFuture |
| Serialization | Protocol Buffers (protobuf-java) |
| Testing | JUnit 5 + AssertJ |
| Distribution | JitPack (GitHub tag-based) |

## API Design

```java
package com.playhouse.connector;

public class Connector implements AutoCloseable {

    // Lifecycle
    public void init(ConnectorConfig config);
    public CompletableFuture<Void> connectAsync(String host, int port);
    public void disconnect();
    public boolean isConnected();

    // Messaging
    public void send(Packet packet);
    public CompletableFuture<Packet> requestAsync(Packet packet);
    public void request(Packet packet, Consumer<Packet> callback);

    // Authentication
    public CompletableFuture<Boolean> authenticateAsync(
        String serviceId, String accountId, byte[] payload);

    // Thread integration
    public void mainThreadAction();

    // Callbacks
    public void setOnConnect(Runnable callback);
    public void setOnReceive(Consumer<Packet> callback);
    public void setOnError(BiConsumer<Integer, String> callback);
    public void setOnDisconnect(Runnable callback);

    @Override
    public void close();
}
```

### Configuration

```java
public class ConnectorConfig {
    private int sendBufferSize = 65536;      // 64KB
    private int receiveBufferSize = 262144;  // 256KB
    private int heartbeatIntervalMs = 10000; // 10s
    private int requestTimeoutMs = 30000;    // 30s
    private boolean enableReconnect = false;
    private int reconnectIntervalMs = 5000;

    // Builder pattern
    public static Builder builder() { ... }
}
```

### Packet

```java
public class Packet implements AutoCloseable {
    public String getMsgId();
    public int getMsgSeq();
    public long getStageId();
    public int getErrorCode();
    public ByteBuffer getPayload();

    // Factory methods
    public static Packet empty(String msgId);
    public static Packet fromProto(Message proto);
    public static Packet fromBytes(String msgId, byte[] bytes);

    // Protobuf deserialization
    public <T extends Message> T parse(Parser<T> parser);

    @Override
    public void close();
}
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

- **Byte Order**: Little-endian (use `ByteOrder.LITTLE_ENDIAN`)
- **String Encoding**: UTF-8
- **MsgSeq**: 0 = Push, >0 = Request-Response
- **OriginalSize**: >0 indicates LZ4 compressed

## Build Instructions

### Gradle Build

```bash
# Build
./gradlew build

# Run tests
./gradlew test

# Publish to local Maven
./gradlew publishToMavenLocal
```

### build.gradle.kts

```kotlin
plugins {
    java
    `java-library`
    `maven-publish`
}

group = "com.playhouse"
version = "1.0.0"

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

dependencies {
    implementation("com.google.protobuf:protobuf-java:3.25.1")
    implementation("org.lz4:lz4-java:1.8.0")

    testImplementation("org.junit.jupiter:junit-jupiter:5.10.1")
    testImplementation("org.assertj:assertj-core:3.24.2")
}

tasks.test {
    useJUnitPlatform()
}
```

## Distribution (JitPack)

### Usage

```gradle
// build.gradle
repositories {
    maven { url 'https://jitpack.io' }
}

dependencies {
    implementation 'com.github.user:playhouse:connector-java-v1.0.0'
}
```

```xml
<!-- pom.xml -->
<repositories>
    <repository>
        <id>jitpack.io</id>
        <url>https://jitpack.io</url>
    </repository>
</repositories>

<dependency>
    <groupId>com.github.user</groupId>
    <artifactId>playhouse</artifactId>
    <version>connector-java-v1.0.0</version>
</dependency>
```

### Release Process

```bash
# Tag and push to trigger JitPack build
git tag connector-java-v1.0.0
git push origin connector-java-v1.0.0
```

## Development Tasks

| Phase | Tasks |
|-------|-------|
| Core | Project setup, packet codec, NIO connection |
| Reliability | Request-response, heartbeat, error handling |
| Testing | Unit tests, Java server E2E integration |
| Release | JitPack deployment, documentation |

## Usage Example

```java
import com.playhouse.connector.*;

public class Example {
    public static void main(String[] args) throws Exception {
        var config = ConnectorConfig.builder()
            .heartbeatIntervalMs(5000)
            .requestTimeoutMs(10000)
            .build();

        try (var connector = new Connector()) {
            connector.init(config);

            connector.setOnConnect(() -> System.out.println("Connected!"));
            connector.setOnReceive(packet -> {
                System.out.println("Received: " + packet.getMsgId());
            });
            connector.setOnError((code, msg) -> {
                System.err.println("Error " + code + ": " + msg);
            });
            connector.setOnDisconnect(() -> System.out.println("Disconnected"));

            // Connect
            connector.connectAsync("localhost", 34001).join();

            // Authenticate
            connector.authenticateAsync("game", "user123", new byte[0]).join();

            // Send request
            var request = Packet.fromProto(EchoRequest.newBuilder()
                .setContent("Hello")
                .build());
            var response = connector.requestAsync(request).join();

            System.out.println("Response: " + response.getMsgId());
        }
    }
}
```

## Thread Model

- **NIO Thread**: Single thread for async I/O (Selector)
- **Callback Queue**: Thread-safe queue for main thread delivery
- **Virtual Threads**: Optional support for JDK 21+

```java
// With Virtual Threads (JDK 21+)
ExecutorService executor = Executors.newVirtualThreadPerTaskExecutor();
connector.setExecutor(executor);
```

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

## Exception Handling

### Timeout and Retry Pattern

```java
public Packet requestWithRetry(Packet packet, int maxRetries) {
    int attempt = 0;
    while (attempt < maxRetries) {
        try {
            return connector.requestAsync(packet)
                .orTimeout(10, TimeUnit.SECONDS)
                .join();
        } catch (CompletionException e) {
            if (e.getCause() instanceof TimeoutException) {
                attempt++;
                if (attempt >= maxRetries) {
                    throw new RuntimeException("Request failed after " + maxRetries + " attempts", e);
                }
                // Exponential backoff
                Thread.sleep((long) Math.pow(2, attempt) * 100);
            } else {
                throw e;
            }
        }
    }
    throw new RuntimeException("Request failed");
}
```

### Cancellation

```java
CompletableFuture<Packet> future = connector.requestAsync(packet);

// Cancel if not completed within timeout
ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor();
scheduler.schedule(() -> {
    if (!future.isDone()) {
        future.cancel(true);
    }
}, 30, TimeUnit.SECONDS);
```

## ByteBuffer Considerations

```java
// Direct buffer (better for I/O, off-heap)
ByteBuffer direct = ByteBuffer.allocateDirect(1024);

// Heap buffer (easier to debug, on-heap)
ByteBuffer heap = ByteBuffer.allocate(1024);

// Connector uses direct buffers internally for network I/O
// Packet payload is copied to heap for user access
```

## LZ4 Compression

When `originalSize > 0` in response packets, payload is LZ4 compressed:

```java
import net.jpountz.lz4.*;

public byte[] decompressPayload(Packet packet) {
    if (packet.getOriginalSize() > 0) {
        LZ4FastDecompressor decompressor = LZ4Factory.fastestInstance().fastDecompressor();
        byte[] restored = new byte[packet.getOriginalSize()];
        decompressor.decompress(packet.getPayload().array(), 0, restored, 0, packet.getOriginalSize());
        return restored;
    }
    return packet.getPayload().array();
}
```

**Dependency**: `org.lz4:lz4-java:1.8.0`

## TLS/SSL Support

TLS support is planned. Current workaround:

- Use stunnel or nginx as TLS termination proxy
- Configure proxy to forward decrypted traffic to PlayHouse server

## JUnit 5 Test Fixtures

```java
import org.junit.jupiter.api.*;
import static org.assertj.core.api.Assertions.*;

class ConnectorTest {

    private static Connector connector;

    @BeforeAll
    static void setup() throws Exception {
        connector = new Connector();
        connector.init(ConnectorConfig.builder()
            .requestTimeoutMs(5000)
            .build());
        connector.connectAsync("localhost", 34001).join();
        connector.authenticateAsync("game", "testuser", new byte[0]).join();
    }

    @AfterAll
    static void teardown() {
        if (connector != null) {
            connector.disconnect();
            connector.close();
        }
    }

    @Test
    void shouldReceiveEchoResponse() throws Exception {
        // Given
        var request = Packet.fromProto(EchoRequest.newBuilder()
            .setContent("Hello")
            .setSequence(1)
            .build());

        // When
        var response = connector.requestAsync(request).join();

        // Then
        assertThat(response.getErrorCode()).isZero();
        assertThat(response.getMsgId()).isEqualTo("EchoReply");

        var reply = EchoReply.parseFrom(response.getPayload());
        assertThat(reply.getContent()).isEqualTo("Hello");
    }

    @Test
    void shouldHandleTimeout() {
        // Given
        var slowRequest = Packet.empty("SlowOperation");

        // When/Then
        assertThatThrownBy(() ->
            connector.requestAsync(slowRequest)
                .orTimeout(100, TimeUnit.MILLISECONDS)
                .join()
        ).hasCauseInstanceOf(TimeoutException.class);
    }
}
```

## Troubleshooting

### Connection Fails

Check network configuration:
```java
// Enable debug logging
System.setProperty("playhouse.log.level", "DEBUG");
```

- Verify host and port are correct
- Ensure firewall allows outgoing TCP connections
- Check server is running and accessible

### Request Timeout

```java
// Increase timeout
ConnectorConfig.builder()
    .requestTimeoutMs(60000)  // 60 seconds
    .build();
```

### Callbacks Not Firing

Ensure `mainThreadAction()` is called if not using `ImmediateSynchronizationContext`:

```java
// In game loop or scheduler
ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor();
scheduler.scheduleAtFixedRate(
    () -> connector.mainThreadAction(),
    0, 16, TimeUnit.MILLISECONDS  // ~60 FPS
);
```

### Memory Leaks

Always use try-with-resources:

```java
try (var connector = new Connector()) {
    connector.init(config);
    // ...
}  // Automatically closed

try (var packet = Packet.fromProto(request)) {
    connector.send(packet);
}  // Packet resources released
```

## References

- [C# Connector](../csharp/) - Reference implementation
- [Protocol Spec](../../docs/architecture/protocol-spec.md)

## License

Apache 2.0 with Commons Clause
