# PlayHouse Unreal Connector

Unreal Engine 5 plugin for the PlayHouse real-time game server framework.

## Overview

- **Purpose**: Unreal Engine client connector for PlayHouse
- **Status**: In progress
- **Engine Version**: UE 5.3+
- **Targets**: All platforms (including consoles)
- **Transports**: TCP, TLS, WS, WSS
- **API**: C++ first, callback-only (no coroutine/future API)

## Development Plan

### Phase 0: Requirements and Constraints

- UE 5.3+ Runtime plugin only.
- All platforms, including consoles.
- Transport support: TCP / TLS / WS / WSS.
- No coroutine/future API. Callback-based request/response only.
- UE-native networking (no ASIO/Boost dependencies).

### Phase 1: Core Extraction (Reusable Logic)

Goal: reuse stable logic from `connectors/cpp` without UE dependencies.

- Packet model and protocol constants.
- Packet codec (encode request / decode response).
- Ring buffer for stream framing.
- Request map with timeout tracking.

Notes:
- Keep this code in a small internal module folder inside the plugin for now.
- Ensure all logic is engine-agnostic and unit-testable outside UE.

### Phase 2: Transport Layer (UE-Native)

Goal: implement platform-safe transports using UE modules.

- **TCP**: `FSocket`, `ISocketSubsystem`, `FTcpSocketBuilder`.
- **TLS**: UE SSL module (OpenSSL) where supported, platform TLS on consoles.
- **WS/WSS**: UE `WebSockets` module (libwebsockets).

Constraints:
- All platform-specific code isolated per transport.
- TLS availability must be detected and reported clearly.

### Phase 3: Connector API (C++ First)

Goal: expose a single C++ connector API with GameThread callbacks.

API surface:
- `Init(config)`
- `Connect(host, port)`
- `Disconnect()`
- `IsConnected()`
- `Send(packet)`
- `Request(packet, callback)`
- `Authenticate(packet, callback)`
- callbacks: `OnConnect`, `OnDisconnect`, `OnReceive`, `OnError`

Rules:
- All callbacks must execute on GameThread.
- Request timeout returns a synthetic error response.

### Phase 4: Threading and Dispatch

Goal: stable threading model and safe callback dispatch.

- Dedicated network thread per connection.
- Lock-free or minimal-lock queues for IO to GameThread.
- `AsyncTask(ENamedThreads::GameThread, ...)` for callbacks.

### Phase 5: Blueprint Optional Layer (Thin Wrapper)

Goal: optional blueprint support without impacting C++ users.

- `UPlayHouseConnectorSubsystem` (GameInstanceSubsystem).
- Minimal `UFUNCTION` wrappers for `Connect`, `Disconnect`, `Send`, `Request`.
- No Blueprint-specific logic in core networking.

### Phase 6: Tests (Pre-Release Quality)

Goal: ensure reliability before distribution.

1) **Core logic tests (PC/CI)**\n
   - Packet codec, ring buffer, request map, timeout handling.
2) **E2E networking tests (PC/CI)**\n
   - Connect/Send/Request/Push for TCP/WS/WSS/TLS.
3) **Console smoke tests**\n
   - Minimal handshake + request/response per transport.

### Phase 7: Packaging and Delivery

Goal: source plugin delivery with clear integration guide.

- Source plugin only (no binaries).
- Minimal sample usage.
- Compatibility notes per platform.

## Repository Layout (Planned)

```
connectors/unreal/
├── Source/
│   └── PlayHouseConnector/
│       ├── Public/
│       ├── Private/
│       │   ├── Transports/
│       │   └── Internal/
│       └── PlayHouseConnector.Build.cs
├── PlayHouseConnector.uplugin
└── README.md
```

## References

- `connectors/cpp` (core logic reference)
- `protocol/` (message definitions)
- `connectors/unreal/doc/docker-e2e.md` (Docker-based test workflow)
- `connectors/unreal/doc/ue-install-wsl-windows.md` (UE install guide)

## License

Apache 2.0 with Commons Clause
