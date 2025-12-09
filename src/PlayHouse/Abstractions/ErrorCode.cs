#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Defines standard error codes used throughout the PlayHouse framework.
/// </summary>
/// <remarks>
/// Error codes from 0-999 are reserved for framework use.
/// User-defined error codes should start from <see cref="UserErrorStart"/> (1000).
/// </remarks>
public static class ErrorCode
{
    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    public const ushort Success = 0;

    /// <summary>
    /// An unknown error occurred.
    /// </summary>
    public const ushort UnknownError = 1;

    /// <summary>
    /// The packet format or content is invalid.
    /// </summary>
    public const ushort InvalidPacket = 2;

    /// <summary>
    /// The operation timed out.
    /// </summary>
    public const ushort Timeout = 3;

    /// <summary>
    /// The requested stage was not found.
    /// </summary>
    public const ushort StageNotFound = 4;

    /// <summary>
    /// The requested actor was not found.
    /// </summary>
    public const ushort ActorNotFound = 5;

    /// <summary>
    /// The request is not authorized.
    /// </summary>
    public const ushort Unauthorized = 6;

    /// <summary>
    /// An internal server error occurred.
    /// </summary>
    public const ushort InternalError = 7;

    /// <summary>
    /// The operation is invalid in the current state.
    /// </summary>
    public const ushort InvalidState = 8;

    /// <summary>
    /// The rate limit for this operation has been exceeded.
    /// </summary>
    public const ushort RateLimitExceeded = 9;

    /// <summary>
    /// The stage has reached its maximum capacity.
    /// </summary>
    public const ushort StageFull = 10;

    /// <summary>
    /// The requested room was not found.
    /// </summary>
    public const ushort RoomNotFound = 11;

    /// <summary>
    /// A duplicate login attempt was detected.
    /// </summary>
    public const ushort DuplicateLogin = 12;

    /// <summary>
    /// Starting value for user-defined error codes.
    /// All custom error codes should be >= this value.
    /// </summary>
    public const ushort UserErrorStart = 1000;
}
