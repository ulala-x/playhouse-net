using PlayHouse.E2E.Verifiers;

namespace PlayHouse.E2E;

public class VerificationRunner
{
    private readonly ServerContext _serverContext;
    private readonly List<VerifierBase> _verifiers = new();

    public VerificationRunner(ServerContext serverContext)
    {
        _serverContext = serverContext;
        RegisterVerifiers();
    }

    private void RegisterVerifiers()
    {
        // Phase 0: Protocol Connection Tests (프로토콜별 연결 테스트)
        if (Environment.GetEnvironmentVariable("ENABLE_PROTOCOL_TESTS") == "1")
        {
            _verifiers.Add(new ProtocolConnectionVerifier(_serverContext));
        }

        // Phase 3-1: Basic Connector Verifiers (27 tests)
        _verifiers.Add(new ConnectionVerifier(_serverContext));
        _verifiers.Add(new MessagingVerifier(_serverContext));
        _verifiers.Add(new PushVerifier(_serverContext));
        _verifiers.Add(new PacketAutoDisposeVerifier(_serverContext));
        _verifiers.Add(new ServerLifecycleVerifier(_serverContext));

        // Phase 3-2: Actor/Stage Callbacks Verifiers (12 tests)
        _verifiers.Add(new ActorCallbackVerifier(_serverContext));
        _verifiers.Add(new ActorSenderVerifier(_serverContext));
        _verifiers.Add(new StageCallbackVerifier(_serverContext));

        // Phase 3-3: Server Communication Verifiers (24 tests)
        _verifiers.Add(new StageToStageVerifier(_serverContext));
        _verifiers.Add(new StageToApiVerifier(_serverContext));
        _verifiers.Add(new ApiToApiVerifier(_serverContext));
        _verifiers.Add(new ApiToPlayVerifier(_serverContext));
        _verifiers.Add(new SelfConnectionVerifier(_serverContext));
        _verifiers.Add(new ServiceRoutingVerifier(_serverContext));
        _verifiers.Add(new SystemMessageVerifier(_serverContext));

        // Phase 2: IStageSender Features (4 + 4 tests)
        _verifiers.Add(new AsyncBlockVerifier(_serverContext));
        _verifiers.Add(new TimerVerifier(_serverContext));
        _verifiers.Add(new GameLoopVerifier(_serverContext));

        // Phase 3-2: Connector Callback Performance (2 tests)
        _verifiers.Add(new ConnectorCallbackPerformanceVerifier(_serverContext));

        // Phase 3: DI Integration & Performance (5 tests)
        if (Environment.GetEnvironmentVariable("ENABLE_DI_TESTS") == "1")
        {
            _verifiers.Add(new DIIntegrationVerifier(_serverContext));
        }
    }

    public async Task<VerificationResult> RunAllAsync(bool verbose = false)
    {
        var allResults = new List<TestResult>();

        foreach (var verifier in _verifiers)
        {
            if (verbose)
                Console.WriteLine($"[{verifier.CategoryName}] Running tests...");

            var categoryResult = await verifier.RunAllTestsAsync();
            allResults.AddRange(categoryResult.Tests);

            if (verbose)
            {
                var passed = categoryResult.Tests.Count(t => t.Passed);
                var failed = categoryResult.Tests.Count(t => !t.Passed);
                Console.WriteLine($"[{verifier.CategoryName}] {passed} passed, {failed} failed");
            }
        }

        return new VerificationResult
        {
            Tests = allResults,
            TotalTests = allResults.Count,
            PassedCount = allResults.Count(t => t.Passed),
            FailedCount = allResults.Count(t => !t.Passed)
        };
    }

    public async Task<VerificationResult> RunCategoryAsync(string categoryName)
    {
        var verifier = _verifiers.FirstOrDefault(v => v.CategoryName == categoryName);
        if (verifier == null)
            throw new ArgumentException($"Category not found: {categoryName}");

        var categoryResult = await verifier.RunAllTestsAsync();

        return new VerificationResult
        {
            Tests = categoryResult.Tests,
            TotalTests = categoryResult.Tests.Count,
            PassedCount = categoryResult.Tests.Count(t => t.Passed),
            FailedCount = categoryResult.Tests.Count(t => !t.Passed)
        };
    }

    public List<CategoryInfo> GetCategories()
    {
        return _verifiers.Select(v => new CategoryInfo
        {
            Name = v.CategoryName,
            TestCount = v.GetTestCount()
        }).ToList();
    }

    public string GetCategory(int index)
    {
        return _verifiers[index].CategoryName;
    }
}

public record VerificationResult
{
    public required List<TestResult> Tests { get; init; }
    public required int TotalTests { get; init; }
    public required int PassedCount { get; init; }
    public required int FailedCount { get; init; }
}

public record CategoryInfo
{
    public required string Name { get; init; }
    public required int TestCount { get; init; }
}
