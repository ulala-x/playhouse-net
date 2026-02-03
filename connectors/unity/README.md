# PlayHouse Unity Connector

Unity package for PlayHouse real-time game server framework.

## Overview

- **Purpose**: Unity game client integration
- **Status**: Ready (uses existing C# Connector)
- **Unity Version**: 2020.3+
- **Source**: [connectors/csharp/](../csharp/)

## Package Structure

```
connectors/unity/
├── Runtime/
│   ├── PlayHouse.Connector.asmdef
│   └── Scripts/
│       └── UnityConnectorExtensions.cs  # Unity-specific helpers
├── Samples~/
│   └── BasicUsage/
│       ├── ConnectExample.cs
│       └── ChatExample.unity
├── Documentation~/
│   └── manual.md
├── package.json
├── CHANGELOG.md
└── LICENSE.md
```

## Installation

### Option 1: Git URL (Recommended)

In Unity Package Manager:
1. Click "+" → "Add package from git URL..."
2. Enter: `https://github.com/user/playhouse.git?path=connectors/unity`

Or add to `Packages/manifest.json`:
```json
{
  "dependencies": {
    "com.playhouse.connector": "https://github.com/user/playhouse.git?path=connectors/unity"
  }
}
```

### Option 2: OpenUPM

```bash
openupm add com.playhouse.connector
```

### Option 3: Local Development

1. Clone repository
2. In Unity Package Manager → "Add package from disk..."
3. Select `connectors/unity/package.json`

## package.json

```json
{
  "name": "com.playhouse.connector",
  "version": "1.0.0",
  "displayName": "PlayHouse Connector",
  "description": "Real-time game server connector for Unity",
  "unity": "2020.3",
  "unityRelease": "0f1",
  "documentationUrl": "https://github.com/user/playhouse/blob/main/docs/connectors/unity-guide.md",
  "changelogUrl": "https://github.com/user/playhouse/blob/main/connectors/unity/CHANGELOG.md",
  "licensesUrl": "https://github.com/user/playhouse/blob/main/LICENSE",
  "keywords": [
    "network",
    "multiplayer",
    "realtime",
    "game-server"
  ],
  "author": {
    "name": "PlayHouse Team",
    "url": "https://github.com/user/playhouse"
  },
  "dependencies": {}
}
```

## Usage

### Basic Connection

```csharp
using PlayHouse.Connector;
using UnityEngine;

public class GameNetwork : MonoBehaviour
{
    private ClientConnector _connector;

    void Start()
    {
        _connector = new ClientConnector();
        _connector.Init(new ConnectorConfig
        {
            HeartbeatIntervalMs = 5000,
            RequestTimeoutMs = 10000
        });

        // Register callbacks
        _connector.OnConnect += OnConnected;
        _connector.OnReceive += OnMessageReceived;
        _connector.OnError += OnError;
        _connector.OnDisconnect += OnDisconnected;
    }

    void Update()
    {
        // IMPORTANT: Process callbacks on Unity main thread
        _connector?.MainThreadAction();
    }

    void OnDestroy()
    {
        _connector?.Disconnect();
    }

    public void Connect(string host, int port)
    {
        _connector.Connect(host, port);  // OnConnect callback will fire on success
    }

    private void OnConnected()
    {
        Debug.Log("Connected to server!");
    }

    private void OnMessageReceived(IPacket packet)
    {
        Debug.Log($"Received: {packet.MsgId}");
    }

    private void OnError(int code, string message)
    {
        Debug.LogError($"Error {code}: {message}");
    }

    private void OnDisconnected()
    {
        Debug.Log("Disconnected from server");
    }
}
```

### Authentication

```csharp
public void Authenticate(string serviceId, string accountId, byte[] token)
{
    _connector.Authenticate(serviceId, accountId, token, success =>
    {
        if (success)
        {
            Debug.Log("Authentication successful!");
        }
        else
        {
            Debug.LogError("Authentication failed!");
        }
    });
}
```

### Request-Response Pattern (Callback)

```csharp
public void SendEchoRequest(string message)
{
    var request = new EchoRequest { Content = message, Sequence = 1 };
    using var packet = new Packet(request);

    _connector.Request(packet, response =>
    {
        if (response.ErrorCode == 0)
        {
            var echoReply = EchoReply.Parser.ParseFrom(response.Payload);
            Debug.Log($"Echo reply: {echoReply.Content}");
        }
        else
        {
            Debug.LogError($"Request failed: {response.ErrorCode}");
        }
    });
}
```

### Fire-and-Forget (Send)

```csharp
public void SendPlayerPosition(Vector3 position)
{
    var positionUpdate = new PlayerPosition
    {
        X = position.x,
        Y = position.y,
        Z = position.z
    };
    using var packet = new Packet(positionUpdate);

    _connector.Send(packet);  // No response expected
}
```

### Server Push Handling

```csharp
private void OnMessageReceived(IPacket packet)
{
    switch (packet.MsgId)
    {
        case "PlayerJoined":
            var joined = PlayerJoined.Parser.ParseFrom(packet.Payload);
            HandlePlayerJoined(joined);
            break;

        case "ChatMessage":
            var chat = ChatMessage.Parser.ParseFrom(packet.Payload);
            HandleChatMessage(chat);
            break;

        default:
            Debug.LogWarning($"Unknown message: {packet.MsgId}");
            break;
    }
}
```

## Platform Support

| Platform | Transport | Notes |
|----------|-----------|-------|
| Windows/Mac/Linux | TCP / TLS / WS / WSS | Full support |
| Android | TCP / TLS / WS / WSS | Full support |
| iOS | TCP / TLS / WS / WSS | Full support |
| WebGL | WS / WSS | Requires WebSocket gateway |

### WebGL Considerations

WebGL does not support raw TCP sockets. For WebGL:

1. Server must expose a WebSocket endpoint
2. Use WebSocket connection instead:

```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
    _connector.ConnectWebSocket("wss://game.example.com/ws");
#else
    _connector.Connect("game.example.com", 34001);
#endif
```

**WebSocket Gateway Requirements**:
- Gateway must run on HTTPS (wss://) for production
- Configure CORS headers for cross-origin requests
- Gateway converts WebSocket frames to PlayHouse TCP protocol
- Each WebSocket message = One complete PlayHouse packet

**Recommended Gateway**:
- Node.js with `ws` library
- nginx with WebSocket proxy
- See `examples/websocket-gateway/` for reference implementation

### IL2CPP Build Considerations

For IL2CPP builds (iOS, Android, WebGL):

```csharp
// Ensure proto types are not stripped
// Add to link.xml in Assets folder:
```

```xml
<!-- link.xml -->
<linker>
    <assembly fullname="PlayHouse.Connector" preserve="all"/>
    <assembly fullname="Google.Protobuf" preserve="all"/>
</linker>
```

**AOT Compilation Notes**:
- Avoid reflection-based proto parsing when possible
- Use `Parser.ParseFrom()` instead of dynamic deserialization
- Test thoroughly on target platform

## MonoBehaviour Lifecycle Integration

For automatic connection management with Unity lifecycle:

```csharp
using PlayHouse.Connector;
using UnityEngine;

/// <summary>
/// Base class for network-aware MonoBehaviours.
/// Automatically manages connection lifecycle with OnEnable/OnDisable.
/// </summary>
public abstract class NetworkBehaviour : MonoBehaviour
{
    protected ClientConnector Connector { get; private set; }

    [SerializeField] private string _host = "localhost";
    [SerializeField] private int _port = 34001;
    [SerializeField] private bool _autoConnect = true;

    protected virtual void Awake()
    {
        Connector = new ClientConnector();
        Connector.Init(new ConnectorConfig
        {
            HeartbeatIntervalMs = 10000,
            RequestTimeoutMs = 30000
        });

        Connector.OnConnect += OnNetworkConnected;
        Connector.OnDisconnect += OnNetworkDisconnected;
        Connector.OnReceive += OnNetworkMessage;
        Connector.OnError += OnNetworkError;
    }

    protected virtual void OnEnable()
    {
        if (_autoConnect && !Connector.IsConnected)
        {
            Connector.Connect(_host, _port);
        }
    }

    protected virtual void OnDisable()
    {
        if (Connector.IsConnected)
        {
            Connector.Disconnect();
        }
    }

    protected virtual void Update()
    {
        Connector?.MainThreadAction();
    }

    protected virtual void OnDestroy()
    {
        Connector?.Disconnect();
        Connector = null;
    }

    // Override these in derived classes
    protected virtual void OnNetworkConnected() { }
    protected virtual void OnNetworkDisconnected() { }
    protected virtual void OnNetworkMessage(IPacket packet) { }
    protected virtual void OnNetworkError(int code, string message) { }
}
```

Usage example:

```csharp
public class GameNetworkManager : NetworkBehaviour
{
    protected override void OnNetworkConnected()
    {
        Debug.Log("Connected! Starting authentication...");
        Authenticate();
    }

    protected override void OnNetworkMessage(IPacket packet)
    {
        switch (packet.MsgId)
        {
            case "ChatMessage":
                HandleChatMessage(packet);
                break;
        }
    }

    private void Authenticate()
    {
        var token = GetAuthToken();
        Connector.Authenticate("game", PlayerId, token, success =>
        {
            if (success) Debug.Log("Authenticated!");
            else Debug.LogError("Authentication failed!");
        });
    }
}
```

## Best Practices

### 1. Always Call MainThreadAction()

```csharp
void Update()
{
    // This processes queued callbacks on the Unity main thread
    _connector?.MainThreadAction();
}
```

### 2. Use Callback Pattern for Requests

```csharp
// Request with callback - responses handled on main thread via MainThreadAction()
public void SendMessage(string content)
{
    var request = new ChatMessage { Content = content };
    using var packet = new Packet(request);

    _connector.Request(packet, response =>
    {
        if (response.ErrorCode == 0)
        {
            Debug.Log("Message sent successfully");
        }
        else
        {
            Debug.LogWarning($"Request failed: {response.ErrorCode}");
        }
    });
}
```

### 3. Dispose Packets

```csharp
// Using statement ensures disposal
public void SendRequest()
{
    using var packet = new Packet(request);
    _connector.Request(packet, OnResponse);
}

// Or manually dispose
public void SendRequestManual()
{
    var packet = new Packet(request);
    _connector.Request(packet, response =>
    {
        // Handle response
        packet.Dispose();
    });
}
```

### 4. Handle Reconnection

```csharp
private void OnDisconnected()
{
    if (_shouldReconnect)
    {
        StartCoroutine(ReconnectAfterDelay(5f));
    }
}

private IEnumerator ReconnectAfterDelay(float seconds)
{
    yield return new WaitForSeconds(seconds);
    _connector.Connect(_host, _port);
}
```

## Compatibility

- **Unity**: 2020.3 LTS and newer
- **.NET**: netstandard2.1
- **Dependencies**: None (pure C#)

## Samples

### Basic Usage Sample

Located at `Samples~/BasicUsage/`:

- `ConnectExample.cs` - Basic connection and messaging
- `ChatExample.unity` - Simple chat room scene

To import samples:
1. Open Package Manager
2. Find "PlayHouse Connector"
3. Click "Samples" → "Import"

## Troubleshooting

### Callbacks Not Firing

Make sure you're calling `MainThreadAction()` in Update:
```csharp
void Update()
{
    _connector?.MainThreadAction();
}
```

### Connection Timeout

Check firewall settings and server availability:
```csharp
_connector.Init(new ConnectorConfig
{
    RequestTimeoutMs = 60000  // Increase timeout to 60s
});
```

### WebGL Build Fails

Ensure you're using WebSocket for WebGL:
```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
    // WebSocket connection
#endif
```

## Protocol and Message Definitions

Message types are defined in Protocol Buffers format:

```
protocol/
├── messages.proto         # Common message definitions
├── game_messages.proto    # Game-specific messages
└── generated/
    └── csharp/           # Generated C# classes
```

To regenerate proto classes:
```bash
# From repository root
./tools/proto-gen/generate-csharp.sh
```

## References

- [C# Connector Source](../csharp/)
- [API Documentation](../../docs/connectors/unity-guide.md)
- [Protocol Specification](../../docs/architecture/protocol-spec.md)
- [Proto Message Definitions](../../protocol/)
- [CHANGELOG](./CHANGELOG.md)

## License

Apache 2.0 with Commons Clause
