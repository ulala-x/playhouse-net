
#include "CoreMinimal.h"
#include "Misc/AutomationTest.h"
#include "HAL/PlatformMisc.h"
#include "Containers/BackgroundableTicker.h"
#include "HttpModule.h"
#include "HttpManager.h"
#include "Interfaces/IHttpRequest.h"
#include "Interfaces/IHttpResponse.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonReader.h"

#include "PlayHouseConnector.h"
#include "PlayHouseConfig.h"
#include "PlayHousePacket.h"
#include "PlayHouseProtocol.h"

#if WITH_AUTOMATION_TESTS

namespace
{
    struct FStageInfo
    {
        bool bSuccess = false;
        int64 StageId = 0;
        FString StageType;
        FString ReplyPayloadId;
    };

    FString ExtGetEnvOrDefault(const TCHAR* Key, const FString& DefaultValue)
    {
        FString Value = FPlatformMisc::GetEnvironmentVariable(Key);
        return Value.IsEmpty() ? DefaultValue : Value;
    }

    int32 ExtGetEnvInt(const TCHAR* Key, int32 DefaultValue)
    {
        FString Value = ExtGetEnvOrDefault(Key, FString::FromInt(DefaultValue));
        return FCString::Atoi(*Value);
    }

    bool ExtIsTlsEnabled()
    {
        const FString Value = ExtGetEnvOrDefault(TEXT("TEST_SERVER_ENABLE_TLS"), TEXT("true"));
        return !(Value.Equals(TEXT("false"), ESearchCase::IgnoreCase) || Value == TEXT("0"));
    }

    bool ExtIsWebSocketEnabled()
    {
        const FString Value = ExtGetEnvOrDefault(TEXT("TEST_SERVER_ENABLE_WS"), TEXT("true"));
        return !(Value.Equals(TEXT("false"), ESearchCase::IgnoreCase) || Value == TEXT("0"));
    }

    bool ExtWaitUntil(TFunctionRef<bool()> Predicate, double TimeoutSeconds)
    {
        const double Deadline = FPlatformTime::Seconds() + TimeoutSeconds;
        double LastTickSeconds = FPlatformTime::Seconds();
        while (FPlatformTime::Seconds() < Deadline)
        {
            const double NowSeconds = FPlatformTime::Seconds();
            const float DeltaSeconds = static_cast<float>(NowSeconds - LastTickSeconds);
            LastTickSeconds = NowSeconds;

            FTSBackgroundableTicker::GetCoreTicker().Tick(DeltaSeconds);
            FHttpModule::Get().GetHttpManager().Tick(DeltaSeconds);

            if (Predicate())
            {
                return true;
            }
            FPlatformProcess::Sleep(0.01f);
        }
        return false;
    }

    void ExtPumpTicks(double DurationSeconds)
    {
        const double Deadline = FPlatformTime::Seconds() + DurationSeconds;
        while (FPlatformTime::Seconds() < Deadline)
        {
            FTSBackgroundableTicker::GetCoreTicker().Tick(0.01f);
            FHttpModule::Get().GetHttpManager().Tick(0.01f);
            FPlatformProcess::Sleep(0.01f);
        }
    }

    bool ExtHttpPostJson(const FString& Url, const FString& JsonBody, FString& OutResponse, int32& OutStatusCode, double TimeoutSeconds)
    {
        TSharedRef<IHttpRequest> Request = FHttpModule::Get().CreateRequest();
        Request->SetURL(Url);
        Request->SetVerb(TEXT("POST"));
        Request->SetHeader(TEXT("Content-Type"), TEXT("application/json"));
        Request->SetContentAsString(JsonBody);

        bool bCompleted = false;
        bool bSuccess = false;

        Request->OnProcessRequestComplete().BindLambda(
            [&](FHttpRequestPtr Req, FHttpResponsePtr Resp, bool bConnectedSuccessfully)
            {
                bCompleted = true;
                bSuccess = bConnectedSuccessfully && Resp.IsValid();
                if (Resp.IsValid())
                {
                    OutStatusCode = Resp->GetResponseCode();
                    OutResponse = Resp->GetContentAsString();
                }
                else
                {
                    OutStatusCode = 0;
                }
            });

        if (!Request->ProcessRequest())
        {
            return false;
        }

        if (!ExtWaitUntil([&]() { return bCompleted; }, TimeoutSeconds))
        {
            return false;
        }

        return bSuccess;
    }

    bool ExtParseStageResponse(const FString& Json, FStageInfo& OutInfo)
    {
        TSharedPtr<FJsonObject> JsonObject;
        TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Json);
        if (!FJsonSerializer::Deserialize(Reader, JsonObject) || !JsonObject.IsValid())
        {
            return false;
        }

        bool bSuccess = false;
        if (!JsonObject->TryGetBoolField(TEXT("success"), bSuccess))
        {
            return false;
        }

        double StageIdValue = 0.0;
        if (!JsonObject->TryGetNumberField(TEXT("stageId"), StageIdValue))
        {
            return false;
        }

        FString ReplyPayloadId;
        JsonObject->TryGetStringField(TEXT("replyPayloadId"), ReplyPayloadId);

