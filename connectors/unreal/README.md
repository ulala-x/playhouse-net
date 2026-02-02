# PlayHouse Unreal Connector

Unreal Engine 5 plugin for PlayHouse real-time game server framework.

## Overview

- **Purpose**: Unreal Engine game client integration
- **Status**: Planned
- **Engine Version**: UE 5.1+
- **Implementation**: Native UE5 API (FSocket, FRunnable)

## Plugin Structure

```
connectors/unreal/
├── Source/
│   └── PlayHouseConnector/
│       ├── Public/
│       │   ├── PlayHouseConnector.h           # Module header
│       │   ├── PlayHouseConnectorSubsystem.h  # Game instance subsystem
│       │   ├── PlayHousePacket.h              # UObject packet wrapper
│       │   └── PlayHouseBPLibrary.h           # Blueprint function library
│       ├── Private/
│       │   ├── PlayHouseConnector.cpp
│       │   ├── PlayHouseConnectorSubsystem.cpp
│       │   ├── PlayHousePacket.cpp
│       │   └── PlayHouseBPLibrary.cpp
│       └── PlayHouseConnector.Build.cs
├── Resources/
│   └── Icon128.png
├── Config/
│   └── FilterPlugin.ini
├── PlayHouseConnector.uplugin
└── README.md
```

## Installation

### Option 1: Clone to Plugins Folder

```bash
# From your UE project root
cd Plugins
git clone https://github.com/user/playhouse.git --sparse
cd playhouse
git sparse-checkout set connectors/unreal connectors/cpp
```

### Option 2: Git Submodule

```bash
git submodule add https://github.com/user/playhouse.git Plugins/PlayHouse
```

### Option 3: Download Release

