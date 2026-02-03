#ifndef PLAYHOUSE_TEST_PROTO_HELPERS_HPP
#define PLAYHOUSE_TEST_PROTO_HELPERS_HPP

#include <cstdint>
#include <string>
#include <vector>

#include <playhouse/types.hpp>

namespace playhouse::test::proto {

inline void WriteVarint(uint64_t value, Bytes& out) {
    while (value > 0x7F) {
        out.push_back(static_cast<uint8_t>((value & 0x7F) | 0x80));
        value >>= 7;
    }
    out.push_back(static_cast<uint8_t>(value & 0x7F));
}

inline void WriteKey(uint32_t field_number, uint8_t wire_type, Bytes& out) {
    WriteVarint((static_cast<uint64_t>(field_number) << 3) | wire_type, out);
}

inline void WriteStringField(uint32_t field_number, const std::string& value, Bytes& out) {
    WriteKey(field_number, 2, out);
    WriteVarint(value.size(), out);
    out.insert(out.end(), value.begin(), value.end());
}

inline void WriteBytesField(uint32_t field_number, const Bytes& value, Bytes& out) {
    WriteKey(field_number, 2, out);
    WriteVarint(value.size(), out);
    out.insert(out.end(), value.begin(), value.end());
}

inline void WriteInt32Field(uint32_t field_number, int32_t value, Bytes& out) {
    WriteKey(field_number, 0, out);
    WriteVarint(static_cast<uint32_t>(value), out);
}

inline void WriteInt64Field(uint32_t field_number, int64_t value, Bytes& out) {
    WriteKey(field_number, 0, out);
    WriteVarint(static_cast<uint64_t>(value), out);
}

inline Bytes EncodeAuthenticateRequest(const std::string& user_id, const std::string& token) {
    Bytes out;
    if (!user_id.empty()) {
        WriteStringField(1, user_id, out);
    }
    if (!token.empty()) {
        WriteStringField(2, token, out);
    }
    return out;
}

inline Bytes EncodeEchoRequest(const std::string& content, int32_t sequence) {
    Bytes out;
    WriteStringField(1, content, out);
    WriteInt32Field(2, sequence, out);
    return out;
}

inline Bytes EncodeBroadcastRequest(const std::string& content) {
    Bytes out;
    WriteStringField(1, content, out);
    return out;
}

inline Bytes EncodeNoResponseRequest(int32_t delay_ms) {
    Bytes out;
    WriteInt32Field(1, delay_ms, out);
    return out;
}

inline Bytes EncodeFailRequest(int32_t error_code, const std::string& error_message) {
    Bytes out;
    WriteInt32Field(1, error_code, out);
    WriteStringField(2, error_message, out);
    return out;
}

inline Bytes EncodeLargePayloadRequest(int32_t size_bytes) {
    Bytes out;
    WriteInt32Field(1, size_bytes, out);
    return out;
}

inline bool ReadVarint(const Bytes& data, size_t& offset, uint64_t& value) {
    value = 0;
    int shift = 0;
    while (offset < data.size() && shift < 64) {
        uint8_t byte = data[offset++];
        value |= static_cast<uint64_t>(byte & 0x7F) << shift;
        if ((byte & 0x80) == 0) {
            return true;
        }
        shift += 7;
    }
    return false;
}

inline bool ReadLengthDelimited(const Bytes& data, size_t& offset, Bytes& out) {
    uint64_t length = 0;
    if (!ReadVarint(data, offset, length)) {
        return false;
    }
    if (offset + length > data.size()) {
        return false;
    }
    out.assign(data.begin() + static_cast<std::ptrdiff_t>(offset),
               data.begin() + static_cast<std::ptrdiff_t>(offset + length));
    offset += static_cast<size_t>(length);
    return true;
}

inline bool SkipField(uint8_t wire_type, const Bytes& data, size_t& offset) {
    switch (wire_type) {
        case 0: {
            uint64_t dummy = 0;
            return ReadVarint(data, offset, dummy);
        }
        case 1: {
            if (offset + 8 > data.size()) return false;
            offset += 8;
            return true;
        }
        case 2: {
            Bytes dummy;
            return ReadLengthDelimited(data, offset, dummy);
        }
        case 5: {
            if (offset + 4 > data.size()) return false;
            offset += 4;
            return true;
        }
        default:
            return false;
    }
}

inline bool DecodeEchoReply(const Bytes& data, std::string& content, int32_t& sequence) {
    size_t offset = 0;
    content.clear();
    sequence = 0;
    while (offset < data.size()) {
        uint64_t key = 0;
        if (!ReadVarint(data, offset, key)) return false;
        uint32_t field = static_cast<uint32_t>(key >> 3);
        uint8_t wire = static_cast<uint8_t>(key & 0x7);

        if (field == 1 && wire == 2) {
            Bytes value;
            if (!ReadLengthDelimited(data, offset, value)) return false;
            content.assign(value.begin(), value.end());
        } else if (field == 2 && wire == 0) {
            uint64_t value = 0;
            if (!ReadVarint(data, offset, value)) return false;
            sequence = static_cast<int32_t>(value);
        } else {
            if (!SkipField(wire, data, offset)) return false;
        }
    }
    return true;
}

inline bool DecodeFailReply(const Bytes& data, int32_t& error_code, std::string& message) {
    size_t offset = 0;
    error_code = 0;
    message.clear();
    while (offset < data.size()) {
        uint64_t key = 0;
        if (!ReadVarint(data, offset, key)) return false;
        uint32_t field = static_cast<uint32_t>(key >> 3);
        uint8_t wire = static_cast<uint8_t>(key & 0x7);

        if (field == 1 && wire == 0) {
            uint64_t value = 0;
            if (!ReadVarint(data, offset, value)) return false;
            error_code = static_cast<int32_t>(value);
        } else if (field == 2 && wire == 2) {
            Bytes value;
            if (!ReadLengthDelimited(data, offset, value)) return false;
            message.assign(value.begin(), value.end());
        } else {
            if (!SkipField(wire, data, offset)) return false;
        }
    }
    return true;
}

inline bool DecodeBroadcastNotify(const Bytes& data, std::string& event_type, std::string& payload) {
    size_t offset = 0;
    event_type.clear();
    payload.clear();
    while (offset < data.size()) {
        uint64_t key = 0;
        if (!ReadVarint(data, offset, key)) return false;
        uint32_t field = static_cast<uint32_t>(key >> 3);
        uint8_t wire = static_cast<uint8_t>(key & 0x7);

        if (field == 1 && wire == 2) {
            Bytes value;
            if (!ReadLengthDelimited(data, offset, value)) return false;
            event_type.assign(value.begin(), value.end());
        } else if (field == 2 && wire == 2) {
            Bytes value;
            if (!ReadLengthDelimited(data, offset, value)) return false;
            payload.assign(value.begin(), value.end());
        } else {
            if (!SkipField(wire, data, offset)) return false;
        }
    }
    return true;
}

inline bool DecodeBenchmarkReplyPayload(const Bytes& data, Bytes& payload) {
    size_t offset = 0;
    payload.clear();
    while (offset < data.size()) {
        uint64_t key = 0;
        if (!ReadVarint(data, offset, key)) return false;
        uint32_t field = static_cast<uint32_t>(key >> 3);
        uint8_t wire = static_cast<uint8_t>(key & 0x7);

        if (field == 3 && wire == 2) {
            Bytes value;
            if (!ReadLengthDelimited(data, offset, value)) return false;
            payload.swap(value);
        } else {
            if (!SkipField(wire, data, offset)) return false;
        }
    }
    return true;
}

} // namespace playhouse::test::proto

#endif // PLAYHOUSE_TEST_PROTO_HELPERS_HPP
