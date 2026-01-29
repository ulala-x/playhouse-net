# ServerType/ServiceId 리팩토링 변경사항

**작성일**: 2026-01-29
**작성자**: Claude (Multi-AI Team Collaboration)
**상태**: 완료

## 개요

`ServiceId`가 서버 타입(Play=1, Api=2)을 구분하는 용도로 오버로딩되어 사용되던 구조를 분리하여, `ServerType` enum과 `ServiceId`를 명확히 구분하는 리팩토링을 수행했습니다.

### 변경 전
```csharp
// ServiceId가 서버 타입과 서비스 그룹을 모두 구분
sender.SendToService(serviceId: 2, packet);  // 2 = API (하드코딩)
```

### 변경 후
```csharp
// ServerType과 ServiceId 분리
sender.SendToService(ServerType.Api, serviceId: 1, packet);  // 명시적
```

---

## 변경된 파일 목록

### 신규 생성 파일

| 파일 | 용도 |
|------|------|
| `src/PlayHouse/Abstractions/Internal/ServerOptionValidator.cs` | 서버 옵션 검증 로직 중앙화 |

### 수정된 파일

#### Abstractions 계층
- `src/PlayHouse/Abstractions/ServerType.cs` - `ServiceIdDefaults.Default` 상수 추가
- `src/PlayHouse/Abstractions/IServerInfoCenter.cs` - `Update()` 메서드 추가

#### Core 계층
- `src/PlayHouse/Core/Shared/XSender.cs` - 헬퍼 메서드 추가 (CreateApiHeader, CreateStageHeader, SendRequest 등)
- `src/PlayHouse/Core/Play/XStageSender.cs` - `BaseStage?` 타입 강화, 타이머 메서드 통합
- `src/PlayHouse/Core/Play/XActorSender.cs` - 필드명 개선 (_sessionNid → _sessionServerId)
- `src/PlayHouse/Core/Play/PlayDispatcher.cs` - 필드명 개선 (_nid → _serverId)
- `src/PlayHouse/Core/Api/ApiDispatcher.cs` - 필드명 개선 (_nid → _serverId)
- `src/PlayHouse/Core/Play/Bootstrap/PlayServerOption.cs` - 공유 검증 로직 적용
- `src/PlayHouse/Core/Api/Bootstrap/ApiServerOption.cs` - 공유 검증 로직 적용

#### Runtime 계층
- `src/PlayHouse/Runtime/ServerMesh/ServerConfig.cs` - `BindAddress`/`ServiceIds` 제거
- `src/PlayHouse/Runtime/ServerMesh/XServerInfoCenter.cs` - `GetServersByService` 헬퍼 추가
- `src/PlayHouse/Runtime/ServerMesh/ServerAddressResolver.cs` - `IServerInfoCenter` 인터페이스 의존
- `src/PlayHouse/Runtime/ServerMesh/Communicator/CommunicatorOption.cs` - 공유 검증 로직 적용

#### 테스트 파일
- `tests/unit/PlayHouse.Unit/Runtime/ServerConfigTests.cs` - `ServiceIds` 참조 제거
- `tests/e2e/PlayHouse.E2E/Verifiers/ServiceRoutingVerifier.cs` - `ServiceIds.Api` → 직접 값 사용

---

## 주요 변경 내용

### 1. 코드 중복 제거

#### ServerOptionValidator (신규)
```csharp
internal static class ServerOptionValidator
{
    internal static void ValidateIdentity(ServerType serverType, string serverId, string bindEndpoint);
    internal static void ValidateRequestTimeout(int requestTimeoutMs);
}
```
- 3개 Option 클래스(ApiServerOption, PlayServerOption, CommunicatorOption)의 검증 로직 통합

#### XSender 헬퍼 메서드
- `CreateApiHeader()` - API 서버 통신용 헤더 생성
- `CreateStageHeader()` - Stage 간 통신용 헤더 생성
- `CreateReplyHeader()` - 응답 헤더 생성
- `SendRequest()` - 요청 전송 + 응답 등록 통합
- `ResolveServiceServer()` - 서비스 서버 조회 통합

