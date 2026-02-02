#pragma once

#include "CoreMinimal.h"

UENUM()
enum class EPlayHouseTransport : uint8
{
    Tcp,
    Tls,
    Ws,
    Wss
};

struct FPlayHouseConfig
{
    int32 SendBufferSize = 64 * 1024;
    int32 ReceiveBufferSize = 256 * 1024;
    int32 HeartbeatIntervalMs = 10000;
    int32 RequestTimeoutMs = 30000;

    bool bEnableReconnect = false;
    int32 ReconnectIntervalMs = 5000;
    int32 MaxReconnectAttempts = 0;

    EPlayHouseTransport Transport = EPlayHouseTransport::Tcp;
};
