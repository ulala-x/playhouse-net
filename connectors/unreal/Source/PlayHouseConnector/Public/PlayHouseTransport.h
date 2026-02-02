#pragma once

#include "CoreMinimal.h"

/**
 * PlayHouse Transport Layer Interface
 *
 * Thread Safety:
 * - Connect/Disconnect/SendBytes: Must be called from the Game Thread
 * - IsConnected: Thread-safe (can be called from any thread)
 * - Callbacks (OnBytesReceived, OnDisconnected, OnTransportError):
 *   Invoked on Game Thread (via async tasks or delegates)
 *
 * Lifecycle:
 * 1. Create transport instance
 * 2. Set callback functions (OnBytesReceived, OnDisconnected, OnTransportError)
 * 3. Call Connect() from Game Thread
 * 4. Use SendBytes() to send data (Game Thread only)
 * 5. Call Disconnect() when done (Game Thread only)
 */
class IPlayHouseTransport
{
public:
    virtual ~IPlayHouseTransport() = default;

    /**
     * Initiates connection to the specified host and port.
     * @param Host The hostname or IP address to connect to
     * @param Port The port number to connect to
     * @return true if connection started successfully, false otherwise
     * @note Must be called from Game Thread. For async transports (WebSocket),
     *       check IsConnected() after OnConnected callback fires.
     */
    virtual bool Connect(const FString& Host, int32 Port) = 0;

    /**
     * Disconnects from the remote host and cleans up resources.
     * @note Must be called from Game Thread. Safe to call multiple times.
     */
    virtual void Disconnect() = 0;

    /**
     * Checks if the transport is currently connected.
     * @return true if connected, false otherwise
     * @note Thread-safe. Can be called from any thread.
     */
    virtual bool IsConnected() const = 0;

    /**
     * Sends raw bytes over the transport.
     * @param Data Pointer to the data buffer to send
     * @param Size Number of bytes to send
     * @return true if data was queued for sending, false if not connected or error occurred
     * @note Must be called from Game Thread. Data is copied internally for async sending.
     */
    virtual bool SendBytes(const uint8* Data, int32 Size) = 0;

    /**
     * Callback invoked when data is received from the remote host.
     * @param Data Pointer to the received data buffer (valid only during callback)
     * @param Size Number of bytes received
     * @note Invoked on Game Thread. Data is only valid during the callback.
     */
    TFunction<void(const uint8* Data, int32 Size)> OnBytesReceived;

    /**
     * Callback invoked when the transport is disconnected.
     * @note Invoked on Game Thread.
     */
    TFunction<void()> OnDisconnected;

    /**
     * Callback invoked when a transport error occurs.
     * @param Code Error code (see PlayHouse::ErrorCode namespace)
     * @param Message Human-readable error message
     * @note Invoked on Game Thread.
     */
    TFunction<void(int32 Code, const FString& Message)> OnTransportError;
};
