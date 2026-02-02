#pragma once

#include "CoreMinimal.h"

namespace PlayHouse
{
    /**
     * PlayHouse Error Codes
     *
     * Error Code Ranges:
     * - 0: Success
     * - 1000-1999: Connection errors
     * - 2000-2999: Protocol/request errors
     * - 3000-3999: Authentication/authorization errors
     * - 4000-4999: Application-level errors (reserved for user code)
     */
    namespace ErrorCode
    {
        // Success
        constexpr int32 Success = 0;

        // Connection errors (1000-1999)
        constexpr int32 ConnectionFailed = 1001;     // Failed to establish connection
        constexpr int32 ConnectionTimeout = 1002;    // Connection attempt timed out
        constexpr int32 ConnectionClosed = 1003;     // Connection was closed (gracefully or by error)
        constexpr int32 SocketError = 1004;          // Low-level socket error
        constexpr int32 TlsHandshakeFailed = 1005;   // TLS/SSL handshake failed

        // Protocol/request errors (2000-2999)
        constexpr int32 RequestTimeout = 2001;       // Request timed out waiting for response
        constexpr int32 InvalidResponse = 2002;      // Response packet is malformed or invalid
        constexpr int32 EncodeFailed = 2003;         // Failed to encode packet
        constexpr int32 DecodeFailed = 2004;         // Failed to decode packet

        // Authentication/authorization errors (3000-3999)
        constexpr int32 AuthenticationFailed = 3001; // Authentication failed

        // Application-level errors (4000-4999) - Reserved for user code
    }

    namespace MsgId
    {
        static const TCHAR* Heartbeat = TEXT("@Heart@Beat@");
        static const TCHAR* Debug = TEXT("@Debug@");
        static const TCHAR* Timeout = TEXT("@Timeout@");
    }

    namespace Protocol
    {
        constexpr uint32 MaxMsgIdLength = 256;
        constexpr uint32 MaxBodySize = 1024 * 1024 * 2;  // 2MB max body size
        constexpr uint32 MinHeaderSize = 21;
        constexpr uint32 RequestHeaderSize = 15;
    }
}
