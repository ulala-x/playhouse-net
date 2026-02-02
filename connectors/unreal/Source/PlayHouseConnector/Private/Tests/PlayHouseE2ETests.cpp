#include "CoreMinimal.h"
#include "Misc/AutomationTest.h"
#include "HAL/PlatformMisc.h"

#include "PlayHouseConnector.h"
#include "PlayHouseConfig.h"
#include "PlayHousePacket.h"

#if WITH_AUTOMATION_TESTS

namespace
{
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
        while (FPlatformTime::Seconds() < Deadline)
        {
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

        Ctx.Connector.OnConnect = [&]() { Ctx.Connected = true; };
        Ctx.Connector.OnError = [&](int32, const FString&) { Ctx.Error = true; };

        const FString Host = GetEnvOrDefault(TEXT("TEST_SERVER_HOST"), TEXT("localhost"));
        Ctx.Connector.Connect(Host, Port);

        bool Connected = WaitUntil([&]() { return Ctx.Connected || Ctx.Error; }, 5.0);
        if (!Connected || !Ctx.Connected)
        {
            return false;
        }

        FString Payload = TEXT("{\"content\":\"Hello\",\"sequence\":1}");
        FTCHARToUTF8 Utf8(*Payload);
        FPlayHousePacket Packet;
        Packet.MsgId = TEXT("EchoRequest");
        Packet.Payload.Append(reinterpret_cast<const uint8*>(Utf8.Get()), Utf8.Length());

        Ctx.Connector.Request(MoveTemp(Packet), [&](FPlayHousePacket&& Response) {
            Ctx.Response = MoveTemp(Response);
            Ctx.ResponseReady = true;
        });

        bool GotResponse = WaitUntil([&]() { return Ctx.ResponseReady; }, 5.0);
        if (!GotResponse)
        {
            return false;
        }

        return Ctx.Response.MsgId == TEXT("EchoReply") && Ctx.Response.ErrorCode == 0;
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
    const int32 Port = GetEnvInt(TEXT("TEST_SERVER_HTTP_PORT"), 8080);
    TestTrue(TEXT("WS E2E"), RunEchoE2E(EPlayHouseTransport::Ws, Port));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseWssE2E, "PlayHouse.E2E.Wss", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FPlayHouseWssE2E::RunTest(const FString& Parameters)
{
    const int32 Port = GetEnvInt(TEXT("TEST_SERVER_HTTPS_PORT"), 8443);
    TestTrue(TEXT("WSS E2E"), RunEchoE2E(EPlayHouseTransport::Wss, Port));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseTlsE2E, "PlayHouse.E2E.Tls", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FPlayHouseTlsE2E::RunTest(const FString& Parameters)
{
    const int32 Port = GetEnvInt(TEXT("TEST_SERVER_TCP_TLS_PORT"), 34002);
    TestTrue(TEXT("TLS E2E"), RunEchoE2E(EPlayHouseTransport::Tls, Port));
    return true;
}

#endif // WITH_AUTOMATION_TESTS
