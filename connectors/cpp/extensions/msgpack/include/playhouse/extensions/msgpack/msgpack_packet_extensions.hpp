#ifndef PLAYHOUSE_EXTENSIONS_MSGPACK_PACKET_EXTENSIONS_HPP
#define PLAYHOUSE_EXTENSIONS_MSGPACK_PACKET_EXTENSIONS_HPP

#include <playhouse/packet.hpp>
#include <msgpack.hpp>
#include <optional>
#include <sstream>
#include <string>

namespace playhouse::extensions::msgpack {

/// Pack an object into MessagePack format and create a Packet
/// @tparam T Type to serialize (must be compatible with MessagePack)
/// @param obj Object to serialize
/// @param msg_id Message ID for the packet
/// @return Packet containing the MessagePack-serialized object
template<typename T>
Packet Create(const T& obj, const std::string& msg_id) {
    std::stringstream ss;
    ::msgpack::pack(ss, obj);

    std::string packed = ss.str();
    Bytes payload(packed.begin(), packed.end());
    return Packet::FromBytes(msg_id, std::move(payload));
}

/// Unpack a Packet's payload as MessagePack and deserialize into type T
/// @tparam T Type to deserialize into (must be compatible with MessagePack)
/// @param packet Packet containing MessagePack payload
/// @return Deserialized object of type T
/// @throws msgpack::type_error if unpacking fails
template<typename T>
T Parse(const Packet& packet) {
    const auto& payload = packet.GetPayload();

    ::msgpack::object_handle oh = ::msgpack::unpack(
        reinterpret_cast<const char*>(payload.data()),
        payload.size()
    );

    return oh.get().as<T>();
}

/// Try to parse a Packet's payload as MessagePack (non-throwing version)
/// @tparam T Type to deserialize into
/// @param packet Packet containing MessagePack payload
/// @return std::optional<T> containing the object if successful, std::nullopt otherwise
template<typename T>
std::optional<T> TryParse(const Packet& packet) {
    try {
        return Parse<T>(packet);
    } catch (...) {
        return std::nullopt;
    }
}

/// Unpack raw MessagePack data from packet payload
/// @param packet Packet containing MessagePack payload
/// @return msgpack::object_handle containing the unpacked data
inline ::msgpack::object_handle UnpackRaw(const Packet& packet) {
    const auto& payload = packet.GetPayload();

    return ::msgpack::unpack(
        reinterpret_cast<const char*>(payload.data()),
        payload.size()
    );
}

/// Create a Packet from a pre-packed MessagePack buffer
/// @param buffer MessagePack-packed buffer
/// @param msg_id Message ID for the packet
/// @return Packet containing the MessagePack data
inline Packet CreateFromBuffer(const std::string& buffer, const std::string& msg_id) {
    Bytes payload(buffer.begin(), buffer.end());
    return Packet::FromBytes(msg_id, std::move(payload));
}

/// Pack multiple objects into a single MessagePack array
/// @tparam Args Types to serialize (must be compatible with MessagePack)
/// @param msg_id Message ID for the packet
/// @param args Objects to serialize
/// @return Packet containing the MessagePack-serialized array
template<typename... Args>
Packet CreateArray(const std::string& msg_id, const Args&... args) {
    std::stringstream ss;
    ::msgpack::packer<std::stringstream> packer(ss);

    packer.pack_array(sizeof...(Args));
    (packer.pack(args), ...);

    std::string packed = ss.str();
    Bytes payload(packed.begin(), packed.end());
    return Packet::FromBytes(msg_id, std::move(payload));
}

} // namespace playhouse::extensions::msgpack

#endif // PLAYHOUSE_EXTENSIONS_MSGPACK_PACKET_EXTENSIONS_HPP
