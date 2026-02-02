using System.Text.Json;

namespace PlayHouse.Benchmark.SS.Client;

/// <summary>
/// 서버의 HTTP API를 통해 메트릭을 조회합니다.
/// </summary>
public class ServerMetricsClient(string serverHost, int httpPort = 5080)
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    /// <summary>
    /// 서버 메트릭을 조회합니다.
    /// </summary>
    public async Task<ServerMetricsResponse?> GetMetricsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"http://{serverHost}:{httpPort}/benchmark/stats");
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
            var response = await _httpClient.PostAsync($"http://{serverHost}:{httpPort}/benchmark/reset", null);
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
    public double ThroughputMbPerSec { get; init; }
    public double LatencyMeanMs { get; init; }
    public double LatencyP50Ms { get; init; }
    public double LatencyP95Ms { get; init; }
    public double LatencyP99Ms { get; init; }
    public double MemoryAllocatedMb { get; init; }
    public int GcGen0Count { get; init; }
    public int GcGen1Count { get; init; }
    public int GcGen2Count { get; init; }
    public double CpuUsagePercent { get; init; }
}
