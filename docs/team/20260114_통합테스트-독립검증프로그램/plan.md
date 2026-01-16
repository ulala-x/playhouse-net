# 통합 테스트를 독립 검증 프로그램으로 전환 계획

## 개요

현재 PlayHouse.Tests.Integration의 통합 테스트(2,100+ 줄, 15개 파일)를 zlink 스타일의 독립 실행 프로그램으로 전환합니다. 각 검증 프로그램은 실행 가능한 예제이자 CI/CD 자동 검증 도구 역할을 수행합니다.

## 목표

1. **기능 검증**: 각 프로그램이 특정 기능의 정상 동작을 검증
2. **살아있는 문서**: 사용자가 직접 실행하며 PlayHouse 사용법을 배움
3. **CI/CD 통합**: `--ci` 옵션으로 자동화된 검증 및 성공/실패 exit code 반환
4. **테스트 안정성**: 독립 프로세스로 실행되어 테스트 간섭 제거

## 설계 원칙 (zlink 패턴 적용)

### 1. 계층화된 유틸리티 구조

```
각 검증 프로그램 (verify_*.cs)
    ↓ includes
VerifyUtil (공통 검증 로직)
    ↓ depends on
TestStageImpl, TestActorImpl (기존 인프라 재사용)
    ↓ links to
PlayHouse.Connector, PlayHouse
```

zlink의 `testutil.hpp → testutil_unity.hpp → test_*.cpp` 패턴을 .NET으로 적용합니다.

### 2. 중앙 집중식 빌드 (sln 파일 + csproj)

- 모든 검증 프로그램을 `playhouse-net.sln`에 등록
- 조건부 컴파일 없이 모든 프로그램 항상 빌드
- `dotnet build`로 일괄 빌드, `dotnet test` 대신 각 프로그램을 개별 실행

### 3. 공유 인프라 라이브러리

**PlayHouse.Verification.Shared** (새 프로젝트):
- VerifyUtil: 어서션, 서버 시작/종료, 타이머 기반 콜백 폴링
- TestStageImpl, TestActorImpl: 기존 Integration Tests에서 이동
- test_messages.proto: 기존 테스트 메시지 재사용

## 디렉토리 구조

```
tests/
├── PlayHouse.Tests.Unit/              # 유지 (단위 테스트)
├── PlayHouse.Tests.Integration/       # 삭제 예정 (단계적으로)
├── benchmark_cs/                      # 유지 (성능 벤치마크)
├── benchmark_ss/                      # 유지 (서버간 벤치마크)
└── verification/                      # 신규 디렉토리
    ├── PlayHouse.Verification.Shared/         # 공통 인프라
    │   ├── PlayHouse.Verification.Shared.csproj
    │   ├── VerifyUtil.cs                      # 공통 유틸리티
    │   ├── Infrastructure/
    │   │   ├── TestStageImpl.cs               # 기존에서 이동
    │   │   ├── TestActorImpl.cs
    │   │   ├── TestApiController.cs
    │   │   └── TestSystemController.cs
    │   └── Proto/
    │       └── test_messages.proto            # 기존에서 이동
    │
    ├── PlayHouse.Verify.Connection/           # 연결/인증 검증
    │   ├── PlayHouse.Verify.Connection.csproj
    │   ├── Program.cs
    │   └── README.md
    │
    ├── PlayHouse.Verify.Messaging/            # Request/Send 검증
    │   ├── PlayHouse.Verify.Messaging.csproj
    │   ├── Program.cs
    │   └── README.md
    │
    ├── PlayHouse.Verify.Push/                 # OnReceive/Push 검증
    ├── PlayHouse.Verify.ActorCallback/        # OnAuthenticate 등
    ├── PlayHouse.Verify.ActorSender/          # AccountId, LeaveStage
    ├── PlayHouse.Verify.StageCallback/        # OnDispatch, OnJoinStage
    ├── PlayHouse.Verify.StageToStage/         # Stage간 통신
    ├── PlayHouse.Verify.StageToApi/           # Stage → API 통신
    ├── PlayHouse.Verify.ApiToApi/             # API 서버간 통신
    ├── PlayHouse.Verify.ApiToPlay/            # API → PlayServer 통신
    ├── PlayHouse.Verify.AsyncBlock/           # AsyncBlock 동작
    ├── PlayHouse.Verify.Timer/                # 타이머 (반복/카운트)
    ├── PlayHouse.Verify.AutoDispose/          # 패킷 자동 Dispose
    ├── PlayHouse.Verify.ServerLifecycle/      # 서버 시작/종료
    └── PlayHouse.Verify.CallbackPerformance/  # MainThreadAction 성능
```

