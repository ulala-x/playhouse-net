using System.Text;
using PlayHouse.Core.Api.Bootstrap;
using PlayHouse.Core.Play.Bootstrap;
using PlayHouse.Core.Shared;
using PlayHouse.E2E.Shared.Proto;
using PlayHouse.E2E.Shared.Utils;

namespace PlayHouse.E2E;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var config = ParseArguments(args);

        // 🔥 서버/클라이언트 한 번만 시작
        var serverContext = await StartServersAsync();

        try
        {
            if (config.InteractiveMode)
                return await RunInteractiveMode(serverContext);
            else if (config.Category != null)
                return await RunSelectedCategories(config, serverContext);
            else
                return await RunAllTests(config, serverContext);
        }
        finally
        {
            // 🔥 프로그램 종료 시 한 번만 정리
            await StopServersAsync(serverContext);
        }
    }

    static async Task<ServerContext> StartServersAsync()
    {
        Console.WriteLine("[서버 시작 중...]");

        // 환경 변수로 DI 모드 활성화
        var enableDI = Environment.GetEnvironmentVariable("ENABLE_DI_TESTS") == "1";
        var enableProtocolTests = Environment.GetEnvironmentVariable("ENABLE_PROTOCOL_TESTS") == "1";

        // ZMQ 포트 동적 할당 (CI 환경 충돌 방지)
        var zmqPortOffset = int.Parse(Environment.GetEnvironmentVariable("ZMQ_PORT_OFFSET") ?? "0");
        var zmqPlayPort = 15000 + zmqPortOffset;
        var zmqApi1Port = 15300 + zmqPortOffset;
        var zmqApi2Port = 15301 + zmqPortOffset;
        var zmqDIPort = 15100 + zmqPortOffset;
        var zmqTlsPort = 15200 + zmqPortOffset;
        var zmqWsPort = 15400 + zmqPortOffset;

        // 1. PlayServer (TCP 동적, ZMQ 동적)
        var playServer = await ServerFactory.CreatePlayServerAsync(
            serverId: "play-1",
            tcpPort: 0,
            zmqPort: zmqPlayPort
        );
        var actualTcpPort = playServer.ActualTcpPort;
        Console.WriteLine($"✓ PlayServer started on ZMQ:{zmqPlayPort}, TCP:{actualTcpPort}");

        // 2. ApiServer 2개 (서버간 통신 테스트용)
        var (apiServer1, httpApp, httpPort) = await ServerFactory.CreateApiServerWithHttpAsync(
            serverId: "api-1",
            zmqPort: zmqApi1Port,
            playServerId: "play-1"
        );
        var apiServer2 = await ServerFactory.CreateApiServerAsync(
            serverId: "api-2",
            zmqPort: zmqApi2Port
        );
        Console.WriteLine($"✓ ApiServer-1 started on ZMQ:{zmqApi1Port}, HTTP:{httpPort}");
        Console.WriteLine($"✓ ApiServer-2 started on ZMQ:{zmqApi2Port}");

        // 🔥 ApiServer 양방향 연결 대기 (초기 안정화 시간 + 빠른 헬스체크)
        await Task.Delay(3000); // 초기 안정화 시간 (ZMQ 연결 완료 대기)
        await WaitForApiServerConnectionAsync(apiServer1, apiServer2);

        // 3. DI PlayServer (조건부)
        PlayServer? diPlayServer = null;
        IServiceProvider? diServiceProvider = null;
        int diTcpPort = 0;

        if (enableDI)
        {
            (diPlayServer, diServiceProvider) = await ServerFactory.CreateDIPlayServerAsync(
                serverId: "di-1",
                tcpPort: 0,
                zmqPort: zmqDIPort
            );
            diTcpPort = diPlayServer.ActualTcpPort;
            Console.WriteLine($"✓ DI PlayServer started on ZMQ:{zmqDIPort}, TCP:{diTcpPort}");

            // DI 서버 추가 후 ApiServer 연결 재확인
            await Task.Delay(2000);
            await WaitForApiServerConnectionAsync(apiServer1, apiServer2);
        }

        // 4. 프로토콜 테스트용 서버 (조건부)
        PlayServer? tlsPlayServer = null;
        int tcpTlsPort = 0;
        PlayServer? wsPlayServer = null;
        Microsoft.AspNetCore.Builder.WebApplication? wsHttpApp = null;
        int wsPort = 0;

        if (enableProtocolTests)
        {
            // TCP + TLS 서버
            tlsPlayServer = await ServerFactory.CreatePlayServerWithTlsAsync(
                serverId: "tls-1",
                tcpPort: 0,
                zmqPort: zmqTlsPort
            );
            tcpTlsPort = tlsPlayServer.ActualTcpPort;
            Console.WriteLine($"✓ TLS PlayServer started on ZMQ:{zmqTlsPort}, TCP+TLS:{tcpTlsPort}");

            // WebSocket 서버
            (wsPlayServer, wsHttpApp, wsPort) = await ServerFactory.CreatePlayServerWithWebSocketAsync(
                serverId: "ws-1",
                zmqPort: zmqWsPort
            );
            Console.WriteLine($"✓ WebSocket PlayServer started on ZMQ:{zmqWsPort}, HTTP:{wsPort}");
        }

        // 5. 클라이언트 생성 (한 번만!)
        var connector = new PlayHouse.Connector.Connector();
        connector.Init(new PlayHouse.Connector.ConnectorConfig
        {
            RequestTimeoutMs = 30000
        });
        Console.WriteLine($"✓ Client connector initialized\n");

        return new ServerContext
        {
            PlayServer = playServer,
            ApiServer1 = apiServer1,
            ApiServer2 = apiServer2,
            HttpApp = httpApp,
            Connector = connector,
            TcpPort = actualTcpPort,
            ApiServer1HttpPort = httpPort,
            PlayServerId = "play-1",
            ApiServer1Id = "api-1",
            ApiServer2Id = "api-2",
            DIPlayServer = diPlayServer,
            DIServiceProvider = diServiceProvider,
            DITcpPort = diTcpPort,
            // 프로토콜 테스트용
            TcpTlsPort = tcpTlsPort,
            WebSocketPort = wsPort,
            TlsPlayServer = tlsPlayServer,
            WebSocketPlayServer = wsPlayServer,
            WebSocketHttpApp = wsHttpApp
        };
    }

    static async Task WaitForApiServerConnectionAsync(ApiServer s1, ApiServer s2)
    {
        // ApiServer 양방향 연결 헬스체크 (최대 2초, 빠른 재시도)
        const int maxAttempts = 10; // 10회 * 200ms = 2초
        bool s1ToS2 = false, s2ToS1 = false;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (!s1ToS2)
            {
                try
                {
                    var req = new ApiEchoRequest { Content = "HealthCheck" };
                    var res = await s1.ApiSender!.RequestToApi("api-2", CPacket.Of(req));
                    if (!res.MsgId.StartsWith("Error:"))
                    {
                        s1ToS2 = true;
                    }
                }
                catch { }
            }

            if (!s2ToS1)
            {
                try
                {
                    var req = new ApiEchoRequest { Content = "HealthCheck" };
                    var res = await s2.ApiSender!.RequestToApi("api-1", CPacket.Of(req));
                    if (!res.MsgId.StartsWith("Error:"))
                    {
                        s2ToS1 = true;
                    }
                }
                catch { }
            }

            if (s1ToS2 && s2ToS1)
            {
                Console.WriteLine("[Program] ✓ ApiServer 양방향 연결 완료\n");
                return;
            }

            await Task.Delay(200); // 빠른 재시도
        }

        throw new TimeoutException("ApiServer 양방향 연결 실패");
    }

    static async Task StopServersAsync(ServerContext ctx)
    {
        Console.WriteLine("\n[서버 종료 중...]");
        if (ctx.Connector != null) await ctx.Connector.DisposeAsync();
        if (ctx.HttpApp != null) await ctx.HttpApp.StopAsync();
        if (ctx.DIPlayServer != null) await ctx.DIPlayServer.DisposeAsync();
        if (ctx.PlayServer != null) await ctx.PlayServer.DisposeAsync();
        if (ctx.ApiServer1 != null) await ctx.ApiServer1.DisposeAsync();
        if (ctx.ApiServer2 != null) await ctx.ApiServer2.DisposeAsync();
        // 프로토콜 테스트용 서버 정리
        if (ctx.WebSocketHttpApp != null) await ctx.WebSocketHttpApp.StopAsync();
        if (ctx.TlsPlayServer != null) await ctx.TlsPlayServer.DisposeAsync();
        if (ctx.WebSocketPlayServer != null) await ctx.WebSocketPlayServer.DisposeAsync();
        Console.WriteLine("✓ All servers stopped");
    }

    static async Task<int> RunInteractiveMode(ServerContext ctx)
    {
        var runner = new VerificationRunner(ctx);

        while (true)
        {
            Console.Clear();
            PrintMenu(runner);

            var input = Console.ReadLine();
            if (!int.TryParse(input, out var choice))
                continue;

            if (choice == 0) break;

            if (choice == 1)
            {
                var result = await runner.RunAllAsync(verbose: true);
                PrintResults(result);
            }
            else if (choice >= 2)
            {
                var categories = runner.GetCategories();
                var categoryIndex = choice - 2;
                if (categoryIndex < 0 || categoryIndex >= categories.Count)
                {
                    Console.WriteLine("Invalid option");
                    Console.WriteLine("\nPress any key to continue...");
                    Console.ReadKey();
                    continue;
                }
                var category = categories[categoryIndex].Name;
                var result = await runner.RunCategoryAsync(category);
                PrintResults(result);
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        return 0;
    }

    static async Task<int> RunAllTests(Config config, ServerContext ctx)
    {
        var runner = new VerificationRunner(ctx);
        var result = await runner.RunAllAsync(verbose: true);

        // 콘솔 출력
        PrintResults(result);

        // CI 모드면 TAP 출력도 생성
        if (config.TapOutput)
        {
            var tapOutput = new StringBuilder();
            tapOutput.AppendLine($"1..{result.TotalTests}");
            for (int i = 0; i < result.Tests.Count; i++)
            {
                var test = result.Tests[i];
                if (test.Passed)
                {
                    tapOutput.AppendLine($"ok {i + 1} - {test.CategoryName}: {test.TestName}");
                }
                else
                {
                    tapOutput.AppendLine($"not ok {i + 1} - {test.CategoryName}: {test.TestName}");
                    tapOutput.AppendLine($"  # {test.Error}");
                }
            }

            tapOutput.AppendLine($"# {result.PassedCount} tests passed, {result.FailedCount} failed");

            Console.WriteLine("\n" + tapOutput.ToString());

            // TAP 파일 저장
            var tapFile = Path.Combine(Directory.GetCurrentDirectory(), "verification-results.tap");
            await File.WriteAllTextAsync(tapFile, tapOutput.ToString());
        }

        // Exit code: 실패 있으면 1
        return result.FailedCount > 0 ? 1 : 0;
    }

    static async Task<int> RunSelectedCategories(Config config, ServerContext ctx)
    {
        var runner = new VerificationRunner(ctx);
        var result = await runner.RunCategoryAsync(config.Category!);

        // 콘솔 출력
        PrintResults(result);

        // TAP 출력 옵션
        if (config.TapOutput)
        {
            var tapOutput = new StringBuilder();
            tapOutput.AppendLine($"1..{result.TotalTests}");
            for (int i = 0; i < result.Tests.Count; i++)
            {
                var test = result.Tests[i];
                if (test.Passed)
                {
                    tapOutput.AppendLine($"ok {i + 1} - {test.CategoryName}: {test.TestName}");
                }
                else
                {
                    tapOutput.AppendLine($"not ok {i + 1} - {test.CategoryName}: {test.TestName}");
                    tapOutput.AppendLine($"  # {test.Error}");
                }
            }

            tapOutput.AppendLine($"# {result.PassedCount} tests passed, {result.FailedCount} failed");

            Console.WriteLine("\n" + tapOutput.ToString());
        }

        // Exit code: 실패 있으면 1
        return result.FailedCount > 0 ? 1 : 0;
    }

    static void PrintMenu(VerificationRunner runner)
    {
        var categories = runner.GetCategories();
        var totalTests = categories.Sum(c => c.TestCount);

        Console.WriteLine("========================================");
        Console.WriteLine("PlayHouse Verification Program");
        Console.WriteLine("========================================");
        Console.WriteLine($"1. Run All Tests ({totalTests} tests)");

        int index = 2;
        foreach (var category in categories)
        {
            Console.WriteLine($"{index}. {category.Name} ({category.TestCount} tests)");
            index++;
        }

        Console.WriteLine("0. Exit");
        Console.WriteLine("========================================");
        Console.Write("Select option: ");
    }

    static void PrintResults(VerificationResult result)
    {
        Console.WriteLine("\n========================================");
        Console.WriteLine("Test Results");
        Console.WriteLine("========================================");

        foreach (var test in result.Tests)
        {
            var status = test.Passed ? "✓" : "✗";
            Console.WriteLine($"{status} {test.CategoryName}: {test.TestName} ({test.Duration.TotalMilliseconds:F0}ms)");
            if (!test.Passed)
            {
                Console.WriteLine($"  Error: {test.Error}");
            }
        }

        Console.WriteLine("========================================");
        Console.WriteLine($"Total: {result.TotalTests}, Passed: {result.PassedCount}, Failed: {result.FailedCount}");
    }

    static Config ParseArguments(string[] args)
    {
        bool interactiveMode = false;
        bool tapOutput = false;
        string? category = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--interactive":
                case "-i":
                    interactiveMode = true;
                    break;
                case "--tap":
                    tapOutput = true;
                    break;
                case "--category":
                    if (i + 1 < args.Length)
                        category = args[++i];
                    break;
            }
        }

        return new Config { InteractiveMode = interactiveMode, TapOutput = tapOutput, Category = category };
    }
}

record Config
{
    public bool InteractiveMode { get; init; }
    public bool TapOutput { get; init; }
    public string? Category { get; init; }
}
