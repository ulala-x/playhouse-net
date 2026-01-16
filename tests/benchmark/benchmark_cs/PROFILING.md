# PlayHouse Benchmark Profiling Guide

## 목차
1. [개요](#개요)
2. [프로파일링 도구](#프로파일링-도구)
3. [프로파일링 실행](#프로파일링-실행)
4. [결과 분석](#결과-분석)
5. [예상 병목 가설](#예상-병목-가설)
6. [문제 해결](#문제-해결)

---

## 개요

### 프로파일링 목적

PlayHouse 벤치마크 서버의 성능 병목을 식별하여 1M TPS 목표를 달성하기 위한 최적화 방향을 결정합니다.

**현재 성능**: ~300k TPS (100 connections, 1KB messages)
**목표 성능**: 1,000k TPS (1M TPS)
**필요 향상**: 3.3배

### 주요 측정 항목

1. **CPU Hotspots**: 어느 함수가 CPU를 가장 많이 사용하는가?
2. **Runtime Metrics**: GC, ThreadPool, 메모리 할당 상태
3. **Context Switching**: 스레드 전환으로 인한 오버헤드
4. **Lock Contention**: 락 경합으로 인한 대기 시간

---

## 프로파일링 도구

### 1. dotnet-trace

**목적**: CPU 핫스팟 분석

CPU 샘플링 프로파일러로, 어느 함수가 CPU 시간을 가장 많이 소비하는지 파악합니다.

**설치**:
```bash
dotnet tool install -g dotnet-trace
```

**주요 기능**:
- CPU 샘플 수집 (샘플링 방식)
- 콜스택 분석
- speedscope.app과 호환되는 nettrace 파일 생성

**확인할 핫스팟**:
- `BaseStage.ProcessOneMessageAsync`: 메시지 처리 오버헤드
- `TcpTransportSession.SendLoopAsync`: 네트워크 I/O
- `ConcurrentQueue.Enqueue/TryDequeue`: 큐 경합
- `MessageCodec.WriteResponseBody`: 직렬화 비용
- `EventLoop` 스레드: EventLoop 포화 여부

### 2. dotnet-counters

**목적**: 실시간 런타임 메트릭 모니터링

.NET 런타임의 성능 카운터를 실시간으로 수집합니다.

**설치**:
```bash
dotnet tool install -g dotnet-counters
```

**주요 메트릭**:

| 메트릭 | 설명 | 정상 범위 | 병목 징후 |
|--------|------|-----------|-----------|
| `cpu-usage` | CPU 사용률 (%) | < 80% | > 90% (포화) |
| `threadpool-queue-length` | ThreadPool 대기 작업 수 | < 10 | > 100 (고갈) |
| `alloc-rate` | 메모리 할당 속도 (MB/s) | < 500 | > 1000 (GC 압박) |
| `gen-0-gc-count` | Gen0 GC 횟수/초 | < 10 | > 50 (과도한 할당) |
| `gen-1-gc-count` | Gen1 GC 횟수/초 | < 1 | > 5 |
| `gen-2-gc-count` | Gen2 GC 횟수/초 | < 0.1 | > 1 (메모리 누수) |
| `lock-contention-count` | Lock 경합 횟수/초 | < 100 | > 1000 |
| `exception-count` | 예외 발생 횟수/초 | 0 | > 0 |

### 3. perf (Linux only)

**목적**: Context switching 측정

Linux 커널 레벨에서 프로세스의 컨텍스트 스위칭을 측정합니다.

**설치** (Ubuntu/Debian):
```bash
sudo apt-get install linux-tools-common linux-tools-generic
```

**주요 메트릭**:
- `context-switches`: 컨텍스트 스위칭 횟수
- `cpu-migrations`: CPU 코어 간 마이그레이션
- `page-faults`: 페이지 폴트 발생

**병목 기준**:
- Context switches/sec > 100,000: 과도한 스레드 경합
- 스레드 수 / CPU 코어 수 > 10: 스레드 과다

### 4. speedscope.app

**목적**: CPU 프로파일 시각화

dotnet-trace가 생성한 `.nettrace` 파일을 시각적으로 분석합니다.

**사용법**:
1. https://www.speedscope.app 접속
2. `cpu-profile.nettrace` 파일 업로드
3. Flame Graph로 CPU 핫스팟 확인

**Flame Graph 해석**:
- **너비**: 함수가 CPU를 차지한 시간 (%)
- **높이**: 콜스택 깊이
- **색상**: 함수별 구분 (의미 없음)

**확인 순서**:
1. 가장 넓은 박스 찾기 (CPU 핫스팟)
2. 해당 함수의 호출 경로 추적
3. 최적화 가능한 지점 식별

---

## 프로파일링 실행

### 기본 실행

```bash
cd /home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_cs
./profile-benchmark.sh
```

**기본 설정**:
- 연결 수: 100
- 벤치마크 시간: 60초
- 메시지 크기: 1KB
- 최대 동시 요청: 1000
- 워밍업 시간: 10초
- CPU 프로파일링: 30초
- 카운터 모니터링: 40초

### 커스텀 실행

```bash
# 연결 수와 시간 조정
./profile-benchmark.sh <connections> <duration>

# 예시: 200 연결, 120초 테스트
./profile-benchmark.sh 200 120
```

### 실행 흐름

```
[1/7] 프로젝트 빌드
[2/7] 기존 프로세스 정리
[3/7] 벤치마크 서버 시작
[4/7] 벤치마크 클라이언트 시작
[5/7] 워밍업 (10초)
[6/7] 프로파일링 데이터 수집
  └── [6/7-1] CPU 프로파일 (30초)
  └── [6/7-2] 런타임 카운터 (40초)
  └── [6/7-3] Context switching (40초, Linux only)
[7/7] 벤치마크 완료 대기
```

### 결과 위치

```
profiling-results/
└── 20260105_173045/              # 타임스탬프
    ├── cpu-profile.nettrace      # CPU 프로파일
    ├── runtime-counters.txt      # 런타임 메트릭
    ├── perf-stat.txt             # Context switching (Linux only)
    ├── test-summary.txt          # 테스트 요약
    ├── client.log                # 클라이언트 로그
    └── server.log                # 서버 로그
```

---

## 결과 분석

### 1. CPU 프로파일 분석

**파일**: `cpu-profile.nettrace`

**단계**:
1. https://www.speedscope.app 에서 파일 열기
2. "Time Order" 뷰에서 전체 CPU 사용 패턴 확인
3. "Left Heavy" 뷰에서 가장 많은 CPU를 사용한 함수 확인
4. 핫스팟 함수 클릭하여 호출 경로 분석

**확인할 핫스팟**:

#### BaseStage.ProcessOneMessageAsync
- **의미**: 메시지 처리 오버헤드
- **병목 징후**: 전체 CPU의 > 40%
- **해결 방향**: 메시지 배치 처리, Inline dispatch

#### TcpTransportSession.SendLoopAsync
- **의미**: 네트워크 송신 오버헤드
- **병목 징후**: 전체 CPU의 > 30%
- **해결 방향**: SendLoop 통합, Zero-copy

#### ConcurrentQueue.Enqueue/TryDequeue
- **의미**: 큐 경합
- **병목 징후**: 전체 CPU의 > 15%
- **해결 방향**: 큐 분산, Lock-free 자료구조

#### MessageCodec.WriteResponseBody
- **의미**: 직렬화 비용
- **병목 징후**: 전체 CPU의 > 10%
- **해결 방향**: Zero-copy payload, Span<T> 활용

#### EventLoop 스레드 분포
- **확인**: EventLoop-1 ~ EventLoop-16 스레드 CPU 사용률
- **병목 징후**: 특정 EventLoop만 > 90% (불균형)
- **해결 방향**: EventLoop 수 증가 (16 → 32)

### 2. 런타임 카운터 분석

**파일**: `runtime-counters.txt`

**분석 예시**:

```
[System.Runtime]
    cpu-usage (%)                                 92.5
    threadpool-queue-length                       15
    alloc-rate (B / 1 sec)                        450.2
    gen-0-gc-count (Count / 1 sec)                8
    gen-1-gc-count (Count / 1 sec)                1
    gen-2-gc-count (Count / 1 sec)                0
    lock-contention-count (Count / 1 sec)         120
```

**진단**:

| 메트릭 | 값 | 진단 |
|--------|-----|------|
| `cpu-usage` | 92.5% | ⚠️ CPU 포화 (최적화 필요) |
| `threadpool-queue-length` | 15 | ✓ 정상 (ThreadPool 여유) |
| `alloc-rate` | 450 MB/s | ✓ 정상 (GC 압박 낮음) |
| `gen-0-gc-count` | 8/sec | ✓ 정상 |
| `gen-1-gc-count` | 1/sec | ✓ 정상 |
| `gen-2-gc-count` | 0/sec | ✓ 정상 |
| `lock-contention-count` | 120/sec | ⚠️ Lock 경합 있음 |

**문제 패턴**:

1. **CPU 포화 (> 90%)**
   - 원인: 처리 속도 < 메시지 유입 속도
   - 해결: EventLoop 증가, 배치 처리, 병렬화

2. **ThreadPool 고갈 (queue-length > 100)**
   - 원인: ThreadPool 스레드 부족
   - 해결: 불필요한 Task 제거, 전용 스레드 사용

3. **과도한 GC (gen-0 > 50/sec)**
   - 원인: 메모리 할당 과다
   - 해결: ArrayPool, struct 사용, 객체 재사용

4. **Gen2 GC 빈발 (> 1/sec)**
   - 원인: 장기 객체 누적, LOH 객체
   - 해결: 객체 수명 관리, 큰 배열 분할

5. **Lock 경합 (> 1000/sec)**
   - 원인: Lock 기반 동기화 과다
   - 해결: Lock-free 자료구조, 파티셔닝

### 3. Context Switching 분석

**파일**: `perf-stat.txt` (Linux only)

**분석 예시**:

```
 Performance counter stats for process id '12345':

         1,234,567      context-switches
            12,345      cpu-migrations
            56,789      page-faults

      40.123456789 seconds time elapsed
```

**계산**:
- Context switches/sec = 1,234,567 / 40 = **30,864 switches/sec**

**진단 기준**:

| Context switches/sec | 상태 | 조치 |
|----------------------|------|------|
| < 10,000 | ✓ 정상 | - |
| 10,000 ~ 50,000 | ⚠️ 주의 | 스레드 수 검토 |
| > 50,000 | ❌ 심각 | 스레드 통합 필수 |

**현재 PlayHouse 아키텍처**:
```
100 연결 = 100 ReceiveLoop + 100 SendLoop + 16 EventLoop
= 216개 스레드 on 16 CPU 코어
= 평균 13.5 스레드/코어
```

**예상 문제**:
- 과도한 context switching (> 50,000/sec 예상)
- CPU cache thrashing
- EventLoop 레이턴시 증가

**해결책**:
- SendLoop 통합 (100 → 16): -84 스레드
- ReceiveLoop 통합 (100 → 0): -100 스레드
- 최종: 32 스레드 (EventLoop만)

### 4. 종합 진단 체크리스트

#### CPU 병목
- [ ] `cpu-usage` > 90%?
- [ ] CPU 프로파일에서 특정 함수가 > 40%?
- [ ] EventLoop 스레드 불균형?

**→ 해결**: EventLoop 증가, 배치 처리, 병렬화

#### ThreadPool 병목
- [ ] `threadpool-queue-length` > 100?
- [ ] CPU 낮은데 TPS 낮음?

**→ 해결**: ThreadPool 크기 증가, async I/O 최적화

#### GC 병목
- [ ] `alloc-rate` > 1000 MB/s?
- [ ] `gen-0-gc-count` > 50/sec?
- [ ] `gen-2-gc-count` > 1/sec?

**→ 해결**: ArrayPool, struct, 객체 풀링

#### Lock 병목
- [ ] `lock-contention-count` > 1000/sec?
- [ ] CPU 프로파일에서 Monitor.Enter 많음?

**→ 해결**: Lock-free 큐, 파티셔닝, CAS 연산

#### Context Switching 병목
- [ ] Context switches > 50,000/sec?
- [ ] 스레드 수 / CPU 코어 > 10?

**→ 해결**: 스레드 통합, 전용 스레드 풀

---

## 예상 병목 가설

현재 성능(300k TPS)과 시스템 구조를 기반으로 예상되는 병목 지점입니다.

### 가설 1: EventLoop 포화 (확률: 높음)

**증상**:
- CPU 사용률 90%+
- 16개 EventLoop 스레드가 모두 100% CPU 사용
- `BaseStage.ProcessOneMessageAsync`가 CPU 프로파일의 > 40%

**원인**:
- 16개 EventLoop가 100개 Stage 처리
- Stage당 평균 6.25 Stage/EventLoop (불균형 가능)
- 메시지 1개씩 처리 → async/await 오버헤드

**검증 방법**:
1. CPU 프로파일에서 EventLoop 스레드별 CPU 사용률 확인
2. 특정 EventLoop만 과부하인지 확인

**해결 방향**:
1. EventLoop 수 증가 (16 → 32): CPU 하이퍼스레딩 활용
2. 메시지 배치 처리 (1개 → 10개): Task 할당 감소
3. Inline dispatch: 현재 스레드가 자신의 EventLoop면 직접 실행

**예상 효과**: +50% TPS (300k → 450k)

### 가설 2: 과도한 Context Switching (확률: 중간)

**증상**:
- Context switches > 50,000/sec
- 220개 스레드 (100 Send + 100 Receive + 16 EventLoop + 기타)
- 16 CPU 코어 → 평균 13.75 스레드/코어

**원인**:
- 세션당 2개 스레드 (SendLoop, ReceiveLoop)
- ThreadPool 스레드와 전용 스레드 혼재
- CPU cache thrashing

**검증 방법**:
1. `perf stat` 결과에서 context-switches/sec 확인
2. 스레드 수 / CPU 코어 수 비율 계산

**해결 방향**:
1. SendLoop 통합 (100 → 16): 공유 풀 사용
2. ReceiveLoop 통합 (100 → 0): SocketAsyncEventArgs 사용
3. 최종 스레드 수: 32 (EventLoop만)

**예상 효과**: +30% TPS (450k → 585k)

### 가설 3: ConcurrentQueue 경합 (확률: 중간)

**증상**:
- CPU 프로파일에서 `ConcurrentQueue.Enqueue/TryDequeue` > 15%
- `lock-contention-count` > 1000/sec
- 다중 스레드가 동일 Stage 큐에 접근

**원인**:
- `BaseStage._messageQueue`는 Stage당 1개 (공유 자원)
- 여러 ReceiveLoop 스레드가 동일 Stage에 Post
- ConcurrentQueue 내부 lock 경합

**검증 방법**:
1. CPU 프로파일에서 `ConcurrentQueue` 비율 확인
2. `lock-contention-count` 메트릭 확인

**해결 방향**:
1. 파티셔닝: Stage별 전용 수신 스레드
2. Lock-free 큐: 단일 생산자-단일 소비자 최적화
3. Batching: 메시지 묶음으로 Enqueue

**예상 효과**: +15% TPS (585k → 673k)

### 가설 4: 메모리 복사 오버헤드 (확률: 낮음)

**증상**:
- `MessageCodec.WriteResponseBody` > 10%
- `alloc-rate` > 1000 MB/s
- 1KB 메시지 × 300k TPS = 300 MB/s (예상)

**원인**:
- Payload 복사 (일부 경로)
- ArrayPool 버퍼 복사

**검증 방법**:
1. CPU 프로파일에서 직렬화 비용 확인
2. `alloc-rate` 메트릭 확인

**해결 방향**:
1. Zero-copy: Payload Move 시맨틱 엄격 적용
2. Direct write: Payload 버퍼를 NetworkStream에 직접 전달
3. Span<T> 활용: IPayload.DataSpan 추가

**예상 효과**: +20% TPS (673k → 808k)

### 우선순위

프로파일링 결과 확인 후 우선순위를 조정하되, 초기 가설 우선순위는:

1. **High**: EventLoop 포화 (가설 1)
2. **High**: Context Switching (가설 2)
3. **Medium**: ConcurrentQueue 경합 (가설 3)
4. **Low**: 메모리 복사 (가설 4)

---

## 문제 해결

### 도구 설치 오류

**dotnet-trace / dotnet-counters 설치 실패**:
```bash
# 전역 도구 초기화
dotnet tool install -g dotnet-trace --version 9.0.652701
dotnet tool install -g dotnet-counters --version 9.0.652701

# 경로 확인
ls -la ~/.dotnet/tools/

# PATH 추가 (필요시)
export PATH="$PATH:$HOME/.dotnet/tools"
```

**perf 설치 실패 (Linux)**:
```bash
# Ubuntu/Debian
sudo apt-get install linux-tools-common linux-tools-generic linux-tools-$(uname -r)

# 권한 오류 시
sudo sysctl -w kernel.perf_event_paranoid=1
```

### 프로파일링 실패

**서버가 시작되지 않음**:
```bash
# 로그 확인
cat profiling-results/<timestamp>/server.log

# 포트 충돌 확인
lsof -i :16110
lsof -i :5080

# 기존 프로세스 강제 종료
pkill -9 -f "PlayHouse.Benchmark"
```

**CPU 프로파일 수집 실패**:
```bash
# PID 확인
pgrep -f "PlayHouse.Benchmark.Server"

# 수동 수집
dotnet-trace collect --process-id <PID> --duration 00:00:30 \
  --providers Microsoft-DotNETCore-SampleProfiler
```

**카운터 모니터링 오류**:
```bash
# 사용 가능한 카운터 확인
dotnet-counters list

# 특정 카운터만 모니터링
dotnet-counters monitor --process-id <PID> \
  --counters System.Runtime[cpu-usage,threadpool-queue-length]
```

### speedscope.app 오류

**파일 업로드 실패**:
- 파일 크기 제한: < 50MB
- 해결: 프로파일링 시간 단축 (30s → 15s)

**Chrome에서 열리지 않음**:
- 브라우저 메모리 부족
- 해결: Firefox 또는 Edge 사용

**nettrace 파일 손상**:
```bash
# 파일 무결성 확인
dotnet-trace report <file.nettrace>

# 변환 (speedscope 형식)
dotnet-trace convert <file.nettrace> --format speedscope
```

---

## 다음 단계

프로파일링 완료 후:

1. **CPU 핫스팟 식별**
   - speedscope.app에서 가장 넓은 함수 3개 기록
   - 각 함수의 CPU 사용률 (%) 계산

2. **병목 유형 분류**
   - CPU bound: EventLoop, 직렬화, 큐 경합
   - I/O bound: Network I/O, Disk I/O
   - Lock bound: ConcurrentQueue, Monitor

3. **최적화 계획 수립**
   - 예상 효과가 큰 병목부터 해결
   - 리스크가 낮은 최적화 우선 (설정 변경 > 코드 수정 > 아키텍처 변경)

4. **최적화 실행**
   - Phase 1: EventLoop 병렬화 + 메시지 배치 처리 + SendLoop 통합
   - Phase 2: Zero-copy 최적화
   - Phase 3: GC 최적화
   - Phase 4: Inline dispatch + ReceiveLoop 통합

5. **검증**
   - 각 최적화 후 벤치마크 재실행
   - TPS 향상률 측정
   - 회귀 검증 (성능 저하 없는지 확인)

**참고 문서**:
- 최적화 계획: `/home/ulalax/.claude/plans/cheeky-growing-snowglobe.md`
- 벤치마크 가이드: `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_cs/BENCHMARK.md`

---

## 참고 자료

### .NET 성능 분석
- [.NET Performance Documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/)
- [dotnet-trace Tutorial](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)
- [dotnet-counters Tutorial](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)

### 프로파일링 도구
- [speedscope.app](https://www.speedscope.app/)
- [PerfView](https://github.com/microsoft/perfview)
- [perf Linux Tool](https://perf.wiki.kernel.org/index.php/Main_Page)

### 성능 최적화
- [High Performance .NET](https://learn.microsoft.com/en-us/dotnet/standard/performance/)
- [Async Best Practices](https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
- [Memory Management in .NET](https://learn.microsoft.com/en-us/dotnet/standard/automatic-memory-management)
