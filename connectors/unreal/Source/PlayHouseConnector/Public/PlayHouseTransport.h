#pragma once

#include "CoreMinimal.h"

class IPlayHouseTransport
{
public:
    virtual ~IPlayHouseTransport() = default;

    virtual bool Connect(const FString& Host, int32 Port) = 0;
    virtual void Disconnect() = 0;
    virtual bool IsConnected() const = 0;

    virtual bool SendBytes(const uint8* Data, int32 Size) = 0;

    TFunction<void(const uint8* Data, int32 Size)> OnBytesReceived;
    TFunction<void()> OnDisconnected;
    TFunction<void(int32 Code, const FString& Message)> OnTransportError;
};
