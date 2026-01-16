# PlayHouse Benchmark 프로젝트

## 개요

PlayHouse-Net의 성능을 측정하기 위한 별도의 벤치마크 프로젝트입니다. 기존 `PlayHouse.Tests.Performance`와 완전히 독립적으로 구현되었으며, 서버와 클라이언트를 별도 프로세스로 분리하여 실제 환경에 가까운 성능 측정을 수행합니다.

## 프로젝트 구조

```
tests/
├── PlayHouse.Benchmark.Shared/        # 공유 Proto 메시지 정의
│   └── Proto/
│       └── benchmark.proto            # BenchmarkRequest/Reply 메시지
│
├── PlayHouse.Benchmark.Server/        # 벤치마크 서버
│   ├── BenchmarkActorImpl.cs          # Actor 구현
│   ├── BenchmarkStageImpl.cs          # Stage 구현
│   ├── ServerMetricsCollector.cs      # 서버 메트릭 수집
│   ├── MetricsController.cs           # HTTP API 컨트롤러
│   └── Program.cs                     # 서버 메인
│
├── PlayHouse.Benchmark.Client/        # 벤치마크 클라이언트
│   ├── BenchmarkRunner.cs             # 벤치마크 실행 로직
│   ├── ClientMetricsCollector.cs      # 클라이언트 메트릭 수집
│   ├── ServerMetricsClient.cs         # 서버 HTTP API 클라이언트
│   ├── Program.cs                     # 클라이언트 메인
│   └── README.md                      # 사용 가이드
│
└── run-benchmark.sh                   # 편의 실행 스크립트
```

## 주요 특징

### 1. 독립 프로세스 아키텍처
- 서버와 클라이언트가 별도 프로세스로 실행
- 실제 네트워크 통신 환경과 동일한 조건에서 측정
- 각 프로세스가 독립적으로 메트릭 수집

### 2. 다양한 메시지 크기 지원
- 요청: ~64 bytes (고정)
- 응답: 256, 1500, 65536 bytes (선택 가능)
- 다양한 페이로드 크기에서의 성능 비교 가능

### 3. 두 가지 벤치마크 모드
- **request-async**: `RequestAsync()` 사용, RTT 레이턴시 정확 측정
- **send-onreceive**: `Send()` + `OnReceive` 사용, 최대 처리량 측정

### 4. 상세한 메트릭 수집

#### 서버 측
- Processed Messages: 처리한 메시지 수
- Server Latency: 요청 수신 → 응답 전송 시간 (Mean, P50, P95, P99)
- Server Throughput: msg/s, MB/s
- Memory: 할당된 메모리 (MB)
- GC: Gen0, Gen1, Gen2 수집 횟수

#### 클라이언트 측
- RTT Latency: 요청 전송 → 응답 수신 시간 (Mean, P50, P95, P99)
- Client Throughput: msg/s (수신 기준)
- Memory: 할당된 메모리 (MB)
- GC: Gen0, Gen1, Gen2 수집 횟수

### 5. HTTP API로 서버 메트릭 조회
- GET /benchmark/stats: 실시간 통계 조회
- POST /benchmark/reset: 통계 리셋
- 클라이언트가 벤치마크 완료 후 서버 메트릭 자동 조회

## 빠른 시작

### 1. 빌드

```bash
dotnet build tests/PlayHouse.Benchmark.Shared --configuration Release
dotnet build tests/PlayHouse.Benchmark.Server --configuration Release
dotnet build tests/PlayHouse.Benchmark.Client --configuration Release
```

### 2. 서버 시작

```bash
dotnet run --project tests/PlayHouse.Benchmark.Server --configuration Release
```

기본 포트:
- PlayServer TCP: 16110
- HTTP API: 5080

### 3. 클라이언트 실행 (별도 터미널)

```bash
# 1 CCU × 10,000 messages
dotnet run --project tests/PlayHouse.Benchmark.Client --configuration Release -- \
  --connections 1 \
  --messages 10000 \
  --response-size 256 \
  --mode request-async

# 1,000 CCU × 10,000 messages
dotnet run --project tests/PlayHouse.Benchmark.Client --configuration Release -- \
  --connections 1000 \
  --messages 10000 \
  --response-size 1500 \
  --mode request-async
```

### 4. 편의 스크립트 사용

```bash
# 기본 실행 (1 CCU, 10000 msg, 256B, request-async)
./tests/run-benchmark.sh

# 커스텀 실행
./tests/run-benchmark.sh 1000 10000 1500 request-async
# 순서: [connections] [messages] [response-size] [mode]
```

## 테스트 시나리오 예시

### 시나리오 1: 낮은 동시성, 작은 페이로드
```bash
./tests/run-benchmark.sh 1 10000 256 request-async
```
- 목적: RTT 레이턴시 정밀 측정
- 예상: P99 < 5ms

### 시나리오 2: 높은 동시성, 중간 페이로드
```bash
./tests/run-benchmark.sh 1000 10000 1500 request-async
```
- 목적: 일반적인 게임 서버 부하 시뮬레이션
- 예상: 50,000+ msg/s

