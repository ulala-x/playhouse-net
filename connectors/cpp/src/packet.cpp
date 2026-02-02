#include "playhouse/packet.hpp"
#include <utility>

namespace playhouse {

// Pimpl implementation
class Packet::Impl {
public:
    std::string msg_id;
    Bytes payload;
    uint16_t msg_seq = 0;
    int64_t stage_id = 0;
    int16_t error_code = 0;
    uint32_t original_size = 0;

    Impl(std::string id, Bytes data)
        : msg_id(std::move(id))
        , payload(std::move(data))
    {}
};

Packet::Packet(std::string msg_id, Bytes payload)
    : impl_(std::make_unique<Impl>(std::move(msg_id), std::move(payload)))
{}

Packet::Packet(Packet&& other) noexcept = default;

Packet& Packet::operator=(Packet&& other) noexcept = default;

Packet::~Packet() = default;

const std::string& Packet::GetMsgId() const {
    return impl_->msg_id;
}

uint16_t Packet::GetMsgSeq() const {
    return impl_->msg_seq;
}

int64_t Packet::GetStageId() const {
    return impl_->stage_id;
}

int16_t Packet::GetErrorCode() const {
    return impl_->error_code;
}

const Bytes& Packet::GetPayload() const {
    return impl_->payload;
}

uint32_t Packet::GetOriginalSize() const {
    return impl_->original_size;
}

void Packet::SetMsgSeq(uint16_t msg_seq) {
    impl_->msg_seq = msg_seq;
}

void Packet::SetStageId(int64_t stage_id) {
    impl_->stage_id = stage_id;
}

void Packet::SetErrorCode(int16_t error_code) {
    impl_->error_code = error_code;
}

void Packet::SetOriginalSize(uint32_t original_size) {
    impl_->original_size = original_size;
}

Packet Packet::Empty(const std::string& msg_id) {
    return Packet(msg_id, Bytes{});
}

Packet Packet::FromBytes(const std::string& msg_id, Bytes bytes) {
    return Packet(msg_id, std::move(bytes));
}

} // namespace playhouse
