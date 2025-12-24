using System.Collections.Concurrent;
using System.Diagnostics;
using PlayHouse.Bootstrap;
using PlayHouse.Connector;
using PlayHouse.Tests.Integration.Proto;
using PlayHouse.Tests.Performance.Infrastructure;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Tests.Performance.Benchmarks;

/// <summary>
/// Client → PlayServer(Stage) 구간 성능 측정.
///
/// 테스트 시나리오:
/// 1. 1 Connection × 10,000 Messages (RequestAsync)
/// 2. 1 Connection × 10,000 Messages (Send + OnReceive)
/// 3. 1,000 Connections × 10,000 Messages (RequestAsync)
/// 4. 1,000 Connections × 10,000 Messages (Send + OnReceive)
///
/// Response Size: 512B, 1KB, 64KB
///
/// 측정 항목:
/// - Latency: Mean, P50, P95, P99, StdDev (ms)
/// - Throughput: msg/sec, MB/s
/// - Memory: 요청당 할당량, 총 할당량
/// - GC: Gen0/Gen1/Gen2 수집 횟수
/// </summary>
public class ClientToPlayServerBenchmarks
{
    private PlayServer _playServer = null!;

    private const int WarmupIterations = 100;
    private const int MeasureIterations = 10000;
    private const int MultiConnectionCount = 1000;

    public async Task SetupAsync()
    {
        _playServer = BenchmarkServerFixture.CreateClientToPlayServerFixture();
        await _playServer.StartAsync();

        Console.WriteLine("Waiting for server to start...");
        await Task.Delay(2000);
    }

    public async Task CleanupAsync()
    {
        if (_playServer != null)
            await _playServer.DisposeAsync();
    }

    /// <summary>
    /// 전체 벤치마크 실행
    /// </summary>
    public async Task RunAsync()
    {
        await SetupAsync();

        try
        {
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine("Client → PlayServer Benchmark");
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine();

            var responseSizes = new[] { 512, 1024, 65536 };

            foreach (var responseSize in responseSizes)
            {
                Console.WriteLine($"Response Size: {FormatBytes(responseSize)}");
                Console.WriteLine("-".PadRight(80, '-'));
                Console.WriteLine();

                // Scenario 1: 1 Connection × 10,000 Messages (RequestAsync)
                await RunSingleConnectionRequestAsync(responseSize);
                Console.WriteLine();

                // Scenario 2: 1 Connection × 10,000 Messages (Send + OnReceive)
                await RunSingleConnectionSendReceive(responseSize);
                Console.WriteLine();

                // Scenario 3: 1,000 Connections × 10,000 Messages (RequestAsync)
                await RunMultiConnectionRequestAsync(responseSize);
                Console.WriteLine();

                // Scenario 4: 1,000 Connections × 10,000 Messages (Send + OnReceive)
                await RunMultiConnectionSendReceive(responseSize);
                Console.WriteLine();
            }
        }
        finally
        {
            await CleanupAsync();
        }
    }

    #region Scenario 1: Single Connection RequestAsync

    private async Task RunSingleConnectionRequestAsync(int responseSize)
    {
        Console.WriteLine($"Scenario: 1 Connection × {MeasureIterations:N0} Messages (RequestAsync)");

        var connector = await CreateAndAuthenticateConnector();

        try
        {
            // Warmup
            Console.Write($"Warming up... ");
            await RunRequestAsyncBatch(connector, responseSize, WarmupIterations);
            Console.WriteLine($"Done ({WarmupIterations} iterations)");

            // GC cleanup
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(500);

            // Measurement
            Console.Write($"Measuring... ");
            var measurements = new List<MeasurementData>();
            var totalStopwatch = Stopwatch.StartNew();

            var gcGen0Before = GC.CollectionCount(0);
            var gcGen1Before = GC.CollectionCount(1);
            var gcGen2Before = GC.CollectionCount(2);
            var memoryBefore = GC.GetTotalAllocatedBytes(precise: true);

            for (int i = 0; i < MeasureIterations; i++)
            {
                var request = new BenchmarkRequest
                {
                    Sequence = i,
                    ResponseSize = responseSize
                };

                var sw = Stopwatch.StartNew();
                var memBefore = GC.GetTotalAllocatedBytes(precise: false);

                await connector.RequestAsync(new ClientPacket(request));

                sw.Stop();
                var memAfter = GC.GetTotalAllocatedBytes(precise: false);

                measurements.Add(new MeasurementData
                {
                    ElapsedTicks = sw.ElapsedTicks,
                    MemoryAllocated = memAfter - memBefore,
                    PayloadSize = responseSize
                });
            }

            totalStopwatch.Stop();
            var memoryAfter = GC.GetTotalAllocatedBytes(precise: true);
            var gcGen0After = GC.CollectionCount(0);
            var gcGen1After = GC.CollectionCount(1);
            var gcGen2After = GC.CollectionCount(2);

            Console.WriteLine($"Done ({MeasureIterations} iterations)");
            Console.WriteLine();

            // Calculate GC stats
            var gcGen0 = gcGen0After - gcGen0Before;
            var gcGen1 = gcGen1After - gcGen1Before;
            var gcGen2 = gcGen2After - gcGen2Before;
            var totalMemory = memoryAfter - memoryBefore;

            PrintStatistics(measurements, totalStopwatch.Elapsed, gcGen0, gcGen1, gcGen2, totalMemory);
        }
        finally
        {
            await CleanupConnector(connector);
        }
    }

