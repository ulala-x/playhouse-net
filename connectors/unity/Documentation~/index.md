# PlayHouse Unity Connector

PlayHouse real-time game server connector for Unity 2022 LTS.

## Install (Git URL)

Unity Package Manager -> Add package from git URL...

```
https://github.com/ulalax/playhouse.git?path=connectors/unity#v0.1.0
```

## Basic Usage

```csharp
using PlayHouse.Connector;
using UnityEngine;

public class PlayHouseClient : MonoBehaviour
{
    private Connector _connector;

    private void Start()
    {
        _connector = new Connector();
        _connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 10000,
            HeartbeatIntervalMs = 5000
        });

        _connector.OnConnect += OnConnected;
        _connector.OnReceive += OnReceive;
        _connector.OnError += OnError;
        _connector.OnDisconnect += OnDisconnected;

        _connector.Connect("127.0.0.1", 34001, 1, "TestStage");
    }

    private void Update()
    {
        _connector?.MainThreadAction();
    }

    private void OnDestroy()
    {
        _connector?.Disconnect();
    }

    private void OnConnected(bool success)
    {
        Debug.Log($"Connected: {success}");
    }

    private void OnReceive(long stageId, string stageType, IPacket packet)
    {
        Debug.Log($"Recv: {packet.MsgId}");
    }

    private void OnError(long stageId, string stageType, ushort errorCode, IPacket request)
    {
        Debug.LogError($"Error {errorCode}");
    }

    private void OnDisconnected()
    {
        Debug.Log("Disconnected");
    }
}
```

## Notes
- Call `MainThreadAction()` from `Update()` to dispatch callbacks on the Unity main thread.
- Use TCP/WebSocket connectors according to your server setup.
