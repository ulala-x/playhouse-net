#include "PlayHouseTcpTransport.h"
#include "PlayHouseProtocol.h"

#include "Sockets.h"
#include "SocketSubsystem.h"
#include "HAL/Runnable.h"
#include "HAL/RunnableThread.h"
#include "Containers/Queue.h"

class FPlayHouseTcpTransport::FTcpWorker : public FRunnable
{
public:
    explicit FTcpWorker(FSocket* InSocket)
        : Socket(InSocket)
    {}

        virtual uint32 Run() override
        {
            constexpr int32 BufferSize = 8 * 1024;
            TArray<uint8> Buffer;
            Buffer.SetNumUninitialized(BufferSize);

            while (!bStopping)
            {
                // Send queued packets
                TArray<uint8> OutData;
                while (SendQueue.Dequeue(OutData))
                {
                    int32 BytesSent = 0;
                    if (!Socket || !Socket->Send(OutData.GetData(), OutData.Num(), BytesSent))
                    {
                        if (OnError)
                        {
                            OnError(PlayHouse::ErrorCode::ConnectionClosed, TEXT("TCP send failed"));
                        }
                        bStopping = true;
                        break;
                    }
                }

                if (bStopping)
                {
                    break;
                }

                // Receive data
                if (Socket)
                {
                    uint32 PendingData = 0;
                    if (Socket->HasPendingData(PendingData))
                    {
                        int32 Read = 0;
                        int32 ToRead = FMath::Min(static_cast<int32>(PendingData), BufferSize);
                        const bool bSuccess = Socket->Recv(Buffer.GetData(), ToRead, Read, ESocketReceiveFlags::None);

                        if (!bSuccess)
                        {
                            // Avoid relying on platform error translation (can assert on unknown codes).
                            const ESocketConnectionState State = Socket->GetConnectionState();
                            if (State == SCS_Connected)
                            {
                                // Non-blocking transient failure, try again later.
                                continue;
                            }
                            UE_LOG(LogTemp, Warning, TEXT("[PlayHouse] Socket recv error (state=%d)"), (int32)State);
                            if (OnError)
                            {
                                OnError(PlayHouse::ErrorCode::ConnectionClosed, TEXT("Socket recv error"));
                            }
                            bStopping = true;
                            break;
                        }

                        if (Read == 0)
                        {
                            // Clean disconnection by remote
                            UE_LOG(LogTemp, Log, TEXT("[PlayHouse] Remote disconnected"));
                            if (OnError)
                            {
                                OnError(PlayHouse::ErrorCode::ConnectionClosed, TEXT("Remote disconnected"));
                            }
                            bStopping = true;
                            break;
                        }

                        if (Read > 0)
                        {
                            // Process data
                            if (OnBytes)
                            {
                                OnBytes(Buffer.GetData(), Read);
                            }
                        }
                    }
                }

                FPlatformProcess::Sleep(0.001f);
            }

            return 0;
        }

        void StopWorker()
        {
            bStopping = true;
        }

        void EnqueueSend(TArray<uint8>&& Data)
        {
            SendQueue.Enqueue(MoveTemp(Data));
        }

        void EnqueueSend(const uint8* Data, int32 Size)
        {
            TArray<uint8> Copy;
            Copy.Append(Data, Size);
            FScopeLock Lock(&SendQueueLock);
            SendQueue.Enqueue(MoveTemp(Copy));
        }

    TFunction<void(const uint8* Data, int32 Size)> OnBytes;
    TFunction<void(int32 Code, const FString& Message)> OnError;

private:
    FSocket* Socket = nullptr;
    TAtomic<bool> bStopping{false};
    TQueue<TArray<uint8>> SendQueue;
    FCriticalSection SendQueueLock;
};

bool FPlayHouseTcpTransport::Connect(const FString& Host, int32 Port)
{
    if (bConnected.Load())
    {
        return true;
    }

    ISocketSubsystem* SocketSubsystem = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM);
    if (!SocketSubsystem)
    {
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::ConnectionFailed, TEXT("Socket subsystem unavailable"));
        }
        return false;
    }

    TSharedRef<FInternetAddr> Addr = SocketSubsystem->CreateInternetAddr();
    bool bIsValid = false;
    Addr->SetIp(*Host, bIsValid);
    Addr->SetPort(Port);

    if (!bIsValid)
    {
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::ConnectionFailed, TEXT("Invalid host address"));
        }
        return false;
    }

    Socket = SocketSubsystem->CreateSocket(NAME_Stream, TEXT("PlayHouseTcp"), false);
    if (!Socket)
    {
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::ConnectionFailed, TEXT("Socket creation failed"));
        }
        return false;
    }

    Socket->SetNoDelay(true);

    if (!Socket->Connect(*Addr))
    {
        SocketSubsystem->DestroySocket(Socket);
        Socket = nullptr;
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::ConnectionFailed, TEXT("TCP connect failed"));
        }
        return false;
    }

    Socket->SetNonBlocking(true);

    Worker = MakeUnique<FTcpWorker>(Socket);
    Worker->OnBytes = [this](const uint8* Data, int32 Size) {
        if (OnBytesReceived)
        {
            OnBytesReceived(Data, Size);
        }
    };
    Worker->OnError = [this](int32 Code, const FString& Message) {
        if (OnTransportError)
        {
            OnTransportError(Code, Message);
        }
    };

    Thread.Reset(FRunnableThread::Create(Worker.Get(), TEXT("PlayHouseTcpWorker")));

    bConnected.Store(true);
    if (OnConnected)
    {
        OnConnected();
    }
    return true;
}

void FPlayHouseTcpTransport::Disconnect()
{
    bConnected.Store(false);

    if (Worker)
    {
        Worker->StopWorker();
    }

    if (Thread)
    {
        Thread->WaitForCompletion();
        Thread.Reset();
    }

    if (Socket)
    {
        Socket->Close();
        ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM)->DestroySocket(Socket);
        Socket = nullptr;
    }

    if (OnDisconnected)
    {
        OnDisconnected();
    }
}

bool FPlayHouseTcpTransport::IsConnected() const
{
    return bConnected.Load() && Socket != nullptr;
}

bool FPlayHouseTcpTransport::SendBytes(const uint8* Data, int32 Size)
{
    if (!IsConnected() || !Worker)
    {
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::ConnectionClosed, TEXT("TCP not connected"));
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

    Worker->EnqueueSend(Data, Size);
    return true;
}
