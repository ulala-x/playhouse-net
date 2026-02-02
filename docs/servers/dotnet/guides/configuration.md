# 설정 및 옵션

PlayHouse의 서버와 클라이언트 Connector는 다양한 설정 옵션을 제공합니다. 이 가이드에서는 각 설정 항목과 사용 방법을 설명합니다.

## 1. PlayServerOption

Play Server의 설정을 정의합니다.

### 1.1 기본 설정

```csharp
builder.UsePlayHouse<PlayServer>(options =>
{
    // 서버 타입 (고정값)
    options.ServerType = ServerType.Play;

    // 서비스 그룹 ID (같은 타입의 서버를 그룹화)
    options.ServiceId = 1;

    // 서버 인스턴스 고유 ID
    options.ServerId = "play-1";

    // ZMQ 서버간 통신 바인드 주소
    options.BindEndpoint = "tcp://0.0.0.0:5000";

    // 요청 타임아웃 (밀리초)
    options.RequestTimeoutMs = 30000;
});
```

### 1.2 인증 및 Stage 설정

```csharp
builder.UsePlayHouse<PlayServer>(options =>
{
    // 인증 메시지 ID (필수)
    // 클라이언트는 인증 전에 이 메시지만 보낼 수 있음
    options.AuthenticateMessageId = "AuthRequest";

    // 인증 성공 후 자동으로 생성/참가할 기본 Stage 타입
    options.DefaultStageType = "LobbyStage";
});
```

### 1.3 Transport 설정 (TCP)

```csharp
builder.UsePlayHouse<PlayServer>(options =>
{
    // TCP 포트 (null이면 비활성화, 0이면 자동 할당)
    options.TcpPort = 6000;

    // TCP 바인드 주소 (기본값: 모든 인터페이스)
    options.TcpBindAddress = "0.0.0.0";

    // TCP TLS 인증서 (null이면 TLS 비활성화)
    options.TcpTlsCertificate = LoadCertificate("server.pfx", "password");
});
```

### 1.4 Transport 설정 (WebSocket)

```csharp
builder.UsePlayHouse<PlayServer>(options =>
{
    // WebSocket 경로 (null 또는 빈 문자열이면 비활성화)
    options.WebSocketPath = "/ws";

    // WebSocket TLS 인증서
    options.WebSocketTlsCertificate = LoadCertificate("server.pfx", "password");
});
```

### 1.5 TransportOptions (고급)

```csharp
builder.UsePlayHouse<PlayServer>(options =>
{
    options.TransportOptions = new TransportOptions
    {
        // 수신 버퍼 크기 (기본값: 64 KB)
        ReceiveBufferSize = 64 * 1024,

        // 송신 버퍼 크기 (기본값: 64 KB)
        SendBufferSize = 64 * 1024,

        // 최대 패킷 크기 (기본값: 2 MB)
        // 이 크기를 초과하는 패킷은 거부됨
        MaxPacketSize = 2 * 1024 * 1024,

        // 하트비트 타임아웃 (기본값: 90초)
        // 이 시간 동안 데이터가 수신되지 않으면 연결 종료
        HeartbeatTimeout = TimeSpan.FromSeconds(90),

        // Writer 일시 정지 임계값 (기본값: 64 KB)
        PauseWriterThreshold = 64 * 1024,

        // Writer 재개 임계값 (기본값: 32 KB)
        ResumeWriterThreshold = 32 * 1024,

        // TCP Keep-Alive 활성화 (기본값: true)
        EnableKeepAlive = true,

        // Keep-Alive 시간 (초, 기본값: 60)
        KeepAliveTime = 60,

        // Keep-Alive 간격 (초, 기본값: 1)
        KeepAliveInterval = 1
    };
});
```

### 1.6 Task Pool 설정

```csharp
builder.UsePlayHouse<PlayServer>(options =>
{
    // 워커 Task 풀 최소 크기 (기본값: 100)
    options.MinTaskPoolSize = 100;

    // 워커 Task 풀 최대 크기 (기본값: 1000)
    options.MaxTaskPoolSize = 1000;
});
```

### 1.7 MessagePool 설정

메시지 전용 메모리 풀 설정으로 GC 압박을 줄입니다.

