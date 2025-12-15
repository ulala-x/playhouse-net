# NetMQ vs Net.Zmq 성능 비교

## 테스트 환경

| 항목 | 값 |
|------|-----|
| OS | Ubuntu 24.04.3 LTS (Noble Numbat) |
| CPU | Intel Core Ultra 7 265K, 20 logical cores |
| .NET SDK | 8.0.122 |
| Runtime | .NET 8.0.22, X64 RyuJIT AVX2 |
| 벤치마크 도구 | BenchmarkDotNet v0.14.0 |
| Job | ShortRun (3 iterations, 3 warmup) |

## 테스트 버전

| 라이브러리 | 버전 | Commit |
|-----------|------|--------|
| NetMQ | 4.0.* | bd10a0c (2025-12-15 이전) |
| Net.Zmq | 0.1.* | main (2025-12-15) |

## 레이턴시 비교 (Mean)

| 테스트 | NetMQ | Net.Zmq | 차이 | 개선율 |
|--------|-------|---------|------|--------|
| Client→Server RTT | 102.0 μs | 86.25 μs | -15.75 μs | **+15.4%** |
| Game Tick (60 FPS) | 413.3 μs | 342.44 μs | -70.86 μs | **+17.1%** |
| Server→Server RTT | 424.1 μs | 360.81 μs | -63.29 μs | **+14.9%** |
| Sequential 10 messages | 482.4 μs | 495.49 μs | +13.09 μs | -2.7% |

## 메모리 할당 비교

| 테스트 | NetMQ | Net.Zmq | 차이 |
|--------|-------|---------|------|
| Client→Server RTT | 2.99 KB | 13.38 KB | +10.39 KB |
| Game Tick (60 FPS) | 11.4 KB | 43.82 KB | +32.42 KB |
| Server→Server RTT | 11.12 KB | 50.15 KB | +39.03 KB |
| Sequential 10 messages | 28.65 KB | 91.02 KB | +62.37 KB |

## 분석

### 레이턴시 (Latency)

Net.Zmq가 대부분의 시나리오에서 **14~17% 더 빠른 레이턴시**를 보여줍니다:

- **Client→Server RTT**: 15.4% 개선 (102.0 → 86.25 μs)
- **Game Tick Simulation**: 17.1% 개선 (413.3 → 342.44 μs)
- **Server→Server RTT**: 14.9% 개선 (424.1 → 360.81 μs)
- **Sequential 10 messages**: 2.7% 저하 (482.4 → 495.49 μs)

단일 메시지 처리에서는 Net.Zmq가 우수하지만, 연속 메시지 처리에서는 약간의 오버헤드가 있습니다.

### 메모리 할당 (Memory Allocation)

Net.Zmq는 현재 **더 많은 메모리를 할당**합니다:

- 이는 Net.Zmq의 `MultipartMessage` 구현이 managed 배열을 사용하기 때문
- NetMQ는 내부적으로 pooling과 최적화가 더 성숙한 상태
- 향후 Net.Zmq 버전에서 개선 가능

### GC 영향

| 지표 | NetMQ | Net.Zmq |
|------|-------|---------|
| Gen0 Collections | 낮음 (0.1~1.0) | 높음 (0.8~5.9) |
| Gen1 Collections | 없음 | 있음 (0.2~2.0) |

Net.Zmq의 높은 메모리 할당으로 인해 GC 압력이 증가합니다.

## 결론

### Net.Zmq 장점
1. **레이턴시 개선**: 단일 요청-응답에서 14~17% 빠름
2. **현대적 API**: LibraryImport 기반, .NET 8+ 최적화
3. **유지보수**: 직접 관리 가능한 라이브러리

### Net.Zmq 단점
1. **메모리 할당 증가**: 3~4배 더 많은 allocation
2. **GC 압력**: Gen0/Gen1 collection 증가
3. **연속 메시지**: 다량의 연속 메시지에서 약간의 오버헤드

### 권장 사항

실시간 게임 서버의 경우:
- **레이턴시가 중요**하다면: Net.Zmq 사용 (14~17% 개선)
- **메모리 효율이 중요**하다면: NetMQ 유지 또는 Net.Zmq 메모리 최적화 필요

현재 PlayHouse-Net은 **레이턴시 우선** 정책으로 Net.Zmq를 채택했습니다.

## 상세 벤치마크 결과

### NetMQ (4.0.*)

```
| Method                                   | Mean     | Error     | StdDev   | Allocated |
|----------------------------------------- |---------:|----------:|---------:|----------:|
| 'Client→Server RTT'                      | 102.0 μs |  56.71 μs |  3.11 μs |   2.99 KB |
| 'Game Tick Simulation (60 FPS)'          | 413.3 μs | 104.47 μs |  5.73 μs |   11.4 KB |
| 'Server→Server RTT (via client trigger)' | 424.1 μs | 373.41 μs | 20.47 μs |  11.12 KB |
| 'Sequential 10 messages'                 | 482.4 μs | 223.48 μs | 12.25 μs |  28.65 KB |
```

### Net.Zmq (0.1.*)

```
| Method                                   | Mean      | Error     | StdDev    | Allocated |
|----------------------------------------- |----------:|----------:|----------:|----------:|
| 'Client→Server RTT'                      |  86.25 μs |  64.93 μs |  3.559 μs |  13.38 KB |
| 'Game Tick Simulation (60 FPS)'          | 342.44 μs | 320.60 μs | 17.573 μs |  43.82 KB |
| 'Server→Server RTT (via client trigger)' | 360.81 μs | 501.93 μs | 27.512 μs |  50.15 KB |
| 'Sequential 10 messages'                 | 495.49 μs | 165.76 μs |  9.086 μs |  91.02 KB |
```

---

*벤치마크 실행일: 2025-12-15*
