#pragma once

#include "CoreMinimal.h"
#include "PlayHouseTransport.h"

class IWebSocket;

class FPlayHouseWsTransport : public IPlayHouseTransport
{
public:
    /**
     * Initiates WebSocket connection (async).
     * @return true if connection attempt started, false if already connected or failed to create socket
     * @note Connection is async. Check IsConnected() after OnConnected callback fires.
     */
    virtual bool Connect(const FString& Host, int32 Port) override;
    virtual void Disconnect() override;
    virtual bool IsConnected() const override;
    virtual bool SendBytes(const uint8* Data, int32 Size) override;

    /** Returns true if currently attempting to connect (but not yet connected) */
    bool IsConnecting() const;

private:
    TAtomic<bool> bConnected{false};
    TSharedPtr<IWebSocket> Socket;
};
