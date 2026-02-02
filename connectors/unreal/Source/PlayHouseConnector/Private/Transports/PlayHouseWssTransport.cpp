#include "PlayHouseWssTransport.h"
#include "PlayHouseProtocol.h"

#include "IWebSocket.h"
#include "WebSocketsModule.h"
#include "Async/Async.h"

namespace
{
    void DispatchToGameThread(TFunction<void()>&& Func)
    {
        if (IsInGameThread())
        {
            Func();
        }
        else
        {
            AsyncTask(ENamedThreads::GameThread, MoveTemp(Func));
        }
    }
}

bool FPlayHouseWssTransport::Connect(const FString& Host, int32 Port)
{
    if (bConnected.Load())
    {
        return true;
    }

    // Check for already connecting state
    if (Socket.IsValid())
    {
        return true; // Already attempting to connect
    }

    FString Url = FString::Printf(TEXT("wss://%s:%d/ws"), *Host, Port);
    if (!FModuleManager::Get().IsModuleLoaded(TEXT("WebSockets")))
    {
        FModuleManager::Get().LoadModule(TEXT("WebSockets"));
    }

    Socket = FWebSocketsModule::Get().CreateWebSocket(Url);
    if (!Socket.IsValid())
    {
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::ConnectionFailed, TEXT("Failed to create WebSocket"));
        }
        return false;
    }

    // Store weak reference for safe callback access
    TWeakPtr<IWebSocket> WeakSocket = Socket;

    Socket->OnConnected().AddLambda([this, WeakSocket]() {
        // Verify socket is still valid (not disconnected during callback)
        if (!WeakSocket.IsValid())
        {
            return;
        }
        bConnected.Store(true);
    });

    Socket->OnConnectionError().AddLambda([this, WeakSocket](const FString& Error) {
        if (!WeakSocket.IsValid())
        {
            return;
        }
        // Marshal to game thread for thread safety
        DispatchToGameThread([this, Error]() {
            if (OnTransportError)
            {
                OnTransportError(PlayHouse::ErrorCode::ConnectionFailed, Error);
            }
        });
    });

    Socket->OnClosed().AddLambda([this, WeakSocket](int32 StatusCode, const FString& Reason, bool bWasClean) {
        if (!WeakSocket.IsValid())
        {
            return;
        }
        bConnected.Store(false);
        // Marshal to game thread for thread safety
        DispatchToGameThread([this]() {
            if (OnDisconnected)
            {
                OnDisconnected();
            }
        });
    });

    Socket->OnRawMessage().AddLambda([this, WeakSocket](const void* Data, SIZE_T Size, SIZE_T BytesRemaining) {
        if (!WeakSocket.IsValid())
        {
            return;
        }

        // Validate incoming data
        if (Data == nullptr || Size == 0)
        {
            return;
        }

        // Validate size is within reasonable bounds
        if (Size > PlayHouse::Protocol::MaxBodySize)
        {
            DispatchToGameThread([this]() {
                if (OnTransportError)
                {
                    OnTransportError(PlayHouse::ErrorCode::InvalidResponse, TEXT("Received data exceeds maximum size"));
                }
            });
            return;
        }

        // Copy data for safe transfer to game thread
        TArray<uint8> DataCopy;
        DataCopy.Append(reinterpret_cast<const uint8*>(Data), static_cast<int32>(Size));

        DispatchToGameThread([this, DataCopy = MoveTemp(DataCopy)]() {
            if (OnBytesReceived)
            {
                OnBytesReceived(DataCopy.GetData(), DataCopy.Num());
            }
        });
    });

    Socket->Connect();
    return true;
}

void FPlayHouseWssTransport::Disconnect()
{
    // Set connected to false first to prevent new sends
    bConnected.Store(false);

    if (Socket.IsValid())
    {
        Socket->Close();
        Socket.Reset();
    }
}

bool FPlayHouseWssTransport::IsConnected() const
{
    return bConnected.Load() && Socket.IsValid();
}

bool FPlayHouseWssTransport::SendBytes(const uint8* Data, int32 Size)
{
    if (!Socket.IsValid() || !bConnected.Load())
    {
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::ConnectionClosed, TEXT("WSS not connected"));
        }
        return false;
    }

    // Validate input parameters
    if (Data == nullptr && Size > 0)
    {
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::InvalidResponse, TEXT("Invalid send data"));
        }
        return false;
    }

    if (Size <= 0)
    {
        return true; // Empty send is no-op
    }

    // Validate size is within bounds
    if (Size > static_cast<int32>(PlayHouse::Protocol::MaxBodySize))
    {
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::InvalidResponse, TEXT("Send data exceeds maximum size"));
        }
        return false;
    }

    Socket->Send(Data, Size, true);
    return true;
}

bool FPlayHouseWssTransport::IsConnecting() const
{
    return Socket.IsValid() && !bConnected.Load();
}
