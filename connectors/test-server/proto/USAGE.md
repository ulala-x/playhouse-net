# PlayHouse Test Server Proto - 사용 가이드

## 개요

이 문서는 PlayHouse 커넥터 테스트 서버의 Protocol Buffers 메시지 정의 사용 방법을 설명합니다.

## 메시지 카테고리

### 1. 인증 (Authentication)

클라이언트 커넥터의 인증 흐름을 검증합니다.

**메시지:**
- `AuthenticateRequest` → `AuthenticateReply`

**사용 예:**
```csharp
// 서버 측 (TestActor.OnAuthenticate)
var authRequest = packet.Parse<AuthenticateRequest>();
var reply = new AuthenticateReply
{
    AccountId = authRequest.UserId,
    Success = authRequest.Token == "valid-token",
    ReceivedUserId = authRequest.UserId,
    ReceivedToken = authRequest.Token
};
return new Packet(reply);
```

```javascript
// 클라이언트 측 (JavaScript)
const authRequest = new AuthenticateRequest();
authRequest.setUserId("test-user");
authRequest.setToken("valid-token");

const reply = await connector.authenticateAsync(authRequest);
console.log(`Authenticated: ${reply.getSuccess()}`);
```

### 2. Stage 생성 (Stage Creation)

Stage의 OnCreate 메서드를 검증합니다.

**메시지:**
- `CreateStagePayload` → `CreateStageReply`

**사용 예:**
```csharp
// 서버 측 (TestStage.OnCreate)
public override IPacket OnCreate(IPacket createPacket)
{
    var payload = createPacket.Parse<CreateStagePayload>();

    return new Packet(new CreateStageReply
    {
        ReceivedStageName = payload.StageName,
        ReceivedMaxPlayers = payload.MaxPlayers,
        Created = true
    });
}
```

### 3. Echo 테스트 (Basic Request/Reply)

기본적인 Request/Reply 패턴을 검증합니다.

**메시지:**
- `EchoRequest` → `EchoReply`

**사용 예:**
```csharp
// 서버 측 (TestActor.OnDispatch)
var echoRequest = packet.Parse<EchoRequest>();
var reply = new EchoReply
{
    Content = echoRequest.Content,
    Sequence = echoRequest.Sequence,
    ProcessedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
};
return new Packet(reply);
```

```java
// 클라이언트 측 (Java/Android)
EchoRequest request = EchoRequest.newBuilder()
    .setContent("Hello")
    .setSequence(1)
    .build();

Packet packet = new Packet(request);
Packet replyPacket = connector.requestAsync(packet).get();

EchoReply reply = replyPacket.parse(EchoReply.class);
System.out.println("Echo: " + reply.getContent());
```

### 4. 에러 처리 (Error Handling)

에러 응답 및 예외 처리를 검증합니다.

**메시지:**
- `FailRequest` → `FailReply`
- `NoResponseRequest` (타임아웃)

**사용 예:**
```csharp
// 서버 측 - 에러 응답
var failRequest = packet.Parse<FailRequest>();
if (failRequest.ErrorCode != 0)
{
    throw new Exception($"Test error: {failRequest.ErrorMessage}");
}

// 서버 측 - 타임아웃 테스트
var noResponseRequest = packet.Parse<NoResponseRequest>();
await Task.Delay(noResponseRequest.DelayMs);
// 응답 없음 (클라이언트에서 타임아웃 발생)
```

```typescript
// 클라이언트 측 (TypeScript)
try {
    const failRequest = new FailRequest();
    failRequest.setErrorCode(TestErrorCodes.GeneralError);
    failRequest.setErrorMessage("Test error");

    await connector.requestAsync(failRequest);
} catch (error) {
    console.error("Expected error:", error);
}
```

### 5. 페이로드 크기 테스트 (Large Payload)

큰 데이터 전송 및 압축을 검증합니다.

**메시지:**
- `LargePayloadRequest` → `LargePayloadReply`

**사용 예:**
```csharp
// 서버 측
var request = packet.Parse<LargePayloadRequest>();
var data = new byte[request.SizeBytes];
new Random().NextBytes(data);

var reply = new LargePayloadReply
{
    Data = Google.Protobuf.ByteString.CopyFrom(data),
    OriginalSize = request.SizeBytes,
    Compressed = request.SizeBytes > 1024 // 1KB 이상이면 압축
};
return new Packet(reply);
```

```cpp
// 클라이언트 측 (C++/Unreal)
playhouse::test::LargePayloadRequest request;
request.set_size_bytes(1024 * 1024); // 1MB

auto replyPacket = connector->RequestAsync(
    std::make_shared<Packet>(request)
).get();

auto reply = replyPacket->Parse<playhouse::test::LargePayloadReply>();
UE_LOG(LogTemp, Log, TEXT("Received %d bytes, compressed: %s"),
    reply.data().size(),
    reply.compressed() ? TEXT("yes") : TEXT("no"));
```

### 6. 브로드캐스트 (Push Notification)

서버에서 클라이언트로의 Push 메시지를 검증합니다.