    private async Task RunRequestAsyncBatch(ClientConnector connector, int responseSize, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var request = new BenchmarkRequest
            {
                Sequence = i,
                ResponseSize = responseSize
            };
            await connector.RequestAsync(new ClientPacket(request));
        }
    }

    #endregion

    #region Scenario 2: Single Connection Send + OnReceive

    private async Task RunSingleConnectionSendReceive(int responseSize)
    {
        Console.WriteLine($"Scenario: 1 Connection × {MeasureIterations:N0} Messages (Send + OnReceive)");

        var connector = await CreateAndAuthenticateConnector();

        try
        {
            // Warmup
            Console.Write($"Warming up... ");
            await RunSendReceiveBatch(connector, responseSize, WarmupIterations);
            Console.WriteLine($"Done ({WarmupIterations} iterations)");

            // GC cleanup
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(500);

            // Measurement
            Console.Write($"Measuring... ");
            var measurements = new ConcurrentBag<MeasurementData>();
            var receivedCount = 0;
            var tcs = new TaskCompletionSource();
            var startTimes = new ConcurrentDictionary<int, long>();

            var gcGen0Before = GC.CollectionCount(0);
            var gcGen1Before = GC.CollectionCount(1);
            var gcGen2Before = GC.CollectionCount(2);
            var memoryBefore = GC.GetTotalAllocatedBytes(precise: true);

            connector.OnReceive += (stageId, packet) =>
            {
                var reply = BenchmarkReply.Parser.ParseFrom(packet.Payload.Data.Span);
                var seq = reply.Sequence;

                if (startTimes.TryRemove(seq, out var startTicks))
                {
                    var elapsed = Stopwatch.GetTimestamp() - startTicks;

                    measurements.Add(new MeasurementData
                    {
                        ElapsedTicks = elapsed,
                        MemoryAllocated = 0, // Cannot measure per-request memory accurately in async mode
                        PayloadSize = responseSize
                    });
                }

                if (Interlocked.Increment(ref receivedCount) >= MeasureIterations)
                {
                    tcs.TrySetResult();
                }
            };

            var totalStopwatch = Stopwatch.StartNew();

            for (int i = 0; i < MeasureIterations; i++)
            {
                var request = new BenchmarkRequest
                {
                    Sequence = i,
                    ResponseSize = responseSize
                };

                startTimes[i] = Stopwatch.GetTimestamp();
                connector.Send(new ClientPacket(request));
            }

            await tcs.Task;
            totalStopwatch.Stop();

            var memoryAfter = GC.GetTotalAllocatedBytes(precise: true);
            var gcGen0After = GC.CollectionCount(0);
            var gcGen1After = GC.CollectionCount(1);
            var gcGen2After = GC.CollectionCount(2);

            Console.WriteLine($"Done ({MeasureIterations} iterations)");
            Console.WriteLine();

            var gcGen0 = gcGen0After - gcGen0Before;
            var gcGen1 = gcGen1After - gcGen1Before;
            var gcGen2 = gcGen2After - gcGen2Before;
            var totalMemory = memoryAfter - memoryBefore;

            PrintStatistics(measurements.ToList(), totalStopwatch.Elapsed, gcGen0, gcGen1, gcGen2, totalMemory);
        }
        finally
        {
            await CleanupConnector(connector);
        }
    }

    private async Task RunSendReceiveBatch(ClientConnector connector, int responseSize, int count)
    {
        if (count <= 0)
            return;

        var receivedCount = 0;
        var tcs = new TaskCompletionSource();

        connector.OnReceive += (stageId, packet) =>
        {
            if (Interlocked.Increment(ref receivedCount) >= count)
            {
                tcs.TrySetResult();
            }
        };

        for (int i = 0; i < count; i++)
        {
            var request = new BenchmarkRequest
            {
                Sequence = i,
                ResponseSize = responseSize
            };
            connector.Send(new ClientPacket(request));
        }

        await tcs.Task;
    }

    #endregion

    #region Scenario 3: Multi Connection RequestAsync

    private async Task RunMultiConnectionRequestAsync(int responseSize)
    {
        Console.WriteLine($"Scenario: {MultiConnectionCount:N0} Connections × {MeasureIterations:N0} Messages (RequestAsync)");

        var connectors = new List<ClientConnector>();

        try
        {
            // Create and authenticate connectors
            Console.Write($"Creating {MultiConnectionCount} connections... ");
            for (int i = 0; i < MultiConnectionCount; i++)
            {
                var connector = await CreateAndAuthenticateConnector();
                connectors.Add(connector);
            }
            Console.WriteLine("Done");

            // Warmup
            Console.Write($"Warming up... ");
            var warmupTasks = connectors.Select(c => RunRequestAsyncBatch(c, responseSize, WarmupIterations / MultiConnectionCount));
            await Task.WhenAll(warmupTasks);
            Console.WriteLine($"Done ({WarmupIterations} iterations total)");

            // GC cleanup
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(500);

            // Measurement
            Console.Write($"Measuring... ");
            var allMeasurements = new List<MeasurementData>[MultiConnectionCount];
            for (int i = 0; i < MultiConnectionCount; i++)
            {
                allMeasurements[i] = new List<MeasurementData>();
            }

            var gcGen0Before = GC.CollectionCount(0);
            var gcGen1Before = GC.CollectionCount(1);
            var gcGen2Before = GC.CollectionCount(2);
            var memoryBefore = GC.GetTotalAllocatedBytes(precise: true);

            var totalStopwatch = Stopwatch.StartNew();

            var tasks = connectors.Select(async (connector, index) =>
            {
                var measurements = allMeasurements[index];
                var iterationsPerConnection = MeasureIterations / MultiConnectionCount;

                for (int i = 0; i < iterationsPerConnection; i++)
                {
                    var request = new BenchmarkRequest
                    {
                        Sequence = i,
                        ResponseSize = responseSize
                    };

                    var sw = Stopwatch.StartNew();
                    await connector.RequestAsync(new ClientPacket(request));
                    sw.Stop();

                    measurements.Add(new MeasurementData
                    {
                        ElapsedTicks = sw.ElapsedTicks,
                        MemoryAllocated = 0, // Cannot measure accurately with multi-connection
                        PayloadSize = responseSize
                    });
                }
            }).ToList();

            await Task.WhenAll(tasks);
            totalStopwatch.Stop();

            var memoryAfter = GC.GetTotalAllocatedBytes(precise: true);
            var gcGen0After = GC.CollectionCount(0);
            var gcGen1After = GC.CollectionCount(1);
            var gcGen2After = GC.CollectionCount(2);

            Console.WriteLine($"Done ({MeasureIterations} iterations total)");
            Console.WriteLine();

            var gcGen0 = gcGen0After - gcGen0Before;
            var gcGen1 = gcGen1After - gcGen1Before;
            var gcGen2 = gcGen2After - gcGen2Before;
            var totalMemory = memoryAfter - memoryBefore;

            var combinedMeasurements = allMeasurements.SelectMany(m => m).ToList();
            PrintStatistics(combinedMeasurements, totalStopwatch.Elapsed, gcGen0, gcGen1, gcGen2, totalMemory);
        }
        finally
        {
            foreach (var connector in connectors)
            {
                await CleanupConnector(connector);
            }
        }
    }

    #endregion

    #region Scenario 4: Multi Connection Send + OnReceive

    private async Task RunMultiConnectionSendReceive(int responseSize)
    {
        Console.WriteLine($"Scenario: {MultiConnectionCount:N0} Connections × {MeasureIterations:N0} Messages (Send + OnReceive)");

        var connectors = new List<ClientConnector>();

        try
        {
            // Create and authenticate connectors
            Console.Write($"Creating {MultiConnectionCount} connections... ");
            for (int i = 0; i < MultiConnectionCount; i++)
            {
                var connector = await CreateAndAuthenticateConnector();
                connectors.Add(connector);
            }
            Console.WriteLine("Done");

            // Warmup
            Console.Write($"Warming up... ");
            var warmupTasks = connectors.Select(c => RunSendReceiveBatch(c, responseSize, WarmupIterations / MultiConnectionCount));
            await Task.WhenAll(warmupTasks);
            Console.WriteLine($"Done ({WarmupIterations} iterations total)");

            // GC cleanup
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(500);

            // Measurement
            Console.Write($"Measuring... ");
            var allMeasurements = new ConcurrentBag<MeasurementData>[MultiConnectionCount];
            var receivedCounts = new int[MultiConnectionCount];
            var tcsList = new TaskCompletionSource[MultiConnectionCount];
            var startTimesList = new ConcurrentDictionary<int, long>[MultiConnectionCount];

            for (int i = 0; i < MultiConnectionCount; i++)
            {
                allMeasurements[i] = new ConcurrentBag<MeasurementData>();
                tcsList[i] = new TaskCompletionSource();
                startTimesList[i] = new ConcurrentDictionary<int, long>();
            }

            var gcGen0Before = GC.CollectionCount(0);
            var gcGen1Before = GC.CollectionCount(1);
            var gcGen2Before = GC.CollectionCount(2);
            var memoryBefore = GC.GetTotalAllocatedBytes(precise: true);

            var iterationsPerConnection = MeasureIterations / MultiConnectionCount;

            // Setup OnReceive handlers
            for (int connectorIndex = 0; connectorIndex < MultiConnectionCount; connectorIndex++)
            {
                var index = connectorIndex;
                var measurements = allMeasurements[index];
                var startTimes = startTimesList[index];
                var tcs = tcsList[index];

                connectors[index].OnReceive += (stageId, packet) =>
                {
                    var reply = BenchmarkReply.Parser.ParseFrom(packet.Payload.Data.Span);
                    var seq = reply.Sequence;

                    if (startTimes.TryRemove(seq, out var startTicks))
                    {
                        var elapsed = Stopwatch.GetTimestamp() - startTicks;

                        measurements.Add(new MeasurementData
                        {
                            ElapsedTicks = elapsed,
                            MemoryAllocated = 0,
                            PayloadSize = responseSize
                        });
                    }

                    if (Interlocked.Increment(ref receivedCounts[index]) >= iterationsPerConnection)
                    {
                        tcs.TrySetResult();
                    }
                };
            }

            var totalStopwatch = Stopwatch.StartNew();

            // Send requests
            for (int connectorIndex = 0; connectorIndex < MultiConnectionCount; connectorIndex++)
            {
                var connector = connectors[connectorIndex];
                var startTimes = startTimesList[connectorIndex];

                for (int i = 0; i < iterationsPerConnection; i++)
                {
                    var request = new BenchmarkRequest
                    {
                        Sequence = i,
                        ResponseSize = responseSize
                    };

                    startTimes[i] = Stopwatch.GetTimestamp();
                    connector.Send(new ClientPacket(request));
                }
            }

            // Wait for all responses
            await Task.WhenAll(tcsList.Select(t => t.Task));
            totalStopwatch.Stop();

            var memoryAfter = GC.GetTotalAllocatedBytes(precise: true);
            var gcGen0After = GC.CollectionCount(0);
            var gcGen1After = GC.CollectionCount(1);
            var gcGen2After = GC.CollectionCount(2);

            Console.WriteLine($"Done ({MeasureIterations} iterations total)");
            Console.WriteLine();

            var gcGen0 = gcGen0After - gcGen0Before;
            var gcGen1 = gcGen1After - gcGen1Before;
            var gcGen2 = gcGen2After - gcGen2Before;
            var totalMemory = memoryAfter - memoryBefore;

            var combinedMeasurements = allMeasurements.SelectMany(m => m).ToList();
            PrintStatistics(combinedMeasurements, totalStopwatch.Elapsed, gcGen0, gcGen1, gcGen2, totalMemory);
        }
        finally
        {
            foreach (var connector in connectors)
            {
                await CleanupConnector(connector);
            }
        }
    }

    #endregion

    #region Helper Methods

    private readonly Dictionary<ClientConnector, Timer> _connectorTimers = new();

    private async Task<ClientConnector> CreateAndAuthenticateConnector()
    {
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });

        var stageId = Random.Shared.NextInt64(1, long.MaxValue);
        await connector.ConnectAsync("127.0.0.1", 16110, stageId);

        // Start callback timer
        var timer = new Timer(_ => connector.MainThreadAction(), null, 0, 20);
        _connectorTimers[connector] = timer;

        await Task.Delay(100);

        // Authenticate
        var authRequest = new AuthenticateRequest { UserId = $"bench-user-{stageId}", Token = "token" };
        await connector.RequestAsync(new ClientPacket(authRequest));

        return connector;
    }

    private async Task CleanupConnector(ClientConnector connector)
    {
        if (connector == null)
            return;

        if (_connectorTimers.TryGetValue(connector, out var timer))
        {
            timer?.Dispose();
            _connectorTimers.Remove(connector);
        }

        connector.Disconnect();
        await Task.Delay(100);
    }

    private void PrintStatistics(List<MeasurementData> measurements, TimeSpan totalElapsed, int gcGen0, int gcGen1, int gcGen2, long totalMemory)
    {
        // Latency calculation (Stopwatch Ticks → ms)
        var latenciesMs = measurements
            .Select(m => (double)m.ElapsedTicks * 1000.0 / Stopwatch.Frequency)
            .OrderBy(x => x)
            .ToList();

        var meanLatency = latenciesMs.Average();
        var p50Latency = Percentile(latenciesMs, 0.50);
        var p95Latency = Percentile(latenciesMs, 0.95);
        var p99Latency = Percentile(latenciesMs, 0.99);
        var stdDevLatency = StandardDeviation(latenciesMs);

        // Throughput calculation
        var totalMessages = measurements.Count;
        var totalSeconds = totalElapsed.TotalSeconds;
        var messagesPerSecond = totalMessages / totalSeconds;
        var totalBytes = measurements.Sum(m => (long)m.PayloadSize);
        var bytesPerSecond = totalBytes / totalSeconds;

        // Memory calculation
        var avgMemoryPerRequest = totalMemory / (double)measurements.Count;

        // Output
        Console.WriteLine($"  Latency:");
        Console.WriteLine($"    Mean   : {meanLatency:F3} ms");
        Console.WriteLine($"    P50    : {p50Latency:F3} ms");
        Console.WriteLine($"    P95    : {p95Latency:F3} ms");
        Console.WriteLine($"    P99    : {p99Latency:F3} ms");
        Console.WriteLine($"    StdDev : {stdDevLatency:F3} ms");
        Console.WriteLine();

        Console.WriteLine($"  Throughput:");
        Console.WriteLine($"    Messages : {messagesPerSecond:F1} msg/s");
        Console.WriteLine($"    Data     : {FormatBytes((long)bytesPerSecond)}/s");
        Console.WriteLine();

        Console.WriteLine($"  Memory:");
        Console.WriteLine($"    Per Request : {FormatBytes((long)avgMemoryPerRequest)}");
        Console.WriteLine($"    Total       : {FormatBytes(totalMemory)}");
        Console.WriteLine();

        Console.WriteLine($"  GC:");
        Console.WriteLine($"    Gen0 : {gcGen0}");
        Console.WriteLine($"    Gen1 : {gcGen1}");
        Console.WriteLine($"    Gen2 : {gcGen2}");
    }

    private static double Percentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
            return 0;

        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        index = Math.Max(0, Math.Min(sortedValues.Count - 1, index));
        return sortedValues[index];
    }

    private static double StandardDeviation(List<double> values)
    {
        if (values.Count < 2)
            return 0;

        var mean = values.Average();
        var variance = values.Sum(v => Math.Pow(v - mean, 2)) / values.Count;
        return Math.Sqrt(variance);
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:F2} {sizes[order]}";
    }

    #endregion

    private class MeasurementData
    {
        public long ElapsedTicks { get; set; }
        public long MemoryAllocated { get; set; }
        public int PayloadSize { get; set; }
    }
}
