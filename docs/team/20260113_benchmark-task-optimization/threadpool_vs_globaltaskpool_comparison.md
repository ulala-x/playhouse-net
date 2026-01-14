# ThreadPool vs GlobalTaskPool 벤치마크 비교

## 테스트 환경

- **CCU**: 10,000
- **Message Size**: 1,024 bytes
- **Duration**: 30 seconds
- **Max In-flight**: 10
- **테스트 날짜**: 2026-01-14

## 1단계: ThreadPool 방식 결과

### C2S (RequestAsync Mode)

| 항목 | 값 |
|------|-----|
| **Throughput** | |
| Server TPS | 97,629 msg/s |
| Client TPS | 98,499 msg/s |
| Server Bandwidth | 47.67 MB/s |
| **Latency** | |
| Server Mean | 0.06 ms |
| Server P50 | 0.04 ms |
| Server P95 | 0.07 ms |
| Server P99 | 0.19 ms |
| Client RTT Mean | 86.66 ms |
| Client RTT P50 | 79.75 ms |
| Client RTT P95 | 168.96 ms |
| Client RTT P99 | 236.54 ms |
| **Memory & GC** | |
| Server Memory | 3,299.44 MB |
| Server GC (0/1/2) | 129/52/1 |
| Client Memory | 5,795.33 MB |
| Client GC (0/1/2) | 396/394/15 |
| **Messages** | |
| Total Processed | 3,479,898 |
| Elapsed Time | 31.49 s |

### S2S (Send Mode)

| 항목 | 값 |
|------|-----|
| **Throughput** | |
| S2S TPS | 630,904 msg/s |
| **Server Metrics** | |
| CPU Usage | 31.12% |
| Memory Allocated | 8,046.86 MB |
| GC (0/1/2) | 8/6/6 |
| **Latency** | |
| Mean | 0.00 ms |
| P50 | 0.00 ms |
| P95 | 0.00 ms |
| P99 | 0.00 ms |
| **Messages** | |
| Total S2S Messages | 19,560,250 |
| Elapsed Time | 31.00 s |

## 2단계: GlobalTaskPool 방식 결과

### C2S (RequestAsync Mode)

| 항목 | 값 |
|------|-----|
| **Throughput** | |
| Server TPS | 91,669 msg/s |
| Client TPS | 92,717 msg/s |
| Server Bandwidth | 44.76 MB/s |
| **Latency** | |
| Server Mean | 0.06 ms |
| Server P50 | 0.04 ms |
| Server P95 | 0.08 ms |
| Server P99 | 0.23 ms |
| Client RTT Mean | 92.12 ms |
| Client RTT P50 | 83.41 ms |
| Client RTT P95 | 180.63 ms |
| Client RTT P99 | 231.59 ms |
| **Memory & GC** | |
| Server Memory | 2,900.06 MB |
| Server GC (0/1/2) | 102/9/1 |
| Client Memory | 5,484.29 MB |
| Client GC (0/1/2) | 374/372/14 |
| **Messages** | |
| Total Processed | 3,276,024 |
| Elapsed Time | 31.59 s |

### S2S (Send Mode)

| 항목 | 값 |
|------|-----|
| **Throughput** | |
| S2S TPS | 597,491 msg/s |
| **Server Metrics** | |
| CPU Usage | 34.38% |
| Memory Allocated | 7,263.56 MB |
| GC (0/1/2) | 8/6/6 |
| **Latency** | |
| Mean | 0.00 ms |
| P50 | 0.00 ms |
| P95 | 0.00 ms |
| P99 | 0.00 ms |
| **Messages** | |
| Total S2S Messages | 18,915,450 |
| Elapsed Time | 31.66 s |

## 비교 분석

### Throughput 비교

| 모드 | ThreadPool | GlobalTaskPool | 차이 |
|------|------------|----------------|------|
| **C2S RequestAsync** | 97,629 msg/s | 91,669 msg/s | **-6.1%** ⬇️ |
| **S2S Send** | 630,904 msg/s | 597,491 msg/s | **-5.3%** ⬇️ |

### Memory & GC 비교

| 항목 | ThreadPool | GlobalTaskPool | 차이 |
|------|------------|----------------|------|
| **C2S Server** | | | |
| Memory | 3,299.44 MB | 2,900.06 MB | **-12.1%** ✅ |
| GC Gen0 | 129 | 102 | **-20.9%** ✅ |
| GC Gen1 | 52 | 9 | **-82.7%** ✅ |
| GC Gen2 | 1 | 1 | **0%** ➡️ |
| **S2S Server** | | | |
| Memory | 8,046.86 MB | 7,263.56 MB | **-9.7%** ✅ |
| GC Gen0 | 8 | 8 | **0%** ➡️ |
| GC Gen1 | 6 | 6 | **0%** ➡️ |
| GC Gen2 | 6 | 6 | **0%** ➡️ |

