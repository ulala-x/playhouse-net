# 튜토리얼: 채팅방 만들기

> 예상 소요 시간: 30분
> 난이도: 초급
> 목표: PlayHouse의 핵심 개념(Stage, Actor, 메시지)을 익히고 실제 작동하는 채팅방 서버 구축

## 완성된 결과 미리보기

이 튜토리얼을 완료하면 다음 기능을 가진 채팅방 서버를 만들 수 있습니다:

- **다중 사용자 채팅**: 여러 클라이언트가 동시에 채팅방에 참가
- **실시간 메시지 브로드캐스트**: 한 사용자의 메시지가 모든 참가자에게 전달
- **입장/퇴장 알림**: 사용자가 들어오거나 나갈 때 자동 알림
- **닉네임 설정**: 인증 시 닉네임 지정
- **참가자 목록**: 현재 채팅방에 있는 사람들 조회

```
[채팅방 입장]
User1: Hello!
-> 모든 참가자에게 "User1: Hello!" 전달

[User2 입장]
-> 모든 참가자에게 "User2님이 입장했습니다" 알림

User2: Hi everyone!
-> 모든 참가자에게 "User2: Hi everyone!" 전달
```

## 목차

1. [프로젝트 설정](#1-프로젝트-설정)
2. [Proto 메시지 정의](#2-proto-메시지-정의)
3. [ChatRoomStage 구현](#3-chatroomstage-구현)
4. [ChatActor 구현](#4-chatactor-구현)
5. [서버 구성](#5-서버-구성)
6. [클라이언트 테스트](#6-클라이언트-테스트)
7. [실행 및 테스트](#7-실행-및-테스트)
8. [다음 단계](#다음-단계)

---

## 1. 프로젝트 설정

### Step 1.1: 프로젝트 생성

```bash
dotnet new console -n ChatRoomServer
cd ChatRoomServer
```

### Step 1.2: 필요한 패키지 설치

```bash
dotnet add package PlayHouse
dotnet add package Google.Protobuf
dotnet add package Grpc.Tools
```

### Step 1.3: 디렉토리 구조 생성

```bash
mkdir Proto
mkdir Stages
mkdir Actors
```

최종 디렉토리 구조:
```
ChatRoomServer/
├── ChatRoomServer.csproj
├── Program.cs
├── Proto/
│   └── chat_messages.proto
├── Stages/
│   └── ChatRoomStage.cs
└── Actors/
    └── ChatActor.cs
```

---

## 2. Proto 메시지 정의

### Step 2.1: Proto 파일 생성

**학습 목표**: Protobuf를 사용한 타입 안전 메시지 정의

`Proto/chat_messages.proto` 파일을 생성하고 다음 내용을 추가하세요:

```protobuf
syntax = "proto3";

package chatroom;

option csharp_namespace = "ChatRoomServer.Proto";

// ============================================
// 인증 관련 메시지
// ============================================

// 클라이언트 → 서버: 인증 요청 (닉네임 설정)
message AuthenticateRequest {
    string nickname = 1;  // 사용자가 원하는 닉네임
}

// 서버 → 클라이언트: 인증 응답
message AuthenticateReply {
    bool success = 1;
    string account_id = 2;    // 할당된 AccountId
    string nickname = 3;       // 설정된 닉네임
}

// ============================================
// 채팅 메시지
// ============================================

// 클라이언트 → 서버: 채팅 메시지 전송
message SendChatRequest {
    string message = 1;
}

// 서버 → 클라이언트: 채팅 메시지 전송 확인
message SendChatReply {
    bool success = 1;
    int64 timestamp = 2;  // 서버에서 메시지를 받은 시간
}

// 서버 → 클라이언트: 채팅 메시지 브로드캐스트 (Push)
message ChatNotify {
    string sender_id = 1;
    string sender_nickname = 2;
    string message = 3;
    int64 timestamp = 4;
}

// ============================================
// 채팅방 참가/퇴장
// ============================================

// 서버 → 클라이언트: 사용자 입장 알림 (Push)
message UserJoinedNotify {
    string account_id = 1;
    string nickname = 2;
    int32 total_users = 3;  // 현재 총 참가자 수
}

// 서버 → 클라이언트: 사용자 퇴장 알림 (Push)
message UserLeftNotify {
    string account_id = 1;
    string nickname = 2;
    int32 total_users = 3;
}

// ============================================
// 채팅방 정보 조회
// ============================================

// 클라이언트 → 서버: 현재 참가자 목록 요청
message GetUsersRequest {
}

// 서버 → 클라이언트: 참가자 목록 응답
message GetUsersReply {
    repeated UserInfo users = 1;
}

message UserInfo {
    string account_id = 1;
    string nickname = 2;
    bool is_connected = 3;  // 현재 연결 상태
}
```

### Step 2.2: Proto 컴파일 설정

`ChatRoomServer.csproj` 파일을 열고 `<ItemGroup>` 섹션에 다음을 추가하세요:

```xml
<ItemGroup>
  <Protobuf Include="Proto\chat_messages.proto" GrpcServices="None" />
</ItemGroup>
```

### Step 2.3: 빌드하여 C# 코드 생성

```bash
dotnet build
```

이제 `ChatRoomServer.Proto` 네임스페이스 하위에 메시지 클래스들이 자동 생성됩니다.

---

## 3. ChatRoomStage 구현

### Step 3.1: 기본 구조 작성

**학습 목표**: Stage의 생명주기와 플레이어 관리

`Stages/ChatRoomStage.cs` 파일을 생성하세요:

```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using ChatRoomServer.Proto;

namespace ChatRoomServer.Stages;

/// <summary>
/// 채팅방을 나타내는 Stage
/// - 여러 사용자(Actor)가 입장하여 채팅 가능
/// - 메시지 브로드캐스트 처리
/// </summary>
public class ChatRoomStage : IStage
{
    // Stage 통신 및 관리 기능을 제공하는 Sender
    public IStageSender StageSender { get; }

    // 채팅방에 참가한 사용자들 (AccountId -> Actor)
    private readonly Dictionary<string, IActor> _users = new();

    // 사용자별 닉네임 매핑 (AccountId -> Nickname)
    private readonly Dictionary<string, string> _nicknames = new();

    // 채팅방 이름
    private string _roomName = "";

    public ChatRoomStage(IStageSender stageSender)
    {
        StageSender = stageSender;
    }

    // ... 생명주기 메서드들은 아래에서 구현
}
```

### Step 3.2: Stage 생성 (OnCreate)

**학습 목표**: Stage 초기화 및 생성 응답

`ChatRoomStage` 클래스에 다음 메서드를 추가하세요:

```csharp
/// <summary>
/// Stage가 생성될 때 호출됩니다.
/// </summary>
public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
{
    // 채팅방 이름 설정 (StageId를 이름으로 사용)
    _roomName = $"Room-{StageSender.StageId}";

    Console.WriteLine($"[ChatRoom] Created: {_roomName}");

    // 빈 응답 반환 (클라이언트는 Connect 성공만 확인)
    var reply = Packet.Empty("CreateStageReply");
    return Task.FromResult<(bool, IPacket)>((true, reply));
}

/// <summary>
/// Stage 생성 후 추가 설정
/// 여기서는 특별한 작업 없음
/// </summary>
public Task OnPostCreate()
{
    return Task.CompletedTask;
}

/// <summary>
/// Stage가 종료될 때 호출됩니다.
/// </summary>
public Task OnDestroy()
{
    Console.WriteLine($"[ChatRoom] Destroyed: {_roomName}");
    _users.Clear();
    _nicknames.Clear();
    return Task.CompletedTask;
}
```

### Step 3.3: 사용자 입장 처리 (OnJoinStage)

**학습 목표**: Actor 입장 검증 및 환영 메시지

```csharp
/// <summary>
/// Actor가 Stage에 입장하려고 할 때 호출됩니다.
/// </summary>
public Task<bool> OnJoinStage(IActor actor)
{
    var accountId = actor.ActorSender.AccountId;

    // Actor를 채팅방 참가자 목록에 추가
    _users[accountId] = actor;

    Console.WriteLine($"[ChatRoom] User joining: {accountId}");

    // 입장 허용
    return Task.FromResult(true);
}

/// <summary>
/// Actor가 입장한 후 호출됩니다.
/// 다른 사용자들에게 입장 알림을 브로드캐스트합니다.
/// </summary>
public Task OnPostJoinStage(IActor actor)
{
    var accountId = actor.ActorSender.AccountId;

    // 닉네임 가져오기 (ChatActor에서 인증 시 설정됨)
    var nickname = _nicknames.GetValueOrDefault(accountId, "Unknown");

    Console.WriteLine($"[ChatRoom] {nickname} joined ({_users.Count} users)");

    // 모든 사용자에게 입장 알림 브로드캐스트
    var notify = new UserJoinedNotify
    {
        AccountId = accountId,
        Nickname = nickname,
        TotalUsers = _users.Count
    };
    BroadcastToAll(notify);

    return Task.CompletedTask;
}
```

### Step 3.4: 연결 상태 변경 처리

**학습 목표**: 재연결/연결 끊김 감지

```csharp
/// <summary>
/// Actor의 네트워크 연결 상태가 변경될 때 호출됩니다.
/// </summary>
public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
{
    var accountId = actor.ActorSender.AccountId;
    var nickname = _nicknames.GetValueOrDefault(accountId, "Unknown");

    if (isConnected)
    {
        Console.WriteLine($"[ChatRoom] {nickname} reconnected");
    }
    else
    {
        Console.WriteLine($"[ChatRoom] {nickname} disconnected");
    }

    return ValueTask.CompletedTask;
}
```

### Step 3.5: 메시지 처리 (OnDispatch)

**학습 목표**: 클라이언트 메시지 처리 및 브로드캐스트

```csharp
/// <summary>
/// 클라이언트로부터 메시지를 받았을 때 호출됩니다.
/// </summary>
public Task OnDispatch(IActor actor, IPacket packet)
{
    // MsgId에 따라 처리 분기
    switch (packet.MsgId)
    {
        case "SendChatRequest":
            HandleSendChat(actor, packet);
            break;

        case "GetUsersRequest":
            HandleGetUsers(actor, packet);
            break;

        default:
            Console.WriteLine($"[ChatRoom] Unknown message: {packet.MsgId}");
            actor.ActorSender.Reply(500); // 에러 코드 반환
            break;
    }

    return Task.CompletedTask;
}

/// <summary>
/// 서버 간 메시지 처리 (이 튜토리얼에서는 사용하지 않음)
/// </summary>
public Task OnDispatch(IPacket packet)
{
    return Task.CompletedTask;
}
```

### Step 3.6: 채팅 메시지 핸들러

**학습 목표**: Request-Response 패턴과 브로드캐스트

```csharp
/// <summary>
/// 채팅 메시지 전송 요청 처리
/// </summary>
private void HandleSendChat(IActor actor, IPacket packet)
{
    var request = SendChatRequest.Parser.ParseFrom(packet.Payload.DataSpan);
    var accountId = actor.ActorSender.AccountId;
    var nickname = _nicknames.GetValueOrDefault(accountId, "Unknown");
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    Console.WriteLine($"[ChatRoom] {nickname}: {request.Message}");

    // 1. 발신자에게 전송 성공 응답
    var reply = new SendChatReply
    {
        Success = true,
        Timestamp = timestamp
    };
    actor.ActorSender.Reply(CPacket.Of(reply));

    // 2. 모든 사용자에게 채팅 메시지 브로드캐스트 (Push)
    var chatNotify = new ChatNotify
    {
        SenderId = accountId,
        SenderNickname = nickname,
        Message = request.Message,
        Timestamp = timestamp
    };
    BroadcastToAll(chatNotify);
}
```

### Step 3.7: 사용자 목록 조회 핸들러

```csharp
/// <summary>
/// 현재 참가자 목록 조회 요청 처리
/// </summary>
private void HandleGetUsers(IActor actor, IPacket packet)
{
    var reply = new GetUsersReply();

    foreach (var (accountId, userActor) in _users)
    {
        var nickname = _nicknames.GetValueOrDefault(accountId, "Unknown");
        reply.Users.Add(new UserInfo
        {
            AccountId = accountId,
            Nickname = nickname,
            IsConnected = true // 실제로는 연결 상태 확인 필요
        });
    }

    actor.ActorSender.Reply(CPacket.Of(reply));
}
```

### Step 3.8: 유틸리티 메서드

**학습 목표**: 브로드캐스트 패턴

```csharp
/// <summary>
/// 모든 사용자에게 메시지 브로드캐스트
/// </summary>
private void BroadcastToAll(Google.Protobuf.IMessage message)
{
    var packet = CPacket.Of(message);

    foreach (var user in _users.Values)
    {
        user.ActorSender.SendToClient(packet);
    }
}

/// <summary>
/// 특정 사용자를 제외한 나머지에게 브로드캐스트
/// </summary>
private void BroadcastToOthers(IActor sender, Google.Protobuf.IMessage message)
{
    var packet = CPacket.Of(message);
    var senderId = sender.ActorSender.AccountId;

    foreach (var user in _users.Values)
    {
        if (user.ActorSender.AccountId != senderId)
        {
            user.ActorSender.SendToClient(packet);
        }
    }
}

/// <summary>
/// 닉네임 등록 (ChatActor에서 호출됨)
/// </summary>
public void RegisterNickname(string accountId, string nickname)
{
    _nicknames[accountId] = nickname;
}
```

---

## 4. ChatActor 구현

### Step 4.1: 기본 구조 작성

**학습 목표**: Actor의 생명주기와 인증

`Actors/ChatActor.cs` 파일을 생성하세요:

```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using ChatRoomServer.Proto;
using ChatRoomServer.Stages;

namespace ChatRoomServer.Actors;

/// <summary>
/// 개별 클라이언트(사용자)를 나타내는 Actor
/// - 인증 처리 (닉네임 설정)
/// - AccountId 관리
/// </summary>
public class ChatActor : IActor
{
    public IActorSender ActorSender { get; }

    private string _nickname = "";

    public ChatActor(IActorSender actorSender)
    {
        ActorSender = actorSender;
    }

    // ... 생명주기 메서드들은 아래에서 구현
}
```

### Step 4.2: Actor 생성 및 소멸

```csharp
/// <summary>
/// Actor가 생성될 때 호출됩니다.
/// </summary>
public Task OnCreate()
{
    Console.WriteLine("[ChatActor] Actor created");
    return Task.CompletedTask;
}

/// <summary>
/// Actor가 소멸될 때 호출됩니다.
/// </summary>
public Task OnDestroy()
{
    Console.WriteLine($"[ChatActor] {_nickname} ({ActorSender.AccountId}) destroyed");
    return Task.CompletedTask;
}
```

### Step 4.3: 인증 처리

**학습 목표**: AccountId 설정 및 닉네임 등록 (중요!)

```csharp
/// <summary>
/// 클라이언트 인증을 처리합니다.
/// ⚠️ 중요: AccountId를 반드시 설정해야 합니다!
/// </summary>
public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
{
    // 1. 인증 요청 파싱
    var request = AuthenticateRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);
    _nickname = string.IsNullOrWhiteSpace(request.Nickname)
        ? "Guest"
        : request.Nickname;

    // 2. AccountId 생성 및 설정 (필수!)
    // 실제 서비스에서는 토큰 검증 후 DB에서 조회
    var accountId = Guid.NewGuid().ToString();
    ActorSender.AccountId = accountId;

    Console.WriteLine($"[ChatActor] Authenticated: {_nickname} ({accountId})");

    // 3. 인증 성공 응답
    var reply = new AuthenticateReply
    {
        Success = true,
        AccountId = accountId,
        Nickname = _nickname
    };

    return Task.FromResult<(bool, IPacket?)>((true, CPacket.Of(reply)));
}

/// <summary>
/// 인증 후 호출됩니다.
/// Stage에 닉네임을 등록합니다.
/// </summary>
public Task OnPostAuthenticate()
{
    // Stage에 닉네임 등록
    // 주의: 이 시점에서 Stage에 접근하려면 Stage 인스턴스가 필요
    // 실제로는 Stage의 OnJoinStage/OnPostJoinStage에서 닉네임 처리

    Console.WriteLine($"[ChatActor] Post-authenticate: {_nickname}");
    return Task.CompletedTask;
}

/// <summary>
/// 닉네임 getter (Stage에서 접근용)
/// </summary>
public string GetNickname() => _nickname;
```

**왜 이렇게 하나요?**
- `AccountId`는 PlayHouse에서 사용자를 식별하는 핵심 값입니다
- 인증 시 반드시 설정해야 하며, 설정하지 않으면 연결이 끊어집니다
- 실제 서비스에서는 JWT 토큰이나 세션 ID를 검증하고 DB에서 사용자 정보를 조회합니다

---

## 5. 서버 구성

### Step 5.1: ChatRoomStage에서 닉네임 처리 수정

**학습 목표**: Stage와 Actor 간 데이터 전달

`ChatRoomStage.cs`의 `OnJoinStage` 메서드를 수정하여 닉네임을 가져옵니다:

```csharp
public Task<bool> OnJoinStage(IActor actor)
{
    var accountId = actor.ActorSender.AccountId;

    // Actor를 채팅방 참가자 목록에 추가
    _users[accountId] = actor;

    // ChatActor에서 닉네임 가져오기
    if (actor is ChatActor chatActor)
    {
        var nickname = chatActor.GetNickname();
        _nicknames[accountId] = nickname;
    }

    Console.WriteLine($"[ChatRoom] User joining: {accountId}");

    return Task.FromResult(true);
}
```

### Step 5.2: Program.cs 작성

**학습 목표**: Bootstrap을 사용한 서버 시작

`Program.cs` 파일을 다음과 같이 작성하세요:

```csharp
using Microsoft.Extensions.Logging;
using PlayHouse.Core.Play.Bootstrap;
using ChatRoomServer.Stages;
using ChatRoomServer.Actors;

Console.WriteLine("=== ChatRoom Server Starting ===");

// PlayServer Bootstrap 생성 및 구성
var server = new PlayServerBootstrap()
    .Configure(options =>
    {
        // 서버 기본 설정
        options.ServerId = "chat-server-1";
        options.BindEndpoint = "tcp://127.0.0.1:11200";  // 서버간 통신용
        options.TcpPort = 12000;                          // 클라이언트 연결용

        // 인증 메시지 설정
        options.AuthenticateMessageId = "AuthenticateRequest";

        // 기본 Stage 타입 (Connect 시 타입 미지정 시 사용)
        options.DefaultStageType = "ChatRoom";
    })
    // ChatRoom Stage와 ChatActor 등록
    .UseStage<ChatRoomStage, ChatActor>("ChatRoom")

    // 로깅 설정
    .UseLoggerFactory(LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    }))
    .Build();

// 서버 시작
await server.StartAsync();

Console.WriteLine("=== ChatRoom Server Started ===");
Console.WriteLine($"Client Port: 12000");
Console.WriteLine($"Press Ctrl+C to stop");

// 종료 시그널 대기
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("\n=== Server Stopping ===");
};

try
{
    await Task.Delay(-1, cts.Token);
}
catch (TaskCanceledException)
{
    // Ctrl+C로 종료
}

// 서버 정리
await server.StopAsync();
Console.WriteLine("=== Server Stopped ===");
```

**왜 이렇게 하나요?**
- `PlayServerBootstrap`: 간편한 서버 구성을 위한 빌더 패턴
- `UseStage<TStage, TActor>`: Stage와 Actor 타입을 함께 등록
- `TcpPort`: 클라이언트가 연결할 포트
- `BindEndpoint`: 다른 서버와 통신할 때 사용 (고급 기능)

---

## 6. 클라이언트 테스트

### Step 6.1: 테스트 클라이언트 프로젝트 생성

```bash
dotnet new console -n ChatRoomClient
cd ChatRoomClient
dotnet add package PlayHouse.Connector
dotnet add package Google.Protobuf
```

### Step 6.2: Proto 파일 복사

서버 프로젝트의 `Proto/chat_messages.proto`를 클라이언트 프로젝트로 복사하세요.

```bash
# ChatRoomClient 디렉토리에서 실행
mkdir Proto
cp ../ChatRoomServer/Proto/chat_messages.proto Proto/
```

`ChatRoomClient.csproj`에 Proto 컴파일 설정 추가:

```xml
<ItemGroup>
  <Protobuf Include="Proto\chat_messages.proto" GrpcServices="None" />
</ItemGroup>
```

### Step 6.3: 클라이언트 코드 작성

**학습 목표**: Connector를 사용한 서버 연결 및 메시지 송수신

`Program.cs`:

```csharp
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using ChatRoomServer.Proto;

Console.WriteLine("=== ChatRoom Client ===");

// 닉네임 입력
Console.Write("Enter your nickname: ");
var nickname = Console.ReadLine() ?? "Guest";

// Connector 생성
var connector = new ClientConnector();
connector.Init(new ConnectorConfig
{
    RequestTimeoutMs = 10000
});

// Push 메시지 수신 핸들러 등록
connector.SetOnReceive(OnReceivePush);

try
{
    // 1. 서버 연결 (StageId = 1, StageType = "ChatRoom")
    Console.WriteLine("Connecting to server...");
    var stageId = 1L;
    var connected = await connector.ConnectAsync("127.0.0.1", 12000, stageId, "ChatRoom");
    if (!connected)
    {
        Console.WriteLine("❌ Connection failed");
        return;
    }
    Console.WriteLine("✅ Connected!");

    // 2. 인증 (닉네임 설정)
    Console.WriteLine($"Authenticating as '{nickname}'...");
    var authRequest = new AuthenticateRequest { Nickname = nickname };
    using var authPacket = new Packet(authRequest);
    using var authResponse = await connector.AuthenticateAsync(authPacket);

    if (!connector.IsAuthenticated())
    {
        Console.WriteLine("❌ Authentication failed");
        return;
    }

    var authReply = AuthenticateReply.Parser.ParseFrom(authResponse.Payload.DataSpan);
    Console.WriteLine($"✅ Authenticated! AccountId: {authReply.AccountId}");

    // 3. 참가자 목록 조회
    using var getUsersReq = new Packet(new GetUsersRequest());
    using var getUsersRes = await connector.RequestAsync(getUsersReq);
    var usersReply = GetUsersReply.Parser.ParseFrom(getUsersRes.Payload.DataSpan);

    Console.WriteLine($"\n📋 Current users ({usersReply.Users.Count}):");
    foreach (var user in usersReply.Users)
    {
        Console.WriteLine($"  - {user.Nickname} ({user.AccountId})");
    }

    // 4. 채팅 메시지 송수신
    Console.WriteLine("\n💬 Chat started! Type your message (or 'quit' to exit):");

    while (true)
    {
        // 콜백 폴링 (Push 메시지 수신 처리)
        connector.MainThreadAction();

        // 사용자 입력 확인
        if (Console.KeyAvailable)
        {
            var message = Console.ReadLine();

            if (message == "quit")
                break;

            if (!string.IsNullOrWhiteSpace(message))
            {
                // 채팅 메시지 전송
                var chatRequest = new SendChatRequest { Message = message };
                using var chatPacket = new Packet(chatRequest);
                using var chatResponse = await connector.RequestAsync(chatPacket);

                var chatReply = SendChatReply.Parser.ParseFrom(chatResponse.Payload.DataSpan);
                if (!chatReply.Success)
                {
                    Console.WriteLine("❌ Failed to send message");
                }
            }
        }

        await Task.Delay(10); // CPU 사용률 조절
    }

    // 5. 연결 종료
    connector.Disconnect();
    Console.WriteLine("\n👋 Disconnected from server");
}
finally
{
    await connector.DisposeAsync();
}

// Push 메시지 수신 콜백
void OnReceivePush(IPacket packet)
{
    switch (packet.MsgId)
    {
        case "ChatNotify":
            var chatNotify = ChatNotify.Parser.ParseFrom(packet.Payload.DataSpan);
            Console.WriteLine($"[{chatNotify.SenderNickname}] {chatNotify.Message}");
            break;

        case "UserJoinedNotify":
            var joinNotify = UserJoinedNotify.Parser.ParseFrom(packet.Payload.DataSpan);
            Console.WriteLine($"✅ {joinNotify.Nickname} joined ({joinNotify.TotalUsers} users)");
            break;

        case "UserLeftNotify":
            var leftNotify = UserLeftNotify.Parser.ParseFrom(packet.Payload.DataSpan);
            Console.WriteLine($"❌ {leftNotify.Nickname} left ({leftNotify.TotalUsers} users)");
            break;

        default:
            Console.WriteLine($"Unknown push: {packet.MsgId}");
            break;
    }
}
```

**왜 이렇게 하나요?**
- `ConnectAsync`: Stage에 연결 (Stage가 없으면 자동 생성)
- `AuthenticateAsync`: Actor 생성 및 인증
- `RequestAsync`: Request-Response 패턴 (채팅 전송)
- `SetOnReceive`: Push 메시지 수신 콜백 등록 (입장 알림, 채팅 수신)
- `MainThreadAction`: 큐에 쌓인 콜백을 메인 스레드에서 실행

---

## 7. 실행 및 테스트

### Step 7.1: 서버 실행

터미널 1:
```bash
cd ChatRoomServer
dotnet run
```

출력:
```
=== ChatRoom Server Starting ===
=== ChatRoom Server Started ===
Client Port: 12000
Press Ctrl+C to stop
```

### Step 7.2: 클라이언트 1 실행

터미널 2:
```bash
cd ChatRoomClient
dotnet run
```

입력:
```
Enter your nickname: Alice
```

출력:
```
Connecting to server...
✅ Connected!
Authenticating as 'Alice'...
✅ Authenticated! AccountId: 12345...

📋 Current users (1):
  - Alice (12345...)

💬 Chat started! Type your message (or 'quit' to exit):
```

### Step 7.3: 클라이언트 2 실행

터미널 3:
```bash
cd ChatRoomClient
dotnet run
```

입력:
```
Enter your nickname: Bob
```

**Alice의 화면에 출력:**
```
✅ Bob joined (2 users)
```

**Bob의 화면에 출력:**
```
📋 Current users (2):
  - Alice (12345...)
  - Bob (67890...)
```

### Step 7.4: 채팅 테스트

**Bob이 입력:**
```
Hello Alice!
```

**Alice의 화면:**
```
[Bob] Hello Alice!
```

**Alice가 입력:**
```
Hi Bob!
```

**Bob의 화면:**
```
[Alice] Hi Bob!
```

### Step 7.5: 서버 로그 확인

서버 터미널(터미널 1)에서 다음과 같은 로그를 확인할 수 있습니다:

```
[ChatRoom] Created: Room-1
[ChatActor] Authenticated: Alice (12345...)
[ChatRoom] User joining: 12345...
[ChatRoom] Alice joined (1 users)
[ChatActor] Authenticated: Bob (67890...)
[ChatRoom] User joining: 67890...
[ChatRoom] Bob joined (2 users)
[ChatRoom] Bob: Hello Alice!
[ChatRoom] Alice: Hi Bob!
```

---

## 축하합니다! 🎉

첫 PlayHouse 채팅방 서버를 성공적으로 구축했습니다!

### 배운 내용

1. **Stage**: 여러 사용자가 모이는 공간 (채팅방)
   - `OnCreate`: Stage 생성 및 초기화
   - `OnJoinStage`: 사용자 입장 처리
   - `OnDispatch`: 메시지 처리

2. **Actor**: 개별 사용자를 나타냄
   - `OnAuthenticate`: 인증 및 AccountId 설정 (필수!)
   - `ActorSender.AccountId`: 사용자 식별자

3. **메시지 패턴**:
   - **Request-Response**: `SendChatRequest` → `SendChatReply`
   - **Push**: `ChatNotify`, `UserJoinedNotify` (서버 → 클라이언트 일방향)

4. **브로드캐스트**:
   - `BroadcastToAll`: 모든 사용자에게 전송
   - `actor.ActorSender.SendToClient`: 특정 사용자에게 Push

---

## 다음 단계

### 기능 확장 아이디어

1. **퇴장 처리 개선**
   - Actor가 나갈 때 `UserLeftNotify` 전송
   - `ChatRoomStage`에 `OnLeaveStage` 추가

2. **최대 인원 제한**
   - `OnJoinStage`에서 입장 거부 로직
   - 방 가득 참 알림

3. **귓속말 기능**
   - 특정 사용자에게만 메시지 전송
   - `SendWhisperRequest` 메시지 추가

4. **채팅 기록 저장**
   - `AsyncIO`를 사용해 DB에 저장
   - 입장 시 최근 메시지 불러오기

### 더 배우기

- [타이머 및 게임루프](../06-timer-gameloop.md): 주기적인 게임 로직 실행
- [서버 간 통신](../07-server-communication.md): Stage 간 메시지 전달
- [비동기 작업](../09-async-operations.md): AsyncIO/AsyncCompute 사용법

---

## 전체 코드

이 튜토리얼의 전체 코드는 다음 위치에서 확인할 수 있습니다:
- 서버: `ChatRoomServer/`
- 클라이언트: `ChatRoomClient/`

### 핵심 파일 요약

```
ChatRoomServer/
├── Proto/chat_messages.proto       # 메시지 정의
├── Stages/ChatRoomStage.cs         # 채팅방 로직
├── Actors/ChatActor.cs             # 사용자 인증
└── Program.cs                      # 서버 시작

ChatRoomClient/
├── Proto/chat_messages.proto       # 메시지 정의 (서버와 동일)
└── Program.cs                      # 클라이언트 로직
```

즐거운 개발 되세요! 😊
