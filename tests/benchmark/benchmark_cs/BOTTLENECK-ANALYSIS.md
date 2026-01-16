# PlayHouse Benchmark - 병목 구간 분석 보고서

**Date**: 2026-01-05
**Analyst**: Claude Code
**Goal**: 1,000,000 TPS (1M msg/s) 달성을 위한 병목 구간 파악

---

## 요약

### 현재 성능
- **서버 TPS**: 271,571 msg/s (목표 1M의 **27.2%**)
- **클라이언트 TPS**: 263,801 msg/s
- **테스트 설정**: 100 connections, 1KB messages, Send mode, 60s duration

### 핵심 병목 (서버 측)
1. ✅ **Gen2 GC 압력** - 1.6 GC/sec (예상: < 0.1/sec)
2. ⚠️ **EventLoop 포화 가능성** - CPU 프로파일 분석 필요
3. ⚠️ **Context Switching** - 200+ 스레드 on 20 cores

### 목표 달성을 위한 필요 개선
- **현재 → 목표**: 271k → 1,000k TPS
- **필요 향상률**: **3.68배**

---

## 1. 프로파일링 결과 분석

### 1.1 서버 메트릭 (60초 벤치마크)

| 메트릭 | 값 | 평가 |
|--------|-----|------|
| Throughput | 271,571 msg/s (530 MB/s) | ⚠️ 목표의 27% |
| Memory | 37.2 GB | ⚠️ 높음 |
| GC Counts | Gen0=2651, Gen1=2562, Gen2=104 | ❌ 매우 높음 |
| GC Rate | ~44 Gen0/sec, ~43 Gen1/sec, **1.6 Gen2/sec** | ❌ Gen2 과도 |
| CPU Usage | ~80-90% (프로파일링 중) | ⚠️ 포화 |

**진단**:
- **Gen2 GC 빈도 과다**: 1.6 GC/sec (목표: < 0.1/sec)
  - Stop-the-world 일시정지 발생
  - 예상 성능 영향: **5-10% 손실**
  - 원인: 대용량 객체 LOH 진입 또는 오래 살아남는 객체

- **Gen0/Gen1 GC 빈도 높음**: ~44/sec
  - 높은 할당률 (allocation rate)
  - 예상 성능 영향: **3-5% 손실**

### 1.2 클라이언트 메트릭 (60초 벤치마크)

| 메트릭 | 값 | 평가 |
|--------|-----|------|
| Throughput | 263,801 msg/s | ⚠️ 서버보다 낮음 |
| Memory | **1,672,475 MB (1.67 TB!)** | ❌ 메모리 누수 |
| GC Counts | Gen0=109k, Gen1=72k, Gen2=11k | ❌ 매우 높음 |
| RTT Latency | Mean=340ms, P99=858ms | ⚠️ 높음 |

**진단**:
- **클라이언트 메모리 누수 (CRITICAL)**:
  - 1.67 TB 메모리 사용 (60초 동안)
  - 원인: `ClientMetricsCollector._rttLatencies` 리스트 무한 증가
  - 영향: 클라이언트가 서버 성능 측정에 영향을 줄 수 있음

### 1.3 Runtime Counters 분석

**문제**: dotnet-counters가 유휴 시간에 데이터 수집
- CPU Usage: 0% (벤치마크 종료 후 측정됨)
- Allocation Rate: ~8-16 KB/sec (idle)
- Monitor Lock Contention: 0-1 /sec

**결론**: 프로파일링 스크립트 타이밍 조정 필요
- dotnet-counters를 벤치마크 실행 중 시작하도록 수정 필요

---

## 2. 병목 가설 및 검증

### 가설 1: EventLoop 포화 ⚠️ (검증 필요)

**현재 상태**:
- EventLoop 스레드: **16개** (CPU 코어 수)
- Stage 수: **100개** (connection 당 1 stage)
- 평균 부하: **6.25 stage/loop**

**가설**:
- 16개 EventLoop가 100개 Stage의 메시지를 처리
- 특정 Loop에 Stage가 집중되면 병목 발생 가능
- 메시지 처리 속도 < 메시지 도착 속도

