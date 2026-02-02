#pragma once

#include "CoreMinimal.h"

namespace PlayHouse
{
    namespace ErrorCode
    {
        constexpr int32 Success = 0;
        constexpr int32 ConnectionFailed = 1001;
        constexpr int32 ConnectionTimeout = 1002;
        constexpr int32 ConnectionClosed = 1003;
        constexpr int32 RequestTimeout = 2001;
        constexpr int32 InvalidResponse = 2002;
        constexpr int32 AuthenticationFailed = 3001;
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
        constexpr uint32 MaxBodySize = 1024 * 1024 * 2;
        constexpr uint32 MinHeaderSize = 21;
        constexpr uint32 RequestHeaderSize = 15;
    }
}
