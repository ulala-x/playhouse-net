#ifndef PLAYHOUSE_TYPES_HPP
#define PLAYHOUSE_TYPES_HPP

#include <cstdint>
#include <vector>
#include <string>
#include <functional>
#include <memory>

namespace playhouse {

// Type aliases
using Bytes = std::vector<uint8_t>;
using ErrorCallback = std::function<void(int code, std::string message)>;

// Error codes
namespace error_code {
    constexpr int SUCCESS = 0;
    constexpr int CONNECTION_FAILED = 1001;
    constexpr int CONNECTION_TIMEOUT = 1002;
    constexpr int CONNECTION_CLOSED = 1003;
    constexpr int REQUEST_TIMEOUT = 2001;
    constexpr int INVALID_RESPONSE = 2002;
    constexpr int PROTOCOL_VIOLATION = 2003;
    constexpr int BUFFER_OVERFLOW = 2004;
    constexpr int AUTHENTICATION_FAILED = 3001;
}

// Special message IDs
namespace msg_id {
    constexpr const char* HEARTBEAT = "@Heart@Beat@";
    constexpr const char* DEBUG = "@Debug@";
    constexpr const char* TIMEOUT = "@Timeout@";
}

// Protocol constants
namespace protocol {
    constexpr uint32_t MAX_MSG_ID_LENGTH = 256;
    constexpr uint32_t MAX_BODY_SIZE = 1024 * 1024 * 2;  // 2MB
    constexpr uint32_t MIN_HEADER_SIZE = 21;  // ContentSize(4) + MsgIdLen(1) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4)
    constexpr uint32_t REQUEST_HEADER_SIZE = 15;  // ContentSize(4) + MsgIdLen(1) + MsgSeq(2) + StageId(8)
}

} // namespace playhouse

#endif // PLAYHOUSE_TYPES_HPP
