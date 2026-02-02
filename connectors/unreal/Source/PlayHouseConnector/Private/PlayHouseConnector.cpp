#include "PlayHouseConnector.h"
#include "PlayHouseProtocol.h"
#include "PlayHouseTransport.h"
#include "Internal/PlayHousePacketCodec.h"
#include "Internal/PlayHouseRingBuffer.h"
#include "Internal/PlayHouseRequestMap.h"
#include "Transports/PlayHouseTcpTransport.h"
#include "Transports/PlayHouseTlsTransport.h"
#include "Transports/PlayHouseWsTransport.h"
#include "Transports/PlayHouseWssTransport.h"

#include "Async/Async.h"
#include "Containers/Ticker.h"

namespace
{
    class FPlayHouseConnectorState
    {
    public:
        FPlayHouseRingBuffer ReceiveBuffer;
        FPlayHouseRequestMap RequestMap;
        FCriticalSection CallbackMutex;
        uint16 MsgSeqCounter = 1;
        int64 StageId = 0;

        explicit FPlayHouseConnectorState(int32 BufferSize)
            : ReceiveBuffer(BufferSize)
        {}

        uint16 NextMsgSeq()
        {
            uint16 Seq = MsgSeqCounter++;
            if (Seq == 0)
            {
                Seq = MsgSeqCounter++;
            }
            return Seq;
        }
    };

    void DispatchGameThread(TFunction<void()>&& Func)
    {
        AsyncTask(ENamedThreads::GameThread, MoveTemp(Func));
    }
    TUniquePtr<IPlayHouseTransport> CreateTransport(EPlayHouseTransport TransportType)
    {
        switch (TransportType)
        {
        case EPlayHouseTransport::Tcp:
            return MakeUnique<FPlayHouseTcpTransport>();
        case EPlayHouseTransport::Tls:
            return MakeUnique<FPlayHouseTlsTransport>();
        case EPlayHouseTransport::Ws:
            return MakeUnique<FPlayHouseWsTransport>();
        case EPlayHouseTransport::Wss:
            return MakeUnique<FPlayHouseWssTransport>();
        default:
            return nullptr;
        }
    }
}

class FPlayHouseConnector::FPlayHouseConnectorImpl
{
public:
    FPlayHouseConfig Config;
    TUniquePtr<IPlayHouseTransport> Transport;
    TUniquePtr<FPlayHouseConnectorState> State;
    FTSTicker::FDelegateHandle TickerHandle;
};

FPlayHouseConnector::~FPlayHouseConnector()
{
    if (Impl_)
    {
        if (Impl_->TickerHandle.IsValid())
        {
            FTSTicker::GetCoreTicker().RemoveTicker(Impl_->TickerHandle);
        }
        if (Impl_->Transport)
        {
            Impl_->Transport->Disconnect();
        }
    }
}

void FPlayHouseConnector::Init(const FPlayHouseConfig& Config)
{
    if (!Impl_)
    {
        Impl_ = MakeUnique<FPlayHouseConnectorImpl>();
    }

    Impl_->Config = Config;
    Impl_->State = MakeUnique<FPlayHouseConnectorState>(Config.ReceiveBufferSize);
    LastTimeoutCheckSeconds_ = FPlatformTime::Seconds();

    Impl_->Transport = CreateTransport(Config.Transport);
    if (Impl_->Transport)
    {
        Impl_->Transport->OnBytesReceived = [this](const uint8* Data, int32 Size) {
            HandleBytes(Data, Size);
        };
        Impl_->Transport->OnDisconnected = [this]() {
            TArray<TFunction<void(FPlayHousePacket&&)>> PendingCallbacks;
            Impl_->State->RequestMap.Clear(PendingCallbacks);
            for (auto& Callback : PendingCallbacks)
            {
                FPlayHousePacket Packet;
                Packet.MsgId = PlayHouse::MsgId::Timeout;
                Packet.ErrorCode = PlayHouse::ErrorCode::ConnectionClosed;
                DispatchGameThread([Callback = MoveTemp(Callback), Packet = MoveTemp(Packet)]() mutable {
                    Callback(MoveTemp(Packet));
                });
            }
            if (OnDisconnect)
            {
                DispatchGameThread([Callback = OnDisconnect]() { Callback(); });
            }
        };
        Impl_->Transport->OnTransportError = [this](int32 Code, const FString& Message) {
            if (OnError)
            {
                DispatchGameThread([Callback = OnError, Code, Message]() { Callback(Code, Message); });
            }
        };
    }

    if (!Impl_->TickerHandle.IsValid())
    {
        Impl_->TickerHandle = FTSTicker::GetCoreTicker().AddTicker(
            FTickerDelegate::CreateRaw(this, &FPlayHouseConnector::TickInternal));
    }
}