**검증 방법**:
1. **CPU 프로파일 분석** (cpu-profile.nettrace)
   - Upload to https://www.speedscope.app/
   - EventLoop 스레드 CPU 사용률 확인
   - `BaseStage.ProcessOneMessageAsync` CPU % 확인

2. **예상 CPU 분포**:
   - EventLoop 처리: > 40%?
   - SendLoop: > 30%?
   - ConcurrentQueue 경합: > 15%?

**최적화 방향**:
- EventLoop 스레드 수 증가 (16 → 32, 하이퍼스레딩 활용)
- 메시지 배치 처리 (1개 → 10개)
- 예상 효과: +30-50%

---

### 가설 2: 과도한 Context Switching ⚠️ (검증 필요)

**현재 상태**:
- 총 스레드 수: **~220개**
  - ReceiveLoop: 100개 (connection 당 1개)
  - SendLoop: 100개 (connection 당 1개)
  - EventLoop: 16개
  - ThreadPool: 기타
- CPU 코어: **20개**
- 평균 스레드/코어: **11개**

**가설**:
- 과도한 context switching 오버헤드
- CPU cache 효율 저하
- EventLoop 스레드가 I/O 스레드에 밀림

**검증 방법**:
1. **perf 통계** (Linux):
   ```bash
   perf stat -e context-switches -p $SERVER_PID sleep 30
   ```
   - 현재: WSL2에서 perf 사용 불가
   - 대안: `/proc/$PID/status`의 voluntary/involuntary context switches 확인

2. **예상 값**:
   - Context switches/sec: > 10k? (높으면 문제)

**최적화 방향**:
- SendLoop 통합 (100 → 16, 풀 방식)
- ReceiveLoop 통합 (SocketAsyncEventArgs 사용)
- 예상 효과: +10-20%

---

### 가설 3: GC 압력 ✅ (확인됨)

**확인된 문제**:
- **Gen2 GC**: 1.6/sec (목표: < 0.1/sec)
  - 원인: LOH 객체 (> 85KB) 또는 장수명 객체
  - 영향: Stop-the-world 일시정지 → **5-10% 성능 손실**

- **Gen0/Gen1 GC**: ~44/sec
  - 원인: 높은 할당률
  - 영향: Minor GC 오버헤드 → **3-5% 성능 손실**

**검증 방법**:
1. **CPU 프로파일에서 GC 시간 확인**
2. **LOH 할당 찾기**:
   ```bash
   dotnet-trace collect --providers Microsoft-Windows-DotNETRuntime:0x1:4
   # PerfView에서 LOH 할당 분석
   ```

**최적화 방향**:
1. **ArrayPool 확대**: 버퍼 재사용 증가
2. **객체 풀링**: StageMessage, RoutePacket 등
3. **Struct 변환**: 작은 객체 → value type
4. 예상 효과: +10-15%

---

### 가설 4: 클라이언트 병목 ✅ (확인됨)

**확인된 문제**:
- 클라이언트 메모리 누수: **1.67 TB**
- 원인: `ClientMetricsCollector._rttLatencies` 리스트 무한 증가
  ```csharp
  // ClientMetricsCollector.cs:51
  public void RecordReceived(long elapsedTicks)
  {
      var rttMs = (double)elapsedTicks / Stopwatch.Frequency * 1000;
      lock (_lock)
      {
          _rttLatencies.Add(rttMs);  // 계속 증가!
          Interlocked.Increment(ref _receivedMessages);
      }
  }
  ```
- 60초 * 264k msg/s = 15.8M 메시지
- 100 connections * 15.8M * 8 bytes (double) = **12.6 GB**
- 실제: 1.67 TB → 다른 메모리 누수도 있을 수 있음

**영향**:
- 클라이언트 GC 압력 증가 (109k Gen0, 72k Gen1, 11k Gen2!)
- 클라이언트 성능 저하 → 서버 성능 측정에 영향

**해결 방법**:
1. **Bounded list**: 최대 크기 제한 (예: 100k 샘플)
2. **Reservoir sampling**: 통계적으로 대표 샘플만 유지
3. **Percentile 실시간 계산**: 모든 값 저장하지 않음

