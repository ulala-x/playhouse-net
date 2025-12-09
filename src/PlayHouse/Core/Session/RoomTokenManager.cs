#nullable enable

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PlayHouse.Core.Session;

/// <summary>
/// Room token 생성 및 검증을 담당하는 관리자.
/// HTTP API에서 발급한 토큰을 TCP 연결 시 검증합니다.
/// </summary>
public sealed class RoomTokenManager : IDisposable
{
    private readonly ILogger<RoomTokenManager> _logger;
    private readonly ConcurrentDictionary<string, TokenInfo> _tokens = new();
    private readonly System.Threading.Timer _cleanupTimer;
    private readonly TimeSpan _tokenTtl;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoomTokenManager"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="tokenTtl">Token time-to-live (default: 5 minutes).</param>
    public RoomTokenManager(ILogger<RoomTokenManager> logger, TimeSpan? tokenTtl = null)
    {
        _logger = logger;
        _tokenTtl = tokenTtl ?? TimeSpan.FromMinutes(5);

        // Start cleanup timer (every 1 minute)
        _cleanupTimer = new System.Threading.Timer(
            CleanupExpiredTokens,
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));

        _logger.LogInformation("RoomTokenManager initialized with TTL={TokenTtl}", _tokenTtl);
    }

    /// <summary>
    /// 새 Room 토큰을 생성합니다.
    /// </summary>
    /// <param name="stageId">Stage 식별자.</param>
    /// <param name="nickname">사용자 닉네임.</param>
    /// <returns>생성된 토큰 문자열.</returns>
    public string GenerateToken(int stageId, string nickname)
    {
        // Generate secure random token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");

        var tokenInfo = new TokenInfo
        {
            StageId = stageId,
            Nickname = nickname,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_tokenTtl)
        };

        if (!_tokens.TryAdd(token, tokenInfo))
        {
            _logger.LogWarning("Failed to add token (collision): {Token}", token);
            // Retry with new token
            return GenerateToken(stageId, nickname);
        }

        _logger.LogDebug("Generated token for StageId={StageId}, Nickname={Nickname}, ExpiresAt={ExpiresAt}",
            stageId, nickname, tokenInfo.ExpiresAt);

        return token;
    }

    /// <summary>
    /// 토큰을 검증하고 관련 정보를 반환합니다.
    /// </summary>
    /// <param name="token">검증할 토큰.</param>
    /// <returns>검증 결과 (성공 여부, StageId, Nickname).</returns>
    public (bool IsValid, int StageId, string Nickname) ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Validation failed: Empty token");
            return (false, 0, string.Empty);
        }

        if (!_tokens.TryGetValue(token, out var tokenInfo))
        {
            _logger.LogWarning("Validation failed: Token not found - {Token}", token);
            return (false, 0, string.Empty);
        }

        // Check expiration
        if (DateTime.UtcNow > tokenInfo.ExpiresAt)
        {
            _logger.LogWarning("Validation failed: Token expired - {Token}, ExpiresAt={ExpiresAt}",
                token, tokenInfo.ExpiresAt);
            _tokens.TryRemove(token, out _);
            return (false, 0, string.Empty);
        }

        _logger.LogDebug("Token validated successfully: StageId={StageId}, Nickname={Nickname}",
            tokenInfo.StageId, tokenInfo.Nickname);

        return (true, tokenInfo.StageId, tokenInfo.Nickname);
    }

    /// <summary>
    /// 토큰을 무효화합니다 (1회용 토큰 구현).
    /// </summary>
    /// <param name="token">무효화할 토큰.</param>
    /// <returns>무효화 성공 여부.</returns>
    public bool RevokeToken(string token)
    {
        if (_tokens.TryRemove(token, out var tokenInfo))
        {
            _logger.LogDebug("Token revoked: StageId={StageId}, Nickname={Nickname}",
                tokenInfo.StageId, tokenInfo.Nickname);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 만료된 토큰을 정리합니다.
    /// </summary>
    private void CleanupExpiredTokens(object? state)
    {
        var now = DateTime.UtcNow;
        var expiredCount = 0;

        foreach (var (token, tokenInfo) in _tokens)
        {
            if (now > tokenInfo.ExpiresAt)
            {
                if (_tokens.TryRemove(token, out _))
                {
                    expiredCount++;
                }
            }
        }

        if (expiredCount > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired tokens", expiredCount);
        }
    }

    /// <summary>
    /// 현재 활성 토큰 개수를 반환합니다.
    /// </summary>
    public int ActiveTokenCount => _tokens.Count;

    /// <inheritdoc/>
    public void Dispose()
    {
        _cleanupTimer.Dispose();
        _tokens.Clear();
        _logger.LogInformation("RoomTokenManager disposed");
    }

    /// <summary>
    /// Token information.
    /// </summary>
    private sealed class TokenInfo
    {
        public required int StageId { get; init; }
        public required string Nickname { get; init; }
        public required DateTime CreatedAt { get; init; }
        public required DateTime ExpiresAt { get; init; }
    }
}
