#include "PlayHouseTcpTransport.h"
#include "PlayHouseProtocol.h"

#include "Sockets.h"
#include "SocketSubsystem.h"
#include "HAL/Runnable.h"
#include "HAL/RunnableThread.h"
#include "Containers/Queue.h"

namespace
{
    class FTcpWorker : public FRunnable
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
                        if (Socket->Recv(Buffer.GetData(), ToRead, Read) && Read > 0)
                        {
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

        TFunction<void(const uint8* Data, int32 Size)> OnBytes;
        TFunction<void(int32 Code, const FString& Message)> OnError;

    private:
        FSocket* Socket = nullptr;
        TAtomic<bool> bStopping{false};
        TQueue<TArray<uint8>> SendQueue;
    };
}

bool FPlayHouseTcpTransport::Connect(const FString& Host, int32 Port)
{
    if (bConnected)
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

    Socket->SetNonBlocking(true);
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

    Thread = FRunnableThread::Create(Worker.Get(), TEXT("PlayHouseTcpWorker"));

    bConnected = true;
    return true;
}

void FPlayHouseTcpTransport::Disconnect()
{
    bConnected = false;

    if (Worker)
    {
        Worker->StopWorker();
    }

    if (Thread)
    {
        Thread->Kill(true);
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
    return bConnected && Socket != nullptr;
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

    TArray<uint8> Copy;
    Copy.Append(Data, Size);
    Worker->EnqueueSend(MoveTemp(Copy));
    return true;
}
