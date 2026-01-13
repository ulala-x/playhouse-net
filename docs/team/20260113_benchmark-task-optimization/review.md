# RunRequestAsyncMode 리뷰

## 주요 이슈 (심각도 순)

1) **동시 요청 시 Connector 스레드 안정성 불명확**
   - 위치: `tests/benchmark_cs/PlayHouse.Benchmark.Client/BenchmarkRunner.cs:152`
   - `maxInFlight` 개의 Worker가 동일한 `ClientConnector`에 대해 `RequestAsync`를 동시에 호출합니다.
   - `ClientConnector`가 thread-safe가 아니라면 내부 상태 경쟁, 패킷 순서 꼬임, 예기치 않은 예외가 발생할 수 있습니다.
   - **제안**: `ClientConnector`의 동시성 보장 여부를 확인하고, 보장되지 않으면 요청을 직렬화하거나(예: 채널/큐 기반) Worker당 별도 커넥터를 사용하도록 설계를 변경하세요.

2) **MainThreadAction 호출 스레드/타이밍의 동기화 부재**
   - 위치: `tests/benchmark_cs/PlayHouse.Benchmark.Client/BenchmarkRunner.cs:176`
   - `MainThreadAction()`이 “메인 스레드”에서 호출된다는 전제가 있는 API라면 Worker 스레드에서 호출하는 것이 위험합니다.
   - 또한 다른 Worker들이 동시에 `RequestAsync`를 호출하는 가운데 `MainThreadAction()`이 동일 커넥터 내부 상태를 건드리면 경쟁 조건이 생길 수 있습니다.
   - **제안**: `MainThreadAction()` 호출 스레드 정책을 명확히 하고, 필요 시 전용 루프(단일 스레드)로 분리하거나 커넥터 접근을 직렬화하세요.

## 개선 제안 (선택)

- **예외 로그 폭주 방지**
  - 위치: `tests/benchmark_cs/PlayHouse.Benchmark.Client/BenchmarkRunner.cs:166`
  - 서버/네트워크 장애 상황에서 Worker 루프가 매 요청마다 에러 로그를 남겨 벤치마크 성능을 왜곡하거나 로그가 과도해질 수 있습니다.
  - **제안**: 카운터 누적 후 주기적 집계 로그(예: 1초당 1회) 또는 샘플링을 고려하세요.

## 확인 질문

- `ClientConnector.RequestAsync`는 동일 인스턴스에서 다중 동시 호출이 공식적으로 지원되나요?
- `MainThreadAction`이 호출되어야 하는 스레드 정책(메인/UI/단일 스레드 등)이 문서화되어 있나요?

## Thread-Safety 검증 결과 (Claude 확인)

`ClientNetwork.cs` 분석 결과, **thread-safe**하게 설계되어 있음을 확인했습니다:

| 필드 | 구현 | Thread-Safety |
|------|------|---------------|
| `_msgSeqCounter` | `Interlocked.Increment` | ✅ |
| `_pendingRequests` | `ConcurrentDictionary` | ✅ |
| `_packetQueue` | `ConcurrentQueue` | ✅ |

**RequestAsync 동시 호출**: 안전함
- 각 요청에 고유 MsgSeq 할당 (Interlocked)
- 응답은 ConcurrentDictionary에서 MsgSeq로 매칭

**MainThreadAction 호출**: 안전함
- `_packetQueue`는 ConcurrentQueue로 lock-free
- Heartbeat 전송도 연결 상태 체크 후 전송

## 간단 요약

- 고정된 Worker로 Task 생성 비용을 줄인 방향은 적절합니다.
- **Thread-safety 확인 완료**: ClientConnector는 동시 RequestAsync 호출을 안전하게 처리합니다.
- 구현 승인됨 (LGTM)
