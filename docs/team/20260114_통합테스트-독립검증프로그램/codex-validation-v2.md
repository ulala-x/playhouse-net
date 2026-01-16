# 통합 테스트 → 독립 검증 프로그램 계획 검증 (Codex v2)

검증 대상: `/home/ulalax/.claude/plans/partitioned-drifting-lemur.md`

## 0. 결론 요약
- **73개 테스트 포함 여부**: 기존 Integration 테스트 73개 전부가 계획의 18개 카테고리에 매핑됨을 확인.
- **18개 Verifier 매핑**: 카테고리별 개수는 정확하나, **다중 서버(ZMQ) 동적 포트 처리 방식은 수정 필요**.
- **프로젝트 구조 실행 가능성**: 큰 틀은 실행 가능하나, **csproj 참조 경로 오류**와 **TAP 결과 파일/로그 경로 불일치**로 인해 바로 빌드/CI 실패 위험.
- **기술 구현 가능성**: 대부분 구현 가능하나, **ZMQ 동적 포트 실제 값 조회가 불가**하여 다중 서버 테스트는 고정 포트 또는 별도 포트 예약 전략 필요.
- **Gemini 리뷰 반영**: 문서상 반영은 있으나 일부는 실제 동작 불가/불완전.

---

## 1. 73개 테스트 케이스 포함 여부 (누락 검증)
**결론: 누락 없음.**

`tests/PlayHouse.Tests.Integration`의 `[Fact]/[Theory]` 총 73개를 확인했고, 계획의 18개 카테고리 합계도 73개로 일치합니다.

### 카테고리별 원본 테스트 매핑
- Connection (8): `Connector/ConnectionTests.cs`
- Messaging (10): `Connector/MessagingTests.cs`
- Push (2): `Connector/PushTests.cs`
- PacketAutoDispose (6): `Connector/PacketAutoDisposeTests.cs`
- ServerLifecycle (1): `Connector/ServerLifecycleTests.cs`
- ActorCallback (3): `Play/ActorCallbackTests.cs`
- ActorSender (4): `Play/ActorSenderTests.cs`
- StageCallback (5): `Play/StageCallbackTests.cs`
- StageToStage (5): `Play/StageToStageTests.cs` (class 명칭은 ISenderTests)
- StageToApi (5): `Play/StageToApiTests.cs`
- ApiToApi (5): `Api/ApiToApiTests.cs`
- ApiToPlay (3): `Api/ApiToPlayTests.cs`
- SelfConnection (2): `Api/SelfConnectionTests.cs`
- AsyncBlock (2): `Play/AsyncBlockTests.cs`
- Timer (2): `Play/TimerTests.cs`
- AutoDispose (3): `Play/AutoDisposeTests.cs`
- DIIntegration (5): `Extensions/DIIntegrationTests.cs`
- ConnectorCallbackPerformance (2): `Play/ConnectorCallbackPerformanceTests.cs`

**주의**: `ServerDisconnect_OnDisconnectCallbackInvoked` 테스트명이 Connection/ServerLifecycle에 중복 존재하므로, 결과 집계 시 **카테고리+테스트명 조합으로 고유성 보장** 필요.

---

## 2. 18개 Verifier 클래스 매핑 검증
**결론: 매핑 자체는 정확.**

계획의 Verifier 구성은 현재 통합 테스트 파일 구조와 정확히 대응됩니다. 다만 다음 보완 필요:
- **GetTestCount 구현 누락**: 계획상 VerifierBase의 `GetTestCount()`가 0 반환. 메뉴/CI에 실제 개수 표기를 위해 **각 Verifier 오버라이드 또는 리플렉션 구현 필요**.
- **서버 구성 분기 필요**: 단일 서버/다중 서버/DI 서버 생성 시 구성 옵션이 다르므로, `ServerFactory`는 **카테고리별 구성 분기**를 지원해야 함.

---

## 3. 프로젝트 구조 실행 가능성 검증
**결론: 구조는 타당하지만 즉시 빌드 실패 요인 있음.**

### 확인된 실행 리스크
1. **ProjectReference 경로 오류**
   - 계획: `..\..\src\PlayHouse.Connector\PlayHouse.Connector.csproj`
   - 실제 경로: `connector/PlayHouse.Connector/PlayHouse.Connector.csproj`
   - 결과: 빌드 실패

