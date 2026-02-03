#pragma once

#include "CoreMinimal.h"
#include "PlayHouseTransport.h"

class IWebSocket;

/**
 * WebSocket (WS) Transport Implementation
 *
 * Thread Model:
 * - Game Thread: All public API calls (Connect, Disconnect, SendBytes, IsConnected, IsConnecting)
 * - WebSocket Module Thread: IWebSocket internally manages its own threading
 *   - Callbacks (OnConnected, OnConnectionError, OnClosed, OnRawMessage) are invoked
 *     on an internal WebSocket thread, NOT the Game Thread
 *
 * Thread Safety:
 * - bConnected: Atomic variable, thread-safe read/write
 * - Socket: TSharedPtr, thread-safe reference counting
 * - IWebSocket callbacks: Invoked on WebSocket module's thread
 *   WARNING: Callbacks may NOT be on Game Thread. Use AsyncTask to marshal to Game Thread if needed.
 *
 * Connection States:
 * - Not Connected: Socket is null or bConnected is false
 * - Connecting: Socket is valid but bConnected is false (IsConnecting() returns true)
 * - Connected: Socket is valid and bConnected is true
 *
 * Resource Cleanup Order (in Disconnect()):
 * 1. Close WebSocket connection (Socket->Close())
 * 2. Reset Socket shared pointer
 * 3. Set bConnected to false
 *
 * Note: Connection is asynchronous. Connect() returns immediately.
 *       Wait for OnConnected callback before assuming connection is ready.
 */
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
    struct FWsTransportState
    {
        TAtomic<bool> Alive{true};
        TAtomic<bool> Connected{false};
        TFunction<void()> OnConnected;
        TFunction<void()> OnDisconnected;
        TFunction<void(const uint8* Data, int32 Size)> OnBytesReceived;
        TFunction<void(int32 Code, const FString& Message)> OnTransportError;
    };

    TSharedPtr<FWsTransportState> State;
    TSharedPtr<IWebSocket> Socket;
};
