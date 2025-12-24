using System.Text.Json;

namespace PlayHouse.Benchmark.Client;

/// <summary>
/// 서버의 HTTP API를 통해 메트릭을 조회합니다.
/// </summary>
public class ServerMetricsClient
{
    private readonly string _serverHost;
    private readonly int _httpPort;
    private readonly HttpClient _httpClient;

    public ServerMetricsClient(string serverHost, int httpPort = 5080)
    {
        _serverHost = serverHost;
        _httpPort = httpPort;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    /// <summary>
    /// 서버 메트릭을 조회합니다.
    /// </summary>
    public async Task<ServerMetricsResponse?> GetMetricsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"http://{_serverHost}:{_httpPort}/benchmark/stats");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ServerMetricsResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get server metrics: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 서버 메트릭을 리셋합니다.
    /// </summary>
    public async Task<bool> ResetMetricsAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync($"http://{_serverHost}:{_httpPort}/benchmark/reset", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to reset server metrics: {ex.Message}");
            return false;
        }
    }
}

public record ServerMetricsResponse
{
    public long ProcessedMessages { get; init; }
    public double ThroughputMessagesPerSec { get; init; }
    public double ThroughputMBPerSec { get; init; }
    public double LatencyMeanMs { get; init; }
    public double LatencyP50Ms { get; init; }
    public double LatencyP95Ms { get; init; }
    public double LatencyP99Ms { get; init; }
    public double MemoryAllocatedMB { get; init; }
    public int GcGen0Count { get; init; }
    public int GcGen1Count { get; init; }
    public int GcGen2Count { get; init; }
}