```csharp
builder.UsePlayHouse<PlayServer>(options =>
{
    options.MessagePool = new MessagePoolConfig
    {
        // 각 버킷별 최대 수용량
        MaxTinyCount = 100000,   // ~256 bytes
        MaxSmallCount = 20000,   // ~4 KB
        MaxMediumCount = 5000,   // ~64 KB
        MaxLargeCount = 500,     // ~1 MB
        MaxHugeCount = 100,      // ~2 MB

        // 서버 시작 시 사전 할당할 개수 (웜업)
        TinyWarmUpCount = 20000,
        SmallWarmUpCount = 5000,
        MediumWarmUpCount = 500,
        LargeWarmUpCount = 10,

        // L1 캐시 용량 (스레드 로컬)
        MaxL1Capacity = 64,

        // 자동 축소 설정
        EnableAutoTrim = true,
        TrimCheckInterval = TimeSpan.FromSeconds(30),
        IdleThreshold = TimeSpan.FromSeconds(60)
    };
});
```

## 2. ApiServerOption

API Server의 설정을 정의합니다.

### 2.1 기본 설정

```csharp
builder.UsePlayHouse<ApiServer>(options =>
{
    // 서버 타입 (고정값)
    options.ServerType = ServerType.Api;

    // 서비스 그룹 ID (같은 타입의 서버를 그룹화)
    options.ServiceId = 1;

    // 서버 인스턴스 고유 ID
    options.ServerId = "api-1";

    // ZMQ 서버간 통신 바인드 주소
    options.BindEndpoint = "tcp://0.0.0.0:5100";

    // 요청 타임아웃 (밀리초)
    options.RequestTimeoutMs = 30000;

    // Task Pool 설정
    options.MinTaskPoolSize = 100;
    options.MaxTaskPoolSize = 1000;
});
```

## 3. ConnectorConfig

클라이언트 Connector의 설정을 정의합니다.

### 3.1 기본 설정

```csharp
var config = new ConnectorConfig
{
    // WebSocket 사용 여부 (false면 TCP)
    UseWebsocket = false,

    // SSL/TLS 사용 여부
    UseSsl = false,

    // 서버 인증서 검증 건너뛰기 (개발/테스트용)
    // 주의: 프로덕션에서는 false로 설정
    SkipServerCertificateValidation = false,

    // WebSocket 경로 (UseWebsocket = true일 때만 사용)
    WebSocketPath = "/ws",

    // 연결 유휴 타임아웃 (밀리초, 기본값: 30000)
    ConnectionIdleTimeoutMs = 30000,

    // 하트비트 주기 (밀리초, 기본값: 10000)
    HeartBeatIntervalMs = 10000,

    // 하트비트 타임아웃 (밀리초, 기본값: 30000)
    // 마지막 메시지 수신 후 이 시간이 지나면 OnDisconnect 발생
    // 0이면 비활성화
    HeartbeatTimeoutMs = 30000,

    // 요청 타임아웃 (밀리초, 기본값: 30000)
    RequestTimeoutMs = 30000,

    // 응답 시간 로깅 활성화
    EnableLoggingResponseTime = false,

    // 추적 로깅 활성화
    TurnOnTrace = false
};

var connector = new ClientConnector();
connector.Init(config);
```

## 4. 환경별 설정 예제

### 4.1 개발 환경 (로컬)

```csharp
// appsettings.Development.json
{
  "PlayHouse": {
    "ServerId": "play-local-1",
    "BindEndpoint": "tcp://127.0.0.1:5000",
    "TcpPort": 6000,
    "RequestTimeoutMs": 30000,
    "EnableLogging": true
  }
}

// Program.cs
builder.UsePlayHouse<PlayServer>(options =>
{
    var config = builder.Configuration.GetSection("PlayHouse");

    options.ServerId = config["ServerId"]!;
    options.BindEndpoint = config["BindEndpoint"]!;
    options.TcpPort = config.GetValue<int>("TcpPort");
    options.RequestTimeoutMs = config.GetValue<int>("RequestTimeoutMs");

    // 개발 환경: 인증서 없이 TCP만 사용
    options.TcpTlsCertificate = null;
    options.WebSocketPath = null;
});
```

### 4.2 스테이징 환경

