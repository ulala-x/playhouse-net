#ifndef PLAYHOUSE_PACKET_CODEC_HPP
#define PLAYHOUSE_PACKET_CODEC_HPP

#include "playhouse/packet.hpp"
#include "playhouse/types.hpp"
#include <cstddef>

namespace playhouse {
namespace internal {

/// PacketCodec handles encoding and decoding of PlayHouse protocol packets
class PacketCodec {
public:
    /// Encode a request packet to bytes
    /// Format: ContentSize(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Payload(...)
    static Bytes EncodeRequest(const Packet& packet);

    /// Decode a response packet from bytes
    /// Format: ContentSize(4) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + ErrorCode(2) + OriginalSize(4) + Payload(...)
    static Packet DecodeResponse(const uint8_t* data, size_t size);
};

} // namespace internal
} // namespace playhouse

#endif // PLAYHOUSE_PACKET_CODEC_HPP
