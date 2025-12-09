# PlayHouse-NET 패킷 구조

## 1. 개요

PlayHouse-NET의 패킷 구조는 기존 PlayHouse의 구조를 기반으로 하되, **서버 간 라우팅 정보를 제거**하여 단순화되었습니다. 단일 Room 서버 구조이므로 라우팅이 불필요하며, 클라이언트-서버 통신에 최적화되었습니다.

### 1.1 설계 원칙

- **최소 오버헤드**: 헤더 크기 최소화
- **확장 가능**: 메시지 타입 추가 용이
- **압축 지원**: LZ4 압축으로 대역폭 절약
- **타입 안정성**: 명확한 필드 정의

## 2. 전체 패킷 구조

### 2.1 바이너리 레이아웃

```
┌────────────────────────────────────────────────────────┐
│                    Complete Packet                     │
├────────────────────────────────────────────────────────┤
│                                                        │
│  ┌──────────────────────────────────────────────┐     │
│  │              Header (15 bytes)               │     │
│  ├──────────────────────────────────────────────┤     │
│  │ MsgId Length (1)                             │     │
│  │ MsgId (N)                                    │     │
│  │ MsgSeq (2)                                   │     │
│  │ StageId (8)                                  │     │
│  │ ErrorCode (2)                                │     │
│  │ OriginalSize (4)  [압축시만]                 │     │
│  └──────────────────────────────────────────────┘     │
│                                                        │
│  ┌──────────────────────────────────────────────┐     │
│  │              Body (가변 길이)                 │     │
│  ├──────────────────────────────────────────────┤     │
│  │                                              │     │
│  │         Message Payload (Binary)             │     │
│  │         (최대 2MB, 압축 가능)                 │     │
│  │                                              │     │
│  └──────────────────────────────────────────────┘     │
│                                                        │
└────────────────────────────────────────────────────────┘
```

## 3. 헤더 구조 상세

### 3.1 헤더 필드 정의

| 필드 | 크기 | 타입 | 설명 |
|------|------|------|------|
| ServiceId | 2 bytes | short | 서비스 식별자 (현재 0 사용, 향후 확장용) |
| MsgId Length | 1 byte | byte | MsgId 문자열 길이 (1-255) |
| MsgId | N bytes | string | 메시지 타입 식별자 (UTF-8) |
| MsgSeq | 2 bytes | ushort | 메시지 시퀀스 번호 (Request-Reply 매칭) |
| StageId | 8 bytes | long | 목적지 Stage 식별자 (0 = 없음, 서버 내 로컬 유니크) |
| ErrorCode | 2 bytes | ushort | 오류 코드 (0 = 성공) |
| OriginalSize | 4 bytes | int | 압축 전 원본 크기 (압축 시만 존재) |

### 3.2 헤더 바이트 맵

```
Offset  Size  Field           Description
----------------------------------------------
0       2     ServiceId       서비스 식별자 (현재 0)
2       1     MsgIdLen        MsgId 길이
3       N     MsgId           메시지 식별자 (가변)
3+N     2     MsgSeq          시퀀스 번호
5+N     8     StageId         Stage 식별자 (서버 내 로컬 유니크)
13+N    2     ErrorCode       오류 코드
15+N    4     OriginalSize    원본 크기 (옵션)
```

### 3.3 MsgId 설계

```
메시지 타입 식별자 (문자열)

예시:
- "LoginReq"           : 로그인 요청
- "LoginRes"           : 로그인 응답
- "CreateStageReq"     : Stage 생성 요청
- "CreateStageRes"     : Stage 생성 응답
- "JoinStageReq"       : Stage 입장 요청
- "JoinStageRes"       : Stage 입장 응답
- "StageMsg"           : Stage 내 메시지
- "HeartBeat"          : 하트비트

특징:
- UTF-8 인코딩
- 최대 255자
- Request/Response 쌍 구분 (Req/Res 접미사)
- 명확한 의미 전달
```

### 3.4 MsgSeq (Message Sequence)

