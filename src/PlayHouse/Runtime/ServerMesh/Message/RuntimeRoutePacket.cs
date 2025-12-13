#nullable enable

using Google.Protobuf;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Runtime.ServerMesh.Message;

/// <summary>
/// Server-to-server route packet for NetMQ communication.
/// </summary>
/// <remarks>
/// 3-Frame message structure:
/// - Frame 0: Target NID (UTF-8)
/// - Frame 1: RouteHeader (Protobuf)
/// - Frame 2: Payload (binary)
/// </remarks>
public sealed class RuntimeRoutePacket : IDisposable
{
    /// <summary>
    /// Gets the route header containing routing metadata.
    /// </summary>
    public RouteHeader Header { get; }

    /// <summary>
    /// Gets the payload data.
    /// </summary>
    public IRuntimePayload Payload { get; }

    private bool _disposed;

    private RuntimeRoutePacket(RouteHeader header, IRuntimePayload payload)
    {
        Header = header;
        Payload = payload;
    }

    #region Factory Methods

    /// <summary>
    /// Creates a route packet from received frame data.
    /// </summary>
    /// <param name="headerBytes">RouteHeader bytes from Frame 1.</param>
    /// <param name="payloadBytes">Payload bytes from Frame 2.</param>
    /// <param name="senderNid">Sender NID from Frame 0 (optional, sets Header.From).</param>
    /// <returns>A new RuntimeRoutePacket.</returns>
    public static RuntimeRoutePacket FromFrames(byte[] headerBytes, byte[] payloadBytes, string? senderNid = null)
    {
        var header = RouteHeader.Parser.ParseFrom(headerBytes);

        // Set sender NID if provided (Kairos pattern)
        if (senderNid != null)
        {
            header.From = senderNid;
        }

        var payload = new FramePayload(payloadBytes);
        return new RuntimeRoutePacket(header, payload);
    }

    /// <summary>
    /// Creates a route packet from a Protobuf message.
    /// </summary>
    /// <typeparam name="T">Protobuf message type.</typeparam>
    /// <param name="message">The message to wrap.</param>
    /// <param name="from">Sender NID.</param>
    /// <param name="msgSeq">Message sequence number.</param>
    /// <param name="serviceId">Service ID.</param>
    /// <returns>A new RuntimeRoutePacket.</returns>
    public static RuntimeRoutePacket Of<T>(T message, string from, ushort msgSeq, ushort serviceId)
        where T : IMessage
    {
        var header = new RouteHeader
        {
            MsgSeq = msgSeq,
            ServiceId = serviceId,
            MsgId = typeof(T).Name,
            From = from
        };
        var payload = new ByteStringPayload(message.ToByteString());
        return new RuntimeRoutePacket(header, payload);
    }

    /// <summary>
    /// Creates a route packet with custom header.
    /// </summary>
    /// <param name="header">Route header.</param>
    /// <param name="payload">Payload bytes.</param>
    /// <returns>A new RuntimeRoutePacket.</returns>
    public static RuntimeRoutePacket Of(RouteHeader header, byte[] payload)
    {
        return new RuntimeRoutePacket(header, new ByteArrayPayload(payload));
    }

    /// <summary>
    /// Creates a route packet with custom header and ByteString payload.
    /// </summary>
    /// <param name="header">Route header.</param>
    /// <param name="payload">Payload as ByteString.</param>
    /// <returns>A new RuntimeRoutePacket.</returns>
    public static RuntimeRoutePacket Of(RouteHeader header, ByteString payload)
    {
        return new RuntimeRoutePacket(header, new ByteStringPayload(payload));
    }

    /// <summary>
    /// Creates an empty route packet (for error responses).
    /// </summary>
    /// <param name="header">Route header.</param>
    /// <returns>A new RuntimeRoutePacket with empty payload.</returns>
    public static RuntimeRoutePacket Empty(RouteHeader header)
    {
        return new RuntimeRoutePacket(header, EmptyRuntimePayload.Instance);
    }

    #endregion

    #region Reply Factory Methods

