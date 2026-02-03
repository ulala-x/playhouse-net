# PlayHouse Java Connector

Java Connector for PlayHouse real-time game server framework.

## Overview

- **Purpose**: Java server E2E testing
- **Status**: Active Development
- **JDK**: 22+ (Virtual Threads support)
- **API Styles**: Synchronous (Virtual Thread), Asynchronous (CompletableFuture), Callback

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
| Language | Java 22+ (Virtual Threads) |
| Build | Gradle 8.x (Kotlin DSL) |
| Networking | Java NIO (AsynchronousSocketChannel) |
| Concurrency | Virtual Threads (Project Loom) |
| Async | CompletableFuture |
| Serialization | Protocol Buffers (protobuf-java) |
| Testing | JUnit 5 + AssertJ |
| Distribution | JitPack (GitHub tag-based) |

## API Design

PlayHouse Java Connector는 세 가지 API 스타일을 제공합니다:

### 1. 동기 API (Synchronous - Virtual Thread 권장)

Virtual Thread에서 효율적으로 동작하며, 블로킹 호출처럼 보이지만 내부적으로 비동기로 처리됩니다.

```java
// 동기 API - Virtual Thread에서 효율적
void connect(String host, int port);
Packet request(Packet packet);
boolean authenticate(String serviceId, String accountId, byte[] payload);
boolean authenticate(Packet authPacket);
```

### 2. 비동기 API (Asynchronous - CompletableFuture)

`*Async` 접미사를 가지며, CompletableFuture를 반환합니다.

```java
// 비동기 API - CompletableFuture 반환
CompletableFuture<Void> connectAsync(String host, int port);
CompletableFuture<Packet> requestAsync(Packet packet);
CompletableFuture<Boolean> authenticateAsync(String serviceId, String accountId, byte[] payload);
CompletableFuture<Boolean> authenticateAsync(Packet authPacket);
```

### 3. 콜백 API (Callback)

게임 엔진이나 이벤트 기반 시스템에서 사용하기 좋은 콜백 스타일입니다.

```java
// 콜백 API - Consumer 파라미터
void request(Packet packet, Consumer<Packet> callback);
```

### 전체 API

```java
package com.playhouse.connector;

public class Connector implements AutoCloseable {

    // Lifecycle
    public void init(ConnectorConfig config);

    // 동기 연결
    public void connect(String host, int port);
    // 비동기 연결
    public CompletableFuture<Void> connectAsync(String host, int port);

    public void disconnect();
    public boolean isConnected();
    public boolean isAuthenticated();

    // Messaging
    public void send(Packet packet);  // Fire-and-forget

    // 동기 요청
    public Packet request(Packet packet);
    // 비동기 요청
    public CompletableFuture<Packet> requestAsync(Packet packet);
    // 콜백 요청
    public void request(Packet packet, Consumer<Packet> callback);

    // Authentication
    // 동기 인증
    public boolean authenticate(String serviceId, String accountId, byte[] payload);
    public boolean authenticate(Packet authPacket);
    // 비동기 인증
    public CompletableFuture<Boolean> authenticateAsync(String serviceId, String accountId, byte[] payload);
    public CompletableFuture<Boolean> authenticateAsync(Packet authPacket);

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
    sourceCompatibility = JavaVersion.VERSION_22
    targetCompatibility = JavaVersion.VERSION_22
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

## Usage Examples

### 1. 동기 API with Virtual Thread (권장)

Virtual Thread를 사용하면 동기 코드처럼 작성하면서도 효율적으로 동작합니다.

```java
import com.playhouse.connector.*;

public class VirtualThreadExample {
    public static void main(String[] args) {
        var config = ConnectorConfig.builder()
            .heartbeatIntervalMs(5000)
            .requestTimeoutMs(10000)
            .build();

        try (var connector = new Connector()) {
            connector.init(config);
            connector.setOnReceive(packet -> {
                System.out.println("Received push: " + packet.getMsgId());
            });

            // Virtual Thread에서 실행
            Thread.startVirtualThread(() -> {
                try {
                    // 동기 API - 코드가 간결하고 읽기 쉬움
                    connector.connect("localhost", 34001);
                    System.out.println("Connected!");

                    // 동기 인증
                    boolean authenticated = connector.authenticate("game", "user123", new byte[0]);
                    System.out.println("Authenticated: " + authenticated);

                    // 동기 요청-응답
                    var request = Packet.fromBytes("EchoRequest", "Hello".getBytes());
                    var response = connector.request(request);
                    System.out.println("Response: " + response.getMsgId());

                } catch (ConnectorException e) {
                    System.err.println("Error: " + e.getMessage());
                }
            }).join();
        }
    }
}
```

### 2. 비동기 API with CompletableFuture

기존 비동기 코드와 호환되는 CompletableFuture 기반 API입니다.

```java
import com.playhouse.connector.*;

public class AsyncExample {
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

            // 비동기 체이닝
            connector.connectAsync("localhost", 34001)
                .thenCompose(v -> connector.authenticateAsync("game", "user123", new byte[0]))
                .thenCompose(authenticated -> {
                    System.out.println("Authenticated: " + authenticated);
                    var request = Packet.fromBytes("EchoRequest", "Hello".getBytes());
                    return connector.requestAsync(request);
                })
                .thenAccept(response -> {
                    System.out.println("Response: " + response.getMsgId());
                })
                .join();
        }
    }
}
```

### 3. 콜백 API (게임 엔진용)

Unity, Godot 등 게임 엔진에서 사용하기 좋은 콜백 스타일입니다.

```java
import com.playhouse.connector.*;

