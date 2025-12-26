#nullable enable

using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Runtime.ServerMesh.Message;

/// <summary>
/// Server-to-server route packet for ZMQ communication.
/// </summary>
/// <remarks>
/// 3-Frame message structure:
/// - Frame 0: Target NID (UTF-8)
/// - Frame 1: RouteHeader (Protobuf)
/// - Frame 2: Payload (binary)
/// </remarks>
public sealed class RoutePacket : IDisposable
{
    /// <summary>
    /// Gets the route header containing routing metadata.
    /// </summary>
    public RouteHeader Header { get; }

    /// <summary>
    /// Gets the payload data.
    /// </summary>
    public IPayload Payload { get; }

    private readonly bool _ownsPayload;
    private bool _disposed;

    private RoutePacket(RouteHeader header, IPayload payload, bool ownsPayload = true)
    {
        Header = header;
        Payload = payload;
        _ownsPayload = ownsPayload;
    }

    #region Factory Methods

    /// <summary>
    /// Creates a route packet from received frame data.
    /// </summary>
    /// <param name="headerBytes">RouteHeader bytes from Frame 1.</param>
    /// <param name="payloadBytes">Payload bytes from Frame 2.</param>
    /// <param name="senderNid">Sender NID from Frame 0 (optional, sets Header.From).</param>
    /// <returns>A new RuntimeRoutePacket.</returns>
    public static RoutePacket FromFrames(byte[] headerBytes, byte[] payloadBytes, string? senderNid = null)
    {
        var header = RouteHeader.Parser.ParseFrom(headerBytes);

        // Set sender NID if provided (Kairos pattern)
        if (senderNid != null)
        {
            header.From = senderNid;
        }

        // Legacy support - create MemoryPayload from byte array
        var payload = new MemoryPayload(payloadBytes);
        return new RoutePacket(header, payload);
    }

   /// <summary>
    /// Creates a route packet from a parsed RouteHeader and ZMQ Message payload.
    /// </summary>
    /// <param name="header">Already parsed RouteHeader.</param>
    /// <param name="payloadMessage">ZMQ Message containing payload (ownership transferred).</param>
    /// <param name="senderNid">Sender NID from Frame 0 (optional, sets Header.From).</param>
    /// <returns>A new RuntimeRoutePacket.</returns>
    public static RoutePacket FromFrames(
        RouteHeader header,
        Net.Zmq.Message payloadMessage,
        string? senderNid = null)
    {
        if (senderNid != null)
        {
            header.From = senderNid;
        }

        var payload = new ZmqPayload(payloadMessage);
        return new RoutePacket(header, payload);
    }

   /// <summary>
    /// Creates a route packet with custom header.
    /// </summary>
    /// <param name="header">Route header.</param>
    /// <param name="payload">Payload bytes.</param>
    /// <returns>A new RuntimeRoutePacket.</returns>
    public static RoutePacket Of(RouteHeader header, byte[] payload)
    {
        return new RoutePacket(header, new MemoryPayload(payload));
    }

    /// <summary>
    /// Creates a route packet with custom header and IPayload (shared reference).
    /// Used for sending - does NOT own the payload.
    /// </summary>
    /// <param name="header">Route header.</param>
    /// <param name="payload">Payload instance (caller retains ownership).</param>
    /// <returns>A new RuntimeRoutePacket that does not own the payload.</returns>
    public static RoutePacket Of(RouteHeader header, IPayload payload)
    {
        // For sending: RoutePacket does NOT own the payload
        // The original packet (CPacket) retains ownership and handles disposal
        return new RoutePacket(header, payload, ownsPayload: false);
    }

    /// <summary>
    /// Creates an empty route packet (for error responses).
    /// </summary>
    /// <param name="header">Route header.</param>
    /// <returns>A new RuntimeRoutePacket with empty payload.</returns>
    public static RoutePacket Empty(RouteHeader header)
    {
        return new RoutePacket(header, EmptyPayload.Instance);
    }

    #endregion

    #region Reply Factory Methods

    
    /// <summary>
    /// Creates an error reply packet for this request.
    /// </summary>
    /// <param name="errorCode">Error code.</param>
    /// <returns>A new error reply RuntimeRoutePacket.</returns>
    public RoutePacket CreateErrorReply(ushort errorCode)
    {
        var replyHeader = new RouteHeader
        {
            MsgSeq = Header.MsgSeq,
            ServiceId = Header.ServiceId,
            MsgId = Header.MsgId,
            From = Header.From,
            ErrorCode = errorCode
        };
        return new RoutePacket(replyHeader, EmptyPayload.Instance);
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
    /// Gets the payload data as a ReadOnlySpan for zero-copy access.
    /// </summary>
    /// <returns>Payload data as ReadOnlySpan.</returns>
    public ReadOnlySpan<byte> GetPayloadSpan() => Payload.DataSpan;

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

        // Only dispose payload if we own it
        // For sending: ownsPayload=false, original packet retains ownership
        // For receiving: ownsPayload=true (default), we own the ZmqPayload
        if (_ownsPayload)
        {
            Payload.Dispose();
        }
    }
}