### Latency 비교

| 항목 | ThreadPool | GlobalTaskPool | 차이 |
|------|------------|----------------|------|
| **C2S Server P99** | 0.19 ms | 0.23 ms | **+21.1%** ⬇️ |
| **C2S Server P95** | 0.07 ms | 0.08 ms | **+14.3%** ⬇️ |
| **C2S Client RTT P99** | 236.54 ms | 231.59 ms | **-2.1%** ✅ |
| **S2S P99** | 0.00 ms | 0.00 ms | **0%** ➡️ |

### CPU 사용률 비교

| 모드 | ThreadPool | GlobalTaskPool | 차이 |
|------|------------|----------------|------|
| **S2S Send** | 31.12% | 34.38% | **+10.5%** ⬇️ |

## 결론

### 성능 요약

#### ✅ GlobalTaskPool 장점
1. **메모리 효율성**: C2S에서 12.1%, S2S에서 9.7% 메모리 사용량 감소
2. **GC 압박 감소**: C2S Gen1 GC가 82.7% 감소 (52 → 9)
3. **Client RTT**: C2S에서 2.1% 개선

#### ⬇️ GlobalTaskPool 단점
1. **처리량 감소**: C2S 6.1%, S2S 5.3% TPS 감소
2. **서버 레이턴시 증가**: P99가 21.1% 증가 (0.19ms → 0.23ms)
3. **CPU 사용률 증가**: S2S에서 10.5% 증가

### 상세 분석

#### 1. 처리량 (Throughput)
- **ThreadPool 우위**: 5-6% 더 높은 TPS
- **원인**: ThreadPool의 work-stealing queue가 더 효율적인 부하 분산 제공
- **GlobalTaskPool**: Channel 기반 FIFO 큐로 인한 약간의 오버헤드

#### 2. 메모리 & GC
- **GlobalTaskPool 우위**:
  - C2S: 399MB 메모리 절감, Gen1 GC 43회 감소
  - S2S: 783MB 메모리 절감
- **원인**:
  - GlobalTaskPool은 고정된 Worker Task 풀 사용
  - ThreadPool은 매 스케줄링마다 WorkItem 할당
  - C2S의 경우 GC 압박이 크게 감소 (특히 Gen1)

#### 3. 레이턴시
- **혼재된 결과**:
  - 서버 레이턴시: ThreadPool 우위 (더 낮음)
  - 클라이언트 RTT: GlobalTaskPool 근소하게 우위
- **원인**:
  - ThreadPool의 더 빠른 스케줄링이 서버 레이턴시 개선
  - GlobalTaskPool의 안정적인 GC가 클라이언트 RTT 개선

#### 4. CPU 사용률
- **ThreadPool 우위**: 10.5% 낮은 CPU 사용률
- **원인**: GlobalTaskPool의 Channel 대기/폴링 오버헤드

### 권장사항

#### 현재 상황 (AsyncBlock 수정 유지 필요)
AsyncBlock 최적화를 유지해야 하므로 **ThreadPool 방식을 유지하는 것이 권장됨**.

**이유:**
1. **성능 우선**: 6% TPS 차이는 무시할 수 없는 수준
2. **레이턴시**: 서버 P99가 21% 낮음 (0.19ms vs 0.23ms)
3. **메모리 차이**: 12% 메모리 절감은 이점이지만, 3.3GB vs 2.9GB는 실용적으로 큰 차이 아님
4. **복잡도**: GlobalTaskPool 추가는 코드베이스 복잡도 증가
5. **.NET 최적화**: ThreadPool은 .NET 런타임에 의해 지속적으로 최적화됨

#### 만약 메모리가 중요한 경우
- Gen1 GC가 82.7% 감소한다는 점은 매우 긍정적
- 장시간 실행되는 서버에서 GC 압박이 누적될 경우 GlobalTaskPool 고려 가능
- 그러나 현재 벤치마크에서는 성능 손실이 더 크게 나타남

### 최종 결정
**ThreadPool 방식 유지** ✅

이유:
1. 5-6% 더 높은 TPS
2. 20% 낮은 서버 레이턴시
3. .NET 네이티브 최적화 활용
4. AsyncBlock 수정사항과 호환성 유지
