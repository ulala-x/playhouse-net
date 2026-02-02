#nullable enable

using System.Text.Json;
using PlayHouse.Abstractions;

namespace PlayHouse.Extensions.Json;

/// <summary>
/// Extension methods for parsing JSON from IPacket instances.
/// </summary>
public static class JsonPacketExtensions
{
    /// <summary>
    /// Parses the packet payload as JSON and deserializes it to the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="packet">The packet containing JSON payload.</param>
    /// <returns>The deserialized object.</returns>
    /// <exception cref="InvalidOperationException">Thrown if deserialization fails.</exception>
    /// <example>
    /// <code>
    /// public Task OnDispatch(IPacket packet, IStageSender sender)
    /// {
    ///     var request = packet.Parse&lt;ChatRequest&gt;();
    ///     // Use request...
    ///     return Task.CompletedTask;
    /// }
    /// </code>
    /// </example>
    public static T Parse<T>(this IPacket packet) where T : class
    {
        return JsonSerializer.Deserialize<T>(packet.Payload.DataSpan)
            ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name}");
    }

    /// <summary>
    /// Tries to parse the packet payload as JSON and deserialize it to the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="packet">The packet containing JSON payload.</param>
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
            result = JsonSerializer.Deserialize<T>(packet.Payload.DataSpan);
            return result != null;
        }
        catch
        {
            result = default;
            return false;
        }
    }
}
