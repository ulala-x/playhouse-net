#pragma once

#include "CoreMinimal.h"
#include "PlayHouseTransport.h"

class FSocket;
class FRunnableThread;

class FPlayHouseTcpTransport : public IPlayHouseTransport
{
public:
    virtual bool Connect(const FString& Host, int32 Port) override;
    virtual void Disconnect() override;
    virtual bool IsConnected() const override;
    virtual bool SendBytes(const uint8* Data, int32 Size) override;

private:
    bool bConnected = false;
    FSocket* Socket = nullptr;
    TUniquePtr<class FTcpWorker> Worker;
    TUniquePtr<FRunnableThread> Thread;
};