```
Request-Reply 패턴 매칭

동작 방식:
1. Client → Server: MsgSeq = N (자동 증가)
2. Server → Client: 동일 MsgSeq = N 사용

특징:
- ushort (0-65535)
- 순환 사용 (65535 다음 0)
- Reply 시 원본 MsgSeq 복사
- 타임아웃 시 RequestCache에서 제거

예시:
┌────────┐                      ┌────────┐
│ Client │                      │ Server │
└────┬───┘                      └───┬────┘
     │ LoginReq (MsgSeq=100)       │
     │─────────────────────────────▶│
     │                              │
     │       LoginRes (MsgSeq=100)  │
     │◀─────────────────────────────│
     │                              │
```

### 3.5 StageId

```
목적지 Stage 식별자 (서버 내 로컬 유니크)

값의 의미:
- 0              : Stage 없음 (로그인, 시스템 메시지)
- 1 ~ 2^63-1     : 유효한 Stage ID

생성 방식:
- 단순 증가 카운터 (Interlocked.Increment)
- 서버 내에서만 유니크하면 됨
- 글로벌 식별: Room서버주소(ip:port) + StageId

설계 근거:
- long(8B) 사용으로 프로토콜 호환성 유지
- 단일 Room 서버 구조에서는 int도 충분하지만,
  클라이언트와의 프로토콜 일관성을 위해 8바이트 사용
- Snowflake 같은 분산 ID는 불필요

예시:
0x0000000000000000  - 시스템 메시지
0x0000000000000001  - Stage #1
0x0000000000000002  - Stage #2
...
```

### 3.6 ErrorCode

```
오류 코드 (응답 전용)

값의 범위:
- 0          : 성공 (Success)
- 1-999      : 시스템 오류
- 1000-9999  : 사용자 정의 오류

기본 오류 코드:
0     - Success
1     - UnknownError
2     - InvalidPacket
3     - Timeout
4     - StageNotFound
5     - ActorNotFound
6     - Unauthorized
7     - InternalError
8     - InvalidState
9     - RateLimitExceeded

사용자 정의:
1000  - CustomError1
1001  - CustomError2
...
```

## 4. Body 구조

### 4.1 Body 포맷

```
Body = Payload (Binary)

- 최대 크기: 2MB (2,097,152 bytes)
- 포맷: 자유 형식 (Binary, JSON, Protobuf 등)
- 압축: LZ4 (옵션)
- 인코딩: 사용자 정의
```

### 4.2 Packet & Payload 인터페이스

```csharp
#nullable enable

/// <summary>
/// 패킷 헤더 정보 (불변).
/// </summary>
/// <remarks>
/// readonly record struct로 스택 할당 및 불변성 보장.
/// </remarks>
public readonly record struct PacketHeader(
    string MsgId,
    ushort MsgSeq,
    int StageId,
    ushort ErrorCode);

/// <summary>
/// 패킷 인터페이스.
/// </summary>
public interface IPacket : IDisposable
{
    string MsgId { get; }
    ushort MsgSeq { get; }
    int StageId { get; }
    ushort ErrorCode { get; }
    IPayload Payload { get; }

    /// <summary>헤더 정보를 record struct로 반환</summary>
    PacketHeader Header => new(MsgId, MsgSeq, StageId, ErrorCode);
}

/// <summary>
/// Payload 인터페이스 (메모리 풀링 지원).
/// </summary>
public interface IPayload : IDisposable, IAsyncDisposable
{
    ReadOnlyMemory<byte> Data { get; }
    int Length { get; }

    // IAsyncDisposable 기본 구현
    ValueTask IAsyncDisposable.DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
```

### 4.3 Payload 구현 예시

