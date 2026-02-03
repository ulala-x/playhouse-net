#include "CoreMinimal.h"
#include "Misc/AutomationTest.h"
#include "HAL/PlatformMisc.h"
#include "Containers/BackgroundableTicker.h"
#include "IWebSocket.h"
#include "WebSocketsModule.h"

#include "PlayHouseConnector.h"
#include "PlayHouseConfig.h"
#include "PlayHousePacket.h"

#if WITH_AUTOMATION_TESTS

namespace
{
    void AppendVarint(TArray<uint8>& Out, uint32 Value)
    {
        while (Value >= 0x80)
        {
            Out.Add(static_cast<uint8>((Value & 0x7F) | 0x80));
            Value >>= 7;
        }
        Out.Add(static_cast<uint8>(Value));
    }

    TArray<uint8> BuildEchoRequestPayload(const FString& Content, int32 Sequence)
    {
        // Protobuf wire format for EchoRequest:
        // field 1 (string content) -> tag 0x0A, length-delimited
        // field 2 (int32 sequence) -> tag 0x10, varint
        TArray<uint8> Payload;
        FTCHARToUTF8 Utf8(*Content);

        Payload.Add(0x0A);
        AppendVarint(Payload, static_cast<uint32>(Utf8.Length()));
        Payload.Append(reinterpret_cast<const uint8*>(Utf8.Get()), Utf8.Length());

        Payload.Add(0x10);
        AppendVarint(Payload, static_cast<uint32>(Sequence));

        return Payload;
    }

    TArray<uint8> BuildAuthenticateRequestPayload(const FString& UserId, const FString& Token)
    {
        // Protobuf wire format for AuthenticateRequest:
        // field 1 (string user_id) -> tag 0x0A
        // field 2 (string token)   -> tag 0x12
        TArray<uint8> Payload;
        FTCHARToUTF8 UserIdUtf8(*UserId);
        FTCHARToUTF8 TokenUtf8(*Token);

        Payload.Add(0x0A);
        AppendVarint(Payload, static_cast<uint32>(UserIdUtf8.Length()));
        Payload.Append(reinterpret_cast<const uint8*>(UserIdUtf8.Get()), UserIdUtf8.Length());

        Payload.Add(0x12);
        AppendVarint(Payload, static_cast<uint32>(TokenUtf8.Length()));
        Payload.Append(reinterpret_cast<const uint8*>(TokenUtf8.Get()), TokenUtf8.Length());

        return Payload;
    }

    FString GetEnvOrDefault(const TCHAR* Key, const FString& DefaultValue)
    {
        FString Value = FPlatformMisc::GetEnvironmentVariable(Key);
        return Value.IsEmpty() ? DefaultValue : Value;
    }

    int32 GetEnvInt(const TCHAR* Key, int32 DefaultValue)
    {
        FString Value = GetEnvOrDefault(Key, FString::FromInt(DefaultValue));
        return FCString::Atoi(*Value);
    }

    bool IsTlsEnabled()
    {
        const FString Value = GetEnvOrDefault(TEXT("TEST_SERVER_ENABLE_TLS"), TEXT("true"));
        if (Value.Equals(TEXT("false"), ESearchCase::IgnoreCase) || Value == TEXT("0"))
        {
            return false;
        }
        return true;
    }

    bool IsWebSocketEnabled()
    {
        const FString Value = GetEnvOrDefault(TEXT("TEST_SERVER_ENABLE_WS"), TEXT("true"));
        if (Value.Equals(TEXT("false"), ESearchCase::IgnoreCase) || Value == TEXT("0"))
        {
            return false;
        }
        return true;
    }

    struct FTestContext
    {
        FPlayHouseConnector Connector;
        bool Connected = false;
        bool ResponseReady = false;
        bool Error = false;
        FPlayHousePacket Response;
    };

    bool WaitUntil(TFunctionRef<bool()> Predicate, double TimeoutSeconds)
    {
        const double Deadline = FPlatformTime::Seconds() + TimeoutSeconds;
        double LastTickSeconds = FPlatformTime::Seconds();
        while (FPlatformTime::Seconds() < Deadline)
        {
            const double NowSeconds = FPlatformTime::Seconds();
            const float DeltaSeconds = static_cast<float>(NowSeconds - LastTickSeconds);
            LastTickSeconds = NowSeconds;

            FTSBackgroundableTicker::GetCoreTicker().Tick(DeltaSeconds);
            if (Predicate())
            {
                return true;
            }
            FPlatformProcess::Sleep(0.01f);
        }
        return false;
    }

