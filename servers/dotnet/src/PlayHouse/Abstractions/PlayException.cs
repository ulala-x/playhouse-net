#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Exception class for PlayHouse framework errors.
/// </summary>
public class PlayException : Exception
{
    /// <summary>
    /// Gets the error code associated with this exception.
    /// </summary>
    public ErrorCode ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayException"/> class.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    public PlayException(ErrorCode errorCode)
        : base($"[{errorCode}({(ushort)errorCode})] {errorCode.GetDescription()}")
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayException"/> class with an inner exception.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="innerException">The inner exception.</param>
    public PlayException(ErrorCode errorCode, Exception innerException)
        : base($"[{errorCode}({(ushort)errorCode})] {errorCode.GetDescription()}", innerException)
    {
        ErrorCode = errorCode;
    }
}
