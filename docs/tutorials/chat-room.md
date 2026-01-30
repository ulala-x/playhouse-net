# 튜토리얼: 채팅방 만들기

> 예상 소요 시간: 45분
> 난이도: 초급
> 목표: PlayHouse의 핵심 개념(Stage, Actor, 메시지)과 **API Server를 통한 방 관리** 패턴 익히기

## 전체 아키텍처

이 튜토리얼에서 구축하는 시스템의 전체 구조입니다:

```
┌───────────────────────────────────────────────────────────────────┐
│                         클라이언트                                  │
└───────────────────────────┬───────────────────────────────────────┘
                            │
            ┌───────────────┴───────────────┐
            │ ① HTTP 요청 (방 생성/입장)       │
            ▼                               │
┌───────────────────────┐                   │
│     API Server        │                   │
│   (ChatLobbyController)│                   │
│                       │                   │
│  - 방 목록 조회         │                   │
│  - 방 생성/입장         │                   │
│  ApiLink.CreateStage()                  │
│         │             │                   │
└─────────┼─────────────┘                   │
          ▼                                 │
┌───────────────────────┐                   │
│     Play Server       │                   │
│   (ChatRoomStage)     │◀──────────────────┘
│                       │    ② TCP 연결
│  ┌─────────────────┐  │    (stageId로 직접 접속)
│  │  ChatRoomStage  │  │
│  │   ┌─────────┐   │  │
│  │   │ChatActor│   │  │
│  │   └─────────┘   │  │
│  └─────────────────┘  │
└───────────────────────┘
```

### 흐름 설명

1. **클라이언트 → API Server (HTTP)**
   - 방 목록 조회, 방 생성, 방 입장 요청
   - API Server가 Play Server에 Stage 생성 (`ApiLink.CreateStage`)
   - 생성된 방 정보(playNid, stageId, stageType) 반환

2. **클라이언트 → Play Server (TCP)**
   - API Server에서 받은 정보로 Play Server에 직접 연결
   - 인증 후 실시간 채팅 시작

> **핵심**: 클라이언트는 먼저 API Server로 방 정보를 얻은 후, 그 정보로 Play Server에 접속합니다.