총 15개 검증 프로그램 (기존 통합 테스트 15개 파일 → 1:1 매핑)

## 개별 검증 프로그램 구조

### Program.cs 템플릿 (zlink 패턴 적용)

```csharp
using System.CommandLine;
using PlayHouse.Verification.Shared;

// CLI 옵션 (zlink처럼 간소화, 벤치마크보다 단순)
var ciOption = new Option<bool>(
    name: "--ci",
    description: "CI/CD mode: minimal output, exit code 0/1",
    getDefaultValue: () => false);

var verboseOption = new Option<bool>(
    name: "--verbose",
    description: "Verbose output with detailed logs",
    getDefaultValue: () => false);

var timeoutOption = new Option<int>(
    name: "--timeout",
    description: "Test timeout in seconds",
    getDefaultValue: () => 60);

var rootCommand = new RootCommand("PlayHouse Connection Verification")
{
    ciOption,
    verboseOption,
    timeoutOption
};

rootCommand.SetHandler(async (ci, verbose, timeout) =>
{
    // 1. Setup (zlink의 setup_test_environment 패턴)
    var verifier = new ConnectionVerifier(ci, verbose, timeout);

    try
    {
        // 2. Run tests (zlink의 RUN_TEST 매크로 패턴)
        await verifier.RunAllTests();

        // 3. Report results
        if (ci)
        {
            // CI 모드: 간결한 TAP 출력
            Console.WriteLine(verifier.GetTapReport());
        }
        else
        {
            // 수동 모드: 상세한 출력
            verifier.PrintDetailedReport();
        }

        Environment.Exit(verifier.AllPassed ? 0 : 1);
    }
    catch (Exception ex)
    {
        if (ci)
        {
            Console.WriteLine($"FATAL: {ex.Message}");
        }
        else
        {
            Console.WriteLine($"FATAL ERROR: {ex}");
        }
        Environment.Exit(1);
    }
}, ciOption, verboseOption, timeoutOption);

await rootCommand.InvokeAsync(args);
```

### Verifier 클래스 패턴 (zlink의 testutil_unity 패턴)

```csharp
public class ConnectionVerifier : VerifierBase
{
    // zlink의 SETUP_TEARDOWN_TESTCONTEXT 패턴
    private PlayServer _server = null!;
    private ClientConnector _connector = null!;

    public ConnectionVerifier(bool ci, bool verbose, int timeout)
        : base(ci, verbose, timeout)
    {
    }

    public async Task RunAllTests()
    {
        // zlink처럼 각 테스트 함수 실행
        await RunTest("Connect and Disconnect", Test_ConnectDisconnect);
        await RunTest("Authentication Flow", Test_Authentication);
        await RunTest("IsConnected State", Test_IsConnectedState);
        await RunTest("OnConnect Callback", Test_OnConnectCallback);
        await RunTest("Connection Timeout", Test_ConnectionTimeout);
    }

    private async Task Test_ConnectDisconnect()
    {
        // Setup (zlink의 bind_loopback 패턴)
        _server = VerifyUtil.CreateTestServer(16110, 16100);
        await _server.StartAsync();

        _connector = new ClientConnector();
        _connector.Init(new ConnectorConfig
        {
            ServerAddress = "127.0.0.1",
            ServerPort = 16110
        });

        // Test (zlink의 bounce 패턴)
        var connected = await _connector.ConnectAsync();
        Assert(connected, "Failed to connect");

        var isConnected = _connector.IsConnected();
        Assert(isConnected, "IsConnected() should return true");

        _connector.Disconnect();
        await Task.Delay(100); // SETTLE_TIME

        isConnected = _connector.IsConnected();
        Assert(!isConnected, "IsConnected() should return false after disconnect");

        // Teardown (zlink의 test_context_socket_close 패턴)
        await _server.StopAsync();
        _connector.Dispose();
    }

    // ... 다른 테스트 함수들
}
```

