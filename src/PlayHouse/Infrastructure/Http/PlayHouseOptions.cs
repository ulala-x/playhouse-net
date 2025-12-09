#nullable enable

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using PlayHouse.Infrastructure.Transport.Tcp;

namespace PlayHouse.Infrastructure.Http;

/// <summary>
/// Configuration options for PlayHouse server.
/// Loaded from appsettings.json section "PlayHouse".
/// </summary>
public sealed class PlayHouseOptions : IValidateOptions<PlayHouseOptions>
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "PlayHouse";

    /// <summary>
    /// Server IP address to bind to.
    /// </summary>
    [Required]
    public required string Ip { get; init; }

    /// <summary>
    /// Server port to bind to. Default: 7777.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; init; } = 7777;

    /// <summary>
    /// Request timeout in milliseconds. Default: 30000 ms (30 seconds).
    /// </summary>
    [Range(1000, 300000)]
    public int RequestTimeoutMs { get; init; } = 30000;

    /// <summary>
    /// Maximum number of concurrent connections. Default: 10000.
    /// </summary>
    [Range(1, 100000)]
    public int MaxConnections { get; init; } = 10000;

    /// <summary>
    /// TCP session configuration options.
    /// </summary>
    public TcpSessionOptions Session { get; init; } = new();

    /// <summary>
    /// Whether to enable WebSocket support. Default: true.
    /// </summary>
    public bool EnableWebSocket { get; init; } = true;

    /// <summary>
    /// WebSocket endpoint path. Default: "/ws".
    /// </summary>
    public string WebSocketPath { get; init; } = "/ws";

    /// <summary>
    /// Whether to enable HTTP API endpoints. Default: true.
    /// </summary>
    public bool EnableHttpApi { get; init; } = true;

    /// <summary>
    /// HTTP API base path. Default: "/api".
    /// </summary>
    public string HttpApiPath { get; init; } = "/api";

    /// <summary>
    /// Whether to enable compression. Default: true.
    /// </summary>
    public bool EnableCompression { get; init; } = true;

    /// <summary>
    /// Minimum payload size for compression in bytes. Default: 512.
    /// </summary>
    [Range(0, 1048576)]
    public int CompressionThreshold { get; init; } = 512;

    /// <summary>
    /// Validates the options configuration.
    /// </summary>
    /// <param name="name">The options name.</param>
    /// <param name="options">The options instance to validate.</param>
    /// <returns>Validation result.</returns>
    public ValidateOptionsResult Validate(string? name, PlayHouseOptions options)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(options);

        if (!Validator.TryValidateObject(options, context, validationResults, validateAllProperties: true))
        {
            var errors = validationResults.Select(r => r.ErrorMessage ?? "Unknown error");
            return ValidateOptionsResult.Fail(errors);
        }

        // Additional custom validations
        if (string.IsNullOrWhiteSpace(options.Ip))
        {
            return ValidateOptionsResult.Fail("IP address is required");
        }

        if (options.RequestTimeoutMs < 1000)
        {
            return ValidateOptionsResult.Fail("RequestTimeoutMs must be at least 1000ms");
        }

        if (options.EnableWebSocket && string.IsNullOrWhiteSpace(options.WebSocketPath))
        {
            return ValidateOptionsResult.Fail("WebSocketPath is required when WebSocket is enabled");
        }

        if (options.EnableHttpApi && string.IsNullOrWhiteSpace(options.HttpApiPath))
        {
            return ValidateOptionsResult.Fail("HttpApiPath is required when HTTP API is enabled");
        }

        return ValidateOptionsResult.Success;
    }
}