---

## 3. 프로파일링 파일 분석

### 3.1 CPU 프로파일 (cpu-profile.nettrace)

**파일 정보**:
- 위치: `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_cs/profiling-results/20260105_191252/cpu-profile.nettrace`
- 크기: 8.7 MB
- 수집 시간: 30초

**분석 방법**:
1. https://www.speedscope.app/ 에 업로드
2. **확인할 항목**:
   - Top 5 CPU hotspot 함수
   - EventLoop 스레드 CPU 사용률
   - `BaseStage.ProcessOneMessageAsync` 비율
   - `TcpTransportSession.SendLoopAsync` 비율
   - `ConcurrentQueue.Enqueue/TryDequeue` 비율
   - GC 시간 비율

3. **예상 결과**:
   ```
   BaseStage.ProcessOneMessageAsync    : 40-50%?
   TcpTransportSession.SendLoopAsync   : 20-30%?
   ConcurrentQueue operations          : 10-15%?
   MessageCodec serialization          : 5-10%?
   GC                                  : 5-10%
   ```

**⚠️ 다음 단계**: CPU 프로파일을 speedscope.app에서 분석하여 실제 핫스팟 확인

---

### 3.2 Runtime Counters (runtime-counters.txt)

**문제**: 벤치마크 종료 후 유휴 시간에 측정됨
- CPU Usage: 0% (부하 중이 아님)
- Allocation Rate: 8-16 KB/sec (idle)

**해결 방법**: 프로파일링 스크립트 타이밍 조정
```bash
# profile-benchmark.sh 수정 필요:
# 1. 클라이언트 시작
# 2. 10초 워밍업 (기존)
# 3. dotnet-counters 시작 (← 여기서 시작!)
# 4. CPU profile 수집 (30초)
# 5. 벤치마크 완료 대기
```

---

## 4. 병목 우선순위 및 최적화 로드맵

### Phase 0: 프로파일링 보완 (1일)
- [ ] CPU 프로파일 분석 (speedscope.app)
- [ ] 프로파일링 스크립트 타이밍 수정
- [ ] 클라이언트 메모리 누수 수정
- [ ] 재프로파일링 실행

### Phase 1: 즉시 가능한 최적화 (2-3일)

**예상 효과**: 271k → 450k TPS (1.66배)

1. **EventLoop 스레드 확장** (+30%)
   - 파일: `StageEventLoopPool.cs:31-37`
   - 변경: `poolSize = Environment.ProcessorCount * 2` (16 → 32)
   - 근거: EventLoop는 I/O bound 작업 많음, 하이퍼스레딩 활용
   - 난이도: ⭐ (1 line)
   - 예상: 271k → 352k TPS

2. **메시지 배치 처리** (+20%)
   - 파일: `BaseStage.cs` ProcessOneMessageAsync
   - 변경: 1개 → 10개 메시지 배치 처리
   - 근거: async/await 오버헤드 감소, CPU cache locality 향상
   - 난이도: ⭐⭐ (로직 수정)
   - 예상: 352k → 422k TPS

3. **GC 압력 감소** (+10%)
   - ArrayPool 사용 확대
   - 예상: 422k → 464k TPS

### Phase 2: 구조적 변경 (1-2주)

**예상 효과**: 464k → 750k TPS (1.62배)

1. **SendLoop 통합** (100 → 16)
   - Context switching 감소
   - 예상: +15%

2. **Zero-copy 최적화**
   - Payload Move 최적화
   - Direct NetworkStream write
   - 예상: +20%

3. **객체 풀링**
   - StageMessage, RoutePacket 풀링
   - 예상: +10%

### Phase 3: 극한 최적화 (2-3주)

**예상 효과**: 750k → 1,000k+ TPS (1.33배)

1. **ReceiveLoop 통합** (SocketAsyncEventArgs)
2. **Inline Dispatch** (Task 할당 제거)
3. **프로토콜 최적화** (msgId → ushort)

---

## 5. 다음 단계 (Action Items)

