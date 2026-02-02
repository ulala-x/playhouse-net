#pragma once

#include "CoreMinimal.h"
#include "PlayHouseTransport.h"

class IWebSocket;

class FPlayHouseWssTransport : public IPlayHouseTransport
{
public:
    virtual bool Connect(const FString& Host, int32 Port) override;
    virtual void Disconnect() override;
    virtual bool IsConnected() const override;
    virtual bool SendBytes(const uint8* Data, int32 Size) override;

private:
    TAtomic<bool> bConnected{false};
    TSharedPtr<IWebSocket> Socket;
};