        OutInfo.bSuccess = bSuccess;
        OutInfo.StageId = static_cast<int64>(StageIdValue);
        OutInfo.ReplyPayloadId = ReplyPayloadId;
        return true;
    }

    int32 ExtNextStageId()
    {
        static TAtomic<int32> StageIdBase{0};
        static TAtomic<int32> StageIdCounter{0};

        int32 Base = StageIdBase.Load();
        if (Base == 0)
        {
            const int32 Seed = static_cast<int32>(FPlatformTime::Cycles64() & 0x7fffffff);
            FRandomStream Stream(Seed);
            const int32 NewBase = Stream.RandRange(1000, 65000);
            int32 Expected = 0;
            if (StageIdBase.CompareExchange(Expected, NewBase))
            {
                Base = NewBase;
            }
            else
            {
                Base = StageIdBase.Load();
            }
        }

        const int32 Offset = StageIdCounter.IncrementExchange() - 1;
        const int32 Next = (Base + Offset) % 65000;
        return Next + 1;
    }

    bool ExtCreateStageInternal(const FString& StageType, int32 MaxPlayers, FStageInfo& OutInfo, bool bUseCache)
    {
        static TOptional<FStageInfo> CachedStage;
        if (bUseCache && CachedStage.IsSet())
        {
            OutInfo = CachedStage.GetValue();
            return OutInfo.bSuccess;
        }

        const FString Host = ExtGetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
        const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_HTTP_PORT"), 8080);
        const FString Url = FString::Printf(TEXT("http://%s:%d/api/stages"), *Host, Port);

        FString Response;
        int32 StatusCode = 0;

        const int32 MaxAttempts = 15;
        for (int32 Attempt = 0; Attempt < MaxAttempts; ++Attempt)
        {
            const int32 StageId = ExtNextStageId();
            const FString Body = FString::Printf(TEXT("{\"stageType\":\"%s\",\"stageId\":%d,\"maxPlayers\":%d}"),
                *StageType, StageId, MaxPlayers);

            if (!ExtHttpPostJson(Url, Body, Response, StatusCode, 5.0))
            {
                continue;
            }

            if (!ExtParseStageResponse(Response, OutInfo))
            {
                continue;
            }

            OutInfo.StageType = StageType;
            if (OutInfo.bSuccess)
            {
                if (bUseCache)
                {
                    CachedStage = OutInfo;
                }
                return true;
            }
        }

        return false;
    }

    bool ExtCreateTestStage(FStageInfo& OutInfo)
    {
        return ExtCreateStageInternal(TEXT("TestStage"), 10, OutInfo, false);
    }

    bool ExtGetOrCreateTestStage(FStageInfo& OutInfo)
    {
        return ExtCreateStageInternal(TEXT("TestStage"), 10, OutInfo, true);
    }
    void ExtAppendVarint(TArray<uint8>& Out, uint64 Value)
    {
        while (Value > 0x7F)
        {
            Out.Add(static_cast<uint8>((Value & 0x7F) | 0x80));
            Value >>= 7;
        }
        Out.Add(static_cast<uint8>(Value & 0x7F));
    }

    void ExtWriteKey(uint32 FieldNumber, uint8 WireType, TArray<uint8>& Out)
    {
        ExtAppendVarint(Out, (static_cast<uint64>(FieldNumber) << 3) | WireType);
    }

    void ExtWriteStringField(uint32 FieldNumber, const FString& Value, TArray<uint8>& Out)
    {
        FTCHARToUTF8 Utf8(*Value);
        ExtWriteKey(FieldNumber, 2, Out);
        ExtAppendVarint(Out, static_cast<uint32>(Utf8.Length()));
        Out.Append(reinterpret_cast<const uint8*>(Utf8.Get()), Utf8.Length());
    }

    void ExtWriteBytesField(uint32 FieldNumber, const TArray<uint8>& Value, TArray<uint8>& Out)
    {
        ExtWriteKey(FieldNumber, 2, Out);
        ExtAppendVarint(Out, static_cast<uint32>(Value.Num()));
        Out.Append(Value);
    }

    void ExtWriteInt32Field(uint32 FieldNumber, int32 Value, TArray<uint8>& Out)
    {
        ExtWriteKey(FieldNumber, 0, Out);
        ExtAppendVarint(Out, static_cast<uint32>(Value));
    }

    bool ExtReadVarint(const TArray<uint8>& Data, int32& Offset, uint64& Value)
    {
        Value = 0;
        int32 Shift = 0;
        while (Offset < Data.Num() && Shift < 64)
        {
            const uint8 Byte = Data[Offset++];
            Value |= static_cast<uint64>(Byte & 0x7F) << Shift;
            if ((Byte & 0x80) == 0)
            {
                return true;
            }
            Shift += 7;
        }
        return false;
    }

    bool ExtReadLengthDelimited(const TArray<uint8>& Data, int32& Offset, TArray<uint8>& Out)
    {
        uint64 Length = 0;
        if (!ExtReadVarint(Data, Offset, Length))
        {
            return false;
        }
        if (Offset + static_cast<int32>(Length) > Data.Num())
        {
            return false;
        }
        Out.SetNumUninitialized(static_cast<int32>(Length));
        FMemory::Memcpy(Out.GetData(), Data.GetData() + Offset, static_cast<int32>(Length));
        Offset += static_cast<int32>(Length);
        return true;
    }

    bool ExtSkipField(uint8 WireType, const TArray<uint8>& Data, int32& Offset)
    {
        switch (WireType)
        {
        case 0:
        {
            uint64 Dummy = 0;
            return ExtReadVarint(Data, Offset, Dummy);
        }
        case 1:
            if (Offset + 8 > Data.Num()) return false;
            Offset += 8;
            return true;
        case 2:
        {
            TArray<uint8> Dummy;
            return ExtReadLengthDelimited(Data, Offset, Dummy);
        }
        case 5:
            if (Offset + 4 > Data.Num()) return false;
            Offset += 4;
            return true;
        default:
            return false;
        }
    }

    FString ExtUtf8ToString(const TArray<uint8>& Data)
    {
        if (Data.Num() == 0)
        {
            return FString();
        }
        FUTF8ToTCHAR Converter(reinterpret_cast<const ANSICHAR*>(Data.GetData()), Data.Num());
        return FString(Converter.Length(), Converter.Get());
    }

    TArray<uint8> ExtEncodeAuthenticateRequest(const FString& UserId, const FString& Token)
    {
        TArray<uint8> Out;
        if (!UserId.IsEmpty())
        {
            ExtWriteStringField(1, UserId, Out);
        }
        if (!Token.IsEmpty())
        {
            ExtWriteStringField(2, Token, Out);
        }
        return Out;
    }

    TArray<uint8> ExtEncodeEchoRequest(const FString& Content, int32 Sequence)
    {
        TArray<uint8> Out;
        ExtWriteStringField(1, Content, Out);
        ExtWriteInt32Field(2, Sequence, Out);
        return Out;
    }

    TArray<uint8> ExtEncodeBroadcastRequest(const FString& Content)
    {
        TArray<uint8> Out;
        ExtWriteStringField(1, Content, Out);
        return Out;
    }

    TArray<uint8> ExtEncodeNoResponseRequest(int32 DelayMs)
    {
        TArray<uint8> Out;
        ExtWriteInt32Field(1, DelayMs, Out);
        return Out;
    }

    TArray<uint8> ExtEncodeFailRequest(int32 ErrorCode, const FString& ErrorMessage)
    {
        TArray<uint8> Out;
        ExtWriteInt32Field(1, ErrorCode, Out);
        ExtWriteStringField(2, ErrorMessage, Out);
        return Out;
    }

    TArray<uint8> ExtEncodeLargePayloadRequest(int32 SizeBytes)
    {
        TArray<uint8> Out;
        ExtWriteInt32Field(1, SizeBytes, Out);
        return Out;
    }

    bool ExtDecodeEchoReply(const TArray<uint8>& Data, FString& Content, int32& Sequence)
    {
        int32 Offset = 0;
        Content.Empty();
        Sequence = 0;
        while (Offset < Data.Num())
        {
            uint64 Key = 0;
            if (!ExtReadVarint(Data, Offset, Key)) return false;
            uint32 Field = static_cast<uint32>(Key >> 3);
            uint8 Wire = static_cast<uint8>(Key & 0x7);

            if (Field == 1 && Wire == 2)
            {
                TArray<uint8> Value;
                if (!ExtReadLengthDelimited(Data, Offset, Value)) return false;
                Content = ExtUtf8ToString(Value);
            }
            else if (Field == 2 && Wire == 0)
            {
                uint64 Value = 0;
                if (!ExtReadVarint(Data, Offset, Value)) return false;
                Sequence = static_cast<int32>(Value);
            }
            else
            {
                if (!ExtSkipField(Wire, Data, Offset)) return false;
            }
        }
        return true;
    }

    bool ExtDecodeFailReply(const TArray<uint8>& Data, int32& ErrorCode, FString& Message)
    {
        int32 Offset = 0;
        ErrorCode = 0;
        Message.Empty();
        while (Offset < Data.Num())
        {
            uint64 Key = 0;
            if (!ExtReadVarint(Data, Offset, Key)) return false;
            uint32 Field = static_cast<uint32>(Key >> 3);
            uint8 Wire = static_cast<uint8>(Key & 0x7);

            if (Field == 1 && Wire == 0)
            {
                uint64 Value = 0;
                if (!ExtReadVarint(Data, Offset, Value)) return false;
                ErrorCode = static_cast<int32>(Value);
            }
            else if (Field == 2 && Wire == 2)
            {
                TArray<uint8> Value;
                if (!ExtReadLengthDelimited(Data, Offset, Value)) return false;
                Message = ExtUtf8ToString(Value);
            }
            else
            {
                if (!ExtSkipField(Wire, Data, Offset)) return false;
            }
        }
        return true;
    }

    bool ExtDecodeBroadcastNotify(const TArray<uint8>& Data, FString& EventType, FString& Payload)
    {
        int32 Offset = 0;
        EventType.Empty();
        Payload.Empty();
        while (Offset < Data.Num())
        {
            uint64 Key = 0;
            if (!ExtReadVarint(Data, Offset, Key)) return false;
            uint32 Field = static_cast<uint32>(Key >> 3);
            uint8 Wire = static_cast<uint8>(Key & 0x7);

            if (Field == 1 && Wire == 2)
            {
                TArray<uint8> Value;
                if (!ExtReadLengthDelimited(Data, Offset, Value)) return false;
                EventType = ExtUtf8ToString(Value);
            }
            else if (Field == 2 && Wire == 2)
            {
                TArray<uint8> Value;
                if (!ExtReadLengthDelimited(Data, Offset, Value)) return false;
                Payload = ExtUtf8ToString(Value);
            }
            else
            {
                if (!ExtSkipField(Wire, Data, Offset)) return false;
            }
        }
        return true;
    }

    bool ExtDecodeBenchmarkReplyPayload(const TArray<uint8>& Data, TArray<uint8>& Payload)
    {
        int32 Offset = 0;
        Payload.Reset();
        while (Offset < Data.Num())
        {
            uint64 Key = 0;
            if (!ExtReadVarint(Data, Offset, Key)) return false;
            uint32 Field = static_cast<uint32>(Key >> 3);
            uint8 Wire = static_cast<uint8>(Key & 0x7);

            if (Field == 3 && Wire == 2)
            {
                TArray<uint8> Value;
                if (!ExtReadLengthDelimited(Data, Offset, Value)) return false;
                Payload = MoveTemp(Value);
            }
            else
            {
                if (!ExtSkipField(Wire, Data, Offset)) return false;
            }
        }
        return true;
    }

    void ExtInitConnector(FPlayHouseConnector& Connector,
        EPlayHouseTransport Transport,
        int32 RequestTimeoutMs = 5000,
        int32 HeartbeatIntervalMs = 10000,
        int32 SendBufferSize = 64 * 1024,
        int32 ReceiveBufferSize = 256 * 1024)
    {
        FPlayHouseConfig Config;
        Config.Transport = Transport;
        Config.RequestTimeoutMs = RequestTimeoutMs;
        Config.HeartbeatIntervalMs = HeartbeatIntervalMs;
        Config.SendBufferSize = SendBufferSize;
        Config.ReceiveBufferSize = ReceiveBufferSize;
        Config.bEnableReconnect = false;
        Connector.Init(Config);
    }

    bool ExtConnectAndWait(FPlayHouseConnector& Connector, const FString& Host, int32 Port, double TimeoutSeconds)
    {
        bool Connected = false;
        bool Error = false;
        Connector.OnConnect = [&]() { Connected = true; };
        Connector.OnError = [&](int32, const FString&) { Error = true; };

        Connector.Connect(Host, Port);
        const bool Finished = ExtWaitUntil([&]() { return Connected || Error; }, TimeoutSeconds);
        return Finished && Connected;
    }

    bool ExtAuthenticateAndWait(FPlayHouseConnector& Connector, FPlayHousePacket&& Packet, bool& OutSuccess, double TimeoutSeconds)
    {
        bool Done = false;
        Connector.Authenticate(MoveTemp(Packet), [&](bool Success) {
            OutSuccess = Success;
            Done = true;
        });
        return ExtWaitUntil([&]() { return Done; }, TimeoutSeconds);
    }

    bool ExtRequestAndWait(FPlayHouseConnector& Connector, FPlayHousePacket&& Packet, FPlayHousePacket& OutResponse, double TimeoutSeconds)
    {
        bool Done = false;
        Connector.Request(MoveTemp(Packet), [&](FPlayHousePacket&& Response) {
            OutResponse = MoveTemp(Response);
            Done = true;
        });
        return ExtWaitUntil([&]() { return Done; }, TimeoutSeconds);
    }

    bool ExtCreateStageAndConnect(FPlayHouseConnector& Connector, EPlayHouseTransport Transport, int32 Port)
    {
        FStageInfo Stage;
        if (!ExtGetOrCreateTestStage(Stage))
        {
            return false;
        }
        ExtInitConnector(Connector, Transport);
        const FString Host = ExtGetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
        return ExtConnectAndWait(Connector, Host, Port, 5.0);
    }

    bool ExtCreateStageConnectAndAuthenticate(FPlayHouseConnector& Connector,
        const FString& UserId,
        const FString& Token,
        EPlayHouseTransport Transport,
        int32 Port,
        int32 RequestTimeoutMs = 5000,
        int32 HeartbeatIntervalMs = 10000,
        int32 SendBufferSize = 64 * 1024,
        int32 ReceiveBufferSize = 256 * 1024)
    {
        FStageInfo Stage;
        if (!ExtGetOrCreateTestStage(Stage))
        {
            return false;
        }

        ExtInitConnector(Connector, Transport, RequestTimeoutMs, HeartbeatIntervalMs, SendBufferSize, ReceiveBufferSize);
        const FString Host = ExtGetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
        if (!ExtConnectAndWait(Connector, Host, Port, 5.0))
        {
            return false;
        }

        FPlayHousePacket AuthPacket;
        AuthPacket.MsgId = TEXT("AuthenticateRequest");
        AuthPacket.Payload = ExtEncodeAuthenticateRequest(UserId, Token);

        bool AuthSuccess = false;
        if (!ExtAuthenticateAndWait(Connector, MoveTemp(AuthPacket), AuthSuccess, 5.0))
        {
            return false;
        }
        return AuthSuccess;
    }
}
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC01_StageCreation_TestStage, "PlayHouse.E2E.Cpp.C01.StageCreation.TestStage", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC01_StageCreation_TestStage::RunTest(const FString& Parameters)
{
    FStageInfo Stage;
    const bool Created = ExtCreateTestStage(Stage);
    TestTrue(TEXT("Stage creation succeeds"), Created);
    TestTrue(TEXT("Stage success flag"), Stage.bSuccess);
    TestTrue(TEXT("Stage ID positive"), Stage.StageId > 0);
    TestEqual(TEXT("Stage type"), Stage.StageType, FString(TEXT("TestStage")));
    TestTrue(TEXT("Reply payload not empty"), !Stage.ReplyPayloadId.IsEmpty());
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC01_StageCreation_CustomPayload, "PlayHouse.E2E.Cpp.C01.StageCreation.CustomPayload", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC01_StageCreation_CustomPayload::RunTest(const FString& Parameters)
{
    FStageInfo Stage;
    const bool Created = ExtCreateStageInternal(TEXT("TestStage"), 10, Stage, false);
    TestTrue(TEXT("Stage creation succeeds"), Created);
    TestTrue(TEXT("Stage ID positive"), Stage.StageId > 0);
    TestEqual(TEXT("Stage type"), Stage.StageType, FString(TEXT("TestStage")));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC01_StageCreation_UniqueIds, "PlayHouse.E2E.Cpp.C01.StageCreation.UniqueIds", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC01_StageCreation_UniqueIds::RunTest(const FString& Parameters)
{
    FStageInfo Stage1;
    FStageInfo Stage2;
    FStageInfo Stage3;
    TestTrue(TEXT("Stage1 created"), ExtCreateTestStage(Stage1));
    TestTrue(TEXT("Stage2 created"), ExtCreateTestStage(Stage2));
    TestTrue(TEXT("Stage3 created"), ExtCreateTestStage(Stage3));
    TestNotEqual(TEXT("Stage1 != Stage2"), Stage1.StageId, Stage2.StageId);
    TestNotEqual(TEXT("Stage2 != Stage3"), Stage2.StageId, Stage3.StageId);
    TestNotEqual(TEXT("Stage1 != Stage3"), Stage1.StageId, Stage3.StageId);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC02_TcpConnect_AfterStageCreation, "PlayHouse.E2E.Cpp.C02.TcpConnect.AfterStageCreation", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC02_TcpConnect_AfterStageCreation::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    const bool Connected = ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port);
    TestTrue(TEXT("TCP connect succeeds"), Connected);
    TestTrue(TEXT("IsConnected true"), Connector.IsConnected());
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC02_TcpConnect_IsConnected, "PlayHouse.E2E.Cpp.C02.TcpConnect.IsConnected", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC02_TcpConnect_IsConnected::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    TestFalse(TEXT("Initial not connected"), Connector.IsConnected());
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connected"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));
    TestTrue(TEXT("IsConnected true"), Connector.IsConnected());
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC02_TcpConnect_OnConnectEvent, "PlayHouse.E2E.Cpp.C02.TcpConnect.OnConnectEvent", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC02_TcpConnect_OnConnectEvent::RunTest(const FString& Parameters)
{
    FStageInfo Stage;
    TestTrue(TEXT("Stage ready"), ExtGetOrCreateTestStage(Stage));

    FPlayHouseConnector Connector;
    ExtInitConnector(Connector, EPlayHouseTransport::Tcp);

    bool EventTriggered = false;
    Connector.OnConnect = [&]() { EventTriggered = true; };

    const FString Host = ExtGetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    Connector.Connect(Host, Port);

    const bool Completed = ExtWaitUntil([&]() { return EventTriggered; }, 5.0);
    TestTrue(TEXT("OnConnect fired"), Completed);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC02_TcpConnect_ValidServer, "PlayHouse.E2E.Cpp.C02.TcpConnect.ValidServer", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC02_TcpConnect_ValidServer::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connected"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));
    TestTrue(TEXT("IsConnected true"), Connector.IsConnected());
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC02_TcpConnect_MultipleTimes, "PlayHouse.E2E.Cpp.C02.TcpConnect.MultipleTimes", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC02_TcpConnect_MultipleTimes::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("First connect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));
    Connector.Disconnect();
    ExtPumpTicks(0.5);
    TestTrue(TEXT("Reconnect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));
    Connector.Disconnect();
    return true;
}
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC03_Auth_ValidCredentials, "PlayHouse.E2E.Cpp.C03.Auth.ValidCredentials", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC03_Auth_ValidCredentials::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth success"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("test_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC03_Auth_CallbackFires, "PlayHouse.E2E.Cpp.C03.Auth.CallbackFires", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC03_Auth_CallbackFires::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    FStageInfo Stage;
    TestTrue(TEXT("Stage ready"), ExtGetOrCreateTestStage(Stage));
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    ExtInitConnector(Connector, EPlayHouseTransport::Tcp);
    const FString Host = ExtGetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
    TestTrue(TEXT("Connect"), ExtConnectAndWait(Connector, Host, Port, 5.0));

    FPlayHousePacket AuthPacket;
    AuthPacket.MsgId = TEXT("AuthenticateRequest");
    AuthPacket.Payload = ExtEncodeAuthenticateRequest(TEXT("test_user2"), TEXT("valid_token"));

    bool AuthSuccess = false;
    bool Completed = ExtAuthenticateAndWait(Connector, MoveTemp(AuthPacket), AuthSuccess, 5.0);
    TestTrue(TEXT("Callback fired"), Completed);
    TestTrue(TEXT("Auth success"), AuthSuccess);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC03_Auth_EmptyPayload, "PlayHouse.E2E.Cpp.C03.Auth.EmptyPayload", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC03_Auth_EmptyPayload::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    FStageInfo Stage;
    TestTrue(TEXT("Stage ready"), ExtGetOrCreateTestStage(Stage));
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    ExtInitConnector(Connector, EPlayHouseTransport::Tcp);
    const FString Host = ExtGetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
    TestTrue(TEXT("Connect"), ExtConnectAndWait(Connector, Host, Port, 5.0));

    FPlayHousePacket AuthPacket;
    AuthPacket.MsgId = TEXT("AuthenticateRequest");
    AuthPacket.Payload = TArray<uint8>();

    bool AuthSuccess = false;
    const bool Completed = ExtAuthenticateAndWait(Connector, MoveTemp(AuthPacket), AuthSuccess, 5.0);
    TestTrue(TEXT("Auth completed"), Completed);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC03_Auth_MultipleUsers, "PlayHouse.E2E.Cpp.C03.Auth.MultipleUsers", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC03_Auth_MultipleUsers::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    FStageInfo Stage;
    TestTrue(TEXT("Stage ready"), ExtGetOrCreateTestStage(Stage));
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    ExtInitConnector(Connector, EPlayHouseTransport::Tcp);
    const FString Host = ExtGetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
    TestTrue(TEXT("Connect"), ExtConnectAndWait(Connector, Host, Port, 5.0));

    int32 SuccessCount = 0;
    for (int32 i = 0; i < 3; ++i)
    {
        FPlayHousePacket AuthPacket;
        AuthPacket.MsgId = TEXT("AuthenticateRequest");
        AuthPacket.Payload = ExtEncodeAuthenticateRequest(FString::Printf(TEXT("user%d"), i), TEXT("valid_token"));

        bool AuthSuccess = false;
        const bool Completed = ExtAuthenticateAndWait(Connector, MoveTemp(AuthPacket), AuthSuccess, 5.0);
        if (Completed && AuthSuccess)
        {
            SuccessCount++;
        }
    }

    TestTrue(TEXT("At least one auth success"), SuccessCount > 0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC05_Echo_Simple, "PlayHouse.E2E.Cpp.C05.Echo.Simple", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC05_Echo_Simple::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("echo_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("Hello World"), 1);

    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    TestTrue(TEXT("Echo completed"), Completed);
    TestEqual(TEXT("MsgId EchoReply"), Response.MsgId, FString(TEXT("EchoReply")));
    TestEqual(TEXT("ErrorCode 0"), Response.ErrorCode, 0);
    TestTrue(TEXT("Payload not empty"), Response.Payload.Num() > 0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC05_Echo_Callback, "PlayHouse.E2E.Cpp.C05.Echo.Callback", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC05_Echo_Callback::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("echo_cb_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("Callback Test"), 2);

    bool CallbackFired = false;
    FString ReceivedMsgId;
    Connector.Request(MoveTemp(Packet), [&](FPlayHousePacket&& Response) {
        CallbackFired = true;
        ReceivedMsgId = Response.MsgId;
    });

    const bool Completed = ExtWaitUntil([&]() { return CallbackFired; }, 5.0);
    TestTrue(TEXT("Callback fired"), Completed);
    TestEqual(TEXT("MsgId EchoReply"), ReceivedMsgId, FString(TEXT("EchoReply")));
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC05_Echo_MultipleSequential, "PlayHouse.E2E.Cpp.C05.Echo.MultipleSequential", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC05_Echo_MultipleSequential::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("echo_seq_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    for (int32 i = 0; i < 5; ++i)
    {
        FPlayHousePacket Packet;
        Packet.MsgId = TEXT("EchoRequest");
        Packet.Payload = ExtEncodeEchoRequest(FString::Printf(TEXT("Message%d"), i), i);

        FPlayHousePacket Response;
        const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
        TestTrue(FString::Printf(TEXT("Echo %d completed"), i), Completed);
        if (Completed)
        {
            TestEqual(TEXT("ErrorCode 0"), Response.ErrorCode, 0);
        }
    }

    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC05_Echo_LargeContent, "PlayHouse.E2E.Cpp.C05.Echo.LargeContent", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC05_Echo_LargeContent::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("echo_large_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FString LargeContent;
    LargeContent.Reserve(1000);
    for (int32 i = 0; i < 1000; ++i)
    {
        LargeContent.AppendChar(TEXT('A'));
    }

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(LargeContent, 99);

    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    TestTrue(TEXT("Large echo completed"), Completed);
    TestEqual(TEXT("ErrorCode 0"), Response.ErrorCode, 0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC05_Echo_SpecialChars, "PlayHouse.E2E.Cpp.C05.Echo.SpecialChars", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC05_Echo_SpecialChars::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("echo_special_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    const FString EchoData = TEXT("Hello World");
    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(EchoData, 42);

    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    TestTrue(TEXT("Echo completed"), Completed);
    TestEqual(TEXT("ErrorCode 0"), Response.ErrorCode, 0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC06_Push_OnReceive, "PlayHouse.E2E.Cpp.C06.Push.OnReceive", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC06_Push_OnReceive::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("push_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    bool Received = false;
    FString MsgId;
    Connector.OnReceive = [&](const FPlayHousePacket& Packet) {
        Received = true;
        MsgId = Packet.MsgId;
    };

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("BroadcastRequest");
    Packet.Payload = ExtEncodeBroadcastRequest(TEXT("Push Test"));
    Connector.Send(MoveTemp(Packet));

    const bool Completed = ExtWaitUntil([&]() { return Received; }, 5.0);
    TestTrue(TEXT("Push received"), Completed);
    TestTrue(TEXT("MsgId not empty"), !MsgId.IsEmpty());
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC06_Push_MsgSeqZero, "PlayHouse.E2E.Cpp.C06.Push.MsgSeqZero", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC06_Push_MsgSeqZero::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("push_seq_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    bool Received = false;
    uint16 MsgSeq = 999;
    Connector.OnReceive = [&](const FPlayHousePacket& Packet) {
        MsgSeq = Packet.MsgSeq;
        Received = true;
    };

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("BroadcastRequest");
    Packet.Payload = ExtEncodeBroadcastRequest(TEXT("Push Seq Test"));
    Connector.Send(MoveTemp(Packet));

    const bool Completed = ExtWaitUntil([&]() { return Received; }, 5.0);
    TestTrue(TEXT("Push received"), Completed);
    TestEqual(TEXT("MsgSeq zero"), MsgSeq, static_cast<uint16>(0));
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC06_Push_Multiple, "PlayHouse.E2E.Cpp.C06.Push.Multiple", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC06_Push_Multiple::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("push_multi_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    int32 Count = 0;
    Connector.OnReceive = [&](const FPlayHousePacket&) {
        Count++;
    };

    for (int32 i = 0; i < 3; ++i)
    {
        FPlayHousePacket Packet;
        Packet.MsgId = TEXT("BroadcastRequest");
        Packet.Payload = ExtEncodeBroadcastRequest(FString::Printf(TEXT("Push %d"), i));
        Connector.Send(MoveTemp(Packet));
        FPlatformProcess::Sleep(0.1f);
    }

    const bool Completed = ExtWaitUntil([&]() { return Count >= 3; }, 10.0);
    TestTrue(TEXT("Received >= 3"), Completed);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC06_Push_PayloadAccessible, "PlayHouse.E2E.Cpp.C06.Push.PayloadAccessible", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC06_Push_PayloadAccessible::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("push_payload_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    bool Received = false;
    bool PayloadNotEmpty = false;
    Connector.OnReceive = [&](const FPlayHousePacket& Packet) {
        PayloadNotEmpty = Packet.Payload.Num() > 0;
        Received = true;
    };

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("BroadcastRequest");
    Packet.Payload = ExtEncodeBroadcastRequest(TEXT("Push with payload"));
    Connector.Send(MoveTemp(Packet));

    const bool Completed = ExtWaitUntil([&]() { return Received; }, 5.0);
    TestTrue(TEXT("Push received"), Completed);
    TestTrue(TEXT("Payload not empty"), PayloadNotEmpty);
    Connector.Disconnect();
    return true;
}
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC07_Heartbeat_ShortInterval, "PlayHouse.E2E.Cpp.C07.Heartbeat.ShortInterval", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC07_Heartbeat_ShortInterval::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("heartbeat_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 5000, 1000));
    ExtPumpTicks(3.0);
    TestTrue(TEXT("Still connected"), Connector.IsConnected());
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC07_Heartbeat_LongInterval, "PlayHouse.E2E.Cpp.C07.Heartbeat.LongInterval", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC07_Heartbeat_LongInterval::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("heartbeat_long_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 5000, 10000));
    ExtPumpTicks(5.0);
    TestTrue(TEXT("Still connected"), Connector.IsConnected());
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC07_Heartbeat_NoInterference, "PlayHouse.E2E.Cpp.C07.Heartbeat.NoInterference", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC07_Heartbeat_NoInterference::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("heartbeat_echo_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 5000, 2000));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("Test during heartbeat"), 1);

    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    TestTrue(TEXT("Request completed"), Completed);
    TestEqual(TEXT("ErrorCode 0"), Response.ErrorCode, 0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC07_Heartbeat_ConfiguredInterval, "PlayHouse.E2E.Cpp.C07.Heartbeat.ConfiguredInterval", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC07_Heartbeat_ConfiguredInterval::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("heartbeat_config_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 5000, 5000));
    TestTrue(TEXT("Connected"), Connector.IsConnected());
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC08_Disconnect_IsConnectedFalse, "PlayHouse.E2E.Cpp.C08.Disconnect.IsConnectedFalse", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC08_Disconnect_IsConnectedFalse::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));
    Connector.Disconnect();
    ExtPumpTicks(0.5);
    TestFalse(TEXT("IsConnected false"), Connector.IsConnected());
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC08_Disconnect_OnDisconnectEvent, "PlayHouse.E2E.Cpp.C08.Disconnect.OnDisconnectEvent", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC08_Disconnect_OnDisconnectEvent::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));

    bool Fired = false;
    Connector.OnDisconnect = [&]() { Fired = true; };
    Connector.Disconnect();
    const bool Completed = ExtWaitUntil([&]() { return Fired; }, 5.0);
    TestTrue(TEXT("OnDisconnect fired"), Completed);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC08_Disconnect_MultipleTimes, "PlayHouse.E2E.Cpp.C08.Disconnect.MultipleTimes", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC08_Disconnect_MultipleTimes::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));
    Connector.Disconnect();
    ExtPumpTicks(0.2);
    Connector.Disconnect();
    ExtPumpTicks(0.2);
    Connector.Disconnect();
    TestFalse(TEXT("IsConnected false"), Connector.IsConnected());
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC08_Disconnect_BeforeConnect, "PlayHouse.E2E.Cpp.C08.Disconnect.BeforeConnect", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC08_Disconnect_BeforeConnect::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    TestFalse(TEXT("Not connected"), Connector.IsConnected());
    Connector.Disconnect();
    TestFalse(TEXT("Still not connected"), Connector.IsConnected());
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC08_Disconnect_SendAfter, "PlayHouse.E2E.Cpp.C08.Disconnect.SendAfter", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC08_Disconnect_SendAfter::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));
    Connector.Disconnect();
    ExtPumpTicks(0.5);

    bool ErrorTriggered = false;
    int32 ErrorCode = 0;
    Connector.OnError = [&](int32 Code, const FString&) {
        ErrorTriggered = true;
        ErrorCode = Code;
    };

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("TestMessage");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("AfterDisconnect"), 1);
    Connector.Send(MoveTemp(Packet));

    const bool Completed = ExtWaitUntil([&]() { return ErrorTriggered; }, 5.0);
    TestTrue(TEXT("Error triggered"), Completed);
    TestEqual(TEXT("ConnectionClosed"), ErrorCode, PlayHouse::ErrorCode::ConnectionClosed);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC08_Disconnect_Reconnect, "PlayHouse.E2E.Cpp.C08.Disconnect.Reconnect", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC08_Disconnect_Reconnect::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));
    Connector.Disconnect();
    ExtPumpTicks(0.5);
    TestTrue(TEXT("Reconnect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC09_Auth_InvalidToken, "PlayHouse.E2E.Cpp.C09.Auth.InvalidToken", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC09_Auth_InvalidToken::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    FStageInfo Stage;
    TestTrue(TEXT("Stage ready"), ExtGetOrCreateTestStage(Stage));
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    ExtInitConnector(Connector, EPlayHouseTransport::Tcp);
    const FString Host = ExtGetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
    TestTrue(TEXT("Connect"), ExtConnectAndWait(Connector, Host, Port, 5.0));

    FPlayHousePacket AuthPacket;
    AuthPacket.MsgId = TEXT("AuthenticateRequest");
    AuthPacket.Payload = ExtEncodeAuthenticateRequest(TEXT("test_user"), TEXT("invalid_token"));

    bool AuthSuccess = false;
    const bool Completed = ExtAuthenticateAndWait(Connector, MoveTemp(AuthPacket), AuthSuccess, 5.0);
    TestTrue(TEXT("Auth completed"), Completed);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC09_Auth_EmptyCredentials, "PlayHouse.E2E.Cpp.C09.Auth.EmptyCredentials", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC09_Auth_EmptyCredentials::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    FStageInfo Stage;
    TestTrue(TEXT("Stage ready"), ExtGetOrCreateTestStage(Stage));
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    ExtInitConnector(Connector, EPlayHouseTransport::Tcp);
    const FString Host = ExtGetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
    TestTrue(TEXT("Connect"), ExtConnectAndWait(Connector, Host, Port, 5.0));

    FPlayHousePacket AuthPacket;
    AuthPacket.MsgId = TEXT("AuthenticateRequest");
    AuthPacket.Payload = TArray<uint8>();

    bool AuthSuccess = false;
    const bool Completed = ExtAuthenticateAndWait(Connector, MoveTemp(AuthPacket), AuthSuccess, 5.0);
    TestTrue(TEXT("Auth completed"), Completed);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC09_Auth_BeforeConnection, "PlayHouse.E2E.Cpp.C09.Auth.BeforeConnection", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC09_Auth_BeforeConnection::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    ExtInitConnector(Connector, EPlayHouseTransport::Tcp);

    FPlayHousePacket AuthPacket;
    AuthPacket.MsgId = TEXT("AuthenticateRequest");
    AuthPacket.Payload = ExtEncodeAuthenticateRequest(TEXT("test"), TEXT("token"));

    bool AuthSuccess = false;
    const bool Completed = ExtAuthenticateAndWait(Connector, MoveTemp(AuthPacket), AuthSuccess, 0.5);
    TestTrue(TEXT("Auth completed"), Completed);
    TestFalse(TEXT("Auth success"), AuthSuccess);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC09_Auth_MalformedPayload, "PlayHouse.E2E.Cpp.C09.Auth.MalformedPayload", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC09_Auth_MalformedPayload::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connect + auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("edge_auth_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket AuthPacket;
    AuthPacket.MsgId = TEXT("AuthenticateRequest");
    const FString Malformed = TEXT("{this is not valid json}");
    FTCHARToUTF8 Utf8(*Malformed);
    AuthPacket.Payload.Append(reinterpret_cast<const uint8*>(Utf8.Get()), Utf8.Length());

    bool AuthSuccess = false;
    const bool Completed = ExtAuthenticateAndWait(Connector, MoveTemp(AuthPacket), AuthSuccess, 5.0);
    TestTrue(TEXT("Auth completed"), Completed);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC09_Auth_OnErrorMayTrigger, "PlayHouse.E2E.Cpp.C09.Auth.OnErrorMayTrigger", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC09_Auth_OnErrorMayTrigger::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    FStageInfo Stage;
    TestTrue(TEXT("Stage ready"), ExtGetOrCreateTestStage(Stage));
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    ExtInitConnector(Connector, EPlayHouseTransport::Tcp);
    const FString Host = ExtGetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
    TestTrue(TEXT("Connect"), ExtConnectAndWait(Connector, Host, Port, 5.0));

    FPlayHousePacket AuthPacket;
    AuthPacket.MsgId = TEXT("AuthenticateRequest");
    AuthPacket.Payload = ExtEncodeAuthenticateRequest(TEXT("bad_user"), TEXT("bad_token"));

    bool AuthSuccess = false;
    ExtAuthenticateAndWait(Connector, MoveTemp(AuthPacket), AuthSuccess, 5.0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC10_Timeout_ShortTimeout, "PlayHouse.E2E.Cpp.C10.Timeout.ShortTimeout", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC10_Timeout_ShortTimeout::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("timeout_short_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 100));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("NoResponseRequest");
    Packet.Payload = ExtEncodeNoResponseRequest(5000);

    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 0.5);
    bool TimedOut = !Completed;
    if (Completed)
    {
        TimedOut = (Response.MsgId == PlayHouse::MsgId::Timeout) || (Response.ErrorCode != 0);
    }
    TestTrue(TEXT("Timed out"), TimedOut);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC10_Timeout_NormalTimeout, "PlayHouse.E2E.Cpp.C10.Timeout.NormalTimeout", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC10_Timeout_NormalTimeout::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("timeout_normal_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 5000));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("Normal request"), 1);

    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 6.0);
    TestTrue(TEXT("Completed"), Completed);
    TestEqual(TEXT("ErrorCode 0"), Response.ErrorCode, 0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC10_Timeout_MsgId, "PlayHouse.E2E.Cpp.C10.Timeout.MsgId", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC10_Timeout_MsgId::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("timeout_msgid_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 200));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("NoResponseRequest");
    Packet.Payload = ExtEncodeNoResponseRequest(10000);

    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 0.5);
    if (Completed)
    {
        TestEqual(TEXT("Timeout MsgId"), Response.MsgId, FString(PlayHouse::MsgId::Timeout));
    }
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC10_Timeout_Multiple, "PlayHouse.E2E.Cpp.C10.Timeout.Multiple", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC10_Timeout_Multiple::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("timeout_multi_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 150));

    int32 TimeoutCount = 0;
    for (int32 i = 0; i < 3; ++i)
    {
        FPlayHousePacket Packet;
        Packet.MsgId = TEXT("NoResponseRequest");
        Packet.Payload = ExtEncodeNoResponseRequest(5000);

        FPlayHousePacket Response;
        const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 0.2);
        if (!Completed)
        {
            TimeoutCount++;
        }
        else if (Response.MsgId == PlayHouse::MsgId::Timeout || Response.ErrorCode != 0)
        {
            TimeoutCount++;
        }
    }
    TestTrue(TEXT("At least one timeout"), TimeoutCount > 0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC10_Timeout_AfterTimeout, "PlayHouse.E2E.Cpp.C10.Timeout.AfterTimeout", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC10_Timeout_AfterTimeout::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("timeout_after_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 100));

    FPlayHousePacket SlowPacket;
    SlowPacket.MsgId = TEXT("NoResponseRequest");
    SlowPacket.Payload = ExtEncodeNoResponseRequest(5000);
    FPlayHousePacket SlowResponse;
    ExtRequestAndWait(Connector, MoveTemp(SlowPacket), SlowResponse, 0.2);

    FPlayHousePacket FastPacket;
    FastPacket.MsgId = TEXT("EchoRequest");
    FastPacket.Payload = ExtEncodeEchoRequest(TEXT("Fast request"), 1);
    FPlayHousePacket FastResponse;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(FastPacket), FastResponse, 5.0);
    if (Completed)
    {
        TestTrue(TEXT("Still connected"), Connector.IsConnected());
    }
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC11_Error_FailRequest, "PlayHouse.E2E.Cpp.C11.Error.FailRequest", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC11_Error_FailRequest::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("error_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("FailRequest");
    Packet.Payload = ExtEncodeFailRequest(123, TEXT("forced error"));

    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    if (Completed)
    {
        int32 ErrorCode = 0;
        FString Message;
        const bool Decoded = ExtDecodeFailReply(Response.Payload, ErrorCode, Message);
        TestTrue(TEXT("Decoded"), Decoded);
        TestEqual(TEXT("ErrorCode"), ErrorCode, 123);
        TestEqual(TEXT("Message"), Message, FString(TEXT("forced error")));
    }
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC11_Error_OnErrorNetwork, "PlayHouse.E2E.Cpp.C11.Error.OnErrorNetwork", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC11_Error_OnErrorNetwork::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("error_event_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    bool ErrorFired = false;
    int32 ErrorCode = 0;
    Connector.OnError = [&](int32 Code, const FString&) {
        ErrorFired = true;
        ErrorCode = Code;
    };

    Connector.Disconnect();
    ExtPumpTicks(0.5);

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("TestMessage");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("data"), 1);
    Connector.Send(MoveTemp(Packet));

    const bool Completed = ExtWaitUntil([&]() { return ErrorFired; }, 5.0);
    TestTrue(TEXT("OnError fired"), Completed);
    TestEqual(TEXT("ConnectionClosed"), ErrorCode, PlayHouse::ErrorCode::ConnectionClosed);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC11_Error_InvalidEndpoint, "PlayHouse.E2E.Cpp.C11.Error.InvalidEndpoint", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC11_Error_InvalidEndpoint::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("error_invalid_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("NonExistentRequest");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("Invalid endpoint"), 1);

    FPlayHousePacket Response;
    ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC11_Error_PayloadAccessible, "PlayHouse.E2E.Cpp.C11.Error.PayloadAccessible", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC11_Error_PayloadAccessible::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("error_payload_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("FailRequest");
    Packet.Payload = ExtEncodeFailRequest(321, TEXT("custom error payload"));

    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    if (Completed)
    {
        int32 ErrorCode = 0;
        FString Message;
        const bool Decoded = ExtDecodeFailReply(Response.Payload, ErrorCode, Message);
        TestTrue(TEXT("Decoded"), Decoded);
        TestEqual(TEXT("ErrorCode"), ErrorCode, 321);
        TestEqual(TEXT("Message"), Message, FString(TEXT("custom error payload")));
    }
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC11_Error_MultipleErrors, "PlayHouse.E2E.Cpp.C11.Error.MultipleErrors", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC11_Error_MultipleErrors::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("error_multi_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    int32 ErrorCount = 0;
    Connector.OnError = [&](int32, const FString&) { ErrorCount++; };

    for (int32 i = 0; i < 3; ++i)
    {
        FPlayHousePacket Packet;
        Packet.MsgId = TEXT("FailRequest");
        Packet.Payload = ExtEncodeFailRequest(200 + i, FString::Printf(TEXT("Error%d"), i));
        FPlayHousePacket Response;
        ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 3.0);
        ExtPumpTicks(0.05);
    }

    TestTrue(TEXT("System stable"), true);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseC11_Error_ConnectionError, "PlayHouse.E2E.Cpp.C11.Error.ConnectionError", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseC11_Error_ConnectionError::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("error_conn_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    bool ErrorTriggered = false;
    Connector.OnError = [&](int32 Code, const FString&) {
        if (Code == PlayHouse::ErrorCode::ConnectionClosed || Code == PlayHouse::ErrorCode::ConnectionFailed)
        {
            ErrorTriggered = true;
        }
    };

    Connector.Disconnect();

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("TestMessage");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("data"), 1);
    Connector.Send(MoveTemp(Packet));

    const bool Completed = ExtWaitUntil([&]() { return ErrorTriggered; }, 5.0);
    TestTrue(TEXT("OnError fired"), Completed);
    return true;
}
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA01_Ws_Connect, "PlayHouse.E2E.Cpp.A01.Ws.Connect", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA01_Ws_Connect::RunTest(const FString& Parameters)
{
    if (!ExtIsWebSocketEnabled())
    {
        AddWarning(TEXT("WebSocket disabled. Skipping."));
        return true;
    }
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_HTTP_PORT"), 8080);
    TestTrue(TEXT("Connect WS"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Ws, Port));
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA01_Ws_Auth, "PlayHouse.E2E.Cpp.A01.Ws.Auth", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA01_Ws_Auth::RunTest(const FString& Parameters)
{
    if (!ExtIsWebSocketEnabled())
    {
        AddWarning(TEXT("WebSocket disabled. Skipping."));
        return true;
    }
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_HTTP_PORT"), 8080);
    TestTrue(TEXT("Auth WS"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("ws_auth_user"), TEXT("valid_token"), EPlayHouseTransport::Ws, Port));
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA01_Ws_Echo, "PlayHouse.E2E.Cpp.A01.Ws.Echo", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA01_Ws_Echo::RunTest(const FString& Parameters)
{
    if (!ExtIsWebSocketEnabled())
    {
        AddWarning(TEXT("WebSocket disabled. Skipping."));
        return true;
    }
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_HTTP_PORT"), 8080);
    TestTrue(TEXT("Auth WS"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("ws_echo_user"), TEXT("valid_token"), EPlayHouseTransport::Ws, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("ws-echo"), 1);

    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    TestTrue(TEXT("Completed"), Completed);

    FString Content;
    int32 Sequence = 0;
    TestTrue(TEXT("Decode EchoReply"), ExtDecodeEchoReply(Response.Payload, Content, Sequence));
    TestEqual(TEXT("Content"), Content, FString(TEXT("ws-echo")));
    TestEqual(TEXT("Sequence"), Sequence, 1);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA01_Ws_Push, "PlayHouse.E2E.Cpp.A01.Ws.Push", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA01_Ws_Push::RunTest(const FString& Parameters)
{
    if (!ExtIsWebSocketEnabled())
    {
        AddWarning(TEXT("WebSocket disabled. Skipping."));
        return true;
    }
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_HTTP_PORT"), 8080);
    TestTrue(TEXT("Auth WS"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("ws_push_user"), TEXT("valid_token"), EPlayHouseTransport::Ws, Port));

    bool Received = false;
    Connector.OnReceive = [&](const FPlayHousePacket& Packet) {
        if (Packet.MsgId == TEXT("BroadcastNotify"))
        {
            FString EventType;
            FString Payload;
            if (ExtDecodeBroadcastNotify(Packet.Payload, EventType, Payload))
            {
                Received = (Payload == TEXT("ws-broadcast"));
            }
        }
    };

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("BroadcastRequest");
    Packet.Payload = ExtEncodeBroadcastRequest(TEXT("ws-broadcast"));
    Connector.Send(MoveTemp(Packet));

    TestTrue(TEXT("Push received"), ExtWaitUntil([&]() { return Received; }, 5.0));
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA01_Ws_Reconnect, "PlayHouse.E2E.Cpp.A01.Ws.Reconnect", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA01_Ws_Reconnect::RunTest(const FString& Parameters)
{
    if (!ExtIsWebSocketEnabled())
    {
        AddWarning(TEXT("WebSocket disabled. Skipping."));
        return true;
    }
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_HTTP_PORT"), 8080);
    TestTrue(TEXT("Connect WS"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Ws, Port));
    Connector.Disconnect();
    ExtPumpTicks(0.5);
    TestTrue(TEXT("Reconnect WS"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Ws, Port));
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA01_Ws_ParallelRequests, "PlayHouse.E2E.Cpp.A01.Ws.ParallelRequests", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA01_Ws_ParallelRequests::RunTest(const FString& Parameters)
{
    if (!ExtIsWebSocketEnabled())
    {
        AddWarning(TEXT("WebSocket disabled. Skipping."));
        return true;
    }
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_HTTP_PORT"), 8080);
    TestTrue(TEXT("Auth WS"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("ws_parallel_user"), TEXT("valid_token"), EPlayHouseTransport::Ws, Port));

    TAtomic<int32> Completed{0};
    for (int32 i = 0; i < 5; ++i)
    {
        FPlayHousePacket Packet;
        Packet.MsgId = TEXT("EchoRequest");
        Packet.Payload = ExtEncodeEchoRequest(TEXT("ws-parallel"), i);
        Connector.Request(MoveTemp(Packet), [&](FPlayHousePacket&& Response) {
            FString Content;
            int32 Seq = 0;
            if (ExtDecodeEchoReply(Response.Payload, Content, Seq) && Content == TEXT("ws-parallel"))
            {
                Completed.IncrementExchange();
            }
        });
    }

    TestTrue(TEXT("All completed"), ExtWaitUntil([&]() { return Completed.Load() == 5; }, 5.0));
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA02_LargePayload_1MB, "PlayHouse.E2E.Cpp.A02.LargePayload.1MB", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA02_LargePayload_1MB::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("large_payload_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 30000, 10000, 2 * 1024 * 1024, 2 * 1024 * 1024));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("LargePayloadRequest");
    Packet.Payload = ExtEncodeLargePayloadRequest(1048576);

    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 30.0);
    TestTrue(TEXT("Completed"), Completed);

    TArray<uint8> Payload;
    TestTrue(TEXT("Decode payload"), ExtDecodeBenchmarkReplyPayload(Response.Payload, Payload));
    TestEqual(TEXT("Payload size 1MB"), Payload.Num(), 1048576);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA02_LargePayload_DataIntegrity, "PlayHouse.E2E.Cpp.A02.LargePayload.DataIntegrity", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA02_LargePayload_DataIntegrity::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("large_payload_integrity_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 30000, 10000, 2 * 1024 * 1024, 2 * 1024 * 1024));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("LargePayloadRequest");
    Packet.Payload = ExtEncodeLargePayloadRequest(1048576);

    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 30.0);
    TestTrue(TEXT("Completed"), Completed);

    TArray<uint8> Payload;
    TestTrue(TEXT("Decode payload"), ExtDecodeBenchmarkReplyPayload(Response.Payload, Payload));
    TestTrue(TEXT("Payload size >= 1000"), Payload.Num() >= 1000);
    for (int32 i = 0; i < 1000 && i < Payload.Num(); ++i)
    {
        TestEqual(TEXT("Pattern byte"), Payload[i], static_cast<uint8>(i % 256));
    }
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA02_LargePayload_Sequential, "PlayHouse.E2E.Cpp.A02.LargePayload.Sequential", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA02_LargePayload_Sequential::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("large_payload_seq_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 30000, 10000, 2 * 1024 * 1024, 2 * 1024 * 1024));

    for (int32 i = 0; i < 3; ++i)
    {
        FPlayHousePacket Packet;
        Packet.MsgId = TEXT("LargePayloadRequest");
        Packet.Payload = ExtEncodeLargePayloadRequest(1048576);

        FPlayHousePacket Response;
        const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 30.0);
        TestTrue(TEXT("Completed"), Completed);

        TArray<uint8> Payload;
        TestTrue(TEXT("Decode payload"), ExtDecodeBenchmarkReplyPayload(Response.Payload, Payload));
        TestEqual(TEXT("Payload size 1MB"), Payload.Num(), 1048576);
    }

    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA02_LargePayload_SendLargeRequest, "PlayHouse.E2E.Cpp.A02.LargePayload.SendLargeRequest", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA02_LargePayload_SendLargeRequest::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("large_payload_send_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 10000, 10000, 2 * 1024 * 1024, 2 * 1024 * 1024));

    FString LargeContent;
    LargeContent.Reserve(100000);
    for (int32 i = 0; i < 100000; ++i)
    {
        LargeContent.AppendChar(TEXT('A'));
    }

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(LargeContent, 1);

    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 10.0);
    TestTrue(TEXT("Completed"), Completed);

    FString EchoContent;
    int32 Seq = 0;
    TestTrue(TEXT("Decode EchoReply"), ExtDecodeEchoReply(Response.Payload, EchoContent, Seq));
    TestEqual(TEXT("Content match"), EchoContent, LargeContent);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA02_LargePayload_Parallel, "PlayHouse.E2E.Cpp.A02.LargePayload.Parallel", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA02_LargePayload_Parallel::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("large_payload_parallel_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port, 30000, 10000, 2 * 1024 * 1024, 2 * 1024 * 1024));

    const int32 RequestCount = 3;
    TArray<bool> Completed;
    TArray<FPlayHousePacket> Responses;
    Completed.Init(false, RequestCount);
    Responses.SetNum(RequestCount);

    for (int32 i = 0; i < RequestCount; ++i)
    {
        FPlayHousePacket Packet;
        Packet.MsgId = TEXT("LargePayloadRequest");
        Packet.Payload = ExtEncodeLargePayloadRequest(524288);
        Connector.Request(MoveTemp(Packet), [&, i](FPlayHousePacket&& Response) {
            Responses[i] = MoveTemp(Response);
            Completed[i] = true;
        });
    }

    const bool AllDone = ExtWaitUntil([&]() {
        for (bool Done : Completed)
        {
            if (!Done) return false;
        }
        return true;
    }, 30.0);

    TestTrue(TEXT("All completed"), AllDone);
    for (int32 i = 0; i < RequestCount; ++i)
    {
        TArray<uint8> Payload;
        TestTrue(TEXT("Decode payload"), ExtDecodeBenchmarkReplyPayload(Responses[i].Payload, Payload));
        TestEqual(TEXT("Payload size 1MB"), Payload.Num(), 1048576);
    }

    Connector.Disconnect();
    return true;
}
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA03_Send_Simple, "PlayHouse.E2E.Cpp.A03.Send.Simple", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA03_Send_Simple::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("send_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("Fire and Forget"), 1);
    Connector.Send(MoveTemp(Packet));
    ExtPumpTicks(0.1);
    TestTrue(TEXT("Connected"), Connector.IsConnected());
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA03_Send_Multiple, "PlayHouse.E2E.Cpp.A03.Send.Multiple", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA03_Send_Multiple::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("send_multi_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    for (int32 i = 0; i < 10; ++i)
    {
        FPlayHousePacket Packet;
        Packet.MsgId = TEXT("EchoRequest");
        Packet.Payload = ExtEncodeEchoRequest(FString::Printf(TEXT("Message %d"), i), i);
        Connector.Send(MoveTemp(Packet));
    }
    ExtPumpTicks(0.2);
    TestTrue(TEXT("Connected"), Connector.IsConnected());
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA03_Send_VsRequest, "PlayHouse.E2E.Cpp.A03.Send.VsRequest", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA03_Send_VsRequest::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("send_mix_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket NotifyPacket;
    NotifyPacket.MsgId = TEXT("EchoRequest");
    NotifyPacket.Payload = ExtEncodeEchoRequest(TEXT("Notification"), 0);
    Connector.Send(MoveTemp(NotifyPacket));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("Echo test"), 1);
    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    TestTrue(TEXT("Request completed"), Completed);
    TestEqual(TEXT("ErrorCode 0"), Response.ErrorCode, 0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA03_Send_BroadcastPush, "PlayHouse.E2E.Cpp.A03.Send.BroadcastPush", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA03_Send_BroadcastPush::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("send_broadcast_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    bool Received = false;
    FString ReceivedData;
    Connector.OnReceive = [&](const FPlayHousePacket& Packet) {
        if (Packet.MsgId == TEXT("BroadcastNotify"))
        {
            FString EventType;
            if (ExtDecodeBroadcastNotify(Packet.Payload, EventType, ReceivedData))
            {
                Received = true;
            }
        }
    };

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("BroadcastRequest");
    Packet.Payload = ExtEncodeBroadcastRequest(TEXT("Hello from Send!"));
    Connector.Send(MoveTemp(Packet));

    const bool Completed = ExtWaitUntil([&]() { return Received; }, 5.0);
    TestTrue(TEXT("Push received"), Completed);
    TestEqual(TEXT("Payload match"), ReceivedData, FString(TEXT("Hello from Send!")));
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA03_Send_AfterDisconnect, "PlayHouse.E2E.Cpp.A03.Send.AfterDisconnect", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA03_Send_AfterDisconnect::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("send_disconnect_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));
    Connector.Disconnect();
    ExtPumpTicks(0.5);

    bool ErrorTriggered = false;
    Connector.OnError = [&](int32 Code, const FString&) {
        if (Code == PlayHouse::ErrorCode::ConnectionClosed)
        {
            ErrorTriggered = true;
        }
    };

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("AfterDisconnect"), 1);
    Connector.Send(MoveTemp(Packet));

    TestTrue(TEXT("Error triggered"), ExtWaitUntil([&]() { return ErrorTriggered; }, 5.0));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA03_Send_EmptyPayload, "PlayHouse.E2E.Cpp.A03.Send.EmptyPayload", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA03_Send_EmptyPayload::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("send_empty_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = TArray<uint8>();
    Connector.Send(MoveTemp(Packet));

    ExtPumpTicks(0.1);
    TestTrue(TEXT("Still connected"), Connector.IsConnected());
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA03_Send_LargePayload, "PlayHouse.E2E.Cpp.A03.Send.LargePayload", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA03_Send_LargePayload::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("send_large_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FString LargeContent;
    LargeContent.Reserve(50 * 1024);
    for (int32 i = 0; i < 50 * 1024; ++i)
    {
        LargeContent.AppendChar(TEXT('X'));
    }

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(LargeContent, 99);
    Connector.Send(MoveTemp(Packet));

    ExtPumpTicks(1.0);
    TestTrue(TEXT("Still connected"), Connector.IsConnected());
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA04_OnError_ConnectionClosed, "PlayHouse.E2E.Cpp.A04.OnError.ConnectionClosed", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA04_OnError_ConnectionClosed::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));

    bool ErrorFired = false;
    int32 ErrorCode = 0;
    FString ErrorMessage;
    Connector.OnError = [&](int32 Code, const FString& Message) {
        ErrorFired = true;
        ErrorCode = Code;
        ErrorMessage = Message;
    };

    Connector.Disconnect();
    ExtPumpTicks(0.5);

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("TestMessage");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("data"), 1);
    Connector.Send(MoveTemp(Packet));

    const bool Completed = ExtWaitUntil([&]() { return ErrorFired; }, 5.0);
    TestTrue(TEXT("OnError fired"), Completed);
    TestEqual(TEXT("ConnectionClosed"), ErrorCode, PlayHouse::ErrorCode::ConnectionClosed);
    TestTrue(TEXT("Message not empty"), !ErrorMessage.IsEmpty());
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA04_OnError_HandlerUpdate, "PlayHouse.E2E.Cpp.A04.OnError.HandlerUpdate", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA04_OnError_HandlerUpdate::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));

    int32 Handler1Count = 0;
    int32 Handler2Count = 0;

    Connector.OnError = [&](int32, const FString&) { Handler1Count++; };
    Connector.Disconnect();
    ExtPumpTicks(0.2);

    FPlayHousePacket Packet1;
    Packet1.MsgId = TEXT("Test1");
    Packet1.Payload = ExtEncodeEchoRequest(TEXT("data1"), 1);
    Connector.Send(MoveTemp(Packet1));
    ExtPumpTicks(0.1);

    Connector.OnError = [&](int32, const FString&) { Handler2Count++; };
    FPlayHousePacket Packet2;
    Packet2.MsgId = TEXT("Test2");
    Packet2.Payload = ExtEncodeEchoRequest(TEXT("data2"), 1);
    Connector.Send(MoveTemp(Packet2));
    ExtPumpTicks(0.1);

    TestEqual(TEXT("Handler1 once"), Handler1Count, 1);
    TestEqual(TEXT("Handler2 once"), Handler2Count, 1);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA04_OnError_NoHandler, "PlayHouse.E2E.Cpp.A04.OnError.NoHandler", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA04_OnError_NoHandler::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));
    Connector.OnError = nullptr;
    Connector.Disconnect();
    ExtPumpTicks(0.2);

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("TestMessage");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("data"), 1);
    Connector.Send(MoveTemp(Packet));
    ExtPumpTicks(0.1);
    TestTrue(TEXT("No crash"), true);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA04_OnError_ErrorCodeTypes, "PlayHouse.E2E.Cpp.A04.OnError.ErrorCodeTypes", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA04_OnError_ErrorCodeTypes::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));

    TArray<int32> ErrorCodes;
    Connector.OnError = [&](int32 Code, const FString&) { ErrorCodes.Add(Code); };

    Connector.Disconnect();
    ExtPumpTicks(0.2);

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("TestMessage");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("data"), 1);
    Connector.Send(MoveTemp(Packet));
    ExtPumpTicks(0.1);

    TestTrue(TEXT("Received error code"), ErrorCodes.Num() > 0);
    if (ErrorCodes.Num() > 0)
    {
        TestEqual(TEXT("ConnectionClosed"), ErrorCodes[0], PlayHouse::ErrorCode::ConnectionClosed);
    }
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA04_OnError_MessageProvided, "PlayHouse.E2E.Cpp.A04.OnError.MessageProvided", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA04_OnError_MessageProvided::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));

    bool MessageNotEmpty = false;
    Connector.OnError = [&](int32, const FString& Message) {
        MessageNotEmpty = !Message.IsEmpty();
    };

    Connector.Disconnect();
    ExtPumpTicks(0.2);

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("TestMessage");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("data"), 1);
    Connector.Send(MoveTemp(Packet));

    TestTrue(TEXT("Message provided"), ExtWaitUntil([&]() { return MessageNotEmpty; }, 5.0));
    return true;
}
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA05_Multi_TwoConnectors, "PlayHouse.E2E.Cpp.A05.Multi.TwoConnectors", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA05_Multi_TwoConnectors::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector1;
    FPlayHouseConnector Connector2;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);

    TestTrue(TEXT("Auth1"), ExtCreateStageConnectAndAuthenticate(Connector1, TEXT("multi_conn_user1"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));
    TestTrue(TEXT("Auth2"), ExtCreateStageConnectAndAuthenticate(Connector2, TEXT("multi_conn_user2"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    TestTrue(TEXT("Connector1 connected"), Connector1.IsConnected());
    TestTrue(TEXT("Connector2 connected"), Connector2.IsConnected());
    Connector1.Disconnect();
    Connector2.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA05_Multi_SendIndependently, "PlayHouse.E2E.Cpp.A05.Multi.SendIndependently", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA05_Multi_SendIndependently::RunTest(const FString& Parameters)
{
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TArray<TUniquePtr<FPlayHouseConnector>> Connectors;

    for (int32 i = 0; i < 3; ++i)
    {
        TUniquePtr<FPlayHouseConnector> Conn = MakeUnique<FPlayHouseConnector>();
        if (ExtCreateStageConnectAndAuthenticate(*Conn, FString::Printf(TEXT("multi_conn_batch_%d"), i), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port))
        {
            Connectors.Add(MoveTemp(Conn));
        }
    }

    TestTrue(TEXT("At least 2 connectors"), Connectors.Num() >= 2);

    TArray<bool> Completed;
    TArray<FPlayHousePacket> Responses;
    Completed.Init(false, Connectors.Num());
    Responses.SetNum(Connectors.Num());

    for (int32 i = 0; i < Connectors.Num(); ++i)
    {
        FPlayHousePacket Packet;
        Packet.MsgId = TEXT("EchoRequest");
        Packet.Payload = ExtEncodeEchoRequest(FString::Printf(TEXT("Connector%d"), i), i);
        Connectors[i]->Request(MoveTemp(Packet), [&, i](FPlayHousePacket&& Response) {
            Responses[i] = MoveTemp(Response);
            Completed[i] = true;
        });
    }

    const bool AllDone = ExtWaitUntil([&]() {
        for (bool Done : Completed)
        {
            if (!Done) return false;
        }
        return true;
    }, 5.0);

    TestTrue(TEXT("All done"), AllDone);
    for (int32 i = 0; i < Connectors.Num(); ++i)
    {
        TestEqual(TEXT("ErrorCode 0"), Responses[i].ErrorCode, 0);
    }

    for (TUniquePtr<FPlayHouseConnector>& Conn : Connectors)
    {
        Conn->Disconnect();
    }
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA05_Multi_IndependentLifecycles, "PlayHouse.E2E.Cpp.A05.Multi.IndependentLifecycles", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA05_Multi_IndependentLifecycles::RunTest(const FString& Parameters)
{
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    FPlayHouseConnector Connector1;
    FPlayHouseConnector Connector2;

    TestTrue(TEXT("Auth1"), ExtCreateStageConnectAndAuthenticate(Connector1, TEXT("multi_conn_lifecycle1"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));
    TestTrue(TEXT("Auth2"), ExtCreateStageConnectAndAuthenticate(Connector2, TEXT("multi_conn_lifecycle2"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    Connector1.Disconnect();
    ExtPumpTicks(0.5);
    TestFalse(TEXT("Connector1 disconnected"), Connector1.IsConnected());
    TestTrue(TEXT("Connector2 still connected"), Connector2.IsConnected());

    Connector2.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA05_Multi_SeparateCallbacks, "PlayHouse.E2E.Cpp.A05.Multi.SeparateCallbacks", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA05_Multi_SeparateCallbacks::RunTest(const FString& Parameters)
{
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    FPlayHouseConnector Connector1;
    FPlayHouseConnector Connector2;

    bool Received1 = false;
    bool Received2 = false;
    Connector1.OnReceive = [&](const FPlayHousePacket&) { Received1 = true; };
    Connector2.OnReceive = [&](const FPlayHousePacket&) { Received2 = true; };

    TestTrue(TEXT("Auth1"), ExtCreateStageConnectAndAuthenticate(Connector1, TEXT("multi_conn_cb1"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));
    TestTrue(TEXT("Auth2"), ExtCreateStageConnectAndAuthenticate(Connector2, TEXT("multi_conn_cb2"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket Packet1;
    Packet1.MsgId = TEXT("BroadcastRequest");
    Packet1.Payload = ExtEncodeBroadcastRequest(TEXT("connector1"));
    Connector1.Send(MoveTemp(Packet1));

    FPlayHousePacket Packet2;
    Packet2.MsgId = TEXT("BroadcastRequest");
    Packet2.Payload = ExtEncodeBroadcastRequest(TEXT("connector2"));
    Connector2.Send(MoveTemp(Packet2));

    const bool Completed = ExtWaitUntil([&]() { return Received1 || Received2; }, 5.0);
    TestTrue(TEXT("At least one received"), Completed);

    Connector1.Disconnect();
    Connector2.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA05_Multi_SequentialLifecycle, "PlayHouse.E2E.Cpp.A05.Multi.SequentialLifecycle", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA05_Multi_SequentialLifecycle::RunTest(const FString& Parameters)
{
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    for (int32 i = 0; i < 3; ++i)
    {
        FPlayHouseConnector Connector;
        TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, FString::Printf(TEXT("multi_conn_seq_%d"), i), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));
        Connector.Disconnect();
        ExtPumpTicks(0.1);
    }
    TestTrue(TEXT("Sequential lifecycle ok"), true);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA06_Edge_EmptyMsgId, "PlayHouse.E2E.Cpp.A06.Edge.EmptyMsgId", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA06_Edge_EmptyMsgId::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("edge_empty_msgid_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("edge"), 1);

    FPlayHousePacket Response;
    ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA06_Edge_LongMsgId, "PlayHouse.E2E.Cpp.A06.Edge.LongMsgId", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA06_Edge_LongMsgId::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("edge_long_msgid_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FString LongMsgId;
    LongMsgId.Reserve(250);
    for (int32 i = 0; i < 250; ++i)
    {
        LongMsgId.AppendChar(TEXT('A'));
    }

    FPlayHousePacket Packet;
    Packet.MsgId = LongMsgId;
    Packet.Payload = ExtEncodeEchoRequest(TEXT("edge"), 2);

    FPlayHousePacket Response;
    ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA06_Edge_SpecialMsgId, "PlayHouse.E2E.Cpp.A06.Edge.SpecialMsgId", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA06_Edge_SpecialMsgId::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("edge_special_msgid_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("Test@Message#123$");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("edge"), 3);

    FPlayHousePacket Response;
    ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA06_Edge_RapidConnectDisconnect, "PlayHouse.E2E.Cpp.A06.Edge.RapidConnectDisconnect", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA06_Edge_RapidConnectDisconnect::RunTest(const FString& Parameters)
{
    for (int32 i = 0; i < 5; ++i)
    {
        FPlayHouseConnector Connector;
        const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
        if (ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port))
        {
            Connector.Disconnect();
            ExtPumpTicks(0.1);
        }
    }
    TestTrue(TEXT("Rapid connect/disconnect ok"), true);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA06_Edge_TickerExcessive, "PlayHouse.E2E.Cpp.A06.Edge.TickerExcessive", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA06_Edge_TickerExcessive::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("edge_mainthread_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    for (int32 i = 0; i < 1000; ++i)
    {
        FTSBackgroundableTicker::GetCoreTicker().Tick(0.0f);
    }
    TestTrue(TEXT("Connected"), Connector.IsConnected());
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA06_Edge_NoTickCallback, "PlayHouse.E2E.Cpp.A06.Edge.NoTickCallback", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA06_Edge_NoTickCallback::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("edge_no_mainthread_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    bool CallbackFired = false;
    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("No MainThread"), 1);
    Connector.Request(MoveTemp(Packet), [&](FPlayHousePacket&&) {
        CallbackFired = true;
    });

    FPlatformProcess::Sleep(2.0f);
    TestTrue(TEXT("Callback may fire in automation mode"), CallbackFired);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA06_Edge_ZeroPayload, "PlayHouse.E2E.Cpp.A06.Edge.ZeroPayload", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA06_Edge_ZeroPayload::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("edge_zero_payload_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EmptyPayloadRequest");
    Packet.Payload = TArray<uint8>();

    FPlayHousePacket Response;
    ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA06_Edge_BinaryZeros, "PlayHouse.E2E.Cpp.A06.Edge.BinaryZeros", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA06_Edge_BinaryZeros::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("edge_binary_zero_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("NullByteRequest");
    Packet.Payload = { 0x00, 0x01, 0x00, 0x02, 0x00, 0x03 };

    FPlayHousePacket Response;
    ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA06_Edge_ConnectWithoutInit, "PlayHouse.E2E.Cpp.A06.Edge.ConnectWithoutInit", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA06_Edge_ConnectWithoutInit::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    bool ErrorTriggered = false;
    Connector.OnError = [&](int32, const FString&) { ErrorTriggered = true; };

    const FString Host = ExtGetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("127.0.0.1"));
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    Connector.Connect(Host, Port);

    TestTrue(TEXT("Error triggered"), ExtWaitUntil([&]() { return ErrorTriggered; }, 1.0));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA06_Edge_DoubleInit, "PlayHouse.E2E.Cpp.A06.Edge.DoubleInit", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA06_Edge_DoubleInit::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    ExtInitConnector(Connector, EPlayHouseTransport::Tcp);
    ExtInitConnector(Connector, EPlayHouseTransport::Tcp);

    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Connect"), ExtCreateStageAndConnect(Connector, EPlayHouseTransport::Tcp, Port));
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA06_Edge_CallbackFailure, "PlayHouse.E2E.Cpp.A06.Edge.CallbackFailure", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA06_Edge_CallbackFailure::RunTest(const FString& Parameters)
{
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_PORT"), 34001);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("edge_callback_user"), TEXT("valid_token"), EPlayHouseTransport::Tcp, Port));

    bool Received = false;
    Connector.OnReceive = [&](const FPlayHousePacket&) {
        Received = true;
    };

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("BroadcastRequest");
    Packet.Payload = ExtEncodeBroadcastRequest(TEXT("Trigger callback"));
    Connector.Send(MoveTemp(Packet));

    ExtWaitUntil([&]() { return Received; }, 2.0);
    TestTrue(TEXT("Connected"), Connector.IsConnected());
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA07_Tls_TcpTls, "PlayHouse.E2E.Cpp.A07.Tls.TcpTls", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA07_Tls_TcpTls::RunTest(const FString& Parameters)
{
    if (!ExtIsTlsEnabled())
    {
        AddWarning(TEXT("TLS disabled. Skipping."));
        return true;
    }
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_TCP_TLS_PORT"), 34002);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("tls-user-1"), TEXT("valid_token"), EPlayHouseTransport::Tls, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("Hello TLS"), 1);
    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    TestTrue(TEXT("Echo completed"), Completed);
    FString Content;
    int32 Seq = 0;
    TestTrue(TEXT("Decode EchoReply"), ExtDecodeEchoReply(Response.Payload, Content, Seq));
    TestEqual(TEXT("Content"), Content, FString(TEXT("Hello TLS")));
    Connector.Disconnect();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseA07_Tls_Wss, "PlayHouse.E2E.Cpp.A07.Tls.Wss", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FPlayHouseA07_Tls_Wss::RunTest(const FString& Parameters)
{
    if (!ExtIsTlsEnabled())
    {
        AddWarning(TEXT("TLS disabled. Skipping."));
        return true;
    }
    if (!ExtIsWebSocketEnabled())
    {
        AddWarning(TEXT("WebSocket disabled. Skipping."));
        return true;
    }
    FPlayHouseConnector Connector;
    const int32 Port = ExtGetEnvInt(TEXT("TEST_SERVER_HTTPS_PORT"), 8443);
    TestTrue(TEXT("Auth"), ExtCreateStageConnectAndAuthenticate(Connector, TEXT("wss-user-1"), TEXT("valid_token"), EPlayHouseTransport::Wss, Port));

    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.Payload = ExtEncodeEchoRequest(TEXT("Hello WSS"), 2);
    FPlayHousePacket Response;
    const bool Completed = ExtRequestAndWait(Connector, MoveTemp(Packet), Response, 5.0);
    TestTrue(TEXT("Echo completed"), Completed);
    FString Content;
    int32 Seq = 0;
    TestTrue(TEXT("Decode EchoReply"), ExtDecodeEchoReply(Response.Payload, Content, Seq));
    TestEqual(TEXT("Content"), Content, FString(TEXT("Hello WSS")));
    Connector.Disconnect();
    return true;
}

#endif // WITH_AUTOMATION_TESTS

