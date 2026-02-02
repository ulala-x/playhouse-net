#pragma once

#include "CoreMinimal.h"

struct FPlayHousePacket
{
    FString MsgId;
    TArray<uint8> Payload;

    uint16 MsgSeq = 0;
    int64 StageId = 0;
    int16 ErrorCode = 0;
    uint32 OriginalSize = 0;
};
