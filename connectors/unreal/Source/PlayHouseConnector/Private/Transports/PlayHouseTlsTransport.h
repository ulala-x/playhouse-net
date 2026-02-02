#pragma once

#include "CoreMinimal.h"
#include "PlayHouseTransport.h"

class FSocket;
class FRunnableThread;

#if WITH_SSL
struct ssl_st;
typedef struct ssl_st SSL;
struct ssl_ctx_st;
typedef struct ssl_ctx_st SSL_CTX;
#endif

/**
 * TLS (SSL) Transport Implementation
 *
 * Thread Model:
 * - Game Thread: Connect(), Disconnect(), SendBytes() - all public API calls
 * - Worker Thread: FTlsWorker runs in a separate thread handling SSL I/O
 *   - Performs SSL_read() to receive encrypted data
 *   - Performs SSL_write() to send encrypted data
 *   - Detects SSL errors and invokes OnTransportError callback
 *
 * Thread Safety:
 * - bConnected: Atomic variable, thread-safe read/write
 * - Socket/Ssl/SslCtx: Created/destroyed on Game Thread, accessed on Worker Thread
 * - SendQueue: Thread-safe queue protected by SendQueueLock
 * - OpenSSL: SSL object is NOT thread-safe, only accessed from Worker Thread
 *
 * Resource Cleanup Order (in Disconnect()):
 * 1. Set bConnected to false (prevents new sends)
 * 2. Stop worker thread (sets bStopping flag)
 * 3. Wait for thread completion (ensures worker has exited and SSL is idle)
 * 4. Shutdown and free SSL context (SSL_shutdown, SSL_free, SSL_CTX_free)
 * 5. Close and destroy socket
 * 6. Invoke OnDisconnected callback
 *
 * Note: Requires WITH_SSL=1 build flag. Returns error if SSL is not supported.
 */
class FPlayHouseTlsTransport : public IPlayHouseTransport
{
public:
    virtual bool Connect(const FString& Host, int32 Port) override;
    virtual void Disconnect() override;
    virtual bool IsConnected() const override;
    virtual bool SendBytes(const uint8* Data, int32 Size) override;

private:
    class FTlsWorker;

    TAtomic<bool> bConnected{false};
    FSocket* Socket = nullptr;
    TUniquePtr<FTlsWorker> Worker;
    TUniquePtr<FRunnableThread> Thread;
#if WITH_SSL
    SSL_CTX* SslCtx = nullptr;
    SSL* Ssl = nullptr;
#endif
};
