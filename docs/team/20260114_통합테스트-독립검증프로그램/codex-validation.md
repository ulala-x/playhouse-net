# codex 검증 결과: 통합 테스트 → 독립 검증 프로그램 전환 계획

검증 대상: `docs/team/20260114_통합테스트-독립검증프로그램/plan.md`

## 요약 판단

- 방향성은 타당하나, 기존 통합 테스트 대비 누락 항목과 실행 세부 설계가 부족합니다.
- 계획대로 진행하면 일부 테스트 범위가 유실되거나, 실행 환경에서 실패할 가능성이 있습니다.

## 누락/불일치 사항

1. **테스트 파일 수/매핑 불일치**
   - 계획서는 “15개 파일 → 15개 검증 프로그램”이라고 하나 실제 통합 테스트는 더 많습니다.
   - 누락 후보:
     - `tests/PlayHouse.Tests.Integration/Api/SelfConnectionTests.cs` (Self-connection)
     - `tests/PlayHouse.Tests.Integration/Connector/PacketAutoDisposeTests.cs` (Connector 패킷 자동 Dispose)
     - `tests/PlayHouse.Tests.Integration/Extensions/DIIntegrationTests.cs` (DI 통합)

2. **필수 인프라 타입 누락**
   - DI 통합 및 일부 테스트에서 사용하는 타입들이 계획의 “공유 인프라”에 포함되지 않았습니다.
   - 누락 후보:
     - `tests/PlayHouse.Tests.Integration/Infrastructure/DITestStage.cs`
     - `tests/PlayHouse.Tests.Integration/Infrastructure/DITestActor.cs`
     - `tests/PlayHouse.Tests.Integration/Infrastructure/ITestService.cs`
     - `tests/PlayHouse.Tests.Integration/Infrastructure/Collections.cs`

3. **Fixture 대체 설계 부재**
   - 기존 통합 테스트는 `SinglePlayServerFixture`, `DualPlayServerFixture`, `ApiPlayServerFixture`, `DIPlayServerFixture` 등 xUnit 라이프사이클 기반 Fixture를 사용합니다.
   - 독립 실행 프로그램에서는 동일 기능을 하는 “서버 시작/종료 유틸”이 필요하지만 계획에 구체화되지 않았습니다.

## 실행 가능성/기술적 리스크

1. **`System.CommandLine` 의존성 명시 누락**
   - 샘플은 `System.CommandLine`을 사용하지만 패키지 참조 계획이 없습니다.
   - .NET 8 기본 포함이 아니므로 각 검증 프로그램 csproj에 명시가 필요합니다.

2. **타임아웃 강제 동작 미흡**
   - `VerifierBase.RunTest`에서 `CancellationTokenSource`를 생성하지만 `testFunc`에 전달되지 않아 실제 타임아웃이 동작하지 않습니다.
   - 타임아웃 보장 방식(토큰 전달 또는 `Task.WhenAny` 래핑)이 필요합니다.

3. **서버 종류별 생성 로직 미정**
   - `VerifyUtil.CreateTestServer`는 Play 서버만 생성합니다.
   - `ApiToApi`, `ApiToPlay`, `SelfConnection` 등은 Api 서버/메시 네트워크 구성이 필요하므로 별도 생성 유틸이 필요합니다.

4. **프로젝트 기본 규칙 반영 부족**
   - 계획의 예시 코드에는 네임스페이스 및 XML 문서 주석이 없습니다.
   - 실제 구현 시 파일 스코프 네임스페이스와 public API 문서화 규칙 반영이 필요합니다.

## 보완 권장 사항

1. 누락된 통합 테스트(특히 `SelfConnection`, `PacketAutoDispose`, `DIIntegration`)에 대응하는 검증 프로그램 추가 또는 기존 항목에 병합 여부 명확화.
2. 기존 Fixture를 대체할 수 있는 “서버/메시 구성 유틸” 명세 추가.
3. 검증 프로그램 공통 csproj 템플릿에 필수 패키지(`System.CommandLine`, `Google.Protobuf`, `Grpc.Tools` 등)와 빌드 설정을 명시.
4. 타임아웃과 콜백 폴링 로직을 실제로 강제하는 메커니즘 확정.

