#include "PlayHouseRequestMap.h"

void FPlayHouseRequestMap::Add(uint16 MsgSeq, double DeadlineSeconds, TFunction<void(FPlayHousePacket&&)> Callback)
{
    FScopeLock Lock(&Mutex_);
    FPendingRequest Pending;
    Pending.DeadlineSeconds = DeadlineSeconds;
    Pending.Callback = MoveTemp(Callback);
    Pending_.Add(MsgSeq, MoveTemp(Pending));
}

bool FPlayHouseRequestMap::Resolve(uint16 MsgSeq, TFunction<void(FPlayHousePacket&&)>& OutCallback)
{
    FScopeLock Lock(&Mutex_);
    FPendingRequest* Pending = Pending_.Find(MsgSeq);
    if (!Pending)
    {
        return false;
    }

    OutCallback = MoveTemp(Pending->Callback);
    Pending_.Remove(MsgSeq);
    return true;
}

void FPlayHouseRequestMap::CollectExpired(double NowSeconds, TArray<TPair<uint16, TFunction<void(FPlayHousePacket&&)>>>& OutExpired)
{
    FScopeLock Lock(&Mutex_);
    for (auto It = Pending_.CreateIterator(); It; ++It)
    {
        if (NowSeconds >= It->Value.DeadlineSeconds)
        {
            OutExpired.Emplace(It->Key, MoveTemp(It->Value.Callback));
            It.RemoveCurrent();
        }
    }
}

void FPlayHouseRequestMap::Clear(TArray<TFunction<void(FPlayHousePacket&&)>>& OutCallbacks)
{
    FScopeLock Lock(&Mutex_);
    for (auto& Pair : Pending_)
    {
        OutCallbacks.Add(MoveTemp(Pair.Value.Callback));
    }
    Pending_.Empty();
}
