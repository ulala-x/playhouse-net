#ifndef PLAYHOUSE_EXTENSIONS_JSON_PACKET_EXTENSIONS_HPP
#define PLAYHOUSE_EXTENSIONS_JSON_PACKET_EXTENSIONS_HPP

#include <playhouse/packet.hpp>
#include <nlohmann/json.hpp>
#include <optional>
#include <string>

namespace playhouse::extensions::json {

/// Parse a Packet's payload as JSON and deserialize into type T
/// @tparam T Type to deserialize into (must be compatible with nlohmann::json)
/// @param packet Packet containing JSON payload
/// @return Deserialized object of type T
/// @throws nlohmann::json::exception if parsing fails
template<typename T>
T Parse(const Packet& packet) {
    const auto& payload = packet.GetPayload();
    auto json_str = std::string(payload.begin(), payload.end());
    return nlohmann::json::parse(json_str).get<T>();
}

/// Try to parse a Packet's payload as JSON (non-throwing version)
/// @tparam T Type to deserialize into
/// @param packet Packet containing JSON payload
/// @return std::optional<T> containing the object if successful, std::nullopt otherwise
template<typename T>
std::optional<T> TryParse(const Packet& packet) {
    try {
        return Parse<T>(packet);
    } catch (...) {
        return std::nullopt;
    }
}

/// Create a Packet from a JSON-serializable object
/// @tparam T Type to serialize (must be compatible with nlohmann::json)
/// @param obj Object to serialize
/// @param msg_id Message ID for the packet
/// @return Packet containing the JSON-serialized object
template<typename T>
Packet Create(const T& obj, const std::string& msg_id) {
    nlohmann::json j = obj;
    std::string json_str = j.dump();
    Bytes payload(json_str.begin(), json_str.end());
    return Packet::FromBytes(msg_id, std::move(payload));
}

/// Create a Packet from a JSON-serializable object with custom serialization options
/// @tparam T Type to serialize
/// @param obj Object to serialize
/// @param msg_id Message ID for the packet
/// @param indent Number of spaces for pretty printing (-1 for compact)
/// @return Packet containing the JSON-serialized object
template<typename T>
Packet CreateWithOptions(const T& obj, const std::string& msg_id, int indent = -1) {
    nlohmann::json j = obj;
    std::string json_str = j.dump(indent);
    Bytes payload(json_str.begin(), json_str.end());
    return Packet::FromBytes(msg_id, std::move(payload));
}

/// Parse raw JSON string from packet payload
/// @param packet Packet containing JSON payload
/// @return nlohmann::json object
inline nlohmann::json ParseRaw(const Packet& packet) {
    const auto& payload = packet.GetPayload();
    auto json_str = std::string(payload.begin(), payload.end());
    return nlohmann::json::parse(json_str);
}

/// Create a Packet from a pre-constructed JSON object
/// @param json_obj nlohmann::json object
/// @param msg_id Message ID for the packet
/// @return Packet containing the JSON-serialized object
inline Packet CreateFromJson(const nlohmann::json& json_obj, const std::string& msg_id) {
    std::string json_str = json_obj.dump();
    Bytes payload(json_str.begin(), json_str.end());
    return Packet::FromBytes(msg_id, std::move(payload));
}

} // namespace playhouse::extensions::json

#endif // PLAYHOUSE_EXTENSIONS_JSON_PACKET_EXTENSIONS_HPP