**메시지:**
- `BroadcastRequest` → (서버 내부 처리)
- `BroadcastNotify` → 클라이언트 Push

**사용 예:**
```csharp
// 서버 측 (TestStage.OnDispatch)
var broadcastRequest = packet.Parse<BroadcastRequest>();

// 모든 클라이언트에게 브로드캐스트
var notify = new BroadcastNotify
{
    EventType = "broadcast",
    Data = broadcastRequest.Content,
    FromAccountId = sender.AccountId,
    SenderId = sender.AccountId
};

_stage.Broadcast(new Packet(notify));
```

```javascript
// 클라이언트 측 - Callback 등록
connector.onNotify((packet) => {
    if (packet.msgId === 'BroadcastNotify') {
        const notify = packet.parse(BroadcastNotify);
        console.log(`Broadcast from ${notify.getSenderId()}: ${notify.getData()}`);
    }
});

// 브로드캐스트 요청
const request = new BroadcastRequest();
request.setContent("Hello everyone!");
await connector.sendAsync(request);
```

### 7. API Server 통신

Play Server와 API Server 간 통신을 검증합니다.

**메시지:**
- `ApiEchoRequest` → `ApiEchoReply`

**사용 예:**
```csharp
// API Server 측 (ApiHandlerController)
var apiRequest = packet.Parse<ApiEchoRequest>();
return new Packet(new ApiEchoReply
{
    Content = apiRequest.Content,
    ServerId = _config.ServerId
});

// Play Server에서 API 호출 (TestActor.OnDispatch)
var reply = await _sender.RequestToApi<ApiEchoReply>(
    new Packet(new ApiEchoRequest { Content = "Hello API" })
);
```

### 8. Timer 테스트

타이머 콜백을 검증합니다.

**메시지:**
- `StartRepeatTimerRequest` / `StartCountTimerRequest` → `StartTimerReply`
- `TimerTickNotify` → 클라이언트 Push

**사용 예:**
```csharp
// 서버 측 (TestActor)
var timerRequest = packet.Parse<StartRepeatTimerRequest>();
long timerId = _stage.AddTimer(
    timerRequest.InitialDelayMs,
    timerRequest.IntervalMs,
    async () =>
    {
        var notify = new TimerTickNotify
        {
            TickNumber = ++_tickCount,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TimerType = "repeat"
        };
        await _sender.SendAsync(new Packet(notify));
    }
);

return new Packet(new StartTimerReply { TimerId = timerId, Success = true });
```

### 9. Benchmark

성능 측정을 위한 메시지입니다.

**메시지:**
- `BenchmarkRequest` → `BenchmarkReply`

**사용 예:**
```csharp
// 서버 측
var benchRequest = packet.Parse<BenchmarkRequest>();
var payload = new byte[benchRequest.ResponseSize];

var reply = new BenchmarkReply
{
    Sequence = benchRequest.Sequence,
    ProcessedAt = DateTimeOffset.UtcNow.Ticks,
    Payload = Google.Protobuf.ByteString.CopyFrom(payload)
};
return new Packet(reply);
```

## 커넥터별 사용 예제

### C# Connector

```csharp
using PlayHouse.Connector;
using PlayHouse.TestServer.Proto;

var connector = new PlayHouseConnector("ws://localhost:8080");
await connector.ConnectAsync();

// 인증
var authReply = await connector.AuthenticateAsync(
    new AuthenticateRequest { UserId = "user1", Token = "token123" }
);

// Echo
var echoReply = await connector.RequestAsync(
    new EchoRequest { Content = "Hello", Sequence = 1 }
);

// Push 수신
connector.OnNotify(packet =>
{
    if (packet.MsgId == TestMessageIds.BroadcastNotify)
    {
        var notify = packet.Parse<BroadcastNotify>();
        Console.WriteLine($"Broadcast: {notify.Data}");
    }
});
```

### JavaScript Connector

```javascript
import { PlayHouseConnector } from '@playhouse/connector';
import { EchoRequest, BroadcastNotify } from './proto/test_messages_pb';

const connector = new PlayHouseConnector('ws://localhost:8080');
await connector.connect();

// 인증
const authRequest = new AuthenticateRequest();
authRequest.setUserId('user1');
authRequest.setToken('token123');
const authReply = await connector.authenticateAsync(authRequest);

// Echo
const echoRequest = new EchoRequest();
echoRequest.setContent('Hello');
echoRequest.setSequence(1);
const echoReply = await connector.requestAsync(echoRequest);

// Push 수신
connector.onNotify((packet) => {
    if (packet.msgId === 'BroadcastNotify') {
        const notify = BroadcastNotify.deserializeBinary(packet.data);
        console.log(`Broadcast: ${notify.getData()}`);
    }
});
```

### Java Connector