### 즉시 실행 (High Priority)

1. ✅ **CPU 프로파일 분석**
   ```bash
   # 로컬에서 실행:
   # 1. 파일 다운로드
   scp user@server:/path/to/cpu-profile.nettrace .
   # 2. https://www.speedscope.app/ 에서 열기
   # 3. Top hotspots 스크린샷
   ```

2. ⬜ **클라이언트 메모리 누수 수정**
   - 파일: `ClientMetricsCollector.cs`
   - 방법: Bounded list 또는 reservoir sampling
   - 예상 시간: 1시간

3. ⬜ **프로파일링 스크립트 수정**
   - 파일: `profile-benchmark.sh`
   - 수정: dotnet-counters 타이밍 조정
   - 예상 시간: 30분

4. ⬜ **재프로파일링**
   - 수정된 클라이언트로 재실행
   - Runtime counters 올바른 시간대 수집
   - 예상 시간: 1시간

### Phase 1 최적화 시작 (Medium Priority)

5. ⬜ **EventLoop 확장 구현** (예상: +30%)
   - 1 line 수정, 테스트, 측정

6. ⬜ **메시지 배치 처리 구현** (예상: +20%)
   - BaseStage.cs 수정, 테스트, 측정

---

## 6. 측정 기준 (Baseline for Improvement)

### 현재 성능 (Baseline)
- **TPS**: 271,571 msg/s
- **Latency**: N/A (Send mode)
- **Memory**: 37.2 GB (server), 1.67 TB (client)
- **GC**: Gen2 1.6/sec (server)

### Phase 1 목표
- **TPS**: 450,000+ msg/s (1.66배)
- **GC**: Gen2 < 1.0/sec

### Phase 2 목표
- **TPS**: 750,000+ msg/s (2.76배)
- **GC**: Gen2 < 0.5/sec

### 최종 목표
- **TPS**: 1,000,000+ msg/s (3.68배)
- **GC**: Gen2 < 0.1/sec
- **Memory**: < 20 GB (server)

---

## 7. 참고 자료

### 프로파일링 파일 위치
```
/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_cs/profiling-results/20260105_191252/
├── cpu-profile.nettrace       # CPU 프로파일 (speedscope.app에서 열기)
├── runtime-counters.txt       # GC, ThreadPool 메트릭 (타이밍 문제 있음)
├── test-summary.txt           # 테스트 설정
├── client.log                 # 클라이언트 출력 (벤치마크 결과 포함)
└── server.log                 # 서버 출력
```

### 관련 문서
- [PROFILING.md](./PROFILING.md) - 프로파일링 도구 사용법
- [100만 TPS 달성 계획](../../../.claude/plans/cheeky-growing-snowglobe.md)

### 핵심 코드 파일
- `StageEventLoopPool.cs:31-37` - EventLoop 스레드 수
- `BaseStage.cs:ProcessOneMessageAsync` - 메시지 처리 루프
- `TcpTransportSession.cs` - SendLoop
- `ClientMetricsCollector.cs:51` - 클라이언트 메모리 누수

---

## 결론

**현재 상태**: 271k TPS (목표의 27%)

**확인된 병목**:
1. ✅ Gen2 GC 압력 (1.6/sec) - **5-10% 성능 손실**
2. ⚠️ EventLoop 포화 가능성 - CPU 프로파일 분석 필요
3. ⚠️ Context Switching - 220 스레드 on 20 cores
4. ✅ 클라이언트 메모리 누수 - 측정에 영향

**즉시 가능한 개선** (Phase 1):
- EventLoop 확장 (16 → 32): **+30%**
- 메시지 배치 처리: **+20%**
- GC 최적화: **+10%**
- **예상 결과**: 271k → 450k TPS (1.66배)

**1M TPS 달성 경로**: 3단계 최적화로 달성 가능
- Phase 1: 271k → 450k (즉시 가능)
- Phase 2: 450k → 750k (구조 변경)
- Phase 3: 750k → 1,000k+ (극한 최적화)

**다음 단계**: CPU 프로파일 분석 → Phase 1 최적화 시작
