# ConnectorCallbackPerformanceVerifier 구현 요약

## 개요
Phase 3-2의 일부로 `ConnectorCallbackPerformanceVerifier`를 구현하여 Connector의 RequestCallback 모드에서 MainThreadAction()을 통한 콜백 처리 성능을 검증합니다.

## 구현 파일
- **파일**: `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/verification/PlayHouse.Verification/Verifiers/ConnectorCallbackPerformanceVerifier.cs`
- **카테고리**: `ConnectorCallbackPerformance`
- **테스트 개수**: 2개

## 구현된 테스트

### 1. RequestCallback_8KBMessage_RequiresMainThreadAction
**목적**: 8KB 대용량 메시지를 RequestCallback 모드로 처리할 때 MainThreadAction() 호출이 필수임을 검증

**시나리오**:
- 별도 Connector 인스턴스 생성 (RequestCallback 모드)
- 8KB 메시지 50개 전송
- Timer를 사용해 10ms 간격으로 MainThreadAction() 호출
- 모든 콜백이 5초 이내에 처리되는지 확인

**검증 내용**:
- ✓ SynchronizationContext가 없는 환경에서 RequestCallback 사용
- ✓ MainThreadAction() 호출 시 큐에 쌓인 콜백 처리
- ✓ 8KB 대용량 메시지도 정상 처리
- ✓ Interlocked.Increment를 통한 스레드 안전성 보장

### 2. RequestCallback_MainThreadQueue_RequiresMainThreadAction
**목적**: MainThreadAction() 주기적 호출로 큐에 쌓인 콜백이 처리되는지 검증

**시나리오**:
- 별도 Connector 인스턴스 생성
- 1KB 메시지 10개 전송
- 반복문에서 MainThreadAction() 호출하여 콜백 처리
- 응답 내용을 파싱하여 검증

**검증 내용**:
- ✓ MainThreadAction() 호출로 큐 모드 동작 확인
- ✓ EchoReply 메시지 파싱 및 내용 검증
- ✓ lock을 통한 스레드 안전성 보장

## 주요 구현 특징

### 1. 별도 Connector 인스턴스
```csharp
protected override async Task SetupAsync()
{
    // 이 Verifier는 별도 Connector 인스턴스를 생성하므로 base.SetupAsync() 호출 안 함
    await Task.CompletedTask;
}
```
- ServerContext.Connector를 사용하지 않고 각 테스트에서 별도 인스턴스 생성
- RequestCallback 모드 전용 테스트를 위한 격리

### 2. MainThreadAction() 호출 패턴
**Timer 사용 (8KB 테스트)**:
```csharp
var timer = new Timer(_ =>
{
    try
    {
        connector.MainThreadAction();
    }
    catch { /* Connector가 정리된 경우 무시 */ }
}, null, 0, 10); // 10ms 간격
```

**반복문 사용 (Queue 테스트)**:
```csharp
for (int i = 0; i < 50; i++)
{
    connector.MainThreadAction();
    await Task.Delay(20);

    if (receivedResponses.Count >= 10)
        break;
}
```

### 3. 스레드 안전성
- `Interlocked.Increment`: 카운터 증가
- `lock`: 리스트 접근 보호

### 4. 리소스 정리
```csharp
finally
{
    timer?.Dispose();
    connector.Disconnect();
    await connector.DisposeAsync();

    // Packet 정리
    foreach (var packet in packets)
    {
        packet.Dispose();
    }
}
```

## 테스트 결과

### 실행 결과
```
✓ ConnectorCallbackPerformance: RequestCallback_8KBMessage_RequiresMainThreadAction (219ms)
✓ ConnectorCallbackPerformance: RequestCallback_MainThreadQueue_RequiresMainThreadAction (142ms)
```

### 전체 테스트 현황
- **총 테스트 개수**: 64개
- **통과**: 64개
- **실패**: 0개

## 참고 사항

### E2E 테스트 원칙 준수
1. ✓ 운영 코드에 테스트용 코드 없음
2. ✓ Proto 정의 메시지 사용 (AuthenticateRequest, EchoRequest)
3. ✓ 실제 시스템 전체 흐름 검증
4. ✓ 응답 + 콜백 호출 모두 검증

### CLAUDE.md 규칙 준수
- RequestCallback 모드에서 MainThreadAction() 필요성 명시
- SynchronizationContext 없는 환경 테스트
- Unity Update 루프 시뮬레이션 (10ms 간격)

## 관련 파일
- `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/PlayHouse.Tests.Integration/Play/ConnectorCallbackPerformanceTests.cs` - 기존 통합 테스트 참고
- `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/verification/PlayHouse.Verification/VerificationRunner.cs` - Verifier 등록
