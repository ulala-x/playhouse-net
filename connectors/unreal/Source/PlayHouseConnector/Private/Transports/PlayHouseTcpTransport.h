#pragma once

#include "CoreMinimal.h"
#include "PlayHouseTransport.h"

class FSocket;
class FRunnableThread;

/**
 * TCP Transport Implementation
 *
 * Thread Model:
 * - Game Thread: Connect(), Disconnect(), SendBytes() - all public API calls
 * - Worker Thread: FTcpWorker runs in a separate thread handling socket I/O
 *   - Receives data from socket and invokes OnBytesReceived callback
 *   - Sends queued data from SendQueue to socket
 *   - Detects connection errors and invokes OnTransportError callback
 *
 * Thread Safety:
 * - bConnected: Atomic variable, thread-safe read/write
 * - Socket: Created/destroyed on Game Thread, accessed on Worker Thread (read-only after creation)
 * - SendQueue: Thread-safe queue protected by SendQueueLock
 * - Callbacks: Invoked on Worker Thread but should only access thread-safe data
 *
 * Resource Cleanup Order (in Disconnect()):
 * 1. Set bConnected to false (prevents new sends)
 * 2. Stop worker thread (sets bStopping flag)
 * 3. Wait for thread completion (ensures worker has exited)
 * 4. Close and destroy socket
 * 5. Invoke OnDisconnected callback
 */
class FPlayHouseTcpTransport : public IPlayHouseTransport
{
public:
    virtual bool Connect(const FString& Host, int32 Port) override;
    virtual void Disconnect() override;
    virtual bool IsConnected() const override;
    virtual bool SendBytes(const uint8* Data, int32 Size) override;

private:
    class FTcpWorker;

    TAtomic<bool> bConnected{false};
    FSocket* Socket = nullptr;
    TUniquePtr<FTcpWorker> Worker;
    TUniquePtr<FRunnableThread> Thread;
};