2. **TAP 결과 파일 미생성**
   - CI 아티팩트 업로드 경로: `verification-results.tap`
   - 계획의 Program.cs는 **stdout 출력만** 하고 **파일 생성 로직 없음**

3. **로그 경로 불일치**
   - Program.cs: `logDir = "logs"` (실행 시점 기준 상대 경로)
   - GitHub Actions 업로드: `tests/verification/PlayHouse.Verification/logs/`
   - CI에서 로그 업로드 실패 가능

4. **실행 스크립트 경로/동작은 대체로 가능**
   - `scripts/run-verification.sh`는 현재 구조에서 실행 가능하나, 실질적으로는 단일 프로젝트 실행만 하므로 복잡한 탐색 로직은 불필요.

---

## 4. 기술적 구현 실현 가능성 검증
**결론: 대다수 가능하나, ZMQ 동적 포트 이슈는 설계 수정 필요.**

### 핵심 이슈
1. **ZMQ 동적 포트(0) 사용 시 실제 포트 조회 불가**
   - `PlayServer.ActualTcpPort`는 존재하지만, **ZMQ BindEndpoint의 실제 포트 조회 API 없음**
   - 다중 서버 테스트(StageToStage, ApiToApi, ApiToPlay)는 **서로의 ZMQ 주소를 알아야 하므로** 0 포트 사용 시 연결 불가능
   - 현재 통합 테스트도 이 카테고리는 **고정 포트**를 사용함

2. **ServerFactory 설계 보완 필요**
   - 단일 서버: TCP/BindEndpoint 모두 0 사용 가능
   - 다중 서버: **고정 포트 또는 포트 예약 전략** 필요
   - DI 테스트: `ITestService` 등록 + `ServiceProvider` 접근 필요

3. **성능 테스트 임계값 과도하게 강화됨**
   - 기존 테스트: 8KB 메시지 **1초 이내**
   - 계획: 100ms/50ms 기준은 환경에 따라 **CI 불안정** 가능성 큼

---

## 5. Gemini 리뷰 반영 사항 검증
**결론: 문서상 반영은 있으나 일부는 작동 불가/불완전.**

- **동적 포트 할당**: TCP는 가능하지만, ZMQ는 실제 포트 조회 불가로 다중 서버 테스트에 적용 불가
- **CI 로그 아티팩트**: logDir 및 업로드 경로가 불일치
- **성능 임계값**: 반영되어 있으나 기준이 기존 테스트 대비 과도함
- **Self-Verification 테스트**: 계획에 포함됨 (실행 시점/조건 제안은 타당)
- **자동 탐색 스크립트**: 동작 가능하지만 사실상 단일 프로젝트 실행만 필요
- **공유 인프라 의존성 중앙화**: 구조적으로 타당

---

## 6. 빠진 내용 / 개선점

### 필수 수정/보완
1. **ZMQ 포트 전략 명시**
   - 다중 서버 카테고리는 고정 포트 유지 또는 `GetAvailablePort` 방식으로 **미리 포트 확보 후 BindEndpoint 설정** 필요

2. **csproj 참조 경로 수정**
   - `PlayHouse.Connector` 경로는 `connector/PlayHouse.Connector/PlayHouse.Connector.csproj`로 변경 필요

3. **TAP 결과 파일 생성**
   - CI 아티팩트 업로드를 위해 `verification-results.tap` 파일 생성 로직 추가 필요

4. **로그 경로 일관성**
   - Program.cs의 `logDir`와 GitHub Actions 업로드 경로 통일 필요

5. **GetTestCount 구현**
   - 메뉴 표시 및 정확한 카운트 출력 필요

### 권장 개선
- **중복 테스트명 충돌 방지**: 결과 키를 `CategoryName + TestName`으로 구성
- **Timeout 정책 정리**: 각 Verifier별로 현재 통합 테스트의 timeout/Delay 패턴을 반영
- **Static 상태 초기화 체크리스트**: `TestStageImpl.ResetAll()` 등 리셋 호출을 카테고리별 Setup에 명시
- **실행 옵션 문서화**: `--ci`, `--category`, `--verbose` 처리 방식에 대한 간단한 사용 예시 추가

---

## 부록: 73개 테스트 목록 근거
- Integration 테스트 `[Fact]/[Theory]` 총 73개 확인.
- 카테고리별 합산 73개.
- 해당 근거 파일: `tests/PlayHouse.Tests.Integration/**.cs`

끝.
