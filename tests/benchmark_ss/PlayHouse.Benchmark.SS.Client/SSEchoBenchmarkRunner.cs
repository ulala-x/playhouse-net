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
    long targetStageId,
    string targetNid,
    int durationSeconds = 10,
    int maxInFlight = 200)
{
    public async Task RunAsync()
    {
        Log.Information("================================================================================");
        Log.Information("S2S Benchmark Runner (Trigger & Push Mode)");
        Log.Information("================================================================================");

        var results = new List<BenchmarkResult>();
        var tasks = new Task[connections];
        
        for (int i = 0; i < connections; i++)
        {
            var id = i;
            tasks[i] = Task.Run(async () => {
                var result = await RunSingleTrigger(id);
                if (result != null) lock (results) results.Add(result);
            });
            // Stagger to prevent burst
            await Task.Delay(10);
        }

        await Task.WhenAll(tasks);

        if (results.Count > 0)
        {
            var totalSent = results.Sum(r => r.TotalSent);
            var totalRecv = results.Sum(r => r.TotalReceived);
            var avgTps = results.Sum(r => r.Tps);
            var avgP99 = results.Average(r => r.P99LatencyMs);
            
            Log.Information("================================================================================");
            Log.Information("S2S AGGREGATE RESULT");
            Log.Information("================================================================================");
            Log.Information("Total Throughput: {TPS:N0} msg/s", avgTps);
            Log.Information("Avg P99 Latency:  {P99:F2}ms", avgP99);
            Log.Information("Total Messages:   {Count:N0}", totalRecv);
            Log.Information("================================================================================");
        }
    }

    private async Task<BenchmarkResult?> RunSingleTrigger(int connectionId)
    {
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });

        var tcsResult = new TaskCompletionSource<BenchmarkResult?>();
        connector.OnReceive += (sid, type, packet) => {
            if (packet.MsgId == "BenchmarkResult") {
                tcsResult.TrySetResult(BenchmarkResult.Parser.ParseFrom(packet.Payload.DataSpan));
            }
            packet.Dispose();
        };

        var stageId = 1000 + connectionId;
        if (!await connector.ConnectAsync(serverHost, serverPort, stageId, "BenchmarkStage")) return null;

        try
        {
            await connector.AuthenticateAsync(ClientPacket.Empty("Authenticate"));

            var request = new StartBenchmarkRequest {
                DurationSeconds = durationSeconds, MaxInflight = maxInFlight,
                MessageSize = messageSize, CommMode = commMode,
                TargetNid = targetNid, TargetStageId = targetStageId
            };

            // [핵심] 메시지 수신을 위한 폴링 타이머를 요청 전에 먼저 시작
            using var timer = new Timer(_ => connector.MainThreadAction(), null, 0, 20);

            // 1. Send Trigger and get immediate response
            using var response = await connector.RequestAsync(new ClientPacket(request));
            if (!response.MsgId.Contains("StartBenchmarkAccepted")) 
            {
                Log.Error("Stage {Id} rejected trigger. Expected Accepted, got: {MsgId}", stageId, response.MsgId);
                return null;
            }

            // 2. Wait for Push results (Duration + 15s buffer)
            var result = await tcsResult.Task.WaitAsync(TimeSpan.FromSeconds(durationSeconds + 15));
            if (result == null) Log.Warning("Stage {Id} returned null result", stageId);
            return result;
        }
        catch (Exception ex) { Log.Error("Stage {Id} Error: {Msg}", stageId, ex.Message); }
        finally { connector.Disconnect(); }
        return null;
    }
}