```csharp
// appsettings.Staging.json
{
  "PlayHouse": {
    "ServerId": "play-staging-1",
    "BindEndpoint": "tcp://0.0.0.0:5000",
    "TcpPort": 6000,
    "WebSocketPath": "/ws",
    "RequestTimeoutMs": 30000,
    "CertificatePath": "/certs/staging.pfx",
    "CertificatePassword": "staging-password"
  }
}

// Program.cs
builder.UsePlayHouse<PlayServer>(options =>
{
    var config = builder.Configuration.GetSection("PlayHouse");

    options.ServerId = config["ServerId"]!;
    options.BindEndpoint = config["BindEndpoint"]!;
    options.TcpPort = config.GetValue<int>("TcpPort");
    options.WebSocketPath = config["WebSocketPath"];

    // TLS 인증서 로드
    var certPath = config["CertificatePath"];
    var certPassword = config["CertificatePassword"];
    if (!string.IsNullOrEmpty(certPath))
    {
        var certificate = new X509Certificate2(certPath, certPassword);
        options.TcpTlsCertificate = certificate;
        options.WebSocketTlsCertificate = certificate;
    }
});
```

### 4.3 프로덕션 환경

```csharp
// appsettings.Production.json
{
  "PlayHouse": {
    "ServerType": "Play",
    "ServiceId": 1,
    "ServerId": "play-prod-1",
    "BindEndpoint": "tcp://0.0.0.0:5000",
    "TcpPort": 6000,
    "WebSocketPath": "/ws",
    "RequestTimeoutMs": 30000,
    "MinTaskPoolSize": 200,
    "MaxTaskPoolSize": 2000,
    "CertificatePath": "/certs/production.pfx",
    "MessagePool": {
      "MaxTinyCount": 200000,
      "MaxSmallCount": 40000,
      "TinyWarmUpCount": 40000,
      "SmallWarmUpCount": 10000
    }
  }
}

// Program.cs
builder.UsePlayHouse<PlayServer>(options =>
{
    var config = builder.Configuration.GetSection("PlayHouse");

    options.ServiceId = config.GetValue<ushort>("ServiceId");
    options.ServerId = config["ServerId"]!;
    options.BindEndpoint = config["BindEndpoint"]!;
    options.TcpPort = config.GetValue<int>("TcpPort");
    options.WebSocketPath = config["WebSocketPath"];
    options.RequestTimeoutMs = config.GetValue<int>("RequestTimeoutMs");
    options.MinTaskPoolSize = config.GetValue<int>("MinTaskPoolSize");
    options.MaxTaskPoolSize = config.GetValue<int>("MaxTaskPoolSize");

    // 프로덕션 인증서
    var certPath = config["CertificatePath"];
    var certPassword = Environment.GetEnvironmentVariable("CERT_PASSWORD");
    var certificate = new X509Certificate2(certPath, certPassword);
    options.TcpTlsCertificate = certificate;
    options.WebSocketTlsCertificate = certificate;

    // MessagePool 설정
    var poolConfig = config.GetSection("MessagePool");
    options.MessagePool = new MessagePoolConfig
    {
        MaxTinyCount = poolConfig.GetValue<int>("MaxTinyCount"),
        MaxSmallCount = poolConfig.GetValue<int>("MaxSmallCount"),
        TinyWarmUpCount = poolConfig.GetValue<int>("TinyWarmUpCount"),
        SmallWarmUpCount = poolConfig.GetValue<int>("SmallWarmUpCount")
    };
});
```

### 4.4 다중 서버 구성 (도커/쿠버네티스)

```yaml
# docker-compose.yml
version: '3.8'
services:
  play-1:
    image: myapp/playserver:latest
    environment:
      - PlayHouse__ServerId=play-1
      - PlayHouse__BindEndpoint=tcp://0.0.0.0:5000
      - PlayHouse__ServiceId=1
    ports:
      - "6000:6000"

  play-2:
    image: myapp/playserver:latest
    environment:
      - PlayHouse__ServerId=play-2
      - PlayHouse__BindEndpoint=tcp://0.0.0.0:5000
      - PlayHouse__ServiceId=1
    ports:
      - "6001:6000"

  api-1:
    image: myapp/apiserver:latest
    environment:
      - PlayHouse__ServerId=api-1
      - PlayHouse__BindEndpoint=tcp://0.0.0.0:5100
      - PlayHouse__ServiceId=100
    ports:
      - "5100:5100"
```