### VerifyUtil.cs (공통 유틸리티, zlink의 testutil.hpp 역할)

```csharp
public static class VerifyUtil
{
    // zlink의 SETTLE_TIME 상수
    public const int SettleTimeMs = 300;

    // zlink의 bind_loopback_ipv4 패턴
    public static PlayServer CreateTestServer(int tcpPort, int zmqPort)
    {
        var services = new ServiceCollection();
        services.AddPlayServer(options =>
        {
            options.ServiceType = ServiceType.Play;
            options.ServerId = $"verify-{Guid.NewGuid():N}";
            options.BindEndpoint = $"tcp://0.0.0.0:{zmqPort}";
            options.TcpPort = tcpPort;
            options.AuthenticateMessageId = "AuthenticateRequest";
            options.DefaultStageType = "TestStage";
        })
        .UseStage<TestStageImpl, TestActorImpl>("TestStage")
        .UseSystemController<TestSystemController>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<PlayServer>();
    }

    // zlink의 s_recv, s_send_seq 패턴
    public static async Task<TResponse?> SendAndReceive<TRequest, TResponse>(
        ClientConnector connector, TRequest request, int timeoutMs = 5000)
        where TRequest : IMessage
        where TResponse : IMessage, new()
    {
        using var packet = new Packet(request);
        var response = await connector.RequestAsync(packet, timeoutMs);
        return response != null ? response.ParsePayload<TResponse>() : default;
    }

    // zlink의 expect_monitor_event 패턴
    public static void AssertCallbackInvoked(
        string callbackName,
        Func<bool> checkInvoked,
        int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (checkInvoked())
                return;
            Thread.Sleep(50); // 폴링 간격
        }
        throw new VerificationException($"{callbackName} was not invoked within {timeoutMs}ms");
    }
}
```

### VerifierBase.cs (추상 기본 클래스, zlink의 Unity 프레임워크 패턴)

```csharp
public abstract class VerifierBase
{
    private readonly List<TestResult> _results = new();
    private readonly bool _ciMode;
    private readonly bool _verbose;
    private readonly int _timeoutSeconds;

    protected VerifierBase(bool ci, bool verbose, int timeout)
    {
        _ciMode = ci;
        _verbose = verbose;
        _timeoutSeconds = timeout;
    }

    // zlink의 RUN_TEST 매크로
    protected async Task RunTest(string name, Func<Task> testFunc)
    {
        if (!_ciMode)
            Console.WriteLine($"Running: {name}");

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(_timeoutSeconds * 1000);
            await testFunc();

            _results.Add(new TestResult
            {
                Name = name,
                Passed = true,
                Duration = sw.Elapsed
            });

            if (!_ciMode)
                Console.WriteLine($"  PASS ({sw.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            _results.Add(new TestResult
            {
                Name = name,
                Passed = false,
                Duration = sw.Elapsed,
                Error = ex.Message
            });

            if (!_ciMode)
                Console.WriteLine($"  FAIL: {ex.Message}");
        }
    }

    // zlink의 UNITY_END 패턴
    public bool AllPassed => _results.All(r => r.Passed);

    // TAP (Test Anything Protocol) 출력 (CI용)
    public string GetTapReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"1..{_results.Count}");

        for (int i = 0; i < _results.Count; i++)
        {
            var r = _results[i];
            var status = r.Passed ? "ok" : "not ok";
            sb.AppendLine($"{status} {i + 1} - {r.Name}");
            if (!r.Passed)
                sb.AppendLine($"  # {r.Error}");
        }

        return sb.ToString();
    }

    // 상세 리포트 (수동 실행용)
    public void PrintDetailedReport()
    {
        Console.WriteLine("\n========================================");
        Console.WriteLine("Verification Results");
        Console.WriteLine("========================================");

        foreach (var r in _results)
        {
            var status = r.Passed ? "✓" : "✗";
            Console.WriteLine($"{status} {r.Name} ({r.Duration.TotalMilliseconds:F0}ms)");
            if (!r.Passed)
                Console.WriteLine($"    Error: {r.Error}");
        }

        var passed = _results.Count(r => r.Passed);
        var total = _results.Count;
        Console.WriteLine($"\n{passed}/{total} tests passed");

        if (!AllPassed)
            Console.WriteLine("\nSome tests FAILED");
    }

    // zlink의 TEST_ASSERT_* 매크로들
    protected void Assert(bool condition, string message)
    {
        if (!condition)
            throw new VerificationException(message);
    }

    protected void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new VerificationException(
                $"{message}: expected '{expected}', got '{actual}'");
    }
}

public class VerificationException : Exception
{
    public VerificationException(string message) : base(message) { }
}

public record TestResult
{
    public required string Name { get; init; }
    public required bool Passed { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? Error { get; init; }
}
```

