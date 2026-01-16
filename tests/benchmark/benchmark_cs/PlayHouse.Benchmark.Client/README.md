# PlayHouse Benchmark Suite

PlayHouse-Net의 성능을 측정하는 벤치마크 테스트 프로젝트입니다.

## 프로젝트 구조

```
tests/
├── PlayHouse.Benchmark.Shared/        # 공유 Proto 메시지 정의
├── PlayHouse.Benchmark.Server/        # 벤치마크 서버 프로세스
└── PlayHouse.Benchmark.Client/        # 벤치마크 클라이언트 프로세스
```

## 아키텍처

- **별도 프로세스**: 서버와 클라이언트가 독립적인 프로세스로 실행
- **독립적 메트릭**: 각 프로세스에서 자체적으로 성능 메트릭 측정
- **HTTP API**: 서버 메트릭을 HTTP API로 조회 가능

## 메시지 크기

- **요청 패킷**: ~64 bytes (고정)
- **응답 패킷**: 256, 1500, 65536 bytes (선택 가능)

## 테스트 시나리오

1. **1 CCU × 10,000 messages (RequestAsync)**
2. **1 CCU × 10,000 messages (Send + OnReceive)**
3. **1,000 CCU × 10,000 messages each (RequestAsync)**
4. **1,000 CCU × 10,000 messages each (Send + OnReceive)**

## 측정 메트릭

### 서버 측 (ServerMetricsCollector)
- Processed Messages: 처리한 메시지 수
- Server Latency: 요청 수신 → 응답 전송 시간 (Mean, P50, P95, P99)
- Server Throughput: msg/s, MB/s
- Memory: 할당된 메모리
- GC: Gen0, Gen1, Gen2 횟수

### 클라이언트 측 (ClientMetricsCollector)
- RTT Latency: 요청 전송 → 응답 수신 시간 (Mean, P50, P95, P99)
- Client Throughput: msg/s (수신)
- Memory: 할당된 메모리
- GC: Gen0, Gen1, Gen2 횟수

## 실행 방법

### 1. 서버 시작

```bash
# 기본 포트 사용 (PlayServer: 16110, HTTP API: 5080)
dotnet run --project tests/PlayHouse.Benchmark.Server --configuration Release

# 포트 지정
dotnet run --project tests/PlayHouse.Benchmark.Server --configuration Release -- \
  --port 16110 \
  --http-port 5080
```

### 2. 클라이언트 실행

```bash
# 1 CCU × 10,000 messages, Response 256B, RequestAsync
dotnet run --project tests/PlayHouse.Benchmark.Client --configuration Release -- \
  --server 127.0.0.1:16110 \
  --connections 1 \
  --messages 10000 \
  --response-size 256 \
  --mode request-async

# 1,000 CCU × 10,000 messages, Response 1500B, RequestAsync
dotnet run --project tests/PlayHouse.Benchmark.Client --configuration Release -- \
  --server 127.0.0.1:16110 \
  --connections 1000 \
  --messages 10000 \
  --response-size 1500 \
  --mode request-async

# Send + OnReceive 모드
dotnet run --project tests/PlayHouse.Benchmark.Client --configuration Release -- \
  --server 127.0.0.1:16110 \
  --connections 1000 \
  --messages 10000 \
  --response-size 1500 \
  --mode send-onreceive
```

### 클라이언트 옵션

- `--server`: 서버 주소 (host:port, 기본값: 127.0.0.1:16110)
- `--connections`: 동시 연결 수 (기본값: 1)
- `--messages`: 연결당 메시지 수 (기본값: 10000)
- `--response-size`: 응답 크기 bytes (256, 1500, 65536, 기본값: 256)
- `--mode`: 벤치마크 모드 (request-async, send-onreceive, 기본값: request-async)
- `--http-port`: 서버 HTTP API 포트 (기본값: 5080)

## HTTP API (서버)

서버의 메트릭을 HTTP로 조회할 수 있습니다.

### GET /benchmark/stats
```bash
curl http://localhost:5080/benchmark/stats
```

응답 예시:
```json
{
  "processedMessages": 10000000,
  "throughputMessagesPerSec": 85000,
  "throughputMBPerSec": 127.5,
  "latencyMeanMs": 0.05,
  "latencyP50Ms": 0.04,
  "latencyP95Ms": 0.08,
  "latencyP99Ms": 0.12,
  "memoryAllocatedMB": 150,
  "gcGen0Count": 10,
  "gcGen1Count": 2,
  "gcGen2Count": 0
}
```

### POST /benchmark/reset
```bash
curl -X POST http://localhost:5080/benchmark/reset
```

통계를 리셋합니다.

## 출력 형식

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

## 주의사항

- 기존 `PlayHouse.Tests.Performance`와 완전히 별도 프로젝트
- 코드 공유 없이 독립적으로 구현
- PlayHouse, PlayHouse.Connector, PlayHouse.Abstractions 패키지만 참조
- Proto 파일은 Shared 프로젝트에 새로 정의

## 벤치마크 모드

### request-async
- `Connector.RequestAsync()`를 사용하여 동기적으로 요청/응답
- RTT 레이턴시를 정확하게 측정 가능
- 순차적 처리로 인해 처리량은 상대적으로 낮음

### send-onreceive
- `Connector.Send()`로 메시지 전송, `OnReceive` 콜백으로 수신
- 비동기 처리로 높은 처리량 달성 가능
- 네트워크 대역폭과 서버 처리 능력이 병목
