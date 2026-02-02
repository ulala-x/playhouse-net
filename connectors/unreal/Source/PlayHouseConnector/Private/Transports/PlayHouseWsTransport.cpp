#include "PlayHouseWsTransport.h"
#include "PlayHouseProtocol.h"

#include "IWebSocket.h"
#include "WebSocketsModule.h"

bool FPlayHouseWsTransport::Connect(const FString& Host, int32 Port)
{
    if (bConnected.Load())
    {
        return true;
    }

    FString Url = FString::Printf(TEXT("ws://%s:%d/ws"), *Host, Port);
    if (!FModuleManager::Get().IsModuleLoaded(TEXT("WebSockets")))
    {
        FModuleManager::Get().LoadModule(TEXT("WebSockets"));
    }

    Socket = FWebSocketsModule::Get().CreateWebSocket(Url);
    Socket->OnConnected().AddLambda([this]() {
        bConnected.Store(true);
    });
    Socket->OnConnectionError().AddLambda([this](const FString& Error) {
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::ConnectionFailed, Error);
        }
    });
    Socket->OnClosed().AddLambda([this](int32, const FString&, bool) {
        bConnected.Store(false);
        if (OnDisconnected)
        {
            OnDisconnected();
        }
    });
    Socket->OnRawMessage().AddLambda([this](const void* Data, SIZE_T Size, SIZE_T) {
        if (OnBytesReceived)
        {
            OnBytesReceived(reinterpret_cast<const uint8*>(Data), static_cast<int32>(Size));
        }
    });

    Socket->Connect();
    return true;
}

void FPlayHouseWsTransport::Disconnect()
{
    if (Socket.IsValid())
    {
        Socket->Close();
        Socket.Reset();
    }
    bConnected.Store(false);
}

bool FPlayHouseWsTransport::IsConnected() const
{
    return bConnected.Load();
}

bool FPlayHouseWsTransport::SendBytes(const uint8* Data, int32 Size)
{
    if (!Socket.IsValid() || !bConnected.Load())
    {
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::ConnectionClosed, TEXT("WS not connected"));
        }
        return false;
    }

    Socket->Send(Data, Size, true);
    return true;
}

bool FPlayHouseWsTransport::IsConnecting() const
{
    return Socket.IsValid() && !bConnected.Load();
}
