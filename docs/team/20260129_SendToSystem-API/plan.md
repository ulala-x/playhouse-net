# SendToSystem/RequestToSystem API 추가 계획

## 목표
ISender 인터페이스에 시스템 메시지 전송 메서드를 추가하여 ISystemController.Handles()로 등록된 핸들러에 메시지를 보낼 수 있도록 함.

## 배경
- 현재 `ISystemController.Handles(ISystemHandlerRegister)`로 시스템 메시지 핸들러를 등록할 수 있음
- 하지만 ISender에는 시스템 메시지를 보내는 메서드가 없음
- SendToApi, SendToStage, SendToApiService는 있지만 SendToSystem은 없음

## 요구사항
1. 모든 서버(API + Play)에 시스템 메시지 전송 가능
2. ServerId 기반 전송 (ServiceId 기반은 불필요)
3. RouteHeader에 is_system 플래그로 시스템 메시지 명시적 식별

## 추가할 메서드
```csharp
#region System Communication

/// <summary>
/// Sends a one-way system message to a server.
/// </summary>
void SendToSystem(string serverId, IPacket packet);

/// <summary>
/// Sends a system request with a callback for the reply.
/// </summary>
void RequestToSystem(string serverId, IPacket packet, ReplyCallback replyCallback);

/// <summary>
/// Sends a system request and awaits the reply.
/// </summary>
Task<IPacket> RequestToSystem(string serverId, IPacket packet);

#endregion
```

## 수정 파일 목록

| 파일 | 변경 내용 |
|------|----------|
| `src/PlayHouse/Proto/route_header.proto` | `bool is_system = 11` 필드 추가 |
| `src/PlayHouse/Abstractions/ISender.cs` | System Communication 메서드 선언 추가 |
| `src/PlayHouse/Core/Shared/XSender.cs` | SendToSystem/RequestToSystem 구현 |
| `src/PlayHouse/Core/Play/XActorSender.cs` | ISender 위임 메서드 추가 (Codex 피드백) |
| `src/PlayHouse/Runtime/ServerMesh/PlaySocket/ZmqPlaySocket.cs` | 헤더 풀 IsSystem 초기화 (Codex 피드백) |
| `src/PlayHouse/Core/Api/Bootstrap/ApiServer.cs` | SystemDispatcher 생성 및 시스템 메시지 라우팅 |
| `src/PlayHouse/Core/Play/Bootstrap/PlayServer.cs` | SystemDispatcher 생성 및 시스템 메시지 라우팅 |

## Codex 리뷰 피드백 (반영됨)
1. **XActorSender 업데이트**: ISender를 구현하므로 새 메서드 위임 추가 필요
2. **ZmqPlaySocket 헤더 풀**: IsSystem 필드 false로 초기화 필요
3. **SystemDispatcher 통합**: 현재 미사용 - 서버에서 생성 및 ISystemController.Handles() 등록
4. **하위 호환성**: header.IsSystem || SystemDispatcher.IsSystemMessage(msgId) 체크

## 제한사항 (Phase 1)
- **시스템 핸들러에서 Reply 미지원**: ISystemController.Handles()의 핸들러는 ISender context가 없어 Reply 불가
- SendToSystem은 fire-and-forget, RequestToSystem은 응답 대기만 가능
- 향후 ISystemSender 도입으로 Reply 지원 확장 가능

## 구현 접근 방법

### 1. 프로토콜 변경 (route_header.proto)
RouteHeader에 `is_system` 필드 추가로 시스템 메시지 명시적 식별

### 2. 인터페이스 확장 (ISender.cs)
기존 SendToApi/RequestToApi 패턴을 따라 System Communication region 추가

### 3. 구현체 (XSender.cs)
- `CreateSystemHeader()`: 기존 CreateApiHeader에 IsSystem=true 설정
- `SendToSystem()`: SendToApi와 동일 패턴
- `RequestToSystem()`: RequestToApi와 동일 패턴

### 4. 수신 처리 (ApiServer.cs, PlayServer.cs)
- `header.IsSystem == true` 체크
- SystemDispatcher.DispatchAsync()로 라우팅

## 예상 산출물
1. 수정된 소스 코드 (5개 파일)
2. 빌드 성공 확인
3. 기존 테스트 통과 확인

## 검증 방법
1. `dotnet build` 성공
2. `dotnet test` 모든 테스트 통과
3. proto 컴파일 확인 (RouteHeader.IsSystem 속성 생성)
