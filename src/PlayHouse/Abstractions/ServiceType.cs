#nullable enable

namespace PlayHouse.Abstractions;

/// <summary>
/// Defines the service types in the PlayHouse framework.
/// </summary>
/// <remarks>
/// Each server type has a predefined service ID for routing purposes.
/// Custom service types should use values >= 100.
/// </remarks>
public enum ServiceType : ushort
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