```csharp
/// <summary>
/// ArrayPool을 사용한 Payload 구현.
/// </summary>
public sealed class PooledPayload : IPayload
{
    private readonly byte[] _rentedArray;
    private readonly int _length;
    private bool _disposed;

    public PooledPayload(int length)
    {
        _rentedArray = ArrayPool<byte>.Shared.Rent(length);
        _length = length;
    }

    public ReadOnlyMemory<byte> Data =>
        new(_rentedArray, 0, _length);

    public int Length => _length;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ArrayPool<byte>.Shared.Return(_rentedArray);
    }
}

/// <summary>
/// 간단한 바이트 배열 Payload.
/// </summary>
public sealed class BinaryPayload : IPayload
{
    private readonly byte[] _data;

    public BinaryPayload(byte[] data) => _data = data;

    public ReadOnlyMemory<byte> Data => _data;
    public int Length => _data.Length;

    public void Dispose() { }
}
```

**주요 .NET 스타일 적용:**
- `readonly record struct PacketHeader` - 불변 헤더, 스택 할당
- `IAsyncDisposable` - 비동기 리소스 정리 지원
- `sealed class` - 상속 방지로 성능 최적화
- `ArrayPool<byte>` - 메모리 풀링

## 5. SimplePacket 구현 패턴 (권장)

기존 playhouse-sample-net의 검증된 패턴입니다. **Protobuf 메시지**를 **IPacket**으로 래핑하여 타입 안전한 직렬화/역직렬화를 제공합니다.

### 5.1 SimplePacket 구현

```csharp
using Google.Protobuf;
using Google.Protobuf.Reflection;

/// <summary>
/// Protobuf 메시지를 IPacket으로 래핑하는 구현체.
/// </summary>
/// <remarks>
/// MsgId는 Protobuf Descriptor.Name을 사용하여 자동 생성됩니다.
/// 예: ChatMsg → "ChatMsg", PlayerJoinedNotify → "PlayerJoinedNotify"
/// </remarks>
public class SimplePacket : IPacket
{
    private IMessage? _parsedMessage;

    /// <summary>
    /// Protobuf 메시지로 패킷 생성 (송신용)
    /// </summary>
    public SimplePacket(IMessage message)
    {
        MsgId = message.Descriptor.Name;  // "ChatMsg", "PlayerJoinedNotify" 등
        Payload = new SimpleProtoPayload(message);
        _parsedMessage = message;
        MsgSeq = 0;  // Push 메시지
        StageId = 0;
        ErrorCode = 0;
    }

    /// <summary>
    /// 수신된 데이터로 패킷 재구성 (역직렬화용)
    /// </summary>
    public SimplePacket(string msgId, IPayload payload, ushort msgSeq, int stageId = 0, ushort errorCode = 0)
    {
        MsgId = msgId;
        Payload = new CopyPayload(payload);
        MsgSeq = msgSeq;
        StageId = stageId;
        ErrorCode = errorCode;
    }

    public string MsgId { get; }
    public IPayload Payload { get; }
    public ushort MsgSeq { get; }
    public int StageId { get; }
    public ushort ErrorCode { get; }

    /// <summary>Request 여부 (MsgSeq > 0이면 응답 필요)</summary>
    public bool IsRequest => MsgSeq > 0;

    /// <summary>
    /// 타입 안전 파싱 - Protobuf 메시지로 역직렬화
    /// </summary>
    public T Parse<T>() where T : IMessage, new()
    {
        if (_parsedMessage == null)
        {
            var message = new T();
            _parsedMessage = message.Descriptor.Parser.ParseFrom(Payload.Data.Span);
        }
        return (T)_parsedMessage;
    }

    public void Dispose()
    {
        Payload?.Dispose();
    }
}
```

### 5.2 Payload 구현체들

```csharp
/// <summary>
/// Protobuf 메시지를 직렬화하여 Payload로 제공
/// </summary>
public sealed class SimpleProtoPayload : IPayload
{
    private readonly byte[] _data;

    public SimpleProtoPayload(IMessage message)
    {
        _data = message.ToByteArray();
    }

    public ReadOnlyMemory<byte> Data => _data;
    public int Length => _data.Length;

    public void Dispose() { }
}

/// <summary>
/// 기존 Payload를 복사하여 보관 (수신 시 사용)
/// </summary>
public sealed class CopyPayload : IPayload
{
    private readonly byte[] _data;

    public CopyPayload(IPayload source)
    {
        _data = source.Data.ToArray();
    }

    public CopyPayload(byte[] data)
    {
        _data = data;
    }

    public ReadOnlyMemory<byte> Data => _data;
    public int Length => _data.Length;

    public void Dispose() { }
}
```

