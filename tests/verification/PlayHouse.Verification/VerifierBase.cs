using System.Diagnostics;
using PlayHouse.Core.Api.Bootstrap;
using PlayHouse.Core.Play.Bootstrap;
using PlayHouse.Verification.Shared.Utils;

namespace PlayHouse.Verification;

/// <summary>
/// 모든 Verifier의 기본 클래스
/// </summary>
public abstract class VerifierBase
{
    private readonly List<TestResult> _results = new();

    // ServerContext로 이미 구동 중인 서버/클라이언트 접근
    protected ServerContext ServerContext { get; }
    protected PlayHouse.Connector.Connector Connector => ServerContext.Connector;
    protected PlayServer PlayServer => ServerContext.PlayServer;
    protected ApiServer ApiServer1 => ServerContext.ApiServer1;
    protected ApiServer ApiServer2 => ServerContext.ApiServer2;

    protected AssertHelper Assert { get; } = new();

    public abstract string CategoryName { get; }

    protected VerifierBase(ServerContext serverContext)
    {
        ServerContext = serverContext;
    }

    /// <summary>
    /// 고유한 UserId 생성 (테스트 격리)
    /// </summary>
    protected string GenerateUniqueUserId(string prefix)
    {
        return $"{prefix}_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// 고유한 StageId 생성 (테스트 격리)
    /// </summary>
    protected long GenerateUniqueStageId(int baseOffset = 0)
    {
        return baseOffset + DateTimeOffset.UtcNow.Ticks % 100000;
    }

    public async Task<CategoryResult> RunAllTestsAsync()
    {
        _results.Clear();

        await SetupAsync();

        try
        {
            await RunTestsAsync();
        }
        finally
        {
            await TeardownAsync();
        }

        return new CategoryResult
        {
            CategoryName = CategoryName,
            Tests = _results.ToList()
        };
    }

    /// <summary>
    /// 각 Verifier가 오버라이드하여 테스트 실행
    /// </summary>
    protected abstract Task RunTestsAsync();

    /// <summary>
    /// 각 Verifier가 필요시 오버라이드
    /// 서버 시작 금지! 클라이언트 상태 초기화만
    /// </summary>
    protected virtual Task SetupAsync() => Task.CompletedTask;

    /// <summary>
    /// 각 Verifier가 필요시 오버라이드
    /// 서버 종료 금지! 클라이언트 상태 정리만
    /// </summary>
    protected virtual Task TeardownAsync() => Task.CompletedTask;

    /// <summary>
    /// 테스트 실행 (예외 처리 포함)
    /// 실패해도 멈추지 않고 계속 실행
    /// </summary>
    protected async Task RunTest(string testName, Func<Task> testFunc, int timeoutMs = 60000)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var testTask = testFunc();
            var timeoutTask = Task.Delay(timeoutMs, cts.Token);
            var completedTask = await Task.WhenAny(testTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException($"Test exceeded timeout of {timeoutMs}ms");
            }

            await testTask; // 실제 예외 전파

            _results.Add(new TestResult
            {
                CategoryName = CategoryName,
                TestName = testName,
                Passed = true,
                Duration = sw.Elapsed
            });
        }
        catch (Exception ex)
        {
            _results.Add(new TestResult
            {
                CategoryName = CategoryName,
                TestName = testName,
                Passed = false,
                Duration = sw.Elapsed,
                Error = ex.Message,
                StackTrace = ex.StackTrace
            });
            // throw 안 함! 다음 테스트 계속 실행
        }
    }

    public abstract int GetTestCount();
}

public record CategoryResult
{
    public required string CategoryName { get; init; }
    public required List<TestResult> Tests { get; init; }
}

public record TestResult
{
    public required string CategoryName { get; init; }
    public required string TestName { get; init; }
    public required bool Passed { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? Error { get; init; }
    public string? StackTrace { get; init; }
}