### 시나리오 3: 최대 처리량 측정
```bash
./tests/run-benchmark.sh 1000 10000 1500 send-onreceive
```
- 목적: 서버 최대 처리 능력 측정
- 예상: 80,000+ msg/s

### 시나리오 4: 대용량 페이로드
```bash
./tests/run-benchmark.sh 100 1000 65536 request-async
```
- 목적: 큰 패킷 처리 성능 측정
- 예상: 네트워크 대역폭이 병목

## 결과 해석

### 출력 예시

```
================================================================================
Benchmark Results
================================================================================
Config: 1,000 CCU × 10,000 msg, Request: 64B, Response: 1,500B, Mode: RequestAsync
Total Elapsed: 120.45s

[Server Metrics]
  Processed   : 10,000,000 messages
  Throughput  : 85,000 msg/s (127.5 MB/s)
  Latency     : Mean=0.05ms, P50=0.04ms, P95=0.08ms, P99=0.12ms
  Memory      : 150 MB allocated
  GC          : Gen0=10, Gen1=2, Gen2=0

[Client Metrics]
  Sent        : 10,000,000 messages
  Received    : 10,000,000 messages
  RTT Latency : Mean=1.2ms, P50=1.0ms, P95=2.5ms, P99=5.0ms
  Throughput  : 82,000 msg/s (received)
  Memory      : 80 MB allocated
  GC          : Gen0=5, Gen1=1, Gen2=0
================================================================================
```

### 주요 지표 설명

1. **Server Latency**: 서버 내부 처리 시간
   - 0.1ms 미만이 이상적
   - 1ms 이상이면 서버 로직 최적화 필요

2. **RTT Latency**: 클라이언트 관점 왕복 시간
   - 로컬: 5ms 미만
   - LAN: 10ms 미만
   - Server Latency + 네트워크 지연 + 클라이언트 오버헤드

3. **Throughput**: 초당 처리 메시지 수
   - 서버와 클라이언트가 비슷하면 정상
   - 큰 차이가 있으면 병목 지점 존재

4. **GC Count**:
   - Gen0: 정상적인 수준 (수십 회)
   - Gen1: 최소화 필요 (한 자릿수)
   - Gen2: 피해야 함 (0이 이상적)

## 기존 Performance 프로젝트와의 차이

| 항목 | PlayHouse.Tests.Performance | PlayHouse.Benchmark |
|------|---------------------------|-------------------|
| 목적 | BenchmarkDotNet 기반 마이크로 벤치마크 | 실제 시나리오 기반 벤치마크 |
| 실행 방식 | 단일 프로세스 | 서버/클라이언트 별도 프로세스 |
| 네트워크 | In-process 또는 Loopback | 실제 TCP 소켓 |
| 메트릭 | BenchmarkDotNet 표준 메트릭 | 커스텀 서버/클라이언트 메트릭 |
| 코드 공유 | 가능 | 완전 독립 |
| 사용 사례 | 성능 회귀 감지, 세밀한 최적화 | 전체 시스템 성능 평가 |

## 확장 가능성

현재 구현은 기본적인 Request/Reply 패턴만 지원하지만, 다음과 같이 확장 가능합니다:

1. **Stage간 통신 벤치마크**: SendToStage/RequestToStage
2. **API 서버 벤치마크**: Stage ↔ API 통신
3. **Push 메시지 벤치마크**: SendToClient 성능 측정
4. **타이머 벤치마크**: AddRepeatTimer/AddCountTimer
5. **AsyncBlock 벤치마크**: 비동기 작업 처리 성능

## 트러블슈팅

### 서버가 시작되지 않음
```bash
# 포트가 이미 사용 중인지 확인
lsof -i :16110
lsof -i :5080

# 다른 포트로 시작
dotnet run --project tests/PlayHouse.Benchmark.Server --configuration Release -- \
  --port 16111 --http-port 5081
```

### 클라이언트 연결 실패
```bash
# 서버가 실행 중인지 확인
curl http://localhost:5080/benchmark/stats

# 네트워크 연결 확인
telnet localhost 16110
```

### 메모리 부족
```bash
# 연결 수를 줄이거나 메시지 수를 줄임
./tests/run-benchmark.sh 100 1000 256 request-async
```

## 성능 최적화 팁

1. **Release 빌드 사용**: 항상 `--configuration Release`로 빌드
2. **충분한 대기 시간**: 서버 시작 후 2-3초 대기
3. **GC 튜닝**: `DOTNET_gcServer=1` 환경변수 설정
4. **CPU Affinity**: 서버와 클라이언트를 다른 CPU 코어에 할당

```bash
# GC 서버 모드 사용
export DOTNET_gcServer=1

# CPU affinity 설정 (Linux)
taskset -c 0-3 dotnet run --project tests/PlayHouse.Benchmark.Server ...
taskset -c 4-7 dotnet run --project tests/PlayHouse.Benchmark.Client ...
```

## 라이센스

MIT License - PlayHouse-Net 프로젝트와 동일
