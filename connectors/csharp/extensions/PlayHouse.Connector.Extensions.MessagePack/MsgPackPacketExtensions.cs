#nullable enable

using System;
using System.Buffers;
using MessagePack;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector.Extensions.MessagePack;

/// <summary>
/// Extension methods for parsing MessagePack from IPacket instances.
/// </summary>
public static class MsgPackPacketExtensions
{
    /// <summary>
    /// Parses the packet payload as MessagePack and deserializes it to the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="packet">The packet containing MessagePack payload.</param>
    /// <returns>The deserialized object.</returns>
    /// <exception cref="InvalidOperationException">Thrown if deserialization fails.</exception>
    /// <example>
    /// <code>
    /// public void OnReceive(IPacket packet)
    /// {
    ///     var request = packet.Parse&lt;ChatRequest&gt;();
    ///     // Use request...
    /// }
    /// </code>
    /// </example>
    public static T Parse<T>(this IPacket packet) where T : class
    {
        var span = packet.Payload.DataSpan;
        var sequence = new ReadOnlySequence<byte>(span.ToArray());
        return MessagePackSerializer.Deserialize<T>(sequence)
            ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name}");
    }

    /// <summary>
    /// Tries to parse the packet payload as MessagePack and deserialize it to the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="packet">The packet containing MessagePack payload.</param>
    /// <param name="result">The deserialized object, or default if parsing fails.</param>
    /// <returns>True if deserialization succeeded; otherwise, false.</returns>
    /// <example>
    /// <code>
    /// if (packet.TryParse&lt;ChatRequest&gt;(out var request))
    /// {
    ///     // Use request...
    /// }
    /// </code>
    /// </example>
    public static bool TryParse<T>(this IPacket packet, out T? result) where T : class
    {
        try
        {
            var span = packet.Payload.DataSpan;
            var sequence = new ReadOnlySequence<byte>(span.ToArray());
            result = MessagePackSerializer.Deserialize<T>(sequence);
            return result != null;
        }
        catch
        {
            result = default;
            return false;
        }
    }
}
