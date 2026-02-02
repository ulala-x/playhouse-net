#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Defines the server types in the PlayHouse framework.
/// </summary>
/// <remarks>
/// ServerType distinguishes between different kinds of servers.
/// ServiceId is used to group servers within the same ServerType.
/// </remarks>
public enum ServerType : ushort
{
    /// <summary>
    /// Play Server - handles game logic and real-time communication.
    /// </summary>
    Play = 1,

    /// <summary>
    /// API Server - handles stateless API requests.
    /// </summary>
    Api = 2,
}

/// <summary>
/// Default service group identifiers.
/// </summary>
public static class ServiceIdDefaults
{
    /// <summary>
    /// Default service group.
    /// </summary>
    public const ushort Default = 1;
}