## CI/CD 통합

### GitHub Actions 워크플로우

```yaml
# .github/workflows/verification.yml
name: Verification Programs

on: [push, pull_request]

jobs:
  verify:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        program:
          - Connection
          - Messaging
          - Push
          - ActorCallback
          - ActorSender
          - StageCallback
          - StageToStage
          - StageToApi
          - ApiToApi
          - ApiToPlay
          - AsyncBlock
          - Timer
          - AutoDispose
          - ServerLifecycle
          - CallbackPerformance
      fail-fast: false  # 한 프로그램 실패해도 나머지 계속 실행

    steps:
      - uses: actions/checkout@v3

      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'

      - name: Build
        run: dotnet build -c Release

      - name: Run Verification - ${{ matrix.program }}
        run: |
          dotnet run --project tests/verification/PlayHouse.Verify.${{ matrix.program }} \
            --configuration Release \
            --no-build \
            -- --ci
        timeout-minutes: 3
```

### 로컬 실행 스크립트

```bash
#!/bin/bash
# scripts/run-all-verifications.sh

# zlink의 ctest 패턴

echo "Building verification programs..."
dotnet build -c Release

echo ""
echo "Running verification programs..."

programs=(
  "Connection"
  "Messaging"
  "Push"
  "ActorCallback"
  "ActorSender"
  "StageCallback"
  "StageToStage"
  "StageToApi"
  "ApiToApi"
  "ApiToPlay"
  "AsyncBlock"
  "Timer"
  "AutoDispose"
  "ServerLifecycle"
  "CallbackPerformance"
)

passed=0
failed=0

for prog in "${programs[@]}"; do
  echo ""
  echo "================================"
  echo "Running: $prog"
  echo "================================"

  if dotnet run --project "tests/verification/PlayHouse.Verify.$prog" \
      --configuration Release --no-build -- --ci; then
    ((passed++))
  else
    ((failed++))
  fi
done

echo ""
echo "================================"
echo "Summary: $passed passed, $failed failed"
echo "================================"

exit $failed
```

## 단계적 마이그레이션 계획

### Phase 1: 인프라 구축 (1-2일)

1. `tests/verification/PlayHouse.Verification.Shared/` 프로젝트 생성
2. 기존 TestStageImpl, TestActorImpl, test_messages.proto 이동
3. VerifyUtil, VerifierBase 클래스 구현
4. playhouse-net.sln에 프로젝트 추가

### Phase 2: 첫 번째 검증 프로그램 (1일)

1. `PlayHouse.Verify.Connection` 프로그램 구현 (가장 간단함)
2. ConnectionTests.cs의 4개 테스트를 Program.cs로 전환
3. CI/CD 워크플로우 추가
4. 로컬 실행 스크립트 작성 및 테스트

