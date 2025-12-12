# PlayHouse-Net 프로젝트 규칙

## E2E 테스트 원칙

### 운영 코드에 테스트용 코드 금지
- 운영 코드(src/)에 테스트를 위한 하드코딩된 핸들러, Mock, Stub을 넣지 않는다
- E2E 테스트는 실제 시스템 전체 흐름을 검증해야 한다
- 잘못된 예: PlayServer에 `if (msgId.Contains("Echo"))` 같은 테스트용 분기

### E2E 테스트 vs 통합 테스트 구분
- **E2E 테스트**: 클라이언트 공개 API로 검증 가능한 것
  - Connector → PlayServer → Stage → Actor → Response
  - 응답 내용, 콜백 호출, 상태 변경 등
- **통합 테스트**: 서버 내부 상태만 검증 가능한 것
  - SessionManager.SessionCount
  - 내부 타이머 동작
  - AsyncBlock 내부 동작

### 서버 콜백 E2E 검증 방법
| 콜백 | E2E 검증 방법 |
|------|--------------|
| IActor.OnAuthenticate | IsAuthenticated() 상태 + 응답 패킷 |
| IStage.OnDispatch | 응답 패킷 내용 (Stage에서 설정한 필드) |
| IStage.OnJoinStage | 응답 패킷 + 이후 메시지 처리 가능 여부 |
| IStageSender.SendToClient | OnReceive 콜백으로 Push 수신 |
| IActorSender.Reply | RequestAsync 응답으로 확인 |

### E2E 테스트에서 응답 + 콜백 호출 모두 검증
- E2E 테스트는 **응답 검증**과 **콜백 호출 검증** 두 가지를 모두 해야 함
- 테스트 구현체(TestStageImpl, TestActorImpl)에서 콜백 호출을 기록하고 검증
- 예시:
  ```csharp
  // 1. 응답 검증 (클라이언트 API)
  var response = await _connector.RequestAsync(packet);
  response.MsgId.Should().Be("EchoReply");

  // 2. 콜백 호출 검증 (테스트 구현체)
  testStage.ReceivedMsgIds.Should().Contain("EchoRequest");
  testActor.OnAuthenticateCalled.Should().BeTrue();
  ```

## 메시지 정의 규칙

### Proto 메시지 사용
- 테스트에서도 `Packet.Empty("SomeMessage")` 대신 proto 정의 메시지 사용
- 테스트 코드가 API 사용 가이드 역할을 해야 함
- 예시:
  ```csharp
  // 잘못된 예
  using var packet = Packet.Empty("EchoRequest");

  // 올바른 예
  var echoRequest = new EchoRequest { Content = "Hello", Sequence = 1 };
  using var packet = new Packet(echoRequest);
  ```
