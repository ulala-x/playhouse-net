#pragma once

#include "CoreMinimal.h"
#include "PlayHousePacket.h"

class FPlayHousePacketCodec
{
public:
    static bool EncodeRequest(const FPlayHousePacket& Packet, TArray<uint8>& OutBytes);
    static bool DecodeResponse(const uint8* Data, int32 Size, FPlayHousePacket& OutPacket);
};
