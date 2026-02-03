# Unreal WS/WSS Connector Setup (UE 5.3+)

This note summarizes how to create and use WebSocket (WS) / Secure WebSocket (WSS) connectors in Unreal Engine 5.3+.

## 1) Build.cs modules

The connector module must include `WebSockets` and `SSL` in private dependencies:

```csharp
PrivateDependencyModuleNames.AddRange(new string[]
{
    "Sockets",
    "Networking",
    "WebSockets",
    "SSL"
});
```

## 2) Basic connector usage

Create the socket with `FWebSocketsModule::Get().CreateWebSocket(...)` and bind callbacks:

```cpp
#include "IWebSocket.h"
#include "WebSocketsModule.h"

TSharedPtr<IWebSocket> Socket;
const FString Url = TEXT("wss://127.0.0.1:8443/ws");

Socket = FWebSocketsModule::Get().CreateWebSocket(Url);
Socket->OnConnected().AddLambda([]() { /* connected */ });
Socket->OnConnectionError().AddLambda([](const FString& Error) { /* error */ });
Socket->OnClosed().AddLambda([](int32 Code, const FString& Reason, bool bClean) { /* closed */ });
Socket->OnRawMessage().AddLambda([](const void* Data, SIZE_T Size, SIZE_T /*Remaining*/) {
    /* handle bytes */
});

Socket->Connect();
```

## 3) WSS (TLS) + self-signed certs (local test)

For local self-signed certificates, the built-in LWS client will reject the server by default.
To disable certificate validation for **local testing only**, add to project config:

```ini
[LwsWebSocket]
bDisableCertValidation=true
```

Recommended location:
- Project: `Config/DefaultEngine.ini`

## 4) Runtime notes

- The WebSockets module ticks on the game thread via UE's ticker.
- Automation tests that block ticks can cause WS/WSS connects to stall.

## 5) PlayHouse test server

The PlayHouse test server uses:
- WS: `ws://<host>:8080/ws`
- WSS: `wss://<host>:8443/ws`

Make sure TLS and WebSocket are enabled in the test-server environment.
