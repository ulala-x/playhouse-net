#nullable enable

using System.Buffers;
using System.Text.Json;
using PlayHouse.Abstractions;
using PlayHouse.Core.Shared;

namespace PlayHouse.Extensions.Json;

/// <summary>
/// Extension methods for creating CPacket instances from JSON objects.
/// </summary>
public static class JsonCPacketExtensions
{
    /// <summary>
    /// Creates a CPacket from a JSON-serializable object using zero-copy ArrayPool.
    /// </summary>
    /// <typeparam name="T">The object type to serialize.</typeparam>
    /// <param name="obj">The object to serialize as JSON.</param>
    /// <returns>A new CPacket containing the JSON payload.</returns>
    /// <example>
    /// <code>
    /// var chatMsg = new ChatMessage { Content = "Hello" };
    /// var packet = JsonCPacketExtensions.Of(chatMsg);
    /// sender.Reply(packet);
    /// </code>
    /// </example>
    public static CPacket Of<T>(T obj) where T : class
    {
        var msgId = typeof(T).Name;
        var buffer = ArrayPool<byte>.Shared.Rent(256);

        try
        {
            using var stream = new MemoryStream(buffer);
            JsonSerializer.Serialize(stream, obj);
            var written = (int)stream.Position;

            // ArrayPoolPayload로 소유권 이전 (zero-copy)
            return CPacket.Of(msgId, new ArrayPoolPayload(buffer, written));
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }
}