### 5.3 확장 메서드 (편의성)

```csharp
/// <summary>
/// IPacket에서 Protobuf 메시지를 파싱하는 확장 메서드
/// </summary>
public static class SimplePacketExtension
{
    public static T Parse<T>(this IPacket packet) where T : IMessage, new()
    {
        if (packet is SimplePacket simplePacket)
        {
            return simplePacket.Parse<T>();
        }

        // 일반 IPacket인 경우 직접 파싱
        var message = new T();
        return (T)message.Descriptor.Parser.ParseFrom(packet.Payload.Data.Span);
    }
}
```

### 5.4 사용 예시

```csharp
// ========================================
// 송신: Protobuf 메시지 → SimplePacket
// ========================================

// 채팅 메시지 생성 및 전송
var chatMsg = new ChatMsg
{
    SenderId = player.AccountId,
    SenderName = "Player1",
    Message = "Hello!",
    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
};

// SimplePacket으로 래핑 (MsgId는 "ChatMsg"로 자동 설정)
var packet = new SimplePacket(chatMsg);

// 브로드캐스트
await StageSender.BroadcastAsync(packet);


// ========================================
// 수신: IPacket → Protobuf 메시지
// ========================================

public async ValueTask OnDispatch(IActor actor, IPacket packet)
{
    // MsgId로 메시지 타입 판별 (Descriptor.Name)
    if (packet.MsgId == ChatMsg.Descriptor.Name)  // "ChatMsg"
    {
        // 타입 안전 파싱
        var chatMsg = packet.Parse<ChatMsg>();

        _log.Information("Chat from {Sender}: {Message}",
            chatMsg.SenderName, chatMsg.Message);
    }
    else if (packet.MsgId == PlayerMoveMsg.Descriptor.Name)  // "PlayerMoveMsg"
    {
        var moveMsg = packet.Parse<PlayerMoveMsg>();
        // 이동 처리...
    }
}
```

### 5.5 MsgId 매핑 규칙

| Proto 메시지 | MsgId (Descriptor.Name) |
|-------------|-------------------------|
| `message ChatMsg { ... }` | `"ChatMsg"` |
| `message PlayerJoinedNotify { ... }` | `"PlayerJoinedNotify"` |
| `message CreateRoomReq { ... }` | `"CreateRoomReq"` |
| `message CreateRoomRes { ... }` | `"CreateRoomRes"` |

**장점:**
- 문자열 상수 하드코딩 불필요
- Proto 파일 변경 시 자동 반영
- 타입 안전성 보장

## 6. 압축

### 6.1 압축 정책

```
압축 조건:
- Body 크기 > 512 bytes
- 압축률 > 10%
- 압축 활성화 설정

압축 알고리즘:
- LZ4 (고속)

압축 표시:
- OriginalSize 필드 존재 여부로 판단
- OriginalSize > 0 : 압축됨
- OriginalSize == 0 또는 없음 : 압축 안됨
```

### 6.2 압축 처리 흐름

```
[송신측]
Original Data (1024 bytes)
    │
    ▼
LZ4 Compress
    │
    ▼
Compressed Data (512 bytes)
    │
    ▼
Header.OriginalSize = 1024
Header 작성
    │
    ▼
Send (Header + Compressed Body)


[수신측]
Receive (Header + Compressed Body)
    │
    ▼
Header.OriginalSize 확인 (1024)
    │
    ▼
LZ4 Decompress (512 → 1024 bytes)
    │
    ▼
Original Data (1024 bytes)
```

## 7. 패킷 타입별 구조

### 7.1 클라이언트 → 서버 (Request)

```
┌────────────────────────────────────┐
│         Request Packet             │
├────────────────────────────────────┤
│ MsgIdLen    : 1                    │
│ MsgId       : "LoginReq"           │
│ MsgSeq      : 100                  │
│ StageId     : 0                    │
│ ErrorCode   : 0                    │
│ OriginalSize: 0                    │
├────────────────────────────────────┤
│ Body        : { username, pw }     │
└────────────────────────────────────┘
```

