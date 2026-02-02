#pragma once

#include "CoreMinimal.h"
#include "PlayHouseConfig.h"
#include "PlayHousePacket.h"

class IPlayHouseTransport;

class FPlayHouseConnector
{
public:
    ~FPlayHouseConnector();

    void Init(const FPlayHouseConfig& Config);

    void Connect(const FString& Host, int32 Port);
    void Disconnect();
    bool IsConnected() const;

    void Send(FPlayHousePacket&& Packet);
    void Request(FPlayHousePacket&& Packet, TFunction<void(FPlayHousePacket&&)> OnResponse);
    void Authenticate(FPlayHousePacket&& Packet, TFunction<void(bool)> OnResult);

    TFunction<void()> OnConnect;
    TFunction<void()> OnDisconnect;
    TFunction<void(const FPlayHousePacket&)> OnReceive;
    TFunction<void(int32 Code, const FString& Message)> OnError;

private:
    class FPlayHouseConnectorImpl;
    TUniquePtr<FPlayHouseConnectorImpl> Impl_;
    double LastTimeoutCheckSeconds_ = 0.0;
    bool TickInternal(float DeltaSeconds);
    void HandleBytes(const uint8* Data, int32 Size);
};
