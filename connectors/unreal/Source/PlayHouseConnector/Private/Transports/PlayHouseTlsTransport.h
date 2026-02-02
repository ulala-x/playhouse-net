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

class FPlayHouseTlsTransport : public IPlayHouseTransport
{
public:
    virtual bool Connect(const FString& Host, int32 Port) override;
    virtual void Disconnect() override;
    virtual bool IsConnected() const override;
    virtual bool SendBytes(const uint8* Data, int32 Size) override;

private:
    TAtomic<bool> bConnected{false};
    FSocket* Socket = nullptr;
    TUniquePtr<class FTlsWorker> Worker;
    TUniquePtr<FRunnableThread> Thread;
#if WITH_SSL
    SSL_CTX* SslCtx = nullptr;
    SSL* Ssl = nullptr;
#endif
};