### Phase 3: 나머지 검증 프로그램 (5-7일)

1. 우선순위에 따라 순차적으로 전환:
   - Messaging → Push → ActorCallback → StageCallback (핵심 기능)
   - StageToStage → StageToApi → ApiToApi → ApiToPlay (서버간 통신)
   - Timer → AsyncBlock → AutoDispose (고급 기능)
   - ServerLifecycle → CallbackPerformance (인프라)

2. 각 프로그램마다:
   - 기존 xUnit 테스트를 Verifier 클래스로 변환
   - README.md 작성 (사용 예제)
   - 수동 실행 및 CI 모드 테스트

### Phase 4: 기존 통합 테스트 제거 (1일)

1. 모든 검증 프로그램이 CI에서 통과 확인
2. `PlayHouse.Tests.Integration/` 디렉토리 삭제
3. playhouse-net.sln에서 Integration 프로젝트 제거
4. 문서 업데이트

## 검증 항목별 상세 계획

### 1. PlayHouse.Verify.Connection (ConnectionTests.cs 기반)

**테스트 케이스** (4개):
- `Test_ConnectDisconnect`: 기본 연결/해제
- `Test_Authentication`: 인증 흐름 (AuthenticateRequest → AuthenticateReply)
- `Test_IsConnectedState`: IsConnected() 상태 변화
- `Test_OnConnectCallback`: OnConnect 콜백 호출 검증

**검증 방식**:
- 응답 검증: `await connector.RequestAsync()`
- 콜백 검증: `connector.OnConnect += () => { callbackInvoked = true; }`
- 상태 검증: `connector.IsConnected()`

**Proto 메시지**: AuthenticateRequest, AuthenticateReply

### 2. PlayHouse.Verify.Messaging (MessagingTests.cs 기반)

**테스트 케이스** (6개):
- `Test_RequestAsync`: 요청-응답 패턴
- `Test_Send`: Fire-and-forget 전송
- `Test_MultipleRequests`: 동시 다중 요청
- `Test_Timeout`: 타임아웃 처리
- `Test_ErrorHandling`: 에러 응답 처리
- `Test_LargePayload`: 큰 페이로드 전송

**검증 방식**:
- RequestAsync 응답 확인
- Send → OnReceive 콜백 확인
- 타임아웃 예외 확인

**Proto 메시지**: EchoRequest, EchoReply, ErrorRequest, ErrorReply

### 3. PlayHouse.Verify.Push (PushTests.cs 기반)

**테스트 케이스** (3개):
- `Test_ServerPush`: 서버 → 클라이언트 Push
- `Test_OnReceiveCallback`: OnReceive 콜백 검증
- `Test_MultiplePushMessages`: 다중 Push 순서 보장

**검증 방식**:
- `connector.OnReceive += (msgId, payload) => { ... }`
- TestStageImpl에서 SendToClient() 호출
- 클라이언트 OnReceive 콜백에서 수신 확인

**Proto 메시지**: StatusRequest, StatusNotify

### 4. PlayHouse.Verify.ActorCallback (ActorCallbackTests.cs 기반)

**테스트 케이스** (3개):
- `Test_OnAuthenticate`: OnAuthenticate 콜백
- `Test_OnPostAuthenticate`: OnPostAuthenticate 콜백
- `Test_AuthenticationFlow`: 전체 인증 흐름

**검증 방식**:
- 응답 검증: AuthenticateReply.Success == true
- 콜백 검증: TestActorImpl.AuthenticatedAccountIds 확인
- 상태 검증: connector.IsAuthenticated()

**Proto 메시지**: AuthenticateRequest, AuthenticateReply

### 5. PlayHouse.Verify.StageToStage (StageToStageTests.cs 기반)

**테스트 케이스** (4개):
- `Test_SendToStage`: 단방향 Stage 메시지
- `Test_RequestToStage`: Stage 요청-응답
- `Test_MultiStageChain`: 다단계 Stage 체인
- `Test_RequestCallback`: RequestToStageCallback 검증

