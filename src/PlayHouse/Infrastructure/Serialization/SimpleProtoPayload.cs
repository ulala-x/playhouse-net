#nullable enable

using Google.Protobuf;
using PlayHouse.Abstractions;

namespace PlayHouse.Infrastructure.Serialization;

/// <summary>
/// Protobuf message payload implementation.
/// Serializes a Protobuf message to bytes on construction.
/// </summary>
public sealed class SimpleProtoPayload : IPayload
{
    private readonly byte[] _data;

    /// <summary>
    /// Initializes a new instance by serializing a Protobuf message.
    /// </summary>
    /// <param name="message">The Protobuf message to serialize.</param>
    public SimpleProtoPayload(IMessage message)
    {
        _data = message.ToByteArray();
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Data => _data;

    /// <inheritdoc />
    public int Length => _data.Length;

    /// <inheritdoc />
    public void Dispose()
    {
        // No resources to dispose
    }
}