1. Download from [GitHub Releases](https://github.com/user/playhouse/releases)
2. Extract to `YourProject/Plugins/PlayHouseConnector/`

## Plugin Configuration

### PlayHouseConnector.uplugin

```json
{
    "FileVersion": 3,
    "Version": 1,
    "VersionName": "1.0.0",
    "FriendlyName": "PlayHouse Connector",
    "Description": "Real-time game server connector for PlayHouse framework",
    "Category": "Networking",
    "CreatedBy": "PlayHouse Team",
    "CreatedByURL": "https://github.com/user/playhouse",
    "DocsURL": "https://github.com/user/playhouse/blob/main/docs/connectors/unreal-guide.md",
    "MarketplaceURL": "",
    "SupportURL": "https://github.com/user/playhouse/issues",
    "CanContainContent": false,
    "IsBetaVersion": false,
    "IsExperimentalVersion": false,
    "Installed": false,
    "Modules": [
        {
            "Name": "PlayHouseConnector",
            "Type": "Runtime",
            "LoadingPhase": "Default",
            "PlatformAllowList": [
                "Win64",
                "Mac",
                "Linux",
                "Android",
                "IOS"
            ]
        }
    ]
}
```

### Build.cs

```csharp
using UnrealBuildTool;

public class PlayHouseConnector : ModuleRules
{
    public PlayHouseConnector(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

        PublicDependencyModuleNames.AddRange(new string[]
        {
            "Core",
            "CoreUObject",
            "Engine"
        });

        PrivateDependencyModuleNames.AddRange(new string[]
        {
            "Networking",   // FSocket, FTcpSocketBuilder
            "Sockets"       // ISocketSubsystem
        });

        // Optional: For LZ4 compression support
        // PrivateDependencyModuleNames.Add("lz4");
    }
}
```

## Technology Stack

| Component | UE5 API |
|-----------|---------|
| TCP Socket | FSocket, FTcpSocketBuilder |
| Threading | FRunnable, FRunnableThread |
| Async Tasks | FAsyncTask, AsyncTask() |
| Delegates | DECLARE_DYNAMIC_MULTICAST_DELEGATE |
| Serialization | FMemoryWriter, FMemoryReader |
| String | FString, TCHAR_TO_UTF8 |

## Internal Implementation

### Network Thread (FRunnable)

```cpp
// PlayHouseNetworkRunnable.h
class FPlayHouseNetworkRunnable : public FRunnable
{
public:
    FPlayHouseNetworkRunnable(const FString& Host, int32 Port);
    virtual ~FPlayHouseNetworkRunnable();

    // FRunnable interface
    virtual bool Init() override;
    virtual uint32 Run() override;
    virtual void Stop() override;

    // Thread-safe operations
    void SendPacket(TSharedPtr<FPlayHousePacketData> Packet);
    bool TryDequeueReceivedPacket(TSharedPtr<FPlayHousePacketData>& OutPacket);

private:
    FSocket* Socket;
    FString Host;
    int32 Port;
    TAtomic<bool> bStopping;

    // Thread-safe queues
    TQueue<TSharedPtr<FPlayHousePacketData>> SendQueue;
    TQueue<TSharedPtr<FPlayHousePacketData>> ReceiveQueue;

    void ProcessSendQueue();
    void ProcessReceive();
};
```

### Socket Connection (FSocket)

```cpp
// Connect using UE5 socket API
bool FPlayHouseNetworkRunnable::Init()
{
    ISocketSubsystem* SocketSubsystem = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM);

    Socket = SocketSubsystem->CreateSocket(NAME_Stream, TEXT("PlayHouse"), false);
    if (!Socket)
    {
        return false;
    }

    // Resolve address
    TSharedRef<FInternetAddr> Addr = SocketSubsystem->CreateInternetAddr();
    Addr->SetIp(*Host, bIsValid);
    Addr->SetPort(Port);

    // Connect
    return Socket->Connect(*Addr);
}
```

### Packet Serialization (FMemoryWriter)

```cpp
// Serialize packet to binary format
TArray<uint8> SerializePacket(const FPlayHousePacketData& Packet)
{
    TArray<uint8> Buffer;
    FMemoryWriter Writer(Buffer);

    // ContentSize placeholder (will be filled later)
    int32 ContentSizeOffset = Buffer.Num();
    int32 ContentSize = 0;
    Writer << ContentSize;

    // MsgIdLen + MsgId
    FTCHARToUTF8 MsgIdUtf8(*Packet.MsgId);
    uint8 MsgIdLen = FMath::Min((int32)MsgIdUtf8.Length(), 255);
    Writer << MsgIdLen;
    Writer.Serialize((void*)MsgIdUtf8.Get(), MsgIdLen);

    // MsgSeq (2 bytes, little-endian)
    Writer << Packet.MsgSeq;

    // StageId (8 bytes, little-endian)
    Writer << Packet.StageId;

    // Payload
    Writer.Serialize((void*)Packet.Payload.GetData(), Packet.Payload.Num());

    // Fill ContentSize
    ContentSize = Buffer.Num() - 4;
    FMemory::Memcpy(Buffer.GetData(), &ContentSize, sizeof(int32));

    return Buffer;
}
```

### GameThread Callback Dispatch

```cpp
// In Subsystem Tick (GameThread)
void UPlayHouseConnectorSubsystem::Tick(float DeltaTime)
{
    if (!NetworkRunnable) return;

    // Process received packets on GameThread
    TSharedPtr<FPlayHousePacketData> Packet;
    while (NetworkRunnable->TryDequeueReceivedPacket(Packet))
    {
        // Convert to UObject for Blueprint
        UPlayHousePacket* PacketObj = NewObject<UPlayHousePacket>(this);
        PacketObj->InitFromData(*Packet);

        // Broadcast to listeners
        OnMessageReceived.Broadcast(PacketObj);
    }
}
```

## C++ Usage

### Game Instance Subsystem

```cpp
// MyGameInstance.h
#pragma once

#include "CoreMinimal.h"
#include "Engine/GameInstance.h"
#include "PlayHouseConnectorSubsystem.h"
#include "MyGameInstance.generated.h"

UCLASS()
class MYGAME_API UMyGameInstance : public UGameInstance
{
    GENERATED_BODY()

public:
    virtual void Init() override;
    virtual void Shutdown() override;

    UFUNCTION(BlueprintCallable, Category = "Network")
    void ConnectToServer(const FString& Host, int32 Port);

    UFUNCTION(BlueprintCallable, Category = "Network")
    void Disconnect();

private:
    UFUNCTION()
    void OnConnected();

    UFUNCTION()
    void OnDisconnected();

    UFUNCTION()
    void OnMessageReceived(UPlayHousePacket* Packet);

    UFUNCTION()
    void OnError(int32 Code, const FString& Message);
};
```

```cpp
// MyGameInstance.cpp
#include "MyGameInstance.h"

void UMyGameInstance::Init()
{
    Super::Init();

    if (auto* Subsystem = GetSubsystem<UPlayHouseConnectorSubsystem>())
    {
        Subsystem->OnConnected.AddDynamic(this, &UMyGameInstance::OnConnected);
        Subsystem->OnDisconnected.AddDynamic(this, &UMyGameInstance::OnDisconnected);
        Subsystem->OnMessageReceived.AddDynamic(this, &UMyGameInstance::OnMessageReceived);
        Subsystem->OnError.AddDynamic(this, &UMyGameInstance::OnError);
    }
}

void UMyGameInstance::ConnectToServer(const FString& Host, int32 Port)
{
    if (auto* Subsystem = GetSubsystem<UPlayHouseConnectorSubsystem>())
    {
        Subsystem->Connect(Host, Port);
    }
}

void UMyGameInstance::OnConnected()
{
    UE_LOG(LogTemp, Log, TEXT("Connected to PlayHouse server!"));
}

void UMyGameInstance::OnMessageReceived(UPlayHousePacket* Packet)
{
    UE_LOG(LogTemp, Log, TEXT("Received message: %s"), *Packet->GetMsgId());
}
```

### Request-Response (Callback)

```cpp
void UMyGameInstance::SendEchoRequest(const FString& Message)
{
    if (auto* Subsystem = GetSubsystem<UPlayHouseConnectorSubsystem>())
    {
        // Create request
        EchoRequest Request;
        Request.set_content(TCHAR_TO_UTF8(*Message));
        Request.set_sequence(1);

        // Send request with callback (client only supports callback pattern)
        Subsystem->Request(
            TEXT("EchoRequest"),
            Request.SerializeAsString(),
            [this](UPlayHousePacket* Response)
            {
                if (Response->GetErrorCode() == 0)
                {
                    EchoReply Reply;
                    Reply.ParseFromArray(
                        Response->GetPayload().GetData(),
                        Response->GetPayload().Num());
                    UE_LOG(LogTemp, Log, TEXT("Echo reply: %s"),
                        UTF8_TO_TCHAR(Reply.content().c_str()));
                }
                else
                {
                    UE_LOG(LogTemp, Error, TEXT("Request failed: %d"),
                        Response->GetErrorCode());
                }
            });
    }
}
```

## Blueprint Usage

### Connect and Authenticate

![Blueprint Connect Example](docs/images/bp-connect.png)

```
Event BeginPlay
    → Get Game Instance
    → Get Subsystem (PlayHouseConnectorSubsystem)
    → Connect (Host: "localhost", Port: 34001)

On Connected (Event)
    → Authenticate (ServiceId: "game", AccountId: "player1")
```

### Send Request (Callback)

```
Custom Event: SendChatMessage
    Input: Message (String)
    → Create PlayHouse Packet (MsgId: "ChatMessage")
    → Set String Field (Key: "content", Value: Message)
    → Request (with Callback)
    → On Response: Print String (Response.MsgId)
```

### Handle Server Push

```
On Message Received (Event)
    Input: Packet (PlayHousePacket)
    → Switch on MsgId
        Case "PlayerJoined":
            → Parse PlayerJoined Proto
            → Spawn Player Actor
        Case "ChatMessage":
            → Parse ChatMessage Proto
            → Display Chat UI
```

## Blueprint Function Library

```cpp
UCLASS()
class PLAYHOUSECONNECTOR_API UPlayHouseBPLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()

public:
    UFUNCTION(BlueprintCallable, Category = "PlayHouse",
        meta = (WorldContext = "WorldContextObject"))
    static UPlayHouseConnectorSubsystem* GetConnector(UObject* WorldContextObject);

    UFUNCTION(BlueprintPure, Category = "PlayHouse|Packet")
    static FString GetPacketMsgId(UPlayHousePacket* Packet);

    UFUNCTION(BlueprintPure, Category = "PlayHouse|Packet")
    static int32 GetPacketErrorCode(UPlayHousePacket* Packet);

    UFUNCTION(BlueprintCallable, Category = "PlayHouse|Packet")
    static UPlayHousePacket* CreatePacket(const FString& MsgId);

    UFUNCTION(BlueprintCallable, Category = "PlayHouse|Packet")
    static void SetPacketPayload(UPlayHousePacket* Packet, const TArray<uint8>& Payload);
};
```

## Platform Support

| Platform | Status | Notes |
|----------|--------|-------|
| Windows (Win64) | ✅ Supported | Full TCP support |
| macOS | ✅ Supported | Full TCP support |
| Linux | ✅ Supported | Full TCP support |
| Android | ✅ Supported | TCP over WiFi/Mobile |
| iOS | ✅ Supported | TCP over WiFi/Mobile |
| Consoles | 🔄 Planned | Platform-specific socket APIs |

## Thread Safety

- All network I/O runs on a dedicated thread
- Callbacks are queued and dispatched on GameThread
- `UPlayHouseConnectorSubsystem::Tick()` processes the callback queue
- No explicit `MainThreadAction()` call needed (handled by subsystem)

## Memory and GC Considerations

`UPlayHousePacket` is a `UObject`, which means frequent packet creation can cause GC pressure:

### Recommended: Packet Pooling

```cpp
UCLASS()
class PLAYHOUSECONNECTOR_API UPlayHousePacketPool : public UObject
{
    GENERATED_BODY()

public:
    UFUNCTION(BlueprintCallable, Category = "PlayHouse")
    UPlayHousePacket* AcquirePacket();

    UFUNCTION(BlueprintCallable, Category = "PlayHouse")
    void ReleasePacket(UPlayHousePacket* Packet);

private:
    TArray<UPlayHousePacket*> Pool;
    FCriticalSection PoolLock;
};
```

### Alternative: FStruct-Based Packets

For high-frequency messages, consider using `FStruct` instead of `UObject`:

```cpp
USTRUCT(BlueprintType)
struct FPlayHousePacketData
{
    GENERATED_BODY()

    UPROPERTY(BlueprintReadOnly)
    FString MsgId;

    UPROPERTY(BlueprintReadOnly)
    int32 MsgSeq;

    UPROPERTY(BlueprintReadOnly)
    int64 StageId;

    UPROPERTY(BlueprintReadOnly)
    int32 ErrorCode;

    UPROPERTY(BlueprintReadOnly)
    TArray<uint8> Payload;
};
```

**When to use which**:
| Approach | Use Case |
|----------|----------|
| UPlayHousePacket (UObject) | Blueprint-heavy code, infrequent messages |
| FPlayHousePacketData (FStruct) | C++ code, high-frequency messages |
| Packet Pool | Mixed usage, medium frequency |

## Packet/Proto Helper API

### Creating Packets from Proto

```cpp
// Helper function to create packet from protobuf message
template<typename T>
UPlayHousePacket* CreatePacketFromProto(UObject* Outer, const T& ProtoMessage)
{
    UPlayHousePacket* Packet = NewObject<UPlayHousePacket>(Outer);

    // Get message name from proto descriptor
    Packet->SetMsgId(UTF8_TO_TCHAR(ProtoMessage.GetTypeName().c_str()));

    // Serialize proto to bytes
    std::string SerializedData;
    ProtoMessage.SerializeToString(&SerializedData);

    TArray<uint8> Payload;
    Payload.Append(reinterpret_cast<const uint8*>(SerializedData.data()),
                   SerializedData.size());
    Packet->SetPayload(Payload);

    return Packet;
}

// Usage
EchoRequest Request;
Request.set_content("Hello");
Request.set_sequence(1);

UPlayHousePacket* Packet = CreatePacketFromProto(this, Request);
Subsystem->Request(Packet, [](UPlayHousePacket* Response) {
    // Handle response
});
```

### Parsing Proto from Response

```cpp
template<typename T>
bool ParseProtoFromPacket(const UPlayHousePacket* Packet, T& OutProto)
{
    const TArray<uint8>& Payload = Packet->GetPayload();
    return OutProto.ParseFromArray(Payload.GetData(), Payload.Num());
}

// Usage
void OnResponse(UPlayHousePacket* Response)
{
    if (Response->GetErrorCode() == 0)
    {
        EchoReply Reply;
        if (ParseProtoFromPacket(Response, Reply))
        {
            UE_LOG(LogTemp, Log, TEXT("Reply: %s"),
                UTF8_TO_TCHAR(Reply.content().c_str()));
        }
    }
}
```

## Configuration

### Project Settings

Edit `Config/DefaultGame.ini`:

```ini
[/Script/PlayHouseConnector.PlayHouseSettings]
DefaultHost=localhost
DefaultPort=34001
HeartbeatIntervalMs=10000
RequestTimeoutMs=30000
EnableReconnect=false
ReconnectIntervalMs=5000
```

### Runtime Configuration

```cpp
FPlayHouseConfig Config;
Config.HeartbeatIntervalMs = 5000;
Config.RequestTimeoutMs = 10000;
Config.EnableReconnect = true;

Subsystem->Initialize(Config);
```

## Development Tasks

| Phase | Tasks |
|-------|-------|
| Core | Plugin scaffolding, C++ connector integration |
| System | Subsystem implementation, callback routing |
| Blueprint | Blueprint exposure, BP function library |
| Desktop | Platform testing (Win/Mac/Linux) |
| Mobile | Mobile platform support (Android/iOS) |
| Release | Documentation, example project, Marketplace submission |

## Distribution

| Channel | Notes |
|---------|-------|
| GitHub Releases | Source code (pure UE5 plugin) |
| UE Marketplace | Under consideration |

**No external binaries required** - Uses only UE5 native APIs (Networking, Sockets modules).

## Dedicated Server Build

For Dedicated Server (no rendering, headless):

### Build.cs Configuration

```csharp
if (Target.Type == TargetType.Server)
{
    // Server-specific optimizations
    PublicDefinitions.Add("PLAYHOUSE_SERVER=1");
}
```

### Server-Specific Usage

```cpp
// Check if running as dedicated server
#if UE_SERVER
    // Server-side logic
    // Note: Usually servers don't need client connector
    // Consider using server-side SDK instead
#else
    // Client-side connector usage
    Subsystem->Connect(Host, Port);
#endif
```

### Packaging for Dedicated Server

```bash
# Package server build
RunUAT.bat BuildCookRun -project=MyGame.uproject -platform=Linux -serverconfig=Development -server -pak -stage -archive
```

**Important**: For dedicated server, typically use the server-side PlayHouse SDK (`servers/cpp/`) instead of this client connector.

## Troubleshooting

### Plugin Not Loading

1. Check `Plugins/PlayHouseConnector/` structure
2. Verify `.uplugin` file exists
3. Check Output Log for loading errors

### Connection Fails

```cpp
// Enable verbose logging
UE_SET_LOG_VERBOSITY(LogPlayHouse, Verbose);
```

### Callbacks Not Firing

Ensure the subsystem is properly initialized:
```cpp
if (auto* Subsystem = GetGameInstance()->GetSubsystem<UPlayHouseConnectorSubsystem>())
{
    // Subsystem is valid
}
```

## References

- [C# Connector](../csharp/) - Reference implementation
- [Protocol Specification](../../docs/architecture/protocol-spec.md)
- [UE5 Plugin Development](https://docs.unrealengine.com/5.0/en-US/plugins-in-unreal-engine/)
- [UE5 Networking](https://docs.unrealengine.com/5.0/en-US/networking-and-multiplayer-in-unreal-engine/)
- [Proto Message Definitions](../../protocol/)
- [CHANGELOG](./CHANGELOG.md)

## License

Apache 2.0 with Commons Clause
