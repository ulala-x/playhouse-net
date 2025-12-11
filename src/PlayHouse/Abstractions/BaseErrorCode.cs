#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Base error codes used by the PlayHouse framework.
/// </summary>
/// <remarks>
/// Error codes 0-999 are reserved for framework use.
/// Application-specific error codes should start from 1000.
/// </remarks>
public static class BaseErrorCode
{
    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    public const ushort Success = 0;

    /// <summary>
    /// Request timed out waiting for reply.
    /// </summary>
    public const ushort RequestTimeout = 1;

    /// <summary>
    /// Target server not found or not connected.
    /// </summary>
    public const ushort ServerNotFound = 2;

    /// <summary>
    /// Target stage not found.
    /// </summary>
    public const ushort StageNotFound = 3;

    /// <summary>
    /// Target actor not found.
    /// </summary>
    public const ushort ActorNotFound = 4;

    /// <summary>
    /// Authentication failed.
    /// </summary>
    public const ushort AuthenticationFailed = 5;

    /// <summary>
    /// Not authenticated (requires authentication first).
    /// </summary>
    public const ushort NotAuthenticated = 6;

    /// <summary>
    /// Already authenticated.
    /// </summary>
    public const ushort AlreadyAuthenticated = 7;

    /// <summary>
    /// Stage already exists.
    /// </summary>
    public const ushort StageAlreadyExists = 8;

    /// <summary>
    /// Stage creation failed.
    /// </summary>
    public const ushort StageCreationFailed = 9;

    /// <summary>
    /// Join stage failed.
    /// </summary>
    public const ushort JoinStageFailed = 10;

    /// <summary>
    /// Invalid message format or content.
    /// </summary>
    public const ushort InvalidMessage = 11;

    /// <summary>
    /// Handler not found for the message ID.
    /// </summary>
    public const ushort HandlerNotFound = 12;

    /// <summary>
    /// Invalid stage type.
    /// </summary>
    public const ushort InvalidStageType = 13;

    /// <summary>
    /// System error.
    /// </summary>
    public const ushort SystemError = 14;

    /// <summary>
    /// Unchecked contents error.
    /// </summary>
    public const ushort UncheckedContentsError = 15;

    /// <summary>
    /// Internal server error.
    /// </summary>
    public const ushort InternalError = 99;

    /// <summary>
    /// Invalid account ID (not set after authentication).
    /// </summary>
    public const ushort InvalidAccountId = 16;

    /// <summary>
    /// Join stage rejected by stage.
    /// </summary>
    public const ushort JoinStageRejected = 17;

    /// <summary>
    /// First available code for application use.
    /// </summary>
    public const ushort ApplicationBase = 1000;
}