### 7.2 서버 → 클라이언트 (Response)

```
┌────────────────────────────────────┐
│        Response Packet             │
├────────────────────────────────────┤
│ MsgIdLen    : 1                    │
│ MsgId       : "LoginRes"           │
│ MsgSeq      : 100  (동일)          │
│ StageId     : 0                    │
│ ErrorCode   : 0 (Success)          │
│ OriginalSize: 0                    │
├────────────────────────────────────┤
│ Body        : { token, userId }    │
└────────────────────────────────────┘
```

### 7.3 서버 → 클라이언트 (Push)

```
┌────────────────────────────────────┐
│          Push Packet               │
├────────────────────────────────────┤
│ MsgIdLen    : 1                    │
│ MsgId       : "StageEvent"         │
│ MsgSeq      : 0 (N/A)              │
│ StageId     : 12345                │
│ ErrorCode   : 0                    │
│ OriginalSize: 0                    │
├────────────────────────────────────┤
│ Body        : { eventType, data }  │
└────────────────────────────────────┘
```

### 7.4 Stage 내부 메시지

```
┌────────────────────────────────────┐
│       Stage Message                │
├────────────────────────────────────┤
│ MsgIdLen    : 1                    │
│ MsgId       : "PlayerMove"         │
│ MsgSeq      : 200                  │
│ StageId     : 12345                │
│ ErrorCode   : 0                    │
│ OriginalSize: 0                    │
├────────────────────────────────────┤
│ Body        : { x, y, z }          │
└────────────────────────────────────┘
```

## 8. 직렬화 및 역직렬화

### 8.1 패킷 쓰기 (Serialization)

```csharp
public byte[] SerializePacket(IPacket packet, bool compress)
{
    using var buffer = new MemoryStream();
    using var writer = new BinaryWriter(buffer);

    // ServiceId (2 bytes) - 서버 응답은 0
    writer.Write((short)0);

    // MsgId
    var msgIdBytes = Encoding.UTF8.GetBytes(packet.MsgId);
    writer.Write((byte)msgIdBytes.Length);
    writer.Write(msgIdBytes);

    // MsgSeq
    writer.Write((ushort)packet.MsgSeq);

    // StageId (8 bytes)
    writer.Write((long)packet.StageId);

    // ErrorCode
    writer.Write((ushort)packet.ErrorCode);

    // Body 압축
    var bodyData = packet.Payload.Data.ToArray();
    if (compress && bodyData.Length > 512)
    {
        var compressed = LZ4.Compress(bodyData);
        if (compressed.Length < bodyData.Length * 0.9) // 10% 이상 압축
        {
            writer.Write(bodyData.Length); // OriginalSize
            writer.Write(compressed);
            return buffer.ToArray();
        }
    }

    // 압축 안함
    writer.Write(0); // OriginalSize = 0
    writer.Write(bodyData);

    return buffer.ToArray();
}
```

### 8.2 패킷 읽기 (Deserialization)

```csharp
public IPacket DeserializePacket(byte[] data)
{
    using var buffer = new MemoryStream(data);
    using var reader = new BinaryReader(buffer);

    // ServiceId (2 bytes) - 클라이언트는 파싱만 하고 값은 무시
    var serviceId = reader.ReadInt16();

    // MsgId
    var msgIdLen = reader.ReadByte();
    var msgIdBytes = reader.ReadBytes(msgIdLen);
    var msgId = Encoding.UTF8.GetString(msgIdBytes);

    // MsgSeq
    var msgSeq = reader.ReadUInt16();

    // StageId (8 bytes)
    var stageId = reader.ReadInt64();

    // ErrorCode
    var errorCode = reader.ReadUInt16();

    // OriginalSize
    var originalSize = reader.ReadInt32();

    // Body
    byte[] bodyData;
    if (originalSize > 0)
    {
        // 압축된 데이터
        var compressed = reader.ReadBytes((int)(buffer.Length - buffer.Position));
        bodyData = LZ4.Decompress(compressed, originalSize);
    }
    else
    {
        // 압축 안된 데이터
        bodyData = reader.ReadBytes((int)(buffer.Length - buffer.Position));
    }

    var payload = new BinaryPayload(bodyData);
    return new Packet(msgId, msgSeq, stageId, errorCode, payload);
}
```

