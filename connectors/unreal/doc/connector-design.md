# PlayHouse Unreal Connector Design (UE 5.3+)

## Goals

- UE 5.3+ native plugin (source plugin).
- All platforms, including consoles.
- Transport support: TCP, WS, WSS, TLS.
- C++ first API; Blueprint is optional and thin.
- Callback-only request/response (no coroutine API).

## Summary

The Unreal connector is a Runtime plugin module that exposes a C++-first API and forwards all network callbacks to the GameThread. The implementation uses UE-native sockets for TCP and UE WebSockets module for WS/WSS. TLS over TCP is implemented via UE SSL module or platform-native TLS where required.

## Plugin Layout

```
connectors/unreal/
├── PlayHouseConnector.uplugin
├── Source/
│   └── PlayHouseConnector/
│       ├── Public/
│       │   ├── PlayHouseConnectorModule.h
│       │   ├── PlayHouseConnector.h
│       │   ├── PlayHousePacket.h
│       │   ├── PlayHouseConfig.h
│       │   └── PlayHouseTransport.h
│       ├── Private/
│       │   ├── PlayHouseConnectorModule.cpp
│       │   ├── PlayHouseConnector.cpp
│       │   ├── PlayHousePacket.cpp
│       │   ├── PlayHouseConfig.cpp
│       │   ├── Transports/
│       │   │   ├── PlayHouseTcpTransport.cpp
│       │   │   ├── PlayHouseTlsTransport.cpp
│       │   │   ├── PlayHouseWsTransport.cpp
│       │   │   └── PlayHouseWssTransport.cpp
│       │   └── Internal/
│       │       ├── PlayHousePacketCodec.cpp
│       │       ├── PlayHouseRingBuffer.cpp
│       │       └── PlayHouseRequestMap.cpp
│       └── PlayHouseConnector.Build.cs
└── doc/
    └── connector-design.md
```

## Public API (C++ Only, Callback-Based)

```cpp
class FPlayHouseConnector
{
public:
    void Init(const FPlayHouseConfig& Config);

    void Connect(const FString& Host, int32 Port);
    void Disconnect();
    bool IsConnected() const;

    void Send(FPlayHousePacket&& Packet);
    void Request(FPlayHousePacket&& Packet, TFunction<void(FPlayHousePacket&&)> OnResponse);

    void Authenticate(FPlayHousePacket&& Packet, TFunction<void(bool)> OnResult);

    // GameThread callback hooks
    TFunction<void()> OnConnect;
    TFunction<void()> OnDisconnect;
    TFunction<void(const FPlayHousePacket&)> OnReceive;
    TFunction<void(int32, const FString&)> OnError;
};
```

Notes:
- No coroutine or future APIs.
- All callbacks are invoked on GameThread.
- Request timeouts are handled by a ticker or timer manager.

## Transport Abstraction

A thin interface allows TCP/WS/WSS/TLS to share the same request/response pipeline.

```cpp
class IPlayHouseTransport
{
public:
    virtual ~IPlayHouseTransport() = default;
    virtual bool Connect(const FString& Host, int32 Port) = 0;
    virtual void Disconnect() = 0;
    virtual bool IsConnected() const = 0;

    virtual bool SendBytes(const uint8* Data, int32 Size) = 0;

    // Called on a worker thread by transport implementation
    TFunction<void(const uint8* Data, int32 Size)> OnBytesReceived;
    TFunction<void()> OnDisconnected;
    TFunction<void(int32, const FString&)> OnTransportError;
};
```

## Transport Implementations

### TCP
- Use `ISocketSubsystem`, `FSocket`, and `FTcpSocketBuilder`.
- Worker thread receives bytes and pushes to ring buffer.

### TLS (TCP + TLS)
- Prefer UE SSL module where supported (OpenSSL in UE).
- On consoles, use platform TLS socket or platform-specific secure transport APIs.
- Provide a build flag and a runtime capability check:
  - If TLS is unavailable, return error and fail fast with a clear message.

### WebSocket (WS/WSS)
- Use UE `WebSockets` module (libwebsockets).
- WSS uses TLS handled by WebSockets module.
- This path bypasses raw packet framing and uses binary messages.

## Packet Codec (Same as C++ Connector)

Request encoding:
```
ContentSize(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload
```

Response decoding:
```
ContentSize(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Payload
```

## Threading Model

- Transport runs on a dedicated worker thread.
- Received bytes are written to a ring buffer and decoded to packets.
- Decoded packets are queued and dispatched to GameThread via `AsyncTask(ENamedThreads::GameThread, ...)`.
- Requests map is protected by `FCriticalSection`.

## Request/Response Flow

1. `Request` assigns `MsgSeq` (>=1).
2. Pending map stores callback and timeout deadline.
3. Response with matching `MsgSeq` triggers callback and removes pending.
4. If timeout occurs, callback is invoked with a synthesized error packet.

## Configuration

```cpp
struct FPlayHouseConfig
{
    int32 SendBufferSize = 64 * 1024;
    int32 ReceiveBufferSize = 256 * 1024;
    int32 HeartbeatIntervalMs = 10000;
    int32 RequestTimeoutMs = 30000;

    bool bEnableReconnect = false;
    int32 ReconnectIntervalMs = 5000;
    int32 MaxReconnectAttempts = 0;

    enum class ETransport
    {
        Tcp,
        Tls,
        Ws,
        Wss
    } Transport = ETransport::Tcp;
};
```

## Platform Notes

- Consoles require platform-specific socket/TLS support.
- Avoid external dependencies beyond UE modules.
- Keep conditional compilation in transport layer only.

## No Blueprint Requirement

Blueprint nodes are optional and thin. Core API is C++-first and used directly in game code.

## Tasks Checklist

- Runtime module scaffolding
- Transport interface + TCP implementation
- WebSocket transport (WS/WSS)
- TLS transport (platform dependent)
- Packet codec + ring buffer
- Request map + timeout handling
- GameThread dispatch
- Minimal sample usage
