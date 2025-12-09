#nullable enable

using System;
using System.Text.Json;

namespace PlayHouse.Abstractions;

/// <summary>
/// Extension methods for IPacket to provide convenient parsing capabilities.
/// </summary>
public static class PacketExtensions
{
    /// <summary>
    /// Parses the packet payload as a JSON-serialized object of type T.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize to.</typeparam>
    /// <param name="packet">The packet to parse.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>The deserialized object of type T.</returns>
    /// <exception cref="ArgumentNullException">Thrown when packet is null.</exception>
    /// <exception cref="JsonException">Thrown when the payload cannot be deserialized to type T.</exception>
    /// <remarks>
    /// This method deserializes the packet's payload data using System.Text.Json.
    /// For custom serialization, use the Payload.Data property directly.
    ///
    /// Example usage:
    /// <code>
    /// var chatMsg = packet.Parse&lt;ChatMsg&gt;();
    /// Console.WriteLine(chatMsg.Message);
    /// </code>
    /// </remarks>
    public static T Parse<T>(this IPacket packet, JsonSerializerOptions? options = null)
        where T : class
    {
        if (packet == null)
        {
            throw new ArgumentNullException(nameof(packet));
        }

        if (packet.Payload == null || packet.Payload.Length == 0)
        {
            throw new InvalidOperationException(
                $"Cannot parse packet {packet.MsgId}: payload is null or empty");
        }

        try
        {
            var data = packet.Payload.Data;
            var result = JsonSerializer.Deserialize<T>(data.Span, options);

            if (result == null)
            {
                throw new JsonException(
                    $"Deserialization returned null for packet {packet.MsgId}");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new JsonException(
                $"Failed to deserialize packet {packet.MsgId} to type {typeof(T).Name}", ex);
        }
    }

    /// <summary>
    /// Tries to parse the packet payload as a JSON-serialized object of type T.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize to.</typeparam>
    /// <param name="packet">The packet to parse.</param>
    /// <param name="result">The deserialized object if successful; otherwise, null.</param>
    /// <param name="options">Optional JSON serializer options.</param>
    /// <returns>True if parsing succeeded; otherwise, false.</returns>
    /// <remarks>
    /// This is a non-throwing version of Parse&lt;T&gt; that returns false on failure.
    ///
    /// Example usage:
    /// <code>
    /// if (packet.TryParse&lt;ChatMsg&gt;(out var chatMsg))
    /// {
    ///     Console.WriteLine(chatMsg.Message);
    /// }
    /// </code>
    /// </remarks>
    public static bool TryParse<T>(this IPacket packet, out T? result, JsonSerializerOptions? options = null)
        where T : class
    {
        result = null;

        if (packet == null || packet.Payload == null || packet.Payload.Length == 0)
        {
            return false;
        }

        try
        {
            var data = packet.Payload.Data;
            result = JsonSerializer.Deserialize<T>(data.Span, options);
            return result != null;
        }
        catch
        {
            return false;
        }
    }
}