public class CallbackExample {
    public static void main(String[] args) {
        var connector = new Connector();
        connector.init();

        connector.setOnConnect(() -> {
            System.out.println("Connected!");
            // 연결 성공 시 인증 콜백으로 진행
        });

        connector.setOnReceive(packet -> {
            System.out.println("Received: " + packet.getMsgId());
        });

        // 콜백 방식 요청
        var request = Packet.fromBytes("EchoRequest", "Hello".getBytes());
        connector.request(request, response -> {
            System.out.println("Response: " + response.getMsgId());
        });
    }
}
```

## Thread Model

PlayHouse Java Connector는 효율적인 스레드 모델을 사용합니다:

### 1. NIO Thread (내부)
- 단일 스레드로 모든 네트워크 I/O 처리
- Java NIO Selector를 사용한 비블로킹 I/O
- Netty 기반 고성능 네트워킹

### 2. Virtual Threads (Java 22+)
- **권장**: 동기 API는 Virtual Thread에서 사용
- `CompletableFuture.join()`이 Virtual Thread에서 블로킹 시 다른 작업으로 자동 전환
- 수천 개의 Virtual Thread를 가볍게 생성 가능

```java
// Virtual Thread 생성 및 실행
Thread.startVirtualThread(() -> {
    try {
        connector.connect("localhost", 34001);
        var response = connector.request(packet);
        // 블로킹처럼 보이지만 효율적으로 실행됨
    } catch (ConnectorException e) {
        // 예외 처리
    }
});

// Virtual Thread Executor 사용
try (var executor = Executors.newVirtualThreadPerTaskExecutor()) {
    executor.submit(() -> {
        connector.connect("localhost", 34001);
        // ...
    });
}
```

### 3. Callback Queue (게임 엔진용)
- 콜백을 메인 스레드에서 실행하기 위한 큐
- `mainThreadAction()` 호출 시 큐에서 콜백 실행
- Unity, Godot 등 단일 스레드 게임 엔진에 필요

```java
// 게임 루프에서 호출
ScheduledExecutorService scheduler = Executors.newSingleThreadScheduledExecutor();
scheduler.scheduleAtFixedRate(
    () -> connector.mainThreadAction(),
    0, 16, TimeUnit.MILLISECONDS  // ~60 FPS
);
```

### Virtual Thread의 장점

1. **간결한 코드**: 동기 스타일로 작성하면서도 비동기 성능
2. **높은 동시성**: 수천 개의 스레드를 가볍게 생성
3. **디버깅 용이**: 스택 트레이스가 명확하고 이해하기 쉬움
4. **예외 처리 간편**: try-catch로 간단히 처리

```java
// 기존 Platform Thread (무겁고 제한적)
ExecutorService executor = Executors.newFixedThreadPool(100);

// Virtual Thread (가볍고 확장 가능)
ExecutorService executor = Executors.newVirtualThreadPerTaskExecutor();
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

### 1. 동기 API 예외 처리 (권장)

동기 API는 `ConnectorException`을 직접 throw하므로 예외 처리가 간단합니다.

```java
// 동기 API - 간단한 예외 처리
Thread.startVirtualThread(() -> {
    try {
        connector.connect("localhost", 34001);
        boolean authenticated = connector.authenticate("game", "user123", new byte[0]);

        if (authenticated) {
            var request = Packet.fromBytes("EchoRequest", "Hello".getBytes());
            var response = connector.request(request);
            System.out.println("Success: " + response.getMsgId());
        }
    } catch (ConnectorException e) {
        // ConnectorException: 네트워크/프로토콜 오류
        System.err.println("Connector error: " + e.getErrorCode() + " - " + e.getMessage());
    } catch (IllegalStateException e) {
        // IllegalStateException: 초기화/연결 상태 오류
        System.err.println("State error: " + e.getMessage());
    }
});
```

### 2. 동기 API 재시도 패턴

```java
public Packet requestWithRetry(Packet packet, int maxRetries) {
    for (int attempt = 1; attempt <= maxRetries; attempt++) {
        try {
            return connector.request(packet);
        } catch (ConnectorException e) {
            if (e.getErrorCode() == 2001) {  // Timeout
                if (attempt >= maxRetries) {
                    throw new RuntimeException("Request failed after " + maxRetries + " attempts", e);
                }
                // Exponential backoff
                try {
                    Thread.sleep((long) Math.pow(2, attempt) * 100);
                } catch (InterruptedException ie) {
                    Thread.currentThread().interrupt();
                    throw new RuntimeException("Interrupted", ie);
                }
            } else {
                throw e;  // 다른 오류는 즉시 실패
            }
        }
    }
    throw new RuntimeException("Request failed");
}
```

### 3. 비동기 API 예외 처리

비동기 API는 `CompletionException`으로 감싸져 반환됩니다.

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

The Java connector supports TLS for both TCP and WebSocket transports.

- TCP+TLS: set `useSsl(true)` and connect to the TCP TLS port.
- WSS: set `useWebsocket(true)`, `useSsl(true)`, and connect to the HTTPS port.
- Self-signed certs (tests): set `skipServerCertificateValidation(true)`.

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