```csharp
// Program.cs (환경변수 우선)
builder.UsePlayHouse<PlayServer>(options =>
{
    options.ServerId = Environment.GetEnvironmentVariable("PlayHouse__ServerId")
                       ?? builder.Configuration["PlayHouse:ServerId"]!;

    options.BindEndpoint = Environment.GetEnvironmentVariable("PlayHouse__BindEndpoint")
                           ?? builder.Configuration["PlayHouse:BindEndpoint"]!;

    options.ServiceId = ushort.Parse(
        Environment.GetEnvironmentVariable("PlayHouse__ServiceId")
        ?? builder.Configuration["PlayHouse:ServiceId"]!
    );

    // ... 기타 설정
});
```

## 5. 서비스별 권장 설정

### 5.1 로비/매치메이킹 서비스

```csharp
// API Server: 매치메이킹 전담
builder.UsePlayHouse<ApiServer>(options =>
{
    options.ServerType = ServerType.Api;
    options.ServiceId = 100; // 매치메이킹 서비스
    options.ServerId = "matchmaking-1";
    options.BindEndpoint = "tcp://0.0.0.0:5100";

    // 높은 동시성 처리
    options.MinTaskPoolSize = 200;
    options.MaxTaskPoolSize = 2000;
});

// Play Server: 로비
builder.UsePlayHouse<PlayServer>(options =>
{
    options.ServerType = ServerType.Play;
    options.ServiceId = 1;
    options.ServerId = "lobby-1";
    options.BindEndpoint = "tcp://0.0.0.0:5000";

    options.AuthenticateMessageId = "AuthRequest";
    options.DefaultStageType = "LobbyStage";

    // WebSocket 지원 (웹 클라이언트용)
    options.WebSocketPath = "/ws";
    options.TcpPort = 6000;
});
```

### 5.2 게임 플레이 서비스

```csharp
builder.UsePlayHouse<PlayServer>(options =>
{
    options.ServerType = ServerType.Play;
    options.ServiceId = 2; // 게임 서비스
    options.ServerId = "game-1";
    options.BindEndpoint = "tcp://0.0.0.0:5002";

    options.AuthenticateMessageId = "AuthRequest";

    // TCP만 사용 (네이티브 클라이언트)
    options.TcpPort = 6002;
    options.WebSocketPath = null;

    // 높은 처리량을 위한 메모리 풀 튜닝
    options.MessagePool = new MessagePoolConfig
    {
        TinyWarmUpCount = 40000,  // 많은 소형 메시지
        SmallWarmUpCount = 10000,
        MediumWarmUpCount = 1000
    };

    // TransportOptions 튜닝
    options.TransportOptions = new TransportOptions
    {
        MaxPacketSize = 1 * 1024 * 1024, // 1 MB (게임 상태 동기화)
        HeartbeatTimeout = TimeSpan.FromSeconds(30) // 짧은 타임아웃
    };
});
```

### 5.3 리더보드/통계 서비스

```csharp
builder.UsePlayHouse<ApiServer>(options =>
{
    options.ServerType = ServerType.Api;
    options.ServiceId = 200; // 리더보드 서비스
    options.ServerId = "leaderboard-1";
    options.BindEndpoint = "tcp://0.0.0.0:5200";

    // DB I/O가 많으므로 Task Pool 크게
    options.MinTaskPoolSize = 100;
    options.MaxTaskPoolSize = 1000;

    // 긴 타임아웃 (DB 쿼리 고려)
    options.RequestTimeoutMs = 60000; // 60초
});
```

## 6. 클라이언트 설정

### 6.1 Unity 클라이언트 (WebSocket)

```csharp
var config = new ConnectorConfig
{
    UseWebsocket = true,
    UseSsl = true, // wss://
    WebSocketPath = "/ws",
    SkipServerCertificateValidation = false,
    HeartBeatIntervalMs = 10000,
    HeartbeatTimeoutMs = 30000,
    RequestTimeoutMs = 30000
};

var connector = new ClientConnector();
connector.Init(config);

// 연결
await connector.ConnectAsync("game.example.com", 443);
```

