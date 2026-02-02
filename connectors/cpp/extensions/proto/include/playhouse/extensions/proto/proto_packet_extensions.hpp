#ifndef PLAYHOUSE_EXTENSIONS_PROTO_PACKET_EXTENSIONS_HPP
#define PLAYHOUSE_EXTENSIONS_PROTO_PACKET_EXTENSIONS_HPP

#include <playhouse/packet.hpp>
#include <google/protobuf/message.h>
#include <google/protobuf/message_lite.h>
#include <optional>
#include <string>
#include <memory>

namespace playhouse::extensions::proto {

/// Serialize a Protobuf message and create a Packet
/// @tparam T Protobuf message type (must inherit from google::protobuf::Message)
/// @param message Protobuf message to serialize
/// @param msg_id Message ID for the packet
/// @return Packet containing the serialized Protobuf message
template<typename T>
Packet Create(const T& message, const std::string& msg_id) {
    static_assert(std::is_base_of<google::protobuf::MessageLite, T>::value,
                  "T must be a Protobuf message type");

    std::string serialized;
    if (!message.SerializeToString(&serialized)) {
        throw std::runtime_error("Failed to serialize Protobuf message");
    }

    Bytes payload(serialized.begin(), serialized.end());
    return Packet::FromBytes(msg_id, std::move(payload));
}

/// Parse a Packet's payload as a Protobuf message
/// @tparam T Protobuf message type (must inherit from google::protobuf::Message)
/// @param packet Packet containing Protobuf payload
/// @return Deserialized Protobuf message
/// @throws std::runtime_error if parsing fails
template<typename T>
T Parse(const Packet& packet) {
    static_assert(std::is_base_of<google::protobuf::MessageLite, T>::value,
                  "T must be a Protobuf message type");

    T message;
    const auto& payload = packet.GetPayload();

    if (!message.ParseFromArray(payload.data(), static_cast<int>(payload.size()))) {
        throw std::runtime_error("Failed to parse Protobuf message");
    }

    return message;
}

/// Try to parse a Packet's payload as a Protobuf message (non-throwing version)
/// @tparam T Protobuf message type
/// @param packet Packet containing Protobuf payload
/// @return std::optional<T> containing the message if successful, std::nullopt otherwise
template<typename T>
std::optional<T> TryParse(const Packet& packet) {
    try {
        return Parse<T>(packet);
    } catch (...) {
        return std::nullopt;
    }
}

/// Parse a Packet's payload into an existing Protobuf message
/// @tparam T Protobuf message type
/// @param packet Packet containing Protobuf payload
/// @param message Existing message to parse into
/// @return true if parsing succeeded, false otherwise
template<typename T>
bool ParseInto(const Packet& packet, T& message) {
    static_assert(std::is_base_of<google::protobuf::MessageLite, T>::value,
                  "T must be a Protobuf message type");

    const auto& payload = packet.GetPayload();
    return message.ParseFromArray(payload.data(), static_cast<int>(payload.size()));
}

/// Create a Packet from a Protobuf message pointer
/// @param message Protobuf message pointer
/// @param msg_id Message ID for the packet
/// @return Packet containing the serialized Protobuf message
inline Packet CreateFromPointer(const google::protobuf::MessageLite* message, const std::string& msg_id) {
    if (!message) {
        throw std::invalid_argument("Message pointer is null");
    }

    std::string serialized;
    if (!message->SerializeToString(&serialized)) {
        throw std::runtime_error("Failed to serialize Protobuf message");
    }

    Bytes payload(serialized.begin(), serialized.end());
    return Packet::FromBytes(msg_id, std::move(payload));
}

/// Parse a Packet's payload into a generic Protobuf message
/// @param packet Packet containing Protobuf payload
/// @param message Generic Protobuf message to parse into
/// @return true if parsing succeeded, false otherwise
inline bool ParseGeneric(const Packet& packet, google::protobuf::Message& message) {
    const auto& payload = packet.GetPayload();
    return message.ParseFromArray(payload.data(), static_cast<int>(payload.size()));
}

/// Get the byte size of a Protobuf message without serializing
/// @tparam T Protobuf message type
/// @param message Protobuf message
/// @return Size in bytes
template<typename T>
size_t GetByteSize(const T& message) {
    static_assert(std::is_base_of<google::protobuf::MessageLite, T>::value,
                  "T must be a Protobuf message type");
    return message.ByteSizeLong();
}

/// Check if a Packet's payload is valid Protobuf for type T
/// @tparam T Protobuf message type
/// @param packet Packet to validate
/// @return true if valid, false otherwise
template<typename T>
bool IsValid(const Packet& packet) {
    T message;
    return ParseInto(packet, message);
}

/// Create an empty Protobuf message of type T
/// @tparam T Protobuf message type
/// @return Default-constructed message
template<typename T>
T CreateEmpty() {
    static_assert(std::is_base_of<google::protobuf::MessageLite, T>::value,
                  "T must be a Protobuf message type");
    return T();
}

} // namespace playhouse::extensions::proto

#endif // PLAYHOUSE_EXTENSIONS_PROTO_PACKET_EXTENSIONS_HPP
