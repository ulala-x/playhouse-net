using System.Diagnostics;
using Google.Protobuf;
using PlayHouse.Benchmark.SS.Shared.Proto;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using Serilog;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Benchmark.SS.Client;

public class SSEchoBenchmarkRunner(
    string serverHost,
    int serverPort,
    int connections,
    int messageSize,
    SSCommMode commMode,
    int durationSeconds = 10,
    int maxInFlight = 100)
{
    private readonly ByteString _payload = CreatePayload(messageSize);

    private static ByteString CreatePayload(int size)
    {
        var data = new byte[size];
        new Random(42).NextBytes(data);
        return ByteString.CopyFrom(data);
    }

    public async Task RunAsync()
    {
        Log.Information(">>> S2S Pure Benchmark: {Mode} Mode, {CCU} Connections, BatchSize={Batch} <<<", commMode, connections, maxInFlight);

        var metricsClient = new ServerMetricsClient(serverHost, 5080); // Default PlayServer HTTP port
        Log.Information("Resetting server metrics...");
        await metricsClient.ResetMetricsAsync();
        await Task.Delay(1000);

        var tasks = new Task<long>[connections];
        var startTime = Stopwatch.GetTimestamp();

        for (int i = 0; i < connections; i++)
        {
            var id = i;
            tasks[i] = Task.Run(() => RunSingleClient(id, durationSeconds));
        }

        var results = await Task.WhenAll(tasks);
        var endTime = Stopwatch.GetTimestamp();
        var totalElapsedSec = (double)(endTime - startTime) / Stopwatch.Frequency;

        long totalS2SMessages = results.Sum();
        double clientObservedTps = totalS2SMessages / (totalElapsedSec > 0 ? totalElapsedSec : 1);

        Log.Information("Waiting for server metrics to stabilize...");
        await Task.Delay(2000);
        var serverMetrics = await metricsClient.GetMetricsAsync();

        Log.Information("================================================================================");
        Log.Information("S2S BENCHMARK RESULT");
        Log.Information("================================================================================");
        Log.Information("S2S Throughput:            {TPS:N0} msg/s", clientObservedTps);
        
        if (serverMetrics != null)
        {
            Log.Information("Server Avg CPU:            {CPU:F2}%", serverMetrics.CpuUsagePercent);
            Log.Information("Server Memory Alloc:       {Mem:F2} MB", serverMetrics.MemoryAllocatedMb);
            Log.Information("Server GC (0/1/2):         {G0}/{G1}/{G2}", serverMetrics.GcGen0Count, serverMetrics.GcGen1Count, serverMetrics.GcGen2Count);
            Log.Information("Server Latency (Mean):     {Mean:F2}ms", serverMetrics.LatencyMeanMs);
            Log.Information("Server Latency (P50):      {P50:F2}ms", serverMetrics.LatencyP50Ms);
            Log.Information("Server Latency (P95):      {P95:F2}ms", serverMetrics.LatencyP95Ms);
            Log.Information("Server Latency (P99):      {P99:F2}ms", serverMetrics.LatencyP99Ms);
        }
        
        Log.Information("Total S2S Messages:        {Count:N0}", totalS2SMessages);
        Log.Information("Total Test Time:           {Time:F2}s", totalElapsedSec);
        Log.Information("================================================================================");
    }

    private async Task<long> RunSingleClient(int id, int duration)
    {
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        
        var stageId = 1000 + id;
        if (!await connector.ConnectAsync(serverHost, serverPort, stageId, "BenchmarkStage")) return 0;
        await connector.AuthenticateAsync(ClientPacket.Empty("Authenticate"));

        var timer = new Timer(_ => connector.MainThreadAction(), null, 0, 10);
        long totalS2S = 0;
        var endTime = DateTime.UtcNow.AddSeconds(duration);

        try {
            while (DateTime.UtcNow < endTime)
            {
                var request = new TriggerSSEchoRequest {
                    BatchSize = maxInFlight,
                    CommMode = commMode,
                    Payload = _payload
                };

                using var response = await connector.RequestAsync(new ClientPacket(request));
                if (response.MsgId.Contains("Reply"))
                {
                    var reply = TriggerSSEchoReply.Parser.ParseFrom(response.Payload.DataSpan);
                    totalS2S += reply.Count;
                }
            }
        } finally {
            timer.Dispose();
            connector.Disconnect();
        }

        return totalS2S;
    }
}