void FPlayHouseConnector::Connect(const FString& Host, int32 Port)
{
    if (!Impl_ || !Impl_->Transport)
    {
        if (OnError)
        {
            OnError(PlayHouse::ErrorCode::ConnectionFailed, TEXT("Transport not initialized"));
        }
        return;
    }

    if (!Impl_->Transport->Connect(Host, Port))
    {
        if (OnError)
        {
            OnError(PlayHouse::ErrorCode::ConnectionFailed, TEXT("Connect failed"));
        }
        return;
    }

    if (OnConnect)
    {
        DispatchGameThread([Callback = OnConnect]() { Callback(); });
    }
}

void FPlayHouseConnector::Disconnect()
{
    if (!Impl_ || !Impl_->Transport)
    {
        return;
    }

    Impl_->Transport->Disconnect();
}

bool FPlayHouseConnector::IsConnected() const
{
    return Impl_ && Impl_->Transport && Impl_->Transport->IsConnected();
}

void FPlayHouseConnector::Send(FPlayHousePacket&& Packet)
{
    if (!Impl_ || !Impl_->Transport)
    {
        return;
    }

    if (!IsConnected())
    {
        if (OnError)
        {
            DispatchGameThread([Callback = OnError]() { Callback(PlayHouse::ErrorCode::ConnectionClosed, TEXT("Not connected")); });
        }
        return;
    }

    Packet.MsgSeq = 0;
    Packet.StageId = Impl_->State->StageId;

    TArray<uint8> Encoded;
    if (!FPlayHousePacketCodec::EncodeRequest(Packet, Encoded))
    {
        if (OnError)
        {
            DispatchGameThread([Callback = OnError]() { Callback(PlayHouse::ErrorCode::InvalidResponse, TEXT("Encode failed")); });
        }
        return;
    }

    if (!Impl_->Transport->SendBytes(Encoded.GetData(), Encoded.Num()))
    {
        if (OnError)
        {
            DispatchGameThread([Callback = OnError]() { Callback(PlayHouse::ErrorCode::ConnectionClosed, TEXT("Send failed")); });
        }
    }
}

void FPlayHouseConnector::Request(FPlayHousePacket&& Packet, TFunction<void(FPlayHousePacket&&)> OnResponse)
{
    if (!Impl_ || !Impl_->Transport)
    {
        return;
    }

    if (!IsConnected())
    {
        if (OnError)
        {
            DispatchGameThread([Callback = OnError]() { Callback(PlayHouse::ErrorCode::ConnectionClosed, TEXT("Not connected")); });
        }
        return;
    }

    Packet.MsgSeq = Impl_->State->NextMsgSeq();
    Packet.StageId = Impl_->State->StageId;

    double NowSeconds = FPlatformTime::Seconds();
    double Deadline = NowSeconds + (Impl_->Config.RequestTimeoutMs / 1000.0);

    Impl_->State->RequestMap.Add(Packet.MsgSeq, Deadline, MoveTemp(OnResponse));

    TArray<uint8> Encoded;
    if (!FPlayHousePacketCodec::EncodeRequest(Packet, Encoded))
    {
        if (OnError)
        {
            DispatchGameThread([Callback = OnError]() { Callback(PlayHouse::ErrorCode::InvalidResponse, TEXT("Encode failed")); });
        }
        return;
    }

    if (!Impl_->Transport->SendBytes(Encoded.GetData(), Encoded.Num()))
    {
        if (OnError)
        {
            DispatchGameThread([Callback = OnError]() { Callback(PlayHouse::ErrorCode::ConnectionClosed, TEXT("Send failed")); });
        }
    }
}

