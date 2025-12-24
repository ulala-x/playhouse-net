#nullable enable

using System.ComponentModel;

namespace PlayHouse.Abstractions;

/// <summary>
/// Error codes used by the PlayHouse framework.
/// </summary>
/// <remarks>
/// Error codes 0-999 are reserved for framework use.
/// Application-specific error codes should start from 1000.
/// </remarks>
public enum ErrorCode : ushort
{
    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    [Description("Operation completed successfully")]
    Success = 0,

    /// <summary>
    /// Request timed out waiting for reply.
    /// </summary>
    [Description("Request timed out waiting for reply")]
    RequestTimeout = 1,

    /// <summary>
    /// Target server not found or not connected.
    /// </summary>
    [Description("Target server not found or not connected")]
    ServerNotFound = 2,

    /// <summary>
    /// Target stage not found.
    /// </summary>
    [Description("Target stage not found")]
    StageNotFound = 3,

    /// <summary>
    /// Target actor not found.
    /// </summary>
    [Description("Target actor not found")]
    ActorNotFound = 4,

    /// <summary>
    /// Authentication failed.
    /// </summary>
    [Description("Authentication failed")]
    AuthenticationFailed = 5,

    /// <summary>
    /// Not authenticated - authentication required.
    /// </summary>
    [Description("Not authenticated - authentication required")]
    NotAuthenticated = 6,

    /// <summary>
    /// Already authenticated.
    /// </summary>
    [Description("Already authenticated")]
    AlreadyAuthenticated = 7,

    /// <summary>
    /// Stage already exists.
    /// </summary>
    [Description("Stage already exists")]
    StageAlreadyExists = 8,

    /// <summary>
    /// Stage creation failed.
    /// </summary>
    [Description("Stage creation failed")]
    StageCreationFailed = 9,

    /// <summary>
    /// Join stage failed.
    /// </summary>
    [Description("Join stage failed")]
    JoinStageFailed = 10,

    /// <summary>
    /// Invalid message format or content.
    /// </summary>
    [Description("Invalid message format or content")]
    InvalidMessage = 11,

    /// <summary>
    /// Handler not found for the message ID.
    /// </summary>
    [Description("Handler not found for the message ID")]
    HandlerNotFound = 12,

    /// <summary>
    /// Invalid stage type.
    /// </summary>
    [Description("Invalid stage type")]
    InvalidStageType = 13,

    /// <summary>
    /// System error.
    /// </summary>
    [Description("System error")]
    SystemError = 14,

    /// <summary>
    /// Unchecked contents error.
    /// </summary>
    [Description("Unchecked contents error")]
    UncheckedContentsError = 15,

    /// <summary>
    /// Invalid account ID - not set after authentication.
    /// </summary>
    [Description("Invalid account ID - not set after authentication")]
    InvalidAccountId = 16,

    /// <summary>
    /// Join stage rejected by stage.
    /// </summary>
    [Description("Join stage rejected by stage")]
    JoinStageRejected = 17,

    /// <summary>
    /// Internal server error.
    /// </summary>
    [Description("Internal server error")]
    InternalError = 99,

    /// <summary>
    /// First available code for application use.
    /// </summary>
    [Description("First available code for application use")]
    ApplicationBase = 1000,
}
