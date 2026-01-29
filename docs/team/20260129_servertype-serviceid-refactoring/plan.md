# ServerType/ServiceId 분리 리팩토링 계획

## 1. 목표

### 1.1 현재 문제점
- `ServiceId`가 서버 타입(Play=1, Api=2)을 구분하는 용도로 사용됨
- 같은 타입의 서버를 여러 그룹으로 분리할 수 없음

### 1.2 변경 목표
- `ServerType` enum: 서버 종류 구분 (Play, Api)
- `ServiceId`: 같은 ServerType 내에서 서비스 그룹 구분

```
변경 후 예시:
┌─────────────┬───────────┬─────────────────────────┐
│ ServerType  │ ServiceId │ 용도                     │
├─────────────┼───────────┼─────────────────────────┤
│ Play        │ 1         │ 메인 게임 서버 군        │
│ Play        │ 2         │ PvP 전용 서버 군         │
│ Api         │ 1         │ 일반 API 서버 군         │
│ Api         │ 2         │ 결제 전용 API 서버 군    │
└─────────────┴───────────┴─────────────────────────┘
```

## 2. 범위

### 2.1 수정 대상 파일 (30개+)

| Phase | 영역 | 파일 수 |
|-------|------|---------|
| Phase 1 | Core Types | 3 |
| Phase 2 | Server Info Center | 2 |
| Phase 3 | ISender Interface | 1 |
| Phase 4 | Sender Implementations | 4 |
| Phase 5 | Server Options | 2 |
| Phase 6 | Server Bootstrap & Dispatcher | 5 |
| Phase 7 | Protocol & System | 2 |
| Phase 8 | Communicator & Runtime | 3 |
| Phase 9 | Tests (Unit + E2E + Benchmark) | 10+ |

### 2.2 추가 발견된 수정 대상 (Codex 분석)

**Runtime 계층:**
- `CommunicatorOption.cs` - `WithServerType()` 빌더 추가
- `ZmqPlaySocket.cs` - 헤더 풀 초기화 시 ServerType 반영
- `RoutePacket.cs` - 응답 헤더에 ServerType 필드 반영

**Core 계층:**
- `BaseStage.cs` - 하드코딩된 ServiceId=1 대신 ServerType/ServiceId 사용

### 2.3 주요 API 변경

```csharp
// Before
sender.SendToService(serviceId: 2, packet);  // 2 = API (하드코딩)

// After
sender.SendToService(ServerType.Api, serviceId: 1, packet);  // 명시적
sender.SendToService(ServerType.Play, serviceId: 2, packet); // PvP 서버 군
```

## 3. 접근 방법

### 3.1 구현 순서

```
Step 1: Core Types
  ServiceType.cs → ServerType.cs rename
  IServerInfo.cs → ServerType 속성 추가
  ServerConfig.cs → ServerType 속성 추가
      ↓
Step 2: Server Info Center
  IServerInfoCenter.cs → 새 메서드 시그니처
  XServerInfoCenter.cs → 복합 키 및 새 메서드
      ↓
Step 3: ISender Interface
  ISender.cs → 시그니처 변경
      ↓
Step 4: Sender Implementations
  XSender.cs, XStageSender.cs, XActorSender.cs, ApiSender.cs
      ↓
Step 5: Server Options
  PlayServerOption.cs, ApiServerOption.cs
      ↓
Step 6: Server Bootstrap & Dispatcher
  PlayServer.cs, ApiServer.cs, PlayDispatcher.cs, ApiDispatcher.cs
  CommunicatorOption.cs
      ↓
Step 7: Protocol & System
  route_header.proto → server_type 필드 추가
  StaticSystemController.cs → ServerType 파싱
      ↓
Step 8: Runtime (추가)
  ZmqPlaySocket.cs → 헤더 풀 초기화
  RoutePacket.cs → ServerType 반영
      ↓
Step 9: Tests
  Unit: ServerConfigTests.cs, Extensions/*, ZmqSendRecvTest.cs
  E2E: ServiceRoutingVerifier.cs, ServerContext.cs
  Benchmark: Program.cs 등
```

### 3.2 검증 방법

```bash
# 1. 전체 솔루션 빌드
dotnet build playhouse-net.sln

# 2. 단위 테스트
dotnet test tests/unit/PlayHouse.Unit/PlayHouse.Unit.csproj

# 3. E2E 테스트
dotnet test tests/e2e/PlayHouse.E2E/PlayHouse.E2E.csproj

# 4. 벤치마크 빌드 검증
dotnet build tests/benchmark/benchmark_ss/PlayHouse.Benchmark.SS.PlayServer
dotnet build tests/benchmark/benchmark_ss/PlayHouse.Benchmark.SS.ApiServer
```

### 3.3 Proto 재생성 체크리스트

```bash
# route_header.proto 수정 후 반드시 실행
cd src/PlayHouse
protoc --csharp_out=. Proto/route_header.proto
```

## 4. 설계 결정 사항

### 4.1 결정 완료

| 항목 | 결정 |
|------|------|
| `ServerType` 값 | Play=1, Api=2 (기존 ServiceType 값 유지) |
| `ServiceId` 기본값 | 1 (기존과 동일) |

### 4.2 결정 필요 (구현 시 확정)

| 항목 | 옵션 | 기본 방향 |
|------|------|----------|
| NID 포맷 | `{ServerType}:{ServiceId}:{ServerId}` vs `{ServiceId}:{ServerId}` | 기존 포맷 유지, 필요시 변경 |
| `server_type` 0값 처리 | 에러 vs 기본값(Play) | 기본값(Play)으로 처리 |
| Mixed version 호환성 | 지원 vs 미지원 | Breaking change로 미지원 |

## 5. 예상 산출물

### 5.1 코드 변경
- 30개+ 파일 수정
- `ServiceType` → `ServerType` 이름 변경
- `SendToService`/`RequestToService` 시그니처 변경

### 5.2 문서
- plan.md (본 문서)
- design.md (상세 설계)
- review-*.md (3중 리뷰 결과)
- changes.md (최종 변경사항)

## 6. 위험 요소 및 대응

| 위험 | 대응 |
|------|------|
| 기존 API 호환성 깨짐 | Breaking change로 처리, 마이그레이션 가이드 제공 |
| 테스트 누락 | 전체 솔루션 빌드 + 모든 테스트 프로젝트 실행 |
| Proto 변경 영향 | 클라이언트 측 proto 재생성 필요, 문서화 |
| 헤더 풀 초기화 누락 | ZmqPlaySocket.cs 변경 시 ServerType 초기화 확인 |

## 7. 마이그레이션 가이드 (예시)

### 7.1 코드 변경

```csharp
// Before
options.ServiceId = 1; // Play 서버

// After
options.ServerType = ServerType.Play;
options.ServiceId = 1; // 서비스 그룹 1
```

### 7.2 설정 파일 (appsettings.json)

```json
// Before
{
  "PlayServer": {
    "ServiceId": 1
  }
}

// After
{
  "PlayServer": {
    "ServerType": "Play",
    "ServiceId": 1
  }
}
```

## 8. 참고 문서

- 상세 계획: `doc/plans/servertype-serviceid-refactoring-plan.md`

---

## 리뷰 결과 (Phase 1)

### Gemini 리뷰
- **승인**: 계획서 승인 가능
- 보완 반영: Proto 재생성 체크리스트, 설정 파일 마이그레이션 예시 추가

### Codex 분석
- **승인**: 보완 후 실행 가능
- 보완 반영: 추가 수정 대상 파일, 테스트 범위 확대, 결정 필요 사항 명시