#### XServerInfoCenter
- `GetServersByService()` private 헬퍼 메서드로 필터링 로직 통합

### 2. 네이밍 개선

| 변경 전 | 변경 후 | 위치 |
|---------|---------|------|
| `_nid` | `_serverId` | ApiDispatcher, PlayDispatcher |
| `_sessionNid` | `_sessionServerId` | XActorSender |
| `_apiNid` | `_apiServerId` | XActorSender |
| `_refreshTask` | `_refreshLoopTask` | ServerAddressResolver |
| `BindAddress` | `BindEndpoint` | ServerConfig |

### 3. 구조 최적화

#### 인터페이스 기반 의존성
- `ServerAddressResolver`가 `XServerInfoCenter` 대신 `IServerInfoCenter` 인터페이스에 의존
- 테스트 용이성 및 유연성 향상

#### XStageSender 타입 강화
- `object? _baseStage` → `BaseStage? _baseStage`
- 불필요한 `as` 캐스팅 제거

#### 타이머 메서드 통합
- `AddRepeatTimer`, `AddCountTimer`에서 중복 로직을 `AddTimer` 헬퍼로 통합

### 4. 레거시 코드 제거

사용자 요청에 따라 호환성 코드 완전 제거:

| 항목 | 변경 |
|------|------|
| `ServiceIds` 클래스 | 완전 제거 |
| `BindAddress` 속성 | 완전 제거 (BindEndpoint만 유지) |
| `ServiceType` enum | 완전 제거 (ServerType으로 대체) |

---

## API 변경 사항

### SendToService/RequestToService

```csharp
// Before
sender.SendToService(ushort serviceId, IPacket packet);

// After
sender.SendToService(ServerType serverType, ushort serviceId, IPacket packet,
    ServerSelectionPolicy policy = ServerSelectionPolicy.RoundRobin);
```

### ServerConfig

```csharp
// Before
var config = new ServerConfig(serviceId, serverId, bindAddress);

// After
var config = new ServerConfig(ServerType.Play, serviceId, serverId, bindEndpoint);
```

### ServiceIdDefaults

```csharp
// 기본 ServiceId 값
public static class ServiceIdDefaults
{
    public const ushort Default = 1;
}
```

---

## 테스트 결과

```
Test run completed.
Passed!  - Failed: 0, Passed: 372, Skipped: 0, Total: 372
```

- 모든 단위 테스트 및 E2E 테스트 통과
- 2개 테스트 감소 (ServiceIdsTests 제거)

---

## 마이그레이션 가이드

### 코드 변경

```csharp
// Before
options.ServiceId = 1; // Play 서버

// After
options.ServerType = ServerType.Play;
options.ServiceId = 1; // 서비스 그룹 1 (기본값)
```

### SendToService 호출

```csharp
// Before
sender.SendToService(2, packet); // 2 = Api

// After
sender.SendToService(ServerType.Api, 1, packet);
```

### ServerConfig 생성

```csharp
// Before
var config = ServerConfig.Create(serviceId: 1, serverId: "play-1", port: 5555);

// After
var config = ServerConfig.Create(ServerType.Play, serviceId: 1, serverId: "play-1", port: 5555);
```

---

## 리뷰 결과 요약

| 리뷰어 | 결과 | 비고 |
|--------|------|------|
| Claude | ✅ LGTM | 기능 변경 없이 구조만 개선됨 |
| Codex | ✅ 완료 | 리팩토링 수행 |
| Gemini | ⚠️ Rate Limited | Claude 대체 리뷰 완료 |

---

## 참고 문서

- 계획서: `docs/team/20260129_servertype-serviceid-refactoring/plan.md`
- 설계서: `docs/team/20260129_servertype-serviceid-refactoring/design.md`
- 리팩토링 노트: `docs/team/20260129_servertype-serviceid-refactoring/refactoring.md`
- Claude 리뷰: `docs/team/20260129_servertype-serviceid-refactoring/review-claude.md`
- Gemini 리뷰: `docs/team/20260129_servertype-serviceid-refactoring/review-gemini.md`
