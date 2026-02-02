#nullable enable

using System;
using System.Buffers;
using System.IO;
using System.Text.Json;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector.Extensions.Json;

/// <summary>
/// Extension methods for creating Packet instances from JSON objects.
/// </summary>
public static class JsonConnectorExtensions
{
    /// <summary>
    /// Creates a Packet from a JSON-serializable object using zero-copy ArrayPool.
    /// </summary>
    /// <typeparam name="T">The object type to serialize.</typeparam>
    /// <param name="obj">The object to serialize as JSON.</param>
    /// <returns>A new Packet containing the JSON payload.</returns>
    /// <example>
    /// <code>
    /// var chatMsg = new ChatMessage { Content = "Hello" };
    /// var packet = JsonConnectorExtensions.Of(chatMsg);
    /// await connector.SendAsync(packet);
    /// </code>
    /// </example>
    public static Packet Of<T>(T obj) where T : class
    {
        var msgId = typeof(T).Name;
        var buffer = ArrayPool<byte>.Shared.Rent(256);

        try
        {
            using var stream = new MemoryStream(buffer);
            JsonSerializer.Serialize(stream, obj);
            var written = (int)stream.Position;

            // ArrayPoolPayload로 소유권 이전 (zero-copy)
            return new Packet(msgId, new ArrayPoolPayload(buffer, written));
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }
}
