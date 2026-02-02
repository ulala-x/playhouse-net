# C++ 게임 서버 엔진 비동기 패턴 선택 상담

## 배경
- PlayHouse라는 게임 서버 프레임워크 개발 중
- .NET 버전은 Task/async-await (Coroutine 방식) 사용, 10K CCU에서 400K TPS 달성
- C++ 버전 개발 시 Coroutine vs Fiber 선택 필요

## 비교 요약

| 항목 | C++20 Coroutine | Boost.Fiber |
|------|-----------------|-------------|
| 타입 | Stackless | Stackful |
| 메모리 (10K) | ~2MB | ~640MB |
| 컨텍스트 스위칭 | ~10ns | ~100-200ns |
| 1M TPS 오버헤드 | ~1% | ~10% |
| 사용성 | co_await 전염 | 동기 코드처럼 작성 |
| 기존 라이브러리 | 래핑 필요 | 그대로 사용 |
| ASIO 통합 | 네이티브 지원 | 추가 작업 필요 |

## 현재 결론
- C++ 서버/커넥터: Coroutine (성능 우선, .NET 패턴과 유사)
- Java 커넥터: Virtual Thread (Java 22+)

## 질문
1. C++ 고성능 게임 서버에서 Coroutine vs Fiber 중 어떤 것을 추천하나요?
2. 1M+ TPS 목표 시 Coroutine이 맞는 선택인가요?
3. 다른 고려사항이 있나요?