## 9. 패킷 크기 제한

### 9.1 크기 제한

```
제한 사항:
- Header: 최대 ~260 bytes (MsgId 255자 + 고정 필드)
- Body: 최대 2MB (2,097,152 bytes)
- Total: 최대 ~2MB + 260 bytes

권장 사항:
- 일반 메시지: < 64KB
- 대용량 데이터: 분할 전송 고려
- 압축 활용: > 512 bytes
```

### 9.2 크기 초과 처리

```csharp
public void ValidatePacketSize(IPacket packet)
{
    const int MaxHeaderSize = 260;
    const int MaxBodySize = 2 * 1024 * 1024; // 2MB

    var msgIdSize = Encoding.UTF8.GetByteCount(packet.MsgId);
    if (msgIdSize > 255)
    {
        throw new PacketException("MsgId too long");
    }

    if (packet.Payload.Length > MaxBodySize)
    {
        throw new PacketException("Body size exceeds 2MB");
    }
}
```

## 10. 에러 처리

### 10.1 잘못된 패킷 처리

```
수신 오류 시나리오:
1. 헤더 파싱 실패 → 연결 종료
2. Body 크기 초과 → ErrorCode 2 (InvalidPacket) 응답
3. 압축 해제 실패 → ErrorCode 2 응답
4. 알 수 없는 MsgId → ErrorCode 1 (UnknownError) 응답
```

### 10.2 에러 응답 패킷

```
┌────────────────────────────────────┐
│        Error Response              │
├────────────────────────────────────┤
│ MsgIdLen    : 1                    │
│ MsgId       : "ErrorRes"           │
│ MsgSeq      : 100  (원본)          │
│ StageId     : 0                    │
│ ErrorCode   : 2 (InvalidPacket)    │
│ OriginalSize: 0                    │
├────────────────────────────────────┤
│ Body        : { "error msg" }      │
└────────────────────────────────────┘
```

## 11. 성능 최적화

### 11.1 메모리 풀링

메모리 풀링 구현은 **섹션 4.3 Payload 구현 예시** 참조.

```csharp
// ArrayPool 및 PooledPayload 사용 예시
using var payload = new PooledPayload(1024);
// payload 사용 후 자동으로 ArrayPool에 반환
```

### 11.2 Zero-Copy

```csharp
// ReadOnlyMemory를 통한 복사 최소화
// System.IO.Pipelines와 연동
ReadOnlySequence<byte> buffer = await reader.ReadAsync();
// ReadOnlyMemory로 직접 Payload 생성 - 복사 없음
```

### 11.3 직렬화 캐싱

```csharp
// 동일 메시지 재사용 (브로드캐스트)
public class PacketCache
{
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();

    public byte[] GetOrSerialize(string key, IPacket packet)
    {
        return _cache.GetOrAdd(key, _ => SerializePacket(packet));
    }

    public void Clear() => _cache.Clear();
}
```

## 12. 보안 고려사항

### 12.1 패킷 검증

```csharp
// 필수 검증 항목
- MsgId 길이 (1-255)
- Body 크기 (< 2MB)
- MsgSeq 유효성 (중복 방지)
- StageId 권한 확인
- 압축 데이터 무결성
```

### 12.2 DDoS 방지

```
- 속도 제한: 초당 최대 패킷 수 제한
- 크기 제한: 패킷 크기 제한 강제
- 연결 제한: IP당 최대 연결 수
```

## 13. 다음 단계

- `03-stage-actor-model.md`: Stage/Actor에서 패킷 처리 방식
- `06-socket-transport.md`: 소켓을 통한 패킷 전송
- `07-client-protocol.md`: 클라이언트 패킷 처리 가이드