**검증 방식**:
- DualPlayServerFixture 패턴 적용 (2개 서버)
- TestStageImpl.InterStageReceivedMsgIds 확인
- RequestToStageCallback 카운트 검증

**Proto 메시지**: InterStageMessage, InterStageReply

### 6-15. 나머지 검증 프로그램

각 프로그램마다 유사한 패턴:
1. 기존 xUnit 테스트 파일의 테스트 메서드들을 `Test_*` 함수로 변환
2. Fixture 설정을 Verifier 클래스의 Setup으로 이동
3. FluentAssertions를 VerifierBase.Assert로 대체
4. 콜백 검증 로직 유지 (TestStageImpl, TestActorImpl 활용)

## 검증 프로그램 vs 기존 테스트 비교

| 측면 | 기존 Integration Tests | 검증 프로그램 |
|------|----------------------|-------------|
| **실행 방식** | xUnit (dotnet test) | 독립 실행 (dotnet run) |
| **테스트 격리** | Fixture 공유, Static 필드 | 완전 독립 프로세스 |
| **CI/CD** | xUnit 리포터 | TAP 출력 + exit code |
| **사용자 경험** | 개발자 전용 | 실행 가능한 예제 |
| **콜백 처리** | Timer 폴링 (20ms) | 동일 패턴 유지 |
| **어서션** | FluentAssertions | 커스텀 Assert |
| **출력** | xUnit 포맷 | 수동: 상세, CI: TAP |
| **병렬 실행** | xUnit 자동 | GitHub Actions matrix |
| **타임아웃** | xUnit timeout | 프로그램 자체 제어 |

## 핵심 파일 경로

### 새로 생성할 파일

```
tests/verification/PlayHouse.Verification.Shared/
  ├── PlayHouse.Verification.Shared.csproj
  ├── VerifyUtil.cs
  ├── VerifierBase.cs
  ├── Infrastructure/
  │   ├── TestStageImpl.cs          # 이동
  │   ├── TestActorImpl.cs          # 이동
  │   ├── TestApiController.cs      # 이동
  │   └── TestSystemController.cs   # 이동
  └── Proto/
      └── test_messages.proto       # 이동

tests/verification/PlayHouse.Verify.Connection/
  ├── PlayHouse.Verify.Connection.csproj
  ├── Program.cs
  ├── ConnectionVerifier.cs
  └── README.md

tests/verification/PlayHouse.Verify.Messaging/
  ├── PlayHouse.Verify.Messaging.csproj
  ├── Program.cs
  ├── MessagingVerifier.cs
  └── README.md

... (13개 더)
```

### 수정할 파일

- `playhouse-net.sln`: 16개 프로젝트 추가 (Shared + 15개 검증)
- `.github/workflows/verification.yml`: 새 워크플로우
- `scripts/run-all-verifications.sh`: 로컬 실행 스크립트

### 삭제 예정 파일

- `tests/PlayHouse.Tests.Integration/` 전체 디렉토리

## 마이그레이션 체크리스트

### Phase 1: 인프라 (Day 1-2)
- [ ] `tests/verification/` 디렉토리 생성
- [ ] `PlayHouse.Verification.Shared.csproj` 생성
- [ ] TestStageImpl.cs, TestActorImpl.cs 이동
- [ ] test_messages.proto 이동 및 빌드 설정
- [ ] VerifyUtil.cs 구현 (CreateTestServer, SendAndReceive, AssertCallbackInvoked)
- [ ] VerifierBase.cs 구현 (RunTest, Assert, 리포트 생성)
- [ ] playhouse-net.sln에 Shared 프로젝트 추가
- [ ] 빌드 성공 확인

