# Benchmark Task Optimization Plan

## 목표
- 서버 부하(요청 수/동시성)는 동일하게 유지하면서 클라이언트 측 오버헤드(GC/ThreadPool 경합)를 줄인다.

## 범위
- 대상 파일: `tests/benchmark_cs/PlayHouse.Benchmark.Client/BenchmarkRunner.cs`
- 변경 포인트: `RunRequestAsyncMode`의 요청 처리 구조

## 현상 요약
- 요청마다 `Task.Run()` 호출로 대량의 Task 객체가 생성됨
- 수만~수십만 Task로 GC 압박 및 ThreadPool 경쟁이 발생함

## 개선 전략
- 요청마다 Task 생성 → 고정된 Worker Task 풀로 전환
- `maxInFlight` 개의 Worker만 생성하고, 각 Worker가 루프에서 요청 처리
- Task 개수를 `maxInFlight`로 고정(예: 200)
- Worker 루프 내에서 주기적으로 `MainThreadAction()` 호출
- Latency 측정은 순수 네트워크 요청 구간만 포함되도록 계측 범위 정리
- Worker 루프 내 예외 발생 시 로깅 후 계속 진행하도록 처리

## 실행 계획
1) **현 구조 파악**
   - `RunRequestAsyncMode` 내부 흐름 및 요청/동시성 제어 방식 확인
   - 현재 `maxInFlight`가 의미하는 바와 사용 위치 확인
   - `MainThreadAction()` 호출 위치 및 필요 주기 파악
   - Latency 측정 구간(시작/종료 타이밍) 확인

2) **Worker 기반 설계 확정**
   - `maxInFlight` 수 만큼 Worker Task 생성
   - 각 Worker는 반복 루프에서 요청을 수행하고 종료 조건을 확인
   - 요청 카운트/종료 시점 관리 방식 정의(예: Interlocked로 remaining 감소)
   - Worker 루프 내 `MainThreadAction()` 호출 정책 정의(예: N회당 1회)
   - Latency 측정 범위를 네트워크 요청 전/후로 한정

3) **구현**
   - `Task.Run()` per-request 로직 제거
   - Worker Task 생성 및 루프 기반 처리 로직 추가
   - 요청 수/부하 동일성 보장(요청 수, 동시성, pacing 유지)
   - Worker 루프 내부에서 예외를 캐치하고 다음 반복으로 진행
   - Latency 계측 시작/종료 위치를 네트워크 요청 구간으로 정리
   - `MainThreadAction()`이 Worker 루프에서 주기적으로 호출되도록 반영

4) **검증**
   - 기존과 동일한 요청 수/동시성 유지 확인
   - 실행 중 Task 수가 `maxInFlight`로 제한되는지 확인
   - 벤치마크 지표(Throughput/Latency) 비교
   - Latency 측정값이 네트워크 요청 시간만 반영되는지 로그/계측 확인
   - 예외 발생 시 Worker가 계속 동작하는지 확인

## 리스크 및 고려사항
- Worker 루프 종료 조건 오류 시 요청 누락 가능
- 예외 처리 시 Worker 전체 종료 위험
- 클라이언트 측 지연/대기 로직이 기존과 동일하게 유지되는지 확인 필요
- `MainThreadAction()` 호출 누락 시 메인 스레드 관련 부작용 가능
- Latency 측정 범위 변경에 따른 지표 비교 시 주의 필요

## 산출물
- `BenchmarkRunner.cs` 내 Worker 기반 동시성 구조 반영
- 변경 설계 및 검증 요약(필요 시 문서 보강)
