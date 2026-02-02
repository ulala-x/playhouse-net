#include "PlayHouseTlsTransport.h"
#include "PlayHouseProtocol.h"

#include "Sockets.h"
#include "SocketSubsystem.h"
#include "HAL/Runnable.h"
#include "HAL/RunnableThread.h"
#include "Containers/Queue.h"

#if WITH_SSL
THIRD_PARTY_INCLUDES_START
#include <openssl/ssl.h>
#include <openssl/err.h>
THIRD_PARTY_INCLUDES_END
#endif

namespace
{
#if WITH_SSL
    class FTlsWorker : public FRunnable
    {
    public:
        FTlsWorker(FSocket* InSocket, SSL* InSsl)
            : Socket(InSocket)
            , Ssl(InSsl)
        {}

        virtual uint32 Run() override
        {
            constexpr int32 BufferSize = 8 * 1024;
            TArray<uint8> Buffer;
            Buffer.SetNumUninitialized(BufferSize);

            while (!bStopping)
            {
                // Send queued data
                TArray<uint8> OutData;
                while (SendQueue.Dequeue(OutData))
                {
                    if (!Ssl)
                    {
                        bStopping = true;
                        break;
                    }

                    int Written = SSL_write(Ssl, OutData.GetData(), OutData.Num());
                    if (Written <= 0)
                    {
                        int Err = SSL_get_error(Ssl, Written);
                        if (Err != SSL_ERROR_WANT_READ && Err != SSL_ERROR_WANT_WRITE)
                        {
                            if (OnError)
                            {
                                OnError(PlayHouse::ErrorCode::ConnectionClosed, TEXT("TLS send failed"));
                            }
                            bStopping = true;
                            break;
                        }
                    }
                }

                if (bStopping)
                {
                    break;
                }

                if (Ssl)
                {
                    int Read = SSL_read(Ssl, Buffer.GetData(), BufferSize);
                    if (Read > 0)
                    {
                        if (OnBytes)
                        {
                            OnBytes(Buffer.GetData(), Read);
                        }
                    }
                    else if (Read <= 0)
                    {
                        int Err = SSL_get_error(Ssl, Read);
                        if (Err == SSL_ERROR_WANT_READ || Err == SSL_ERROR_WANT_WRITE)
                        {
                            // Non-blocking, try again later
                        }
                        else if (Err == SSL_ERROR_ZERO_RETURN)
                        {
                            // Clean SSL shutdown by remote
                            UE_LOG(LogTemp, Log, TEXT("[PlayHouse] TLS remote disconnected"));
                            if (OnError)
                            {
                                OnError(PlayHouse::ErrorCode::ConnectionClosed, TEXT("TLS remote disconnected"));
                            }
                            bStopping = true;
                            break;
                        }
                        else
                        {
                            // SSL error
                            UE_LOG(LogTemp, Warning, TEXT("[PlayHouse] TLS read error: %d"), Err);
                            if (OnError)
                            {
                                OnError(PlayHouse::ErrorCode::ConnectionClosed, FString::Printf(TEXT("TLS read error: %d"), Err));
                            }
                            bStopping = true;
                            break;
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
        SSL* Ssl = nullptr;
        TAtomic<bool> bStopping{false};
        TQueue<TArray<uint8>> SendQueue;
        FCriticalSection SendQueueLock;
    };
#endif
}

bool FPlayHouseTlsTransport::Connect(const FString& Host, int32 Port)
{
#if !WITH_SSL
    if (OnTransportError)
    {
        OnTransportError(PlayHouse::ErrorCode::ConnectionFailed, TEXT("TLS not supported (WITH_SSL=0)"));
    }
    return false;
#else
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

    Socket = SocketSubsystem->CreateSocket(NAME_Stream, TEXT("PlayHouseTls"), false);
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

    SSL_library_init();
    SSL_load_error_strings();

    SslCtx = SSL_CTX_new(TLS_client_method());
    if (!SslCtx)
    {
        SocketSubsystem->DestroySocket(Socket);
        Socket = nullptr;
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::ConnectionFailed, TEXT("SSL_CTX creation failed"));
        }
        return false;
    }

    Ssl = SSL_new(SslCtx);
    if (!Ssl)
    {
        SSL_CTX_free(SslCtx);
        SslCtx = nullptr;
        SocketSubsystem->DestroySocket(Socket);
        Socket = nullptr;
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::ConnectionFailed, TEXT("SSL creation failed"));
        }
        return false;
    }

    int32 NativeSocket = Socket->GetNativeSocket();
    SSL_set_fd(Ssl, NativeSocket);

    int Result = SSL_connect(Ssl);
    if (Result <= 0)
    {
        SSL_free(Ssl);
        Ssl = nullptr;
        SSL_CTX_free(SslCtx);
        SslCtx = nullptr;
        SocketSubsystem->DestroySocket(Socket);
        Socket = nullptr;
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::ConnectionFailed, TEXT("TLS handshake failed"));
        }
        return false;
    }

    Worker = MakeUnique<FTlsWorker>(Socket, Ssl);
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

    Thread = FRunnableThread::Create(Worker.Get(), TEXT("PlayHouseTlsWorker"));

    bConnected.Store(true);
    return true;
#endif
}

void FPlayHouseTlsTransport::Disconnect()
{
    bConnected.Store(false);

#if WITH_SSL
    if (Worker)
    {
        Worker->StopWorker();
    }

    if (Thread)
    {
        Thread->WaitForCompletion();
        Thread.Reset();
    }

    if (Ssl)
    {
        SSL_shutdown(Ssl);
        SSL_free(Ssl);
        Ssl = nullptr;
    }

    if (SslCtx)
    {
        SSL_CTX_free(SslCtx);
        SslCtx = nullptr;
    }
#endif

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

bool FPlayHouseTlsTransport::IsConnected() const
{
    return bConnected.Load() && Socket != nullptr;
}

bool FPlayHouseTlsTransport::SendBytes(const uint8* Data, int32 Size)
{
#if !WITH_SSL
    if (OnTransportError)
    {
        OnTransportError(PlayHouse::ErrorCode::ConnectionClosed, TEXT("TLS not supported (WITH_SSL=0)"));
    }
    return false;
#else
    if (!IsConnected() || !Worker)
    {
        if (OnTransportError)
        {
            OnTransportError(PlayHouse::ErrorCode::ConnectionClosed, TEXT("TLS not connected"));
        }
        return false;
    }

    Worker->EnqueueSend(Data, Size);
    return true;
#endif
}
