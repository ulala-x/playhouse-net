#pragma once

#include "CoreMinimal.h"
#include "PlayHousePacket.h"

class FPlayHouseRequestMap
{
public:
    void Add(uint16 MsgSeq, double DeadlineSeconds, TFunction<void(FPlayHousePacket&&)> Callback);
    bool Resolve(uint16 MsgSeq, TFunction<void(FPlayHousePacket&&)>& OutCallback);
    void CollectExpired(double NowSeconds, TArray<TPair<uint16, TFunction<void(FPlayHousePacket&&)>>>& OutExpired);
    void Clear(TArray<TFunction<void(FPlayHousePacket&&)>>& OutCallbacks);

private:
    struct FPendingRequest
    {
        double DeadlineSeconds = 0.0;
        TFunction<void(FPlayHousePacket&&)> Callback;
    };

    TMap<uint16, FPendingRequest> Pending_;
    FCriticalSection Mutex_;
};