```java
import com.playhouse.connector.PlayHouseConnector;
import com.playhouse.test.proto.TestMessagesProto.*;

PlayHouseConnector connector = new PlayHouseConnector("ws://localhost:8080");
connector.connect().get();

// 인증
AuthenticateRequest authRequest = AuthenticateRequest.newBuilder()
    .setUserId("user1")
    .setToken("token123")
    .build();
AuthenticateReply authReply = connector.authenticateAsync(authRequest).get();

// Echo
EchoRequest echoRequest = EchoRequest.newBuilder()
    .setContent("Hello")
    .setSequence(1)
    .build();
EchoReply echoReply = connector.requestAsync(echoRequest).get();

// Push 수신
connector.onNotify(packet -> {
    if ("BroadcastNotify".equals(packet.getMsgId())) {
        BroadcastNotify notify = packet.parse(BroadcastNotify.class);
        System.out.println("Broadcast: " + notify.getData());
    }
});
```

### C++ Connector (Unreal)

```cpp
#include "PlayHouseConnector.h"
#include "proto/test_messages.pb.h"

TSharedPtr<FPlayHouseConnector> Connector =
    MakeShared<FPlayHouseConnector>(TEXT("ws://localhost:8080"));
Connector->Connect();

// 인증
playhouse::test::AuthenticateRequest AuthRequest;
AuthRequest.set_user_id("user1");
AuthRequest.set_token("token123");

auto AuthReplyPacket = Connector->AuthenticateAsync(
    MakeShared<FPacket>(AuthRequest)
).get();

// Echo
playhouse::test::EchoRequest EchoRequest;
EchoRequest.set_content("Hello");
EchoRequest.set_sequence(1);

auto EchoReplyPacket = Connector->RequestAsync(
    MakeShared<FPacket>(EchoRequest)
).get();

// Push 수신
Connector->OnNotify([](TSharedPtr<FPacket> Packet)
{
    if (Packet->GetMsgId() == TEXT("BroadcastNotify"))
    {
        auto Notify = Packet->Parse<playhouse::test::BroadcastNotify>();
        UE_LOG(LogTemp, Log, TEXT("Broadcast: %s"),
            UTF8_TO_TCHAR(Notify.data().c_str()));
    }
});
```

## 테스트 시나리오 예제

### 완전한 E2E 테스트 흐름

```csharp
// 1. 연결 및 인증
var connector = new PlayHouseConnector("ws://localhost:8080");
await connector.ConnectAsync();

var authReply = await connector.AuthenticateAsync(
    new AuthenticateRequest { UserId = "test-user", Token = "valid-token" }
);
Assert.True(authReply.Success);

// 2. Echo 테스트
var echoReply = await connector.RequestAsync(
    new EchoRequest { Content = "Hello", Sequence = 1 }
);
Assert.Equal("Hello", echoReply.Content);
Assert.Equal(1, echoReply.Sequence);

// 3. Push 메시지 수신 검증
var notifyReceived = new TaskCompletionSource<BroadcastNotify>();
connector.OnNotify(packet =>
{
    if (packet.MsgId == TestMessageIds.BroadcastNotify)
    {
        notifyReceived.SetResult(packet.Parse<BroadcastNotify>());
    }
});

await connector.SendAsync(new BroadcastRequest { Content = "Test broadcast" });
var notify = await notifyReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
Assert.Equal("Test broadcast", notify.Data);

// 4. 에러 처리 검증
await Assert.ThrowsAsync<Exception>(async () =>
{
    await connector.RequestAsync(new FailRequest
    {
        ErrorCode = TestErrorCodes.GeneralError,
        ErrorMessage = "Test error"
    });
});

// 5. 연결 해제
await connector.DisconnectAsync();
```

## 메시지 ID 사용

`TestMessages.cs`에서 제공하는 상수를 사용하세요.

```csharp
using PlayHouse.TestServer.Shared;

// 메시지 ID 확인
if (packet.MsgId == TestMessageIds.EchoRequest)
{
    // ...
}

// Switch 문
switch (packet.MsgId)
{
    case TestMessageIds.EchoRequest:
        return HandleEcho(packet);
    case TestMessageIds.BroadcastRequest:
        return HandleBroadcast(packet);
    default:
        throw new InvalidOperationException($"Unknown message: {packet.MsgId}");
}
```

## 주의사항

1. **메시지 Dispose**
   - Packet은 IDisposable을 구현하므로 using 문을 사용하세요.
   ```csharp
   using var packet = new Packet(echoRequest);
   ```

2. **비동기 처리**
   - 모든 Request/Reply는 async/await를 사용하세요.
   ```csharp
   var reply = await connector.RequestAsync(request);
   ```

3. **에러 처리**
   - 타임아웃 및 네트워크 에러를 처리하세요.
   ```csharp
   try
   {
       var reply = await connector.RequestAsync(request, TimeSpan.FromSeconds(5));
   }
   catch (TimeoutException)
   {
       // 타임아웃 처리
   }
   catch (Exception ex)
   {
       // 기타 에러 처리
   }
   ```

4. **Callback 등록**
   - Push 메시지는 반드시 OnNotify 콜백을 등록해야 합니다.
   ```csharp
   connector.OnNotify(packet =>
   {
       // Push 메시지 처리
   });
   ```
