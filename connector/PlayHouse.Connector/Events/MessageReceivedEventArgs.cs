namespace PlayHouse.Connector.Events;

using Google.Protobuf;

/// <summary>
/// Event arguments for received messages.
/// </summary>
public sealed class MessageReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the message ID (type identifier).
    /// </summary>
    public ushort MessageId { get; }

    /// <summary>
    /// Gets the message sequence number (0 for one-way messages).
    /// </summary>
    public ushort MessageSeq { get; }

    /// <summary>
    /// Gets the received message.
    /// </summary>
    public IMessage Message { get; }

    /// <summary>
    /// Gets the message type.
    /// </summary>
    public Type MessageType => Message.GetType();

    /// <summary>
    /// Gets the timestamp when the message was received.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageReceivedEventArgs"/> class.
    /// </summary>
    /// <param name="messageId">Message ID</param>
    /// <param name="messageSeq">Message sequence number</param>
    /// <param name="message">Received message</param>
    public MessageReceivedEventArgs(ushort messageId, ushort messageSeq, IMessage message)
    {
        MessageId = messageId;
        MessageSeq = messageSeq;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Timestamp = DateTime.UtcNow;
    }
}