### Phase 2: 첫 번째 검증 프로그램 (Day 3)
- [ ] `PlayHouse.Verify.Connection/` 프로젝트 생성
- [ ] Program.cs 구현 (CLI 옵션)
- [ ] ConnectionVerifier.cs 구현 (4개 테스트)
- [ ] README.md 작성
- [ ] 수동 실행 테스트 (`--verbose`)
- [ ] CI 모드 테스트 (`--ci`)
- [ ] GitHub Actions 워크플로우 작성
- [ ] CI에서 성공 확인

### Phase 3: 나머지 검증 프로그램 (Day 4-10)

**핵심 기능 (Day 4-5)**:
- [ ] PlayHouse.Verify.Messaging (6개 테스트)
- [ ] PlayHouse.Verify.Push (3개 테스트)
- [ ] PlayHouse.Verify.ActorCallback (3개 테스트)
- [ ] PlayHouse.Verify.StageCallback (3개 테스트)

**서버간 통신 (Day 6-7)**:
- [ ] PlayHouse.Verify.StageToStage (4개 테스트)
- [ ] PlayHouse.Verify.StageToApi (3개 테스트)
- [ ] PlayHouse.Verify.ApiToApi (3개 테스트)
- [ ] PlayHouse.Verify.ApiToPlay (3개 테스트)

**고급 기능 (Day 8-9)**:
- [ ] PlayHouse.Verify.Timer (4개 테스트)
- [ ] PlayHouse.Verify.AsyncBlock (4개 테스트)
- [ ] PlayHouse.Verify.AutoDispose (3개 테스트)

**인프라 (Day 10)**:
- [ ] PlayHouse.Verify.ActorSender (3개 테스트)
- [ ] PlayHouse.Verify.ServerLifecycle (2개 테스트)
- [ ] PlayHouse.Verify.CallbackPerformance (2개 테스트)

### Phase 4: 정리 및 문서화 (Day 11)
- [ ] 모든 검증 프로그램 CI 통과 확인
- [ ] `scripts/run-all-verifications.sh` 작성 및 테스트
- [ ] 로컬에서 전체 실행 확인
- [ ] `tests/PlayHouse.Tests.Integration/` 디렉토리 삭제
- [ ] playhouse-net.sln에서 Integration 프로젝트 제거
- [ ] README.md 업데이트 (새 테스트 구조 문서화)
- [ ] CLAUDE.md 업데이트 (검증 프로그램 규칙 추가)

## 예상 효과

1. **테스트 안정성 향상**: 독립 프로세스로 테스트 간섭 제거
2. **실행 속도**: 병렬 실행 가능 (GitHub Actions matrix)
3. **사용자 경험**: 실행 가능한 예제로 학습 곡선 감소
4. **유지보수**: 각 검증 프로그램이 명확한 책임 (단일 기능)
5. **CI/CD**: TAP 출력으로 표준화된 리포트

## 위험 요소 및 완화 방안

### 위험 1: 콜백 타이밍 이슈
- **문제**: Timer 기반 폴링(20ms)이 CI 환경에서 불안정
- **완화**: 타임아웃을 충분히 크게 설정 (5초), SETTLE_TIME 도입

### 위험 2: 포트 충돌
- **문제**: 여러 검증 프로그램 동시 실행 시 포트 충돌
- **완화**: 각 프로그램이 고유 포트 범위 사용 (16110, 16210, 16310 등)

### 위험 3: 마이그레이션 비용
- **문제**: 15개 프로그램 전환에 시간 소요
- **완화**: 단계적 마이그레이션, 첫 번째 프로그램으로 패턴 확립 후 복제

### 위험 4: 기존 테스트 커버리지 손실
- **문제**: 전환 과정에서 일부 테스트 누락
- **완화**: 기존 xUnit 테스트와 검증 프로그램을 병행 실행하며 검증

## 참고 자료

- zlink 테스트 구조: `/home/ulalax/project/ulalax/zlink/tests/`
- 기존 벤치마크: `tests/benchmark_cs/PlayHouse.Benchmark.Server/Program.cs`
- Unity 프레임워크: zlink의 testutil_unity.hpp 패턴
- TAP (Test Anything Protocol): https://testanything.org/
