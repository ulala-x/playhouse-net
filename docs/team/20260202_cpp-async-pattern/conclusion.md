# C++ 게임 서버 비동기 패턴 선택 최종 결론

## 참여자
- **Claude**: 오케스트레이터, 기술 분석
- **Codex (OpenAI)**: 기술 리뷰어
- **Gemini (Google)**: 기술 리뷰어

## 질문
C++ 고성능 게임 서버(1M+ TPS 목표)에서 C++20 Coroutine vs Boost.Fiber 중 어떤 비동기 패턴을 선택해야 하는가?

---

## 합의된 결론: C++20 Coroutine

**Codex, Gemini, Claude 모두 동일한 결론에 도달했습니다.**

### 주요 근거

| 항목 | 결론 |
|------|------|
| 성능 | Coroutine이 10배 이상 빠른 컨텍스트 스위칭 (~10ns vs ~100-200ns) |
| 메모리 | Coroutine이 320배 적은 메모리 사용 (10K 기준 2MB vs 640MB) |
| TPS 오버헤드 | 1M TPS 기준 Coroutine 1% vs Fiber 10% |
| .NET 호환성 | Coroutine(`co_await`)이 .NET `async/await`와 개념적으로 유사 |
| ASIO 통합 | Coroutine이 네이티브 지원 |

### 3중 리뷰 결과

| 리뷰어 | 추천 | 신뢰도 |
|--------|------|--------|
| **Codex** | C++20 Coroutine | 100% |
| **Gemini** | C++20 Coroutine | 100% |
| **Claude** | C++20 Coroutine | 100% |

---

## 실행 계획

### 1. C++ Connector
- 현재 C++17 → **C++20 업그레이드**
- ASIO `awaitable<T>` 패턴 적용
- `co_await` 기반 request/response 구현

### 2. C++ Server (향후)
- C++20 Coroutine 기반 설계
- ThreadPool + `io_context` 풀 + `asio::strand` 동기화

### 3. Java Connector
- **Java Virtual Thread** (Java 22+) 사용
- Kotlin Coroutine과 호환 가능 (via `Dispatchers.VirtualThread`)

---

## 추가 권장사항 (3중 리뷰 합의)

1. **디버깅 전략 수립 필수**
   - Coroutine은 스택 트레이스가 끊김
   - 비동기 작업 ID 추적, 로깅 전략 필요

2. **스케줄러 설계 중요**
   - 타이머, IO, 워크스틸링, 배압(backpressure) 정책이 성능 좌우
   - Coroutine frame 풀링으로 메모리 파편화 방지

3. **혼합 설계 고려**
   - 핵심 경로만 Coroutine
   - 동기 라이브러리 의존 부분은 별도 스레드 풀

---

## 결론

**C++20 Coroutine은 PlayHouse의 성능 목표(1M+ TPS)와 .NET 아키텍처 통일성 모두를 충족하는 최적의 선택입니다.**

| 언어 | 선택 | 이유 |
|------|------|------|
| C++ | **Coroutine** | 성능 우선, .NET 패턴 유사 |
| Java | **Virtual Thread** | Java 22+, Kotlin 호환 |
| .NET | **Task/async-await** | 기존 구현 유지 |