    /// <summary>
    /// Creates a reply packet for this request.
    /// </summary>
    /// <typeparam name="T">Protobuf message type.</typeparam>
    /// <param name="message">Reply message.</param>
    /// <returns>A new reply RuntimeRoutePacket.</returns>
    public RuntimeRoutePacket CreateReply<T>(T message) where T : IMessage
    {
        var replyHeader = new RouteHeader
        {
            MsgSeq = Header.MsgSeq,  // Same sequence for matching
            ServiceId = Header.ServiceId,
            MsgId = typeof(T).Name,
            From = Header.From,  // Will be overwritten by sender
            ErrorCode = 0
        };
        return new RuntimeRoutePacket(replyHeader, new ByteStringPayload(message.ToByteString()));
    }

    /// <summary>
    /// Creates an error reply packet for this request.
    /// </summary>
    /// <param name="errorCode">Error code.</param>
    /// <returns>A new error reply RuntimeRoutePacket.</returns>
    public RuntimeRoutePacket CreateErrorReply(ushort errorCode)
    {
        var replyHeader = new RouteHeader
        {
            MsgSeq = Header.MsgSeq,
            ServiceId = Header.ServiceId,
            MsgId = Header.MsgId,
            From = Header.From,
            ErrorCode = errorCode
        };
        return new RuntimeRoutePacket(replyHeader, EmptyRuntimePayload.Instance);
    }

    /// <summary>
    /// Creates a reply packet from a request packet (static factory).
    /// </summary>
    /// <param name="request">Original request packet.</param>
    /// <param name="from">Sender NID.</param>
    /// <param name="serviceId">Service ID.</param>
    /// <param name="msgId">Reply message ID.</param>
    /// <param name="payload">Reply payload bytes.</param>
    /// <param name="errorCode">Error code (0 for success).</param>
    /// <returns>A new reply RuntimeRoutePacket.</returns>
    public static RuntimeRoutePacket CreateReply(
        RuntimeRoutePacket request,
        string from,
        ushort serviceId,
        string msgId,
        byte[] payload,
        ushort errorCode = 0)
    {
        var replyHeader = new RouteHeader
        {
            MsgSeq = request.Header.MsgSeq,
            ServiceId = serviceId,
            MsgId = msgId,
            From = from,
            ErrorCode = errorCode,
            StageId = request.Header.StageId,
            AccountId = request.Header.AccountId
        };
        return new RuntimeRoutePacket(replyHeader, new ByteArrayPayload(payload));
    }

    #endregion

    #region Serialization

    /// <summary>
    /// Serializes the header to bytes for sending.
    /// </summary>
    /// <returns>Serialized header bytes.</returns>
    public byte[] SerializeHeader()
    {
        return Header.ToByteArray();
    }

    /// <summary>
    /// Gets the payload bytes for sending.
    /// </summary>
    /// <returns>Payload bytes.</returns>
    public byte[] GetPayloadBytes()
    {
        return Payload.DataSpan.ToArray();
    }

    #endregion

    #region Convenience Properties

    /// <summary>
    /// Gets the message ID.
    /// </summary>
    public string MsgId => Header.MsgId;

    /// <summary>
    /// Gets the message sequence number.
    /// </summary>
    public ushort MsgSeq => (ushort)Header.MsgSeq;

    /// <summary>
    /// Gets the error code.
    /// </summary>
    public ushort ErrorCode => (ushort)Header.ErrorCode;

    /// <summary>
    /// Gets the sender NID.
    /// </summary>
    public string From => Header.From;

    /// <summary>
    /// Gets the stage ID.
    /// </summary>
    public long StageId => Header.StageId;

    /// <summary>
    /// Gets the account ID.
    /// </summary>
    public long AccountId => Header.AccountId;

    /// <summary>
    /// Gets the session ID.
    /// </summary>
    public long Sid => Header.Sid;

    /// <summary>
    /// Gets whether this is an error response.
    /// </summary>
    public bool IsError => Header.ErrorCode != 0;

    #endregion

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Payload.Dispose();
    }
}