### 6.2 네이티브 클라이언트 (TCP)

```csharp
var config = new ConnectorConfig
{
    UseWebsocket = false,
    UseSsl = true, // TLS
    SkipServerCertificateValidation = false,
    HeartBeatIntervalMs = 5000,  // 더 자주 하트비트
    HeartbeatTimeoutMs = 15000,
    RequestTimeoutMs = 20000
};

var connector = new ClientConnector();
connector.Init(config);

// 연결
await connector.ConnectAsync("game.example.com", 6000);
```

### 6.3 개발/테스트 클라이언트

```csharp
var config = new ConnectorConfig
{
    UseWebsocket = false,
    UseSsl = false,
    SkipServerCertificateValidation = true, // 개발용
    HeartBeatIntervalMs = 10000,
    RequestTimeoutMs = 60000, // 디버깅용 긴 타임아웃
    EnableLoggingResponseTime = true, // 로깅 활성화
    TurnOnTrace = true // 상세 로그
};

var connector = new ClientConnector();
connector.Init(config);

// 로컬 서버 연결
await connector.ConnectAsync("127.0.0.1", 6000);
```

## 7. 성능 튜닝 가이드

### 7.1 고부하 환경 (10K+ CCU)

```csharp
builder.UsePlayHouse<PlayServer>(options =>
{
    // Task Pool 확대
    options.MinTaskPoolSize = 500;
    options.MaxTaskPoolSize = 5000;

    // MessagePool 대폭 증가
    options.MessagePool = new MessagePoolConfig
    {
        MaxTinyCount = 500000,
        MaxSmallCount = 100000,
        MaxMediumCount = 20000,
        MaxLargeCount = 2000,

        TinyWarmUpCount = 100000,
        SmallWarmUpCount = 20000,
        MediumWarmUpCount = 2000,
        LargeWarmUpCount = 50,

        EnableAutoTrim = true,
        TrimCheckInterval = TimeSpan.FromMinutes(5)
    };

    // Transport 최적화
    options.TransportOptions = new TransportOptions
    {
        ReceiveBufferSize = 128 * 1024,
        SendBufferSize = 128 * 1024,
        PauseWriterThreshold = 128 * 1024,
        ResumeWriterThreshold = 64 * 1024
    };
});
```

### 7.2 저지연 환경 (FPS, 액션 게임)

```csharp
builder.UsePlayHouse<PlayServer>(options =>
{
    // 짧은 타임아웃
    options.RequestTimeoutMs = 10000;

    options.TransportOptions = new TransportOptions
    {
        HeartbeatTimeout = TimeSpan.FromSeconds(15),
        MaxPacketSize = 512 * 1024, // 작은 패킷
        EnableKeepAlive = true,
        KeepAliveTime = 30,
        KeepAliveInterval = 1
    };

    // 작은 메시지 위주
    options.MessagePool = new MessagePoolConfig
    {
        TinyWarmUpCount = 100000,
        SmallWarmUpCount = 20000,
        MediumWarmUpCount = 500
    };
});
```

## 8. 요약

**필수 설정**

- `ServerId`: 서버 고유 ID (클러스터 내 중복 불가)
- `BindEndpoint`: 서버간 통신 주소
- `AuthenticateMessageId`: 인증 메시지 ID (Play Server만)
- `TcpPort` 또는 `WebSocketPath`: 최소 하나의 Transport 활성화

**성능 관련 설정**

- `MinTaskPoolSize` / `MaxTaskPoolSize`: 동시 처리 작업 수
- `MessagePoolConfig`: 메모리 풀 튜닝 (GC 압박 감소)
- `TransportOptions`: 네트워크 버퍼 및 타임아웃

**환경별 주의사항**

- 개발: TLS 비활성화, 긴 타임아웃, 로깅 활성화
- 스테이징: TLS 활성화, 프로덕션 유사 설정
- 프로덕션: TLS 필수, 인증서 관리, 환경변수로 민감 정보 관리

설정은 워크로드와 하드웨어 사양에 따라 조정이 필요합니다. 모니터링과 부하 테스트를 통해 최적의 값을 찾으세요.