void FPlayHouseConnector::Authenticate(FPlayHousePacket&& Packet, TFunction<void(bool)> OnResult)
{
    Request(MoveTemp(Packet), [OnResult = MoveTemp(OnResult)](FPlayHousePacket&& Response) {
        OnResult(Response.ErrorCode == PlayHouse::ErrorCode::Success);
    });
}

bool FPlayHouseConnector::TickInternal(float DeltaSeconds)
{
    if (!Impl_ || !Impl_->State)
    {
        return true;
    }

    double NowSeconds = FPlatformTime::Seconds();
    if (NowSeconds - LastTimeoutCheckSeconds_ < 0.1)
    {
        return true;
    }
    LastTimeoutCheckSeconds_ = NowSeconds;

    TArray<TPair<uint16, TFunction<void(FPlayHousePacket&&)>>> Expired;
    Impl_->State->RequestMap.CollectExpired(NowSeconds, Expired);

    for (auto& Pair : Expired)
    {
        FPlayHousePacket TimeoutPacket;
        TimeoutPacket.MsgId = PlayHouse::MsgId::Timeout;
        TimeoutPacket.MsgSeq = Pair.Key;
        TimeoutPacket.ErrorCode = PlayHouse::ErrorCode::RequestTimeout;
        DispatchGameThread([Callback = MoveTemp(Pair.Value), Packet = MoveTemp(TimeoutPacket)]() mutable {
            Callback(MoveTemp(Packet));
        });
    }

    return true;
}

void FPlayHouseConnector::HandleBytes(const uint8* Data, int32 Size)
{
    if (!Impl_ || !Impl_->State || Size <= 0)
    {
        return;
    }

    Impl_->State->ReceiveBuffer.Write(Data, Size);

    while (true)
    {
        if (Impl_->State->ReceiveBuffer.GetCount() < 4)
        {
            break;
        }

        uint8 SizeBytes[4] = {0, 0, 0, 0};
        Impl_->State->ReceiveBuffer.Peek(SizeBytes, 4, 0);
        uint32 ContentSize =
            static_cast<uint32>(SizeBytes[0]) |
            (static_cast<uint32>(SizeBytes[1]) << 8) |
            (static_cast<uint32>(SizeBytes[2]) << 16) |
            (static_cast<uint32>(SizeBytes[3]) << 24);

        if (ContentSize > PlayHouse::Protocol::MaxBodySize)
        {
            Impl_->State->ReceiveBuffer.Clear();
            if (OnError)
            {
                DispatchGameThread([Callback = OnError]() { Callback(PlayHouse::ErrorCode::InvalidResponse, TEXT("Invalid content size")); });
            }
            break;
        }

        uint32 TotalSize = 4 + ContentSize;
        if (Impl_->State->ReceiveBuffer.GetCount() < static_cast<int32>(TotalSize))
        {
            break;
        }

        TArray<uint8> PacketBytes;
        PacketBytes.SetNumUninitialized(TotalSize);
        Impl_->State->ReceiveBuffer.Read(PacketBytes.GetData(), TotalSize);

        FPlayHousePacket Packet;
        if (!FPlayHousePacketCodec::DecodeResponse(PacketBytes.GetData(), PacketBytes.Num(), Packet))
        {
            if (OnError)
            {
                DispatchGameThread([Callback = OnError]() { Callback(PlayHouse::ErrorCode::InvalidResponse, TEXT("Decode failed")); });
            }
            continue;
        }

        if (Packet.MsgSeq > 0)
        {
            TFunction<void(FPlayHousePacket&&)> Callback;
            if (Impl_->State->RequestMap.Resolve(Packet.MsgSeq, Callback))
            {
                DispatchGameThread([Callback = MoveTemp(Callback), Packet = MoveTemp(Packet)]() mutable {
                    Callback(MoveTemp(Packet));
                });
                continue;
            }
        }

        if (OnReceive)
        {
            DispatchGameThread([Callback = OnReceive, Packet = MoveTemp(Packet)]() mutable {
                Callback(Packet);
            });
        }
    }
}
