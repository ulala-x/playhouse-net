#include "PlayHouseWssTransport.h"
#include "PlayHouseProtocol.h"

#include "IWebSocket.h"
#include "WebSocketsModule.h"
#include "Async/Async.h"

#if WITH_AUTOMATION_TESTS
#include "Misc/AutomationTest.h"
#endif

namespace
{
    void DispatchToGameThreadWss(TFunction<void()>&& Func)
    {
#if WITH_AUTOMATION_TESTS
        if (GIsAutomationTesting)
        {
            Func();
            return;
        }
#endif
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
    if (State.IsValid() && State->Connected.Load())
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

    State = MakeShared<FWssTransportState>();
    State->OnConnected = OnConnected;
    State->OnDisconnected = OnDisconnected;
    State->OnBytesReceived = OnBytesReceived;
    State->OnTransportError = OnTransportError;

    // Store weak reference for safe callback access
    TWeakPtr<IWebSocket> WeakSocket = Socket;
    TWeakPtr<FWssTransportState> WeakState = State;

    Socket->OnConnected().AddLambda([WeakSocket, WeakState]() {
        // Verify socket is still valid (not disconnected during callback)
        if (!WeakSocket.IsValid())
        {
            return;
        }
        const TSharedPtr<FWssTransportState> PinnedState = WeakState.Pin();
        if (!PinnedState.IsValid() || !PinnedState->Alive.Load())
        {
            return;
        }
        PinnedState->Connected.Store(true);
        DispatchToGameThreadWss([PinnedState]() {
            if (PinnedState->OnConnected)
            {
                PinnedState->OnConnected();
            }
        });
    });

    Socket->OnConnectionError().AddLambda([WeakSocket, WeakState](const FString& Error) {
        if (!WeakSocket.IsValid())
        {
            return;
        }
        const TSharedPtr<FWssTransportState> PinnedState = WeakState.Pin();
        if (!PinnedState.IsValid() || !PinnedState->Alive.Load())
        {
            return;
        }
        // Marshal to game thread for thread safety
        DispatchToGameThreadWss([PinnedState, Error]() {
            if (PinnedState->OnTransportError)
            {
                PinnedState->OnTransportError(PlayHouse::ErrorCode::ConnectionFailed, Error);
            }
        });
    });

    Socket->OnClosed().AddLambda([WeakSocket, WeakState](int32 StatusCode, const FString& Reason, bool bWasClean) {
        if (!WeakSocket.IsValid())
        {
            return;
        }
        const TSharedPtr<FWssTransportState> PinnedState = WeakState.Pin();
        if (!PinnedState.IsValid() || !PinnedState->Alive.Load())
        {
            return;
        }
        PinnedState->Connected.Store(false);
        // Marshal to game thread for thread safety
        DispatchToGameThreadWss([PinnedState]() {
            if (PinnedState->OnDisconnected)
            {
                PinnedState->OnDisconnected();
            }
        });
    });

    Socket->OnRawMessage().AddLambda([WeakSocket, WeakState](const void* Data, SIZE_T Size, SIZE_T BytesRemaining) {
        if (!WeakSocket.IsValid())
        {
            return;
        }
        const TSharedPtr<FWssTransportState> PinnedState = WeakState.Pin();
        if (!PinnedState.IsValid() || !PinnedState->Alive.Load())
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
            DispatchToGameThreadWss([PinnedState]() {
                if (PinnedState->OnTransportError)
                {
                    PinnedState->OnTransportError(PlayHouse::ErrorCode::InvalidResponse, TEXT("Received data exceeds maximum size"));
                }
            });
            return;
        }

        // Copy data for safe transfer to game thread
        TArray<uint8> DataCopy;
        DataCopy.Append(reinterpret_cast<const uint8*>(Data), static_cast<int32>(Size));

        DispatchToGameThreadWss([PinnedState, DataCopy = MoveTemp(DataCopy)]() {
            if (PinnedState->OnBytesReceived)
            {
                PinnedState->OnBytesReceived(DataCopy.GetData(), DataCopy.Num());
            }
        });
    });

    Socket->Connect();
    return true;
}

void FPlayHouseWssTransport::Disconnect()
{
    if (State.IsValid())
    {
        State->Alive.Store(false);
        State->Connected.Store(false);
    }

    if (Socket.IsValid())
    {
        Socket->Close();
        Socket.Reset();
    }
}

bool FPlayHouseWssTransport::IsConnected() const
{
    return State.IsValid() && State->Connected.Load() && Socket.IsValid();
}

bool FPlayHouseWssTransport::SendBytes(const uint8* Data, int32 Size)
{
    if (!Socket.IsValid() || !State.IsValid() || !State->Connected.Load())
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
    return Socket.IsValid() && (!State.IsValid() || !State->Connected.Load());
}
