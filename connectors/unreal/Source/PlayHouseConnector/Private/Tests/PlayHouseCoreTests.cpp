#include "CoreMinimal.h"
#include "Misc/AutomationTest.h"

#include "Internal/PlayHousePacketCodec.h"
#include "Internal/PlayHouseRingBuffer.h"
#include "PlayHouseProtocol.h"

#if WITH_AUTOMATION_TESTS

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHousePacketCodecTest, "PlayHouse.Core.PacketCodec", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FPlayHousePacketCodecTest::RunTest(const FString& Parameters)
{
    FPlayHousePacket Packet;
    Packet.MsgId = TEXT("EchoRequest");
    Packet.MsgSeq = 1;
    Packet.StageId = 42;
    Packet.Payload = {0x01, 0x02, 0x03};

    TArray<uint8> Encoded;
    TestTrue(TEXT("Encode request"), FPlayHousePacketCodec::EncodeRequest(Packet, Encoded));
    TestTrue(TEXT("Encoded bytes not empty"), Encoded.Num() > 0);

    // Construct a fake response by reusing request bytes and appending response fields.
    // Response format: ContentSize + MsgIdLen + MsgId + MsgSeq + StageId + ErrorCode + OriginalSize + Payload
    // For test, append ErrorCode=0, OriginalSize=0.
    TArray<uint8> Response = Encoded;
    Response.Add(0x00);
    Response.Add(0x00);
    Response.Add(0x00);
    Response.Add(0x00);
    Response.Add(0x00);
    Response.Add(0x00);

    // Fix ContentSize (request had no error/original fields)
    uint32 ContentSize = Response.Num() - 4;
    Response[0] = static_cast<uint8>(ContentSize & 0xFF);
    Response[1] = static_cast<uint8>((ContentSize >> 8) & 0xFF);
    Response[2] = static_cast<uint8>((ContentSize >> 16) & 0xFF);
    Response[3] = static_cast<uint8>((ContentSize >> 24) & 0xFF);

    FPlayHousePacket Decoded;
    TestTrue(TEXT("Decode response"), FPlayHousePacketCodec::DecodeResponse(Response.GetData(), Response.Num(), Decoded));
    TestEqual(TEXT("MsgId"), Decoded.MsgId, Packet.MsgId);
    TestEqual(TEXT("MsgSeq"), Decoded.MsgSeq, Packet.MsgSeq);
    TestEqual(TEXT("StageId"), Decoded.StageId, Packet.StageId);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FPlayHouseRingBufferTest, "PlayHouse.Core.RingBuffer", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FPlayHouseRingBufferTest::RunTest(const FString& Parameters)
{
    FPlayHouseRingBuffer Buffer(16);
    uint8 DataA[4] = {1, 2, 3, 4};
    Buffer.Write(DataA, 4);
    TestEqual(TEXT("Count after write"), Buffer.GetCount(), 4);

    uint8 Peek[2] = {0, 0};
    Buffer.Peek(Peek, 2, 1);
    TestEqual(TEXT("Peek value"), Peek[0], static_cast<uint8>(2));

    uint8 Out[4] = {0, 0, 0, 0};
    Buffer.Read(Out, 4);
    TestEqual(TEXT("Read value"), Out[3], static_cast<uint8>(4));
    TestEqual(TEXT("Count after read"), Buffer.GetCount(), 0);
    return true;
}

#endif // WITH_AUTOMATION_TESTS
