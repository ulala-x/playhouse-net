namespace PlayHouse.TestServer.Shared;

/// <summary>
/// PlayHouse 테스트 서버용 메시지 ID 상수 정의
/// </summary>
/// <remarks>
/// proto 파일에 정의된 메시지 타입과 1:1 매핑됩니다.
/// 클라이언트 커넥터에서 Packet.MsgId와 비교할 때 사용합니다.
/// </remarks>
public static class TestMessageIds
{
    // ============================================
    // 인증 관련
    // ============================================
    public const string AuthenticateRequest = nameof(AuthenticateRequest);
    public const string AuthenticateReply = nameof(AuthenticateReply);

    // ============================================
    // Stage 생성
    // ============================================
    public const string CreateStagePayload = nameof(CreateStagePayload);
    public const string CreateStageReply = nameof(CreateStageReply);

    // ============================================
    // Echo 테스트
    // ============================================
    public const string EchoRequest = nameof(EchoRequest);
    public const string EchoReply = nameof(EchoReply);

    // ============================================
    // 에러 테스트
    // ============================================
    public const string FailRequest = nameof(FailRequest);
    public const string FailReply = nameof(FailReply);

    // ============================================
    // 타임아웃 테스트
    // ============================================
    public const string NoResponseRequest = nameof(NoResponseRequest);

    // ============================================
    // 큰 페이로드 테스트
    // ============================================
    public const string LargePayloadRequest = nameof(LargePayloadRequest);
    public const string LargePayloadReply = nameof(LargePayloadReply);

    // ============================================
    // 브로드캐스트 테스트
    // ============================================
    public const string BroadcastRequest = nameof(BroadcastRequest);
    public const string BroadcastNotify = nameof(BroadcastNotify);

    // ============================================
    // 상태 조회
    // ============================================
    public const string StatusRequest = nameof(StatusRequest);
    public const string StatusReply = nameof(StatusReply);

    // ============================================
    // 채팅
    // ============================================
    public const string ChatMessage = nameof(ChatMessage);

    // ============================================
    // Stage 관리
    // ============================================
    public const string CloseStageRequest = nameof(CloseStageRequest);
    public const string CloseStageReply = nameof(CloseStageReply);
    public const string ActorLeftNotify = nameof(ActorLeftNotify);
    public const string ConnectionChangedNotify = nameof(ConnectionChangedNotify);

    // ============================================
    // API Server 관련
    // ============================================
    public const string ApiEchoRequest = nameof(ApiEchoRequest);
    public const string ApiEchoReply = nameof(ApiEchoReply);

    // ============================================
    // IActorSender 테스트
    // ============================================
    public const string GetAccountIdRequest = nameof(GetAccountIdRequest);
    public const string GetAccountIdReply = nameof(GetAccountIdReply);
    public const string LeaveStageRequest = nameof(LeaveStageRequest);
    public const string LeaveStageReply = nameof(LeaveStageReply);

    // ============================================
    // Timer 테스트
    // ============================================
    public const string StartRepeatTimerRequest = nameof(StartRepeatTimerRequest);
    public const string StartCountTimerRequest = nameof(StartCountTimerRequest);
    public const string TimerTickNotify = nameof(TimerTickNotify);
    public const string StartTimerReply = nameof(StartTimerReply);

    // ============================================
    // Benchmark
    // ============================================
    public const string BenchmarkRequest = nameof(BenchmarkRequest);
    public const string BenchmarkReply = nameof(BenchmarkReply);
}

/// <summary>
/// 테스트용 에러 코드
/// </summary>
public static class TestErrorCodes
{
    /// <summary>
    /// 테스트용 일반 에러
    /// </summary>
    public const int GeneralError = 1000;

    /// <summary>
    /// 테스트용 인증 실패
    /// </summary>
    public const int AuthenticationFailed = 1001;

    /// <summary>
    /// 테스트용 권한 없음
    /// </summary>
    public const int PermissionDenied = 1002;

    /// <summary>
    /// 테스트용 리소스 없음
    /// </summary>
    public const int ResourceNotFound = 1003;

    /// <summary>
    /// 테스트용 타임아웃
    /// </summary>
    public const int Timeout = 1004;

    /// <summary>
    /// 테스트용 잘못된 요청
    /// </summary>
    public const int InvalidRequest = 1005;
}

/// <summary>
/// Stage 타입 상수
/// </summary>
public static class StageTypes
{
    /// <summary>
    /// 테스트용 기본 Stage
    /// </summary>
    public const string TestStage = "TestStage";

    /// <summary>
    /// Echo 테스트용 Stage
    /// </summary>
    public const string EchoStage = "EchoStage";

    /// <summary>
    /// 브로드캐스트 테스트용 Stage
    /// </summary>
    public const string BroadcastStage = "BroadcastStage";

    /// <summary>
    /// 벤치마크 테스트용 Stage
    /// </summary>
    public const string BenchmarkStage = "BenchmarkStage";
}