    bool RunEchoE2E(EPlayHouseTransport TransportType, int32 Port)
    {
        FTestContext Ctx;
        FPlayHouseConfig Config;
        Config.Transport = TransportType;
        Ctx.Connector.Init(Config);

        Ctx.Connector.OnConnect = [&]() {
            Ctx.Connected = true;
            UE_LOG(LogTemp, Log, TEXT("[PlayHouseTest] Connected"));
        };
        Ctx.Connector.OnReceive = [&](const FPlayHousePacket& Packet) {
            UE_LOG(LogTemp, Warning, TEXT("[PlayHouseTest] Unhandled packet MsgId=%s MsgSeq=%d ErrorCode=%d StageId=%lld Payload=%d"),
                *Packet.MsgId, Packet.MsgSeq, Packet.ErrorCode, Packet.StageId, Packet.Payload.Num());
        };
        Ctx.Connector.OnError = [&](int32 Code, const FString& Message) {
            Ctx.Error = true;
            UE_LOG(LogTemp, Warning, TEXT("[PlayHouseTest] Error %d: %s"), Code, *Message);
        };

        const FString Host = GetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
        Ctx.Connector.Connect(Host, Port);

        bool Connected = WaitUntil([&]() { return Ctx.Connected || Ctx.Error; }, 5.0);
        if (!Connected || !Ctx.Connected)
        {
            UE_LOG(LogTemp, Warning, TEXT("[PlayHouseTest] Connect timeout or error"));
            return false;
        }

        FPlayHousePacket AuthPacket;
        AuthPacket.MsgId = TEXT("AuthenticateRequest");
        AuthPacket.Payload = BuildAuthenticateRequestPayload(TEXT("test-user"), TEXT("valid_token"));

        bool AuthDone = false;
        bool AuthOk = false;
        Ctx.Connector.Authenticate(MoveTemp(AuthPacket), [&](bool Result) {
            AuthOk = Result;
            AuthDone = true;
        });

        bool AuthFinished = WaitUntil([&]() { return AuthDone; }, 5.0);
        if (!AuthFinished || !AuthOk)
        {
            UE_LOG(LogTemp, Warning, TEXT("[PlayHouseTest] Authenticate failed"));
            return false;
        }

        FPlayHousePacket Packet;
        Packet.MsgId = TEXT("EchoRequest");
        Packet.Payload = BuildEchoRequestPayload(TEXT("Hello"), 1);

        Ctx.Connector.Request(MoveTemp(Packet), [&](FPlayHousePacket&& Response) {
            Ctx.Response = MoveTemp(Response);
            Ctx.ResponseReady = true;
            UE_LOG(LogTemp, Log, TEXT("[PlayHouseTest] Response MsgId=%s ErrorCode=%d"), *Ctx.Response.MsgId, Ctx.Response.ErrorCode);
        });

        bool GotResponse = WaitUntil([&]() { return Ctx.ResponseReady; }, 5.0);
        if (!GotResponse)
        {
            UE_LOG(LogTemp, Warning, TEXT("[PlayHouseTest] Response timeout"));
            return false;
        }

        return Ctx.Response.MsgId == TEXT("EchoReply") && Ctx.Response.ErrorCode == 0;
    }