---

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
3. [API Server 구현 (로비)](#3-api-server-구현-로비)
4. [Play Server 구현 (채팅방)](#4-play-server-구현-채팅방)
5. [ChatActor 구현](#5-chatactor-구현)
6. [서버 구성](#6-서버-구성)
7. [클라이언트 구현](#7-클라이언트-구현)
8. [실행 및 테스트](#8-실행-및-테스트)
9. [다음 단계](#다음-단계)

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
mkdir Api
mkdir Stages
mkdir Actors
```

최종 디렉토리 구조:
```
ChatRoomServer/
├── ChatRoomServer.csproj
├── Program.cs
├── Proto/
│   ├── chat_messages.proto    # 채팅 메시지
│   └── lobby_messages.proto   # 로비/방 관리 메시지
├── Api/
│   └── ChatLobbyController.cs # API Server 로비 핸들러
├── Stages/
│   └── ChatRoomStage.cs       # Play Server 채팅방
└── Actors/
    └── ChatActor.cs           # 채팅 참가자
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
    string link_id = 1;
    string link_nickname = 2;
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

### Step 2.2: 로비 메시지 정의

**학습 목표**: API Server에서 사용할 방 관리 메시지 정의

`Proto/lobby_messages.proto` 파일을 생성하세요:

```protobuf
syntax = "proto3";

package chatroom;

option csharp_namespace = "ChatRoomServer.Proto";

// ============================================
// 방 목록 조회
// ============================================

// 클라이언트 → API Server: 방 목록 요청
message GetRoomListRequest {
}

// API Server → 클라이언트: 방 목록 응답
message GetRoomListResponse {
    repeated RoomInfo rooms = 1;
}

// ============================================
// 방 생성
// ============================================

// 클라이언트 → API Server: 방 생성 요청
message CreateRoomRequest {
    string room_name = 1;
    int32 max_users = 2;  // 최대 인원 (0 = 무제한)
}

// API Server → 클라이언트: 방 생성 응답
message CreateRoomResponse {
    bool success = 1;
    RoomInfo room_info = 2;
    string error_message = 3;
}

// ============================================
// 방 입장
// ============================================

// 클라이언트 → API Server: 방 입장 요청
message JoinRoomRequest {
    int64 stage_id = 1;  // 입장할 방 ID
}

// API Server → 클라이언트: 방 입장 응답
message JoinRoomResponse {
    bool success = 1;
    RoomInfo room_info = 2;
    string error_message = 3;
}

// ============================================
// 공통 메시지
// ============================================

// 방 정보 (API Server → 클라이언트)
message RoomInfo {
    string play_nid = 1;       // Play Server NID
    string server_address = 2; // Play Server 주소
    int32 port = 3;            // Play Server 포트
    int64 stage_id = 4;        // Stage ID
    string stage_type = 5;     // Stage 타입 ("ChatRoom")
    string room_name = 6;      // 방 이름
    int32 current_users = 7;   // 현재 인원
    int32 max_users = 8;       // 최대 인원
}

// Stage 생성 시 전달되는 페이로드
message CreateChatRoomPayload {
    string room_name = 1;
    int32 max_users = 2;
}
```

### Step 2.3: Proto 컴파일 설정

`ChatRoomServer.csproj` 파일을 열고 `<ItemGroup>` 섹션에 다음을 추가하세요:

```xml
<ItemGroup>
  <Protobuf Include="Proto\*.proto" GrpcServices="None" />
</ItemGroup>
```

### Step 2.4: 빌드하여 C# 코드 생성

```bash
dotnet build
```

이제 `ChatRoomServer.Proto` 네임스페이스 하위에 메시지 클래스들이 자동 생성됩니다.

---

## 3. API Server 구현 (로비)

> **이 섹션이 핵심입니다!** 클라이언트가 Play Server에 직접 접속하기 전에, 먼저 API Server를 통해 방 정보를 얻어야 합니다.

### Step 3.1: ChatLobbyController 기본 구조

**학습 목표**: API Server에서 방 관리 핸들러 구현

`Api/ChatLobbyController.cs` 파일을 생성하세요:

```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Shared;
using ChatRoomServer.Proto;

namespace ChatRoomServer.Api;

/// <summary>
/// 채팅방 로비 - 방 목록 조회, 생성, 입장 처리
/// API Server에서 실행됩니다.
/// </summary>
public class ChatLobbyController : IApiController
{
    // Play Server NID (실제로는 설정에서 관리)
    private const string PlayNid = "chat-play-server-1";
    private const string PlayServerAddress = "127.0.0.1";
    private const int PlayServerPort = 12000;

    // 방 정보 저장 (실제로는 Redis나 DB 사용)
    private static readonly Dictionary<long, RoomInfo> _rooms = new();
    private static long _nextStageId = 1;

    public void Handles(IHandlerRegister register)
    {
        register.Add<GetRoomListRequest>(nameof(HandleGetRoomList));
        register.Add<CreateRoomRequest>(nameof(HandleCreateRoom));
        register.Add<JoinRoomRequest>(nameof(HandleJoinRoom));
    }

    // ... 핸들러 메서드들
}
```

### Step 3.2: 방 목록 조회

```csharp
/// <summary>
/// 현재 존재하는 방 목록 조회
/// </summary>
private Task HandleGetRoomList(IPacket packet, IApiLink link)
{
    var response = new GetRoomListResponse();

    foreach (var room in _rooms.Values)
    {
        response.Rooms.Add(room);
    }

    link.Reply(CPacket.Of(response));
    Console.WriteLine($"[Lobby] Room list requested: {_rooms.Count} rooms");

    return Task.CompletedTask;
}
```

### Step 3.3: 방 생성 (핵심!)

**학습 목표**: `ApiLink.CreateStage`로 Play Server에 Stage 생성

```csharp
/// <summary>
/// 새 채팅방 생성
/// 1. Play Server에 Stage 생성 요청
/// 2. 성공 시 방 정보를 클라이언트에 반환
/// </summary>
private async Task HandleCreateRoom(IPacket packet, IApiLink link)
{
    var request = CreateRoomRequest.Parser.ParseFrom(packet.Payload.DataSpan);
    var stageId = Interlocked.Increment(ref _nextStageId);

    Console.WriteLine($"[Lobby] Creating room: {request.RoomName} (stageId: {stageId})");

    // ⭐ 핵심: API Server가 Play Server에 Stage 생성 요청
    // CreateStage(playNid, stageType, stageId, packet)
    var createPayload = new CreateChatRoomPayload
    {
        RoomName = request.RoomName,
        MaxUsers = request.MaxUsers
    };

    var result = await link.CreateStage(
        PlayNid,           // Play Server NID
        "ChatRoom",        // Stage 타입
        stageId,           // Stage ID
        CPacket.Of(createPayload)
    );

    if (result.Result)
    {
        // Stage 생성 성공 - 방 정보 저장
        var roomInfo = new RoomInfo
        {
            PlayNid = PlayNid,
            ServerAddress = PlayServerAddress,
            Port = PlayServerPort,
            StageId = stageId,
            StageType = "ChatRoom",
            RoomName = request.RoomName,
            CurrentUsers = 0,
            MaxUsers = request.MaxUsers
        };

        _rooms[stageId] = roomInfo;

        link.Reply(CPacket.Of(new CreateRoomResponse
        {
            Success = true,
            RoomInfo = roomInfo
        }));

        Console.WriteLine($"[Lobby] Room created: {request.RoomName} (stageId: {stageId})");
    }
    else
    {
        link.Reply(CPacket.Of(new CreateRoomResponse
        {
            Success = false,
            ErrorMessage = "Failed to create room on Play Server"
        }));

        Console.WriteLine($"[Lobby] Failed to create room: {request.RoomName}");
    }
}
```

### Step 3.4: 방 입장

```csharp
/// <summary>
/// 기존 방에 입장
/// 방 정보를 반환하면 클라이언트가 Play Server에 직접 연결합니다.
/// </summary>
private Task HandleJoinRoom(IPacket packet, IApiLink link)
{
    var request = JoinRoomRequest.Parser.ParseFrom(packet.Payload.DataSpan);

    if (_rooms.TryGetValue(request.StageId, out var roomInfo))
    {
        // 최대 인원 체크 (선택 사항)
        if (roomInfo.MaxUsers > 0 && roomInfo.CurrentUsers >= roomInfo.MaxUsers)
        {
            link.Reply(CPacket.Of(new JoinRoomResponse
            {
                Success = false,
                ErrorMessage = "Room is full"
            }));
            return Task.CompletedTask;
        }

        // 방 정보 반환 (클라이언트가 이 정보로 Play Server에 연결)
        link.Reply(CPacket.Of(new JoinRoomResponse
        {
            Success = true,
            RoomInfo = roomInfo
        }));

        Console.WriteLine($"[Lobby] Join request for room: {roomInfo.RoomName}");
    }
    else
    {
        link.Reply(CPacket.Of(new JoinRoomResponse
        {
            Success = false,
            ErrorMessage = "Room not found"
        }));
    }

    return Task.CompletedTask;
}
```

**왜 이렇게 하나요?**
- `CreateStage`: API Server가 Play Server에 새 Stage(채팅방)를 생성하도록 요청
- `RoomInfo`: 클라이언트가 Play Server에 연결하는 데 필요한 모든 정보 포함
- 클라이언트는 이 정보(serverAddress, port, stageId)로 Play Server에 직접 TCP 연결

---

## 4. Play Server 구현 (채팅방)

> Play Server는 실제 채팅이 이루어지는 Stage를 관리합니다.

### Step 4.1: ChatRoomStage 기본 구조

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
    // Stage 통신 및 관리 기능을 제공하는 Link
    public IStageLink StageLink { get; }

    // 채팅방에 참가한 사용자들 (AccountId -> Actor)
    private readonly Dictionary<string, IActor> _users = new();

    // 사용자별 닉네임 매핑 (AccountId -> Nickname)
    private readonly Dictionary<string, string> _nicknames = new();

    // 채팅방 이름
    private string _roomName = "";

    public ChatRoomStage(IStageLink stageLink)
    {
        StageLink = stageLink;
    }

    // ... 생명주기 메서드들은 아래에서 구현
}
```

### Step 4.2: Stage 생성 (OnCreate)

**학습 목표**: Stage 초기화 및 생성 응답

`ChatRoomStage` 클래스에 다음 메서드를 추가하세요:

```csharp
/// <summary>
/// Stage가 생성될 때 호출됩니다.
/// </summary>
public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
{
    // 채팅방 이름 설정 (StageId를 이름으로 사용)
    _roomName = $"Room-{StageLink.StageId}";

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

### Step 4.3: 사용자 입장 처리 (OnJoinStage)

**학습 목표**: Actor 입장 검증 및 환영 메시지

```csharp
/// <summary>
/// Actor가 Stage에 입장하려고 할 때 호출됩니다.
/// </summary>
public Task<bool> OnJoinStage(IActor actor)
{
    var accountId = actor.ActorLink.AccountId;

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
    var accountId = actor.ActorLink.AccountId;

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

### Step 4.4: 연결 상태 변경 처리

**학습 목표**: 재연결/연결 끊김 감지

```csharp
/// <summary>
/// Actor의 네트워크 연결 상태가 변경될 때 호출됩니다.
/// </summary>
public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
{
    var accountId = actor.ActorLink.AccountId;
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

### Step 4.5: 메시지 처리 (OnDispatch)

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
            actor.ActorLink.Reply(500); // 에러 코드 반환
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

### Step 4.6: 채팅 메시지 핸들러

**학습 목표**: Request-Response 패턴과 브로드캐스트

```csharp
/// <summary>
/// 채팅 메시지 전송 요청 처리
/// </summary>
private void HandleSendChat(IActor actor, IPacket packet)
{
    var request = SendChatRequest.Parser.ParseFrom(packet.Payload.DataSpan);
    var accountId = actor.ActorLink.AccountId;
    var nickname = _nicknames.GetValueOrDefault(accountId, "Unknown");
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    Console.WriteLine($"[ChatRoom] {nickname}: {request.Message}");

    // 1. 발신자에게 전송 성공 응답
    var reply = new SendChatReply
    {
        Success = true,
        Timestamp = timestamp
    };
    actor.ActorLink.Reply(CPacket.Of(reply));

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

### Step 4.7: 사용자 목록 조회 핸들러

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

    actor.ActorLink.Reply(CPacket.Of(reply));
}
```

### Step 4.8: 유틸리티 메서드

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
        user.ActorLink.SendToClient(packet);
    }
}

/// <summary>
/// 특정 사용자를 제외한 나머지에게 브로드캐스트
/// </summary>
private void BroadcastToOthers(IActor link, Google.Protobuf.IMessage message)
{
    var packet = CPacket.Of(message);
    var linkId = link.ActorLink.AccountId;

    foreach (var user in _users.Values)
    {
        if (user.ActorLink.AccountId != linkId)
        {
            user.ActorLink.SendToClient(packet);
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

## 5. ChatActor 구현

### Step 5.1: 기본 구조 작성

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
    public IActorLink ActorLink { get; }

    private string _nickname = "";

    public ChatActor(IActorLink actorLink)
    {
        ActorLink = actorLink;
    }

    // ... 생명주기 메서드들은 아래에서 구현
}
```

### Step 5.2: Actor 생성 및 소멸

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
    Console.WriteLine($"[ChatActor] {_nickname} ({ActorLink.AccountId}) destroyed");
    return Task.CompletedTask;
}
```

### Step 5.3: 인증 처리

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
    ActorLink.AccountId = accountId;

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

## 6. 서버 구성

### Step 6.1: ChatRoomStage에서 닉네임 처리 수정

**학습 목표**: Stage와 Actor 간 데이터 전달

`ChatRoomStage.cs`의 `OnJoinStage` 메서드를 수정하여 닉네임을 가져옵니다:

```csharp
public Task<bool> OnJoinStage(IActor actor)
{
    var accountId = actor.ActorLink.AccountId;

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

### Step 6.2: Program.cs 작성 (API Server + Play Server)

**학습 목표**: API Server와 Play Server를 함께 구성

`Program.cs` 파일을 다음과 같이 작성하세요:

```csharp
using Microsoft.Extensions.Logging;
using PlayHouse.Core.Api.Bootstrap;
using PlayHouse.Core.Play.Bootstrap;
using ChatRoomServer.Api;
using ChatRoomServer.Stages;
using ChatRoomServer.Actors;

Console.WriteLine("=== ChatRoom Server Starting ===");

// 로거 팩토리 생성 (공유)
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// ============================================
// 1. API Server 구성 (로비)
// ============================================
var apiServer = new ApiServerBootstrap()
    .Configure(options =>
    {
        options.ServerId = "chat-api-server-1";
        options.ServiceId = "chat-lobby";
        options.BindEndpoint = "tcp://127.0.0.1:11100";  // 서버간 통신용
        options.ApiPort = 5000;                           // HTTP API 포트
    })
    // 로비 컨트롤러 등록
    .UseApiController<ChatLobbyController>()
    .UseLoggerFactory(loggerFactory)
    .Build();

// ============================================
// 2. Play Server 구성 (채팅방)
// ============================================
var playServer = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.ServerId = "chat-play-server-1";
        options.BindEndpoint = "tcp://127.0.0.1:11200";  // 서버간 통신용
        options.TcpPort = 12000;                          // 클라이언트 TCP 연결용

        // 인증 메시지 설정
        options.AuthenticateMessageId = "AuthenticateRequest";

        // 기본 Stage 타입
        options.DefaultStageType = "ChatRoom";
    })
    // ChatRoom Stage와 ChatActor 등록
    .UseStage<ChatRoomStage, ChatActor>("ChatRoom")
    .UseLoggerFactory(loggerFactory)
    .Build();

// ============================================
// 3. 서버 시작
// ============================================
await apiServer.StartAsync();
Console.WriteLine("API Server started on port 5000");

await playServer.StartAsync();
Console.WriteLine("Play Server started on port 12000");

Console.WriteLine("\n=== ChatRoom Server Ready ===");
Console.WriteLine("1. API Server (로비): http://localhost:5000");
Console.WriteLine("2. Play Server (채팅방): tcp://localhost:12000");
Console.WriteLine("Press Ctrl+C to stop\n");

// 종료 시그널 대기
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (link, e) =>
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
await playServer.StopAsync();
await apiServer.StopAsync();
Console.WriteLine("=== Server Stopped ===");
```

**왜 이렇게 하나요?**
- **API Server**: 클라이언트가 HTTP로 방 생성/입장 요청
- **Play Server**: 클라이언트가 TCP로 실시간 채팅
- 두 서버가 `BindEndpoint`를 통해 서로 통신 (API → Play로 Stage 생성)

---

## 7. 클라이언트 구현

### Step 7.1: 테스트 클라이언트 프로젝트 생성

```bash
dotnet new console -n ChatRoomClient
cd ChatRoomClient
dotnet add package PlayHouse.Connector
dotnet add package Google.Protobuf
dotnet add package System.Net.Http.Json  # HTTP 클라이언트용
```

### Step 7.2: Proto 파일 복사

서버 프로젝트의 Proto 파일들을 클라이언트 프로젝트로 복사하세요.

```bash
# ChatRoomClient 디렉토리에서 실행
mkdir Proto
cp ../ChatRoomServer/Proto/chat_messages.proto Proto/
cp ../ChatRoomServer/Proto/lobby_messages.proto Proto/
```

`ChatRoomClient.csproj`에 Proto 컴파일 설정 추가:

```xml
<ItemGroup>
  <Protobuf Include="Proto\*.proto" GrpcServices="None" />
</ItemGroup>
```

### Step 7.3: 클라이언트 코드 작성

**학습 목표**: 2단계 접속 패턴 (API Server → Play Server) 이해 및 구현

클라이언트는 **2단계**로 서버에 접속합니다:
1. **HTTP → API Server**: 방 생성/입장 요청 → 방 정보(stageId, 서버 주소) 획득
2. **TCP → Play Server**: 획득한 정보로 실시간 연결

`Program.cs`:

```csharp
using System.Net.Http.Json;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using ChatRoomServer.Proto;

Console.WriteLine("=== ChatRoom Client ===");

// 닉네임 입력
Console.Write("Enter your nickname: ");
var nickname = Console.ReadLine() ?? "Guest";

// HTTP 클라이언트 (API Server 통신용)
using var httpClient = new HttpClient
{
    BaseAddress = new Uri("http://127.0.0.1:5000")
};

// Connector 생성 (Play Server 통신용)
var connector = new ClientConnector();
connector.Init(new ConnectorConfig
{
    RequestTimeoutMs = 10000
});

// Push 메시지 수신 핸들러 등록
connector.SetOnReceive(OnReceivePush);

try
{
    // ========================================
    // Step 1: API Server로 방 정보 획득
    // ========================================
    Console.WriteLine("\n[Step 1] Requesting room info from API Server...");

    // 방 목록 조회 또는 새 방 생성
    Console.Write("Create new room or join existing? (new/join): ");
    var choice = Console.ReadLine()?.ToLower() ?? "new";

    RoomInfo? roomInfo = null;

    if (choice == "new")
    {
        Console.Write("Enter room name: ");
        var roomName = Console.ReadLine() ?? "My Room";

        // API Server에 방 생성 요청
        var createResponse = await httpClient.PostAsJsonAsync("/lobby/create", new
        {
            RoomName = roomName,
            MaxUsers = 10
        });

        if (!createResponse.IsSuccessStatusCode)
        {
            Console.WriteLine("Failed to create room");
            return;
        }

        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoomApiResponse>();
        if (createResult?.Success != true)
        {
            Console.WriteLine($"Failed to create room: {createResult?.Error}");
            return;
        }

        roomInfo = createResult.RoomInfo;
        Console.WriteLine($"Room created: {roomInfo?.RoomName} (StageId: {roomInfo?.StageId})");
    }
    else
    {
        // 방 목록 조회
        var listResponse = await httpClient.GetFromJsonAsync<GetRoomListApiResponse>("/lobby/rooms");
        if (listResponse?.Rooms == null || listResponse.Rooms.Count == 0)
        {
            Console.WriteLine("No rooms available. Creating a new one...");
            // 방이 없으면 새로 생성
            var createResponse = await httpClient.PostAsJsonAsync("/lobby/create", new
            {
                RoomName = "Default Room",
                MaxUsers = 10
            });
            var createResult = await createResponse.Content.ReadFromJsonAsync<CreateRoomApiResponse>();
            roomInfo = createResult?.RoomInfo;
        }
        else
        {
            Console.WriteLine("\nAvailable rooms:");
            for (int i = 0; i < listResponse.Rooms.Count; i++)
            {
                var room = listResponse.Rooms[i];
                Console.WriteLine($"  [{i}] {room.RoomName} ({room.CurrentUsers}/{room.MaxUsers} users)");
            }

            Console.Write("Select room number: ");
            var roomIndex = int.Parse(Console.ReadLine() ?? "0");
            var selectedRoom = listResponse.Rooms[roomIndex];

            // API Server에 방 입장 요청
            var joinResponse = await httpClient.PostAsJsonAsync("/lobby/join", new
            {
                StageId = selectedRoom.StageId
            });

            var joinResult = await joinResponse.Content.ReadFromJsonAsync<JoinRoomApiResponse>();
            if (joinResult?.Success != true)
            {
                Console.WriteLine($"Failed to join room: {joinResult?.Error}");
                return;
            }

            roomInfo = joinResult.RoomInfo;
        }
    }

    if (roomInfo == null)
    {
        Console.WriteLine("Failed to get room info");
        return;
    }

    Console.WriteLine($"\nRoom info received:");
    Console.WriteLine($"  - Server: {roomInfo.ServerAddress}:{roomInfo.Port}");
    Console.WriteLine($"  - StageId: {roomInfo.StageId}");
    Console.WriteLine($"  - StageType: {roomInfo.StageType}");

    // ========================================
    // Step 2: Play Server에 TCP 연결
    // ========================================
    Console.WriteLine("\n[Step 2] Connecting to Play Server...");

    var connected = await connector.ConnectAsync(
        roomInfo.ServerAddress,  // API Server에서 받은 주소
        roomInfo.Port,           // API Server에서 받은 포트
        roomInfo.StageId,        // API Server에서 받은 stageId
        roomInfo.StageType       // "ChatRoom"
    );

    if (!connected)
    {
        Console.WriteLine("Connection failed");
        return;
    }
    Console.WriteLine("Connected to Play Server!");

    // ========================================
    // Step 3: 인증 (닉네임 설정)
    // ========================================
    Console.WriteLine($"\n[Step 3] Authenticating as '{nickname}'...");
    var authRequest = new AuthenticateRequest { Nickname = nickname };
    using var authPacket = new Packet(authRequest);
    using var authResponse = await connector.AuthenticateAsync(authPacket);

    if (!connector.IsAuthenticated())
    {
        Console.WriteLine("Authentication failed");
        return;
    }

    var authReply = AuthenticateReply.Parser.ParseFrom(authResponse.Payload.DataSpan);
    Console.WriteLine($"Authenticated! AccountId: {authReply.AccountId}");

    // ========================================
    // Step 4: 참가자 목록 조회
    // ========================================
    using var getUsersReq = new Packet(new GetUsersRequest());
    using var getUsersRes = await connector.RequestAsync(getUsersReq);
    var usersReply = GetUsersReply.Parser.ParseFrom(getUsersRes.Payload.DataSpan);

    Console.WriteLine($"\nCurrent users ({usersReply.Users.Count}):");
    foreach (var user in usersReply.Users)
    {
        Console.WriteLine($"  - {user.Nickname} ({user.AccountId})");
    }

    // ========================================
    // Step 5: 채팅 메시지 송수신
    // ========================================
    Console.WriteLine("\nChat started! Type your message (or 'quit' to exit):");

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
                    Console.WriteLine("Failed to send message");
                }
            }
        }

        await Task.Delay(10); // CPU 사용률 조절
    }

    // Step 6: 연결 종료
    connector.Disconnect();
    Console.WriteLine("\nDisconnected from server");
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
            Console.WriteLine($"+ {joinNotify.Nickname} joined ({joinNotify.TotalUsers} users)");
            break;

        case "UserLeftNotify":
            var leftNotify = UserLeftNotify.Parser.ParseFrom(packet.Payload.DataSpan);
            Console.WriteLine($"- {leftNotify.Nickname} left ({leftNotify.TotalUsers} users)");
            break;

        default:
            Console.WriteLine($"Unknown push: {packet.MsgId}");
            break;
    }
}

// API Server 응답 DTO
record RoomInfo(string PlayNid, string ServerAddress, int Port, long StageId, string StageType, string RoomName, int CurrentUsers, int MaxUsers);
record CreateRoomApiResponse(bool Success, RoomInfo? RoomInfo, string? Error);
record JoinRoomApiResponse(bool Success, RoomInfo? RoomInfo, string? Error);
record GetRoomListApiResponse(List<RoomInfo>? Rooms);
```

### 클라이언트 흐름 정리

```
┌─────────────────────────────────────────────────────────────────────┐
│                         클라이언트 접속 흐름                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  [Step 1] HTTP 요청 → API Server                                    │
│           POST /lobby/create 또는 POST /lobby/join                   │
│           → 응답: { stageId, serverAddress, port, stageType }       │
│                                                                     │
│  [Step 2] TCP 연결 → Play Server                                    │
│           ConnectAsync(serverAddress, port, stageId, stageType)     │
│                                                                     │
│  [Step 3] 인증 → Play Server                                        │
│           AuthenticateAsync(AuthenticateRequest)                    │
│                                                                     │
│  [Step 4+] 실시간 통신 (Request/Push)                                │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

**왜 2단계 접속인가요?**
- **API Server (로비)**: 방 목록 관리, 방 생성/삭제, 입장 가능 여부 확인 등 **관리 기능**
- **Play Server (게임방)**: 실시간 메시지 처리, 상태 관리 등 **실시간 기능**
- 역할 분리로 확장성과 유지보수성 향상

---

## 8. 실행 및 테스트

### Step 8.1: 서버 실행

터미널 1:
```bash
cd ChatRoomServer
dotnet run
```

출력:
```
=== ChatRoom Server Starting ===
[API Server] Started on port 5000
[Play Server] Started on port 12000
=== ChatRoom Server Started ===
Press Ctrl+C to stop
```

### Step 8.2: 클라이언트 1 실행 (방 생성)

터미널 2:
```bash
cd ChatRoomClient
dotnet run
```

입력 및 출력:
```
=== ChatRoom Client ===
Enter your nickname: Alice

[Step 1] Requesting room info from API Server...
Create new room or join existing? (new/join): new
Enter room name: My Room
Room created: My Room (StageId: 1000001)

Room info received:
  - Server: 127.0.0.1:12000
  - StageId: 1000001
  - StageType: ChatRoom

[Step 2] Connecting to Play Server...
Connected to Play Server!

[Step 3] Authenticating as 'Alice'...
Authenticated! AccountId: 12345...

Current users (1):
  - Alice (12345...)

Chat started! Type your message (or 'quit' to exit):
```

### Step 8.3: 클라이언트 2 실행 (방 입장)

터미널 3:
```bash
cd ChatRoomClient
dotnet run
```

입력:
```
Enter your nickname: Bob
Create new room or join existing? (new/join): join
```

출력 (Bob 화면):
```
Available rooms:
  [0] My Room (1/10 users)

Select room number: 0

Room info received:
  - Server: 127.0.0.1:12000
  - StageId: 1000001
  - StageType: ChatRoom

[Step 2] Connecting to Play Server...
Connected to Play Server!

[Step 3] Authenticating as 'Bob'...
Authenticated! AccountId: 67890...

Current users (2):
  - Alice (12345...)
  - Bob (67890...)
```

**Alice의 화면에 출력:**
```
+ Bob joined (2 users)
```

### Step 8.4: 채팅 테스트

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

### Step 8.5: 서버 로그 확인

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

## 축하합니다!

첫 PlayHouse 채팅방 서버를 성공적으로 구축했습니다!

### 배운 내용

1. **2단계 접속 패턴** (핵심!)
   - **API Server**: 방 목록, 방 생성/입장 등 관리 기능 (HTTP)
   - **Play Server**: 실시간 메시지 처리 (TCP)
   - 클라이언트는 API Server에서 방 정보를 얻은 후 Play Server에 접속

2. **API Server (IApiController)**
   - `ApiLink.CreateStage()`: Play Server에 Stage 생성
   - HTTP 스타일 요청 처리 (Stateless)
   - 방 관리, 로비 기능 담당

3. **Stage**: 여러 사용자가 모이는 공간 (채팅방)
   - `OnCreate`: Stage 생성 및 초기화
   - `OnJoinStage`: 사용자 입장 처리
   - `OnDispatch`: 메시지 처리

4. **Actor**: 개별 사용자를 나타냄
   - `OnAuthenticate`: 인증 및 AccountId 설정 (필수!)
   - `ActorLink.AccountId`: 사용자 식별자

5. **메시지 패턴**:
   - **Request-Response**: `SendChatRequest` → `SendChatReply`
   - **Push**: `ChatNotify`, `UserJoinedNotify` (서버 → 클라이언트 일방향)

6. **브로드캐스트**:
   - `BroadcastToAll`: 모든 사용자에게 전송
   - `actor.ActorLink.SendToClient`: 특정 사용자에게 Push

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

- [타이머 및 게임루프](../concepts/timer-gameloop.md): 주기적인 게임 로직 실행
- [서버 간 통신](../guides/server-communication.md): Stage 간 메시지 전달
- [비동기 작업](../guides/async-operations.md): AsyncIO/AsyncCompute 사용법

---

## 전체 코드

이 튜토리얼의 전체 코드는 다음 위치에서 확인할 수 있습니다:
- 서버: `ChatRoomServer/`
- 클라이언트: `ChatRoomClient/`

### 핵심 파일 요약

```
ChatRoomServer/
├── Proto/
│   ├── chat_messages.proto         # 채팅 메시지 정의
│   └── lobby_messages.proto        # 로비 메시지 정의
├── Api/
│   └── ChatLobbyController.cs      # API Server 로비 (방 관리)
├── Stages/
│   └── ChatRoomStage.cs            # Play Server 채팅방 로직
├── Actors/
│   └── ChatActor.cs                # 사용자 인증
└── Program.cs                      # 서버 시작 (API + Play)

ChatRoomClient/
├── Proto/
│   ├── chat_messages.proto         # 채팅 메시지 (서버와 동일)
│   └── lobby_messages.proto        # 로비 메시지 (서버와 동일)
└── Program.cs                      # 클라이언트 로직 (HTTP + TCP)
```

즐거운 개발 되세요!