    bool RunWebSocketSmoke(bool bSecure, const FString& Host, int32 Port, double TimeoutSeconds)
    {
        FString Url = FString::Printf(TEXT("%s://%s:%d/ws"), bSecure ? TEXT("wss") : TEXT("ws"), *Host, Port);

        if (!FModuleManager::Get().IsModuleLoaded(TEXT("WebSockets")))
        {
            FModuleManager::Get().LoadModule(TEXT("WebSockets"));
        }

        TSharedPtr<IWebSocket> Socket = FWebSocketsModule::Get().CreateWebSocket(Url);
        if (!Socket.IsValid())
        {
            UE_LOG(LogTemp, Warning, TEXT("[PlayHouseTest] WebSocket create failed: %s"), *Url);
            return false;
        }

        bool Connected = false;
        bool Closed = false;
        bool Error = false;

        Socket->OnConnected().AddLambda([&]() {
            Connected = true;
            Socket->Close();
        });

        Socket->OnConnectionError().AddLambda([&](const FString& InError) {
            Error = true;
            UE_LOG(LogTemp, Warning, TEXT("[PlayHouseTest] WebSocket error: %s"), *InError);
        });

        Socket->OnClosed().AddLambda([&](int32 /*Code*/, const FString& /*Reason*/, bool /*bClean*/) {
            Closed = true;
        });

        Socket->Connect();

        if (!WaitUntil([&]() { return Connected || Error; }, TimeoutSeconds))
        {
            UE_LOG(LogTemp, Warning, TEXT("[PlayHouseTest] WebSocket connect timeout"));
            return false;
        }

        if (Error || !Connected)
        {
            return false;
        }

        if (!WaitUntil([&]() { return Closed || Error; }, TimeoutSeconds))
        {
            UE_LOG(LogTemp, Warning, TEXT("[PlayHouseTest] WebSocket close timeout"));
            return false;
        }

        return !Error;
    }
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseTcpE2E, "PlayHouse.E2E.Tcp", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FPlayHouseTcpE2E::RunTest(const FString& Parameters)
{
    const int32 Port = GetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("TCP E2E"), RunEchoE2E(EPlayHouseTransport::Tcp, Port));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseWsE2E, "PlayHouse.E2E.Ws", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FPlayHouseWsE2E::RunTest(const FString& Parameters)
{
    if (!IsWebSocketEnabled())
    {
        AddWarning(TEXT("WebSocket disabled (TEST_SERVER_ENABLE_WS=false). Skipping WS E2E."));
        return true;
    }
    const int32 Port = GetEnvInt(TEXT("TEST_SERVER_HTTP_PORT"), 8080);
    TestTrue(TEXT("WS E2E"), RunEchoE2E(EPlayHouseTransport::Ws, Port));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseWsSmoke, "PlayHouse.E2E.WsSmoke", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FPlayHouseWsSmoke::RunTest(const FString& Parameters)
{
    if (!IsWebSocketEnabled())
    {
        AddWarning(TEXT("WebSocket disabled (TEST_SERVER_ENABLE_WS=false). Skipping WS smoke."));
        return true;
    }
    const FString Host = GetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
    const int32 Port = GetEnvInt(TEXT("TEST_SERVER_HTTP_PORT"), 8080);
    TestTrue(TEXT("WS Smoke"), RunWebSocketSmoke(false, Host, Port, 5.0));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseWssE2E, "PlayHouse.E2E.Wss", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FPlayHouseWssE2E::RunTest(const FString& Parameters)
{
    if (!IsTlsEnabled())
    {
        AddWarning(TEXT("TLS disabled (TEST_SERVER_ENABLE_TLS=false). Skipping WSS E2E."));
        return true;
    }
    const int32 Port = GetEnvInt(TEXT("TEST_SERVER_HTTPS_PORT"), 8443);
    TestTrue(TEXT("WSS E2E"), RunEchoE2E(EPlayHouseTransport::Wss, Port));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseWssSmoke, "PlayHouse.E2E.WssSmoke", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FPlayHouseWssSmoke::RunTest(const FString& Parameters)
{
    if (!IsTlsEnabled())
    {
        AddWarning(TEXT("TLS disabled (TEST_SERVER_ENABLE_TLS=false). Skipping WSS smoke."));
        return true;
    }
    const FString Host = GetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
    const int32 Port = GetEnvInt(TEXT("TEST_SERVER_HTTPS_PORT"), 8443);
    TestTrue(TEXT("WSS Smoke"), RunWebSocketSmoke(true, Host, Port, 5.0));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseTlsE2E, "PlayHouse.E2E.Tls", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FPlayHouseTlsE2E::RunTest(const FString& Parameters)
{
    if (!IsTlsEnabled())
    {
        AddWarning(TEXT("TLS disabled (TEST_SERVER_ENABLE_TLS=false). Skipping TLS E2E."));
        return true;
    }
    const int32 Port = GetEnvInt(TEXT("TEST_SERVER_TCP_TLS_PORT"), 34002);
    TestTrue(TEXT("TLS E2E"), RunEchoE2E(EPlayHouseTransport::Tls, Port));
    return true;
}

#endif // WITH_AUTOMATION_TESTS
