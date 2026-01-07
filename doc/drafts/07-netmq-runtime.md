# ZMQ Runtime 상세 구현 가이드

## 문서 목적

이 문서는 PlayHouse 참조 시스템의 ZMQ 기반 Runtime 코드를 PlayHouse-NET 프로젝트에 재사용하기 위한 상세 가이드입니다. 각 클래스의 정확한 위치, 핵심 코드, 그리고 통합 방법을 제공합니다.

**참조 시스템 경로**: `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Runtime`

## 1. 아키텍처 개요

### 1.1 ZMQ Router-Router 패턴

PlayHouse는 **Router-Router 패턴**을 사용합니다. 모든 서버가 RouterSocket을 사용하여 Bind와 Connect를 동시에 수행합니다.

```
Server A (Router)          Server B (Router)          Server C (Router)
    ↓                          ↓                          ↓
Bind("tcp://*:8001")     Bind("tcp://*:9001")     Bind("tcp://*:10001")
    ↓                          ↓                          ↓
Connect("tcp://B:9001")  Connect("tcp://A:8001")  Connect("tcp://A:8001")
Connect("tcp://C:10001") Connect("tcp://C:10001") Connect("tcp://B:9001")
```

**Router 소켓의 장점**:
- 하나의 소켓으로 Bind와 Connect를 동시에 사용 가능
- NID(Node ID) 기반의 명확한 라우팅
- Identity 기반 메시지 라우팅으로 Full-Mesh 연결 구현
- 동적 서버 추가/제거 지원

### 1.2 계층 구조

```
┌─────────────────────────────────────────┐
│          Communicator                   │ ← 메시지 디스패치 오케스트레이터
│  - OnReceive(RoutePacket)               │
│  - Dispatch()                           │
└─────────────────────────────────────────┘
            ↓           ↑
┌───────────────┐   ┌───────────────┐
│ XServerCommunicator│   │XClientCommunicator│
│ (수신 전용)    │   │ (송신 전용)    │
│ - Bind()       │   │ - Connect()    │
│ - Receive()    │   │ - Send()       │
└───────────────┘   └───────────────┘
            ↓           ↑
        ┌───────────────────┐
        │  IPlaySocket      │
        │  (NetMqPlaySocket)│
        │  - RouterSocket   │
        └───────────────────┘
            ↓
    [ZMQ RouterSocket]
```

### 1.3 스레드 모델

```
Main Thread
    ↓
┌──────────────────────────────────┐
│  MessageLoop.Start()             │
│  ├─ ServerThread 생성 및 시작    │
│  └─ ClientThread 생성 및 시작    │
└──────────────────────────────────┘
    ↓                   ↓
[ServerThread]      [ClientThread]
    ↓                   ↓
While(running)      While(queue has items)
    ↓                   ↓
Receive()           Pop from Queue
    ↓                   ↓
OnReceive()         Send() / Connect()
```

## 2. 핵심 클래스 및 파일 위치

### 2.1 PlaySocket 계층

#### 📁 파일 위치
```
Runtime/PlaySocket/
├── IPlaySocket.cs           (18 lines)
├── ZMQPlaySocket.cs       (163 lines)
├── PlaySocketConfig.cs      (11 lines)
├── PlaySocketFactory.cs     (팩토리 클래스)
└── SocketConfig.cs          (8 lines)
```

#### 📄 IPlaySocket.cs (그대로 복사)

**파일**: `Runtime/PlaySocket/IPlaySocket.cs`

```csharp
using PlayHouse.Runtime.Message;

namespace PlayHouse.Runtime.PlaySocket;

internal interface IPlaySocket
{
    string GetBindEndpoint();
    void Bind();
    void Send(string nid, RoutePacket routerPacket);
    void Connect(string endPoint);
    RoutePacket? Receive();
    void Disconnect(string endPoint);
    void Close();
    string EndPoint();
    string Nid();
}
```

**메서드 설명**:
- `Bind()`: 자신의 엔드포인트에 바인딩 (서버 역할)
- `Connect(endPoint)`: 다른 서버의 엔드포인트에 연결 (클라이언트 역할)
- `Send(nid, routePacket)`: 특정 NID를 가진 서버로 메시지 전송
- `Receive()`: 메시지 수신 (1초 타임아웃)
- `Disconnect(endPoint)`: 연결 해제
- `GetBindEndpoint()`: 자신의 바인드 엔드포인트 반환

#### 📄 ZMQPlaySocket.cs (핵심 구현 클래스)

**파일**: `Runtime/PlaySocket/ZMQPlaySocket.cs` (163 lines)

**핵심 코드 1 - 생성자 및 소켓 옵션 설정**:

```csharp
internal class NetMqPlaySocket : IPlaySocket
{
    private readonly RouterSocket _socket = new();
    private readonly string _bindEndpoint;
    private readonly string _nid;
    private readonly PooledByteBuffer _buffer = new(ConstOption.MaxPacketSize);

    public NetMqPlaySocket(SocketConfig socketConfig)
    {
        _bindEndpoint = socketConfig.BindEndpoint;
        _nid = socketConfig.Nid;

        // 중요: NID를 소켓 Identity로 설정
        _socket.Options.Identity = Encoding.UTF8.GetBytes(_nid);

        // Router 소켓 필수 옵션
        _socket.Options.DelayAttachOnConnect = true;  // 즉시 연결
        _socket.Options.RouterHandover = true;        // Identity 핸드오버 허용
        _socket.Options.RouterMandatory = true;       // 미연결 대상 전송 시 실패

        // 성능 옵션
        _socket.Options.Backlog = socketConfig.PlaySocketConfig.BackLog;
        _socket.Options.Linger = TimeSpan.FromMilliseconds(
            socketConfig.PlaySocketConfig.Linger);
        _socket.Options.TcpKeepalive = true;
        _socket.Options.SendBuffer = socketConfig.PlaySocketConfig.SendBufferSize;
        _socket.Options.ReceiveBuffer = socketConfig.PlaySocketConfig.ReceiveBufferSize;
        _socket.Options.SendHighWatermark =
            socketConfig.PlaySocketConfig.SendHighWatermark;
        _socket.Options.ReceiveHighWatermark =
            socketConfig.PlaySocketConfig.ReceiveHighWatermark;
    }
}
```

**중요 옵션 설명**:
- `Identity`: Router 소켓의 고유 식별자로 NID를 사용
- `RouterHandover`: 같은 Identity로 재연결 시 기존 연결을 새 연결로 교체
- `RouterMandatory`: 연결되지 않은 대상에게 메시지 전송 시 즉시 실패 (에러 감지)
- `DelayAttachOnConnect`: Connect() 호출 시 즉시 연결 시도

**핵심 코드 2 - 메시지 수신 (Receive)**:

```csharp
public RoutePacket? Receive()
{
    var message = new ZMQMessage();

    // 1초 타임아웃으로 수신 시도
    if (_socket.TryReceiveMultipartMessage(TimeSpan.FromSeconds(1), ref message))
    {
        if (message.Count() < 3)
        {
            _log.Error(() => $"message size is invalid : {message.Count()}");
            return null;
        }

        // Frame 0: 송신자 NID (Router 소켓이 자동으로 추가)
        var source = Encoding.UTF8.GetString(message[0].Buffer);

        // Frame 1: RouteHeader (Protobuf 파싱)
        var header = RouteHeaderMsg.Parser.ParseFrom(message[1].Buffer);

        // Frame 2: Payload (Zero-Copy를 위해 FramePayload로 래핑)
        var payload = new FramePayload(message[2]);

        var routePacket = RoutePacket.Of(new RouteHeader(header), payload);
        routePacket.RouteHeader.From = source;  // 송신자 NID 설정
        return routePacket;
    }

    return null;  // 타임아웃 시 null 반환
}
```

**수신 프로세스**:
1. `TryReceiveMultipartMessage()`: 1초 타임아웃으로 3-Frame 메시지 수신
2. Frame 0은 Router 소켓이 자동으로 추가한 송신자의 Identity (NID)
3. Frame 1은 RouteHeader (Protobuf 직렬화)
4. Frame 2는 Payload (FramePayload로 Zero-Copy 래핑)

**핵심 코드 3 - 메시지 송신 (Send)**:

```csharp
public void Send(string nid, RoutePacket routePacket)
{
    using (routePacket)
    {
        var message = new ZMQMessage();
        var payload = routePacket.Payload;
        ZMQFrame frame;

        _buffer.Clear();

        // 클라이언트로 전송하는 경우 ClientPacket 형식으로 변환
        if (routePacket.IsToClient())
        {
            routePacket.WriteClientPacketBytes(_buffer);
            frame = new ZMQFrame(_buffer.Buffer(), _buffer.Count);
        }
        else
        {
            // Zero-Copy: FramePayload는 Frame을 그대로 재사용
            if (payload is FramePayload framePayload)
            {
                frame = framePayload.Frame;
            }
            else
            {
                _buffer.Write(payload.DataSpan);
                frame = new ZMQFrame(_buffer.Buffer(), _buffer.Count);
            }
        }

        // Frame 0: Target NID (UTF-8 문자열)
        message.Append(new ZMQFrame(Encoding.UTF8.GetBytes(nid)));

        // Frame 1: RouteHeader (Protobuf 직렬화)
        var routerHeaderMsg = routePacket.RouteHeader.ToMsg();
        var headerSize = routerHeaderMsg.CalculateSize();
        var headerFrame = new ZMQFrame(headerSize);
        routerHeaderMsg.WriteTo(new MemoryStream(headerFrame.Buffer));
        message.Append(headerFrame);

        // Frame 2: Payload
        message.Append(frame);

        // 송신 실패 시 로그 출력
        if (!_socket.TrySendMultipartMessage(message))
        {
            _log.Error(() =>
                $"PostAsync fail to - [nid:{nid}, MsgName:{routePacket.MsgId}]");
        }
    }
}
```

**송신 프로세스**:
1. Target NID를 Frame 0에 추가
2. RouteHeader를 Protobuf로 직렬화하여 Frame 1에 추가
3. Payload를 Frame 2에 추가 (FramePayload인 경우 Zero-Copy)
4. `TrySendMultipartMessage()`로 전송

#### 📄 SocketConfig.cs (그대로 복사)

**파일**: `Runtime/PlaySocket/SocketConfig.cs`

```csharp
namespace PlayHouse.Runtime.PlaySocket;

public class SocketConfig(string nid, string bindEndpoint, PlaySocketConfig playSocketConfig)
{
    public PlaySocketConfig PlaySocketConfig { get; set; } = playSocketConfig;
    public string Nid { get; internal set; } = nid;
    public string BindEndpoint { get; internal set; } = bindEndpoint;
}
```

#### 📄 PlaySocketConfig.cs (그대로 복사)

**파일**: `Runtime/PlaySocket/PlaySocketConfig.cs`

```csharp
namespace PlayHouse.Runtime.PlaySocket;

public class PlaySocketConfig
{
    public int BackLog { get; internal set; } = 1000;
    public int Linger { get; internal set; } = 0;
    public int SendBufferSize { get; internal set; } = 1024 * 1024 * 2;  // 2MB
    public int ReceiveBufferSize { get; internal set; } = 1024 * 1024 * 2;  // 2MB
    public int SendHighWatermark { get; internal set; } = 1000000;
    public int ReceiveHighWatermark { get; internal set; } = 1000000;
}
```

**설정값 설명**:
- `BackLog`: TCP 연결 대기 큐 크기 (기본 1000)
- `Linger`: 소켓 종료 시 대기 시간 (0 = 즉시 종료)
- `SendBufferSize`: OS 레벨 송신 버퍼 (2MB)
- `ReceiveBufferSize`: OS 레벨 수신 버퍼 (2MB)
- `SendHighWatermark`: 내부 송신 큐 최대 메시지 수 (1,000,000)
- `ReceiveHighWatermark`: 내부 수신 큐 최대 메시지 수 (1,000,000)

### 2.2 Message 계층

#### 📁 파일 위치
```
Runtime/Message/
├── IBasePacket.cs       (인터페이스)
├── RoutePacket.cs       (484 lines) - 라우팅 패킷
├── Payload.cs           (76 lines) - Payload 구현체들
└── ClientPacket.cs      (클라이언트 전용)
```

#### 📄 Payload.cs (그대로 복사)

**파일**: `Runtime/Message/Payload.cs` (76 lines)

```csharp
using Google.Protobuf;
using ZMQ;
using PlayHouse.Infrastructure.Common.Buffers;

namespace PlayHouse.Runtime.Message;

public interface IPayload : IDisposable
{
    public ReadOnlyMemory<byte> Data { get; }
    public ReadOnlySpan<byte> DataSpan => Data.Span;
}

// Zero-Copy를 위한 ZMQ Frame 래퍼
public class FramePayload(ZMQFrame frame) : IPayload
{
    public ZMQFrame Frame { get; } = frame;
    public ReadOnlyMemory<byte> Data => new(Frame.Buffer, 0, Frame.MessageSize);
    public void Dispose() { }
}

// Protobuf 메시지 Payload
public class ProtoPayload(IMessage proto) : IPayload
{
    public ReadOnlyMemory<byte> Data => proto.ToByteArray();
    public void Dispose() { }
    public IMessage GetProto() => proto;
}

// 복사된 바이트 배열 Payload
public class CopyPayload(IPayload payload) : IPayload
{
    private readonly byte[] _data = payload.Data.ToArray();
    public ReadOnlyMemory<byte> Data => _data;
    public void Dispose() { }
}

// 풀링된 버퍼 Payload
public class PooledBytePayload(PooledByteBuffer ringBuffer) : IPayload
{
    public ReadOnlyMemory<byte> Data => ringBuffer.AsMemory();
    public void Dispose() { ringBuffer.Clear(); }
}

// 빈 Payload
public class EmptyPayload : IPayload
{
    public ReadOnlyMemory<byte> Data => new();
    public void Dispose() { }
}

// ByteString Payload (Protobuf용)
public class ByteStringPayload(ByteString byteString) : IPayload
{
    public ReadOnlyMemory<byte> Data => byteString.ToByteArray();
    public void Dispose() { }
}
```

**Payload 타입별 용도**:
- `FramePayload`: ZMQ 수신 메시지의 Zero-Copy 래핑
- `ProtoPayload`: Protobuf 메시지 직렬화
- `CopyPayload`: 메시지 복사본 생성
- `PooledBytePayload`: 재사용 가능한 버퍼
- `EmptyPayload`: 빈 메시지 (Reply 등)

#### 📄 RoutePacket.cs (핵심 부분만 발췌)

**파일**: `Runtime/Message/RoutePacket.cs` (484 lines)

**핵심 코드 1 - RouteHeader 구조**:

```csharp
public class RouteHeader(Header header)
{
    public Header Header { get; } = header;        // 기본 헤더
    public long Sid { get; set; }                  // Session ID
    public bool IsSystem { get; set; }             // 시스템 메시지 여부
    public bool IsBase { get; set; }               // Base 프레임워크 메시지
    public bool IsBackend { get; set; }            // 백엔드 간 통신 여부
    public bool IsReply { get; set; }              // 응답 메시지 여부
    public long AccountId { get; set; }            // 계정 ID
    public long StageId { get; set; }              // Stage ID
    public string From { get; set; } = "";         // 송신자 NID (수신 시 설정)
    public bool IsToClient { get; set; }           // 클라이언트로 전송 여부

    public string MsgId => Header.MsgId;

    public RouteHeaderMsg ToMsg()
    {
        var message = new RouteHeaderMsg();
        message.HeaderMsg = Header.ToMsg();
        message.Sid = Sid;
        message.IsSystem = IsSystem;
        message.IsBase = IsBase;
        message.IsBackend = IsBackend;
        message.IsReply = IsReply;
        message.AccountId = AccountId;
        message.StageId = StageId;
        return message;
    }
}
```

**핵심 코드 2 - RoutePacket 클래스**:

```csharp
internal class RoutePacket : IBasePacket
{
    public RouteHeader RouteHeader;
    public IPayload Payload;

    protected RoutePacket(RouteHeader routeHeader, IPayload payload)
    {
        RouteHeader = routeHeader;
        Payload = payload;
    }

    public string MsgId => RouteHeader.MsgId;
    public Header Header => RouteHeader.Header;
    public long AccountId => RouteHeader.AccountId;
    public long StageId => RouteHeader.StageId;
    public bool IsSystem => RouteHeader.IsSystem;
    public ushort MsgSeq => Header.MsgSeq;

    // 팩토리 메서드
    public static RoutePacket Of(RouteHeader routeHeader, IPayload payload)
    {
        return new RoutePacket(routeHeader, payload);
    }

    public static RoutePacket ReplyOf(
        ushort serviceId,
        RouteHeader sourceHeader,
        ushort errorCode,
        IPacket? reply)
    {
        Header header = new(msgId: reply != null ? reply.MsgId : "")
        {
            ServiceId = serviceId,
            MsgSeq = sourceHeader.Header.MsgSeq
        };

        var routeHeader = RouteHeader.Of(header);
        routeHeader.IsReply = true;
        routeHeader.IsToClient = !sourceHeader.IsBackend;
        routeHeader.Sid = sourceHeader.Sid;
        routeHeader.IsBackend = sourceHeader.IsBackend;
        routeHeader.IsBase = sourceHeader.IsBase;
        routeHeader.AccountId = sourceHeader.AccountId;

        var routePacket = reply != null
            ? new RoutePacket(routeHeader, reply.Payload)
            : new RoutePacket(routeHeader, new EmptyPayload());
        routePacket.RouteHeader.Header.ErrorCode = errorCode;
        return routePacket;
    }

    public void Dispose()
    {
        Payload.Dispose();
    }
}
```

### 2.3 Communicator 계층

#### 📁 파일 위치
```
Runtime/
├── Communicator.cs           (319 lines) - 메인 오케스트레이터
├── XServerCommunicator.cs    (50 lines) - 수신 전용
├── XClientCommunicator.cs    (139 lines) - 송신 전용
├── MessageLoop.cs            (55 lines) - 스레드 관리
├── RequestCache.cs           (150 lines) - Request-Response 매칭
├── ServerAddressResolver.cs  (100 lines) - 서버 디스커버리
└── CommunicateListener.cs    (인터페이스)
```

#### 📄 XServerCommunicator.cs (그대로 복사)

**파일**: `Runtime/XServerCommunicator.cs` (50 lines)

```csharp
using PlayHouse.Runtime.PlaySocket;
using PlayHouse.Infrastructure.Common.Utils;
using PlayHouse.Infrastructure.Common.Logging;

namespace PlayHouse.Runtime;

internal class XServerCommunicator(IPlaySocket playSocket) : IServerCommunicator
{
    private readonly LOG<XServerCommunicator> _log = new();
    private ICommunicateListener? _listener;
    private bool _running = true;

    public void Bind(ICommunicateListener listener)
    {
        _listener = listener;
        playSocket.Bind();
    }

    public void Communicate()
    {
        while (_running)
        {
            var packet = playSocket.Receive();
            while (packet != null)
            {
                try
                {
                    _log.Trace(() =>
                        $"recvFrom:{packet.RouteHeader.From} - " +
                        $"[accountId:{packet.AccountId}, packetInfo:{packet.RouteHeader}]");

                    _listener!.OnReceive(packet);
                }
                catch (Exception e)
                {
                    _log.Error(() =>
                        $"{playSocket.EndPoint()} Error during communication - {e.Message}");
                }

                packet = playSocket.Receive();
            }

            Thread.Sleep(ConstOption.ThreadSleep);  // 1ms
        }
    }

    public void Stop()
    {
        _running = false;
    }
}
```

**동작 방식**:
1. `Bind()`: Listener 등록 및 소켓 바인딩
2. `Communicate()`: 무한 루프로 메시지 수신
3. `Receive()`: 1초 타임아웃으로 메시지 수신
4. 메시지 수신 시 `_listener.OnReceive()` 호출
5. 모든 메시지 처리 후 1ms Sleep

#### 📄 XClientCommunicator.cs (그대로 복사)

**파일**: `Runtime/XClientCommunicator.cs` (139 lines)

```csharp
using System.Collections.Concurrent;
using PlayHouse.Runtime.Message;
using PlayHouse.Runtime.PlaySocket;
using PlayHouse.Infrastructure.Common.Utils;
using PlayHouse.Infrastructure.Common.Logging;

namespace PlayHouse.Runtime;

internal class XClientCommunicator(IPlaySocket playSocket) : IClientCommunicator
{
    private readonly HashSet<string> _connected = new();
    private readonly HashSet<string> _disconnected = new();
    private readonly LOG<XClientCommunicator> _log = new();
    private readonly BlockingCollection<Action> _queue = new();

    public void Connect(string nid, string endpoint)
    {
        if (!_connected.Add(endpoint))
        {
            return;  // 이미 연결됨
        }

        _queue.Add(() =>
        {
            try
            {
                playSocket.Connect(endpoint);
                _log.Info(() => $"connected with - [nid:{nid},endpoint:{endpoint}]");
            }
            catch (Exception ex)
            {
                _log.Error(() =>
                    $"connect error - [nid:{nid},endpoint:{endpoint}], error:{ex.Message}");
            }
        });
    }

    public void Disconnect(string nid, string endpoint)
    {
        if (_disconnected.Contains(endpoint))
        {
            return;
        }

        try
        {
            if (_connected.Contains(endpoint))
            {
                playSocket.Disconnect(endpoint);
                _log.Info(() => $"disconnected with - [nid:{nid},endpoint:{endpoint}]");
                _connected.Remove(endpoint);
                _disconnected.Add(endpoint);
            }
        }
        catch (Exception ex)
        {
            _log.Error(() =>
                $"disconnect error - [nid:{nid},endpoint:{endpoint}],error:{ex.Message}");
        }
    }

    public void Send(string nid, RoutePacket routePacket)
    {
        _log.Trace(() =>
            $"before send queue:{nid} - " +
            $"[accountId:{routePacket.AccountId}, packetInfo:{routePacket.RouteHeader}]");

        _queue.Add(() =>
        {
            try
            {
                using (routePacket)
                {
                    _log.Trace(() =>
                        $"sendTo: nid:{nid} - " +
                        $"[accountId:{routePacket.AccountId}, packetInfo:{routePacket.RouteHeader}]");
                    playSocket.Send(nid, routePacket);
                }
            }
            catch (Exception e)
            {
                _log.Error(() =>
                    $"socket send error : [target nid:{nid}, target msgId:{routePacket.MsgId}, " +
                    $"accountId:{routePacket.AccountId}] - {e.Message}");
            }
        });
    }

    public void Communicate()
    {
        // BlockingCollection에서 작업을 꺼내 순차 실행
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action.Invoke();
            }
            catch (Exception e)
            {
                _log.Error(() =>
                    $"{playSocket.EndPoint()} Error during communication - {e.Message}");
            }
        }
    }

    public void Stop()
    {
        _queue.CompleteAdding();
    }
}
```

**동작 방식**:
1. `Connect()`, `Send()`: 작업을 `BlockingCollection`에 추가 (즉시 반환)
2. `Communicate()`: 별도 스레드에서 큐의 작업을 순차 실행
3. `_connected`: 연결된 엔드포인트 추적
4. 비동기 큐 방식으로 논블로킹 보장

#### 📄 MessageLoop.cs (그대로 복사)

**파일**: `Runtime/MessageLoop.cs` (55 lines)

```csharp
using PlayHouse.Infrastructure.Common.Utils;
using PlayHouse.Infrastructure.Common.Logging;

namespace PlayHouse.Runtime;

internal class MessageLoop
{
    private readonly IClientCommunicator _client;
    private readonly Thread _clientThread;
    private readonly LOG<MessageLoop> _log = new();
    private readonly IServerCommunicator _server;
    private readonly Thread _serverThread;

    public MessageLoop(IServerCommunicator server, IClientCommunicator client)
    {
        _server = server;
        _client = client;

        _serverThread = new Thread(() =>
        {
            _log.Info(() => $"start Server Communicator");
            _server.Communicate();
        })
        {
            Name = "server:Communicator"
        };

        _clientThread = new Thread(() =>
        {
            _log.Info(() => $"start client Communicator");
            _client.Communicate();
        })
        {
            Name = "client:Communicator"
        };
    }

    public void Start()
    {
        _serverThread.Start();
        _clientThread.Start();
    }

    public void Stop()
    {
        _server.Stop();
        _client.Stop();
    }

    public void AwaitTermination()
    {
        _clientThread.Join();
        _serverThread.Join();
    }
}
```

**스레드 관리**:
- `ServerThread`: 메시지 수신 전용 (Busy-Wait + 1ms Sleep)
- `ClientThread`: 메시지 송신 및 연결 관리 (BlockingCollection)

#### 📄 Communicator.cs (핵심 부분만 발췌)

**파일**: `Runtime/Communicator.cs` (319 lines)

**핵심 코드 1 - 초기화 및 시작**:

```csharp
internal class Communicator : ICommunicateListener
{
    private readonly XServerCommunicator _serverCommunicator;
    private readonly XClientCommunicator _clientCommunicator;
    private readonly MessageLoop _messageLoop;
    private readonly RequestCache _requestCache;
    private readonly IService _service;
    private readonly SystemDispatcher _systemDispatcher;

    public Communicator(
        CommunicatorOption option,
        PlaySocketConfig config,
        RequestCache requestCache,
        XServerInfoCenter serverInfoCenter,
        IService service,
        XClientCommunicator clientCommunicator)
    {
        _option = option;
        _requestCache = requestCache;
        _service = service;
        _clientCommunicator = clientCommunicator;
        _serviceId = _service.ServiceId;

        // ServerCommunicator 생성
        _serverCommunicator = new XServerCommunicator(
            PlaySocketFactory.CreatePlaySocket(
                new SocketConfig(option.Nid, option.BindEndpoint, config)));

        // MessageLoop 생성
        _messageLoop = new MessageLoop(_serverCommunicator, _clientCommunicator);

        // 기타 컴포넌트 초기화...
    }

    public void Start()
    {
        var nid = _option.Nid;
        var bindEndpoint = _option.BindEndpoint;

        // 1. 서버 소켓 바인딩
        _serverCommunicator.Bind(this);

        // 2. 송수신 스레드 시작
        _messageLoop.Start();

        // 3. 자기 자신에게 Connect (루프백 연결)
        _clientCommunicator.Connect(nid, bindEndpoint);

        // 4. 서버 디스커버리 시작
        _addressResolver.Start();

        // 5. 서비스 시작
        _service.OnStart();
        _performanceTester.Start();
        _systemDispatcher.Start();
        _requestCache.Start();

        _log.Info(() => $"============== server start ==============");
        _log.Info(() => $"Ready for nid:{nid},bind:{bindEndpoint}");
    }
}
```

**핵심 코드 2 - 메시지 디스패치**:

```csharp
public void OnReceive(RoutePacket routePacket)
{
    _performanceTester.IncCounter();
    Dispatch(routePacket);
}

private void Dispatch(RoutePacket routePacket)
{
    try
    {
        // 1. Backend Reply 메시지 처리 (Request-Response 패턴)
        if (routePacket.IsBackend() && routePacket.IsReply())
        {
            _requestCache.OnReply(routePacket);
            return;
        }

        // 2. 시스템 메시지 처리
        if (routePacket.IsSystem)
        {
            _systemDispatcher.OnPost(routePacket);
        }
        // 3. 서비스 메시지 처리
        else
        {
            _service.OnPost(routePacket);
        }
    }
    catch (Exception e)
    {
        // 에러 처리 및 응답...
    }
}
```

#### 📄 RequestCache.cs (핵심 부분만 발췌)

**파일**: `Runtime/RequestCache.cs` (150 lines)

**핵심 코드**:

```csharp
internal class RequestCache(int timeout)
{
    private readonly ConcurrentDictionary<int, ReplyObject> _cache = new();
    private readonly AtomicShort _sequence = new();
    private bool _isRunning = true;

    // MsgSeq 생성
    public ushort GetSequence()
    {
        return _sequence.IncrementAndGet();
    }

    // Request 등록
    public void Put(int seq, ReplyObject replyObject)
    {
        _cache[seq] = replyObject;
    }

    // Reply 처리
    public void OnReply(RoutePacket routePacket)
    {
        try
        {
            int msgSeq = routePacket.Header.MsgSeq;
            var replyObject = Get(msgSeq);

            if (replyObject != null)
            {
                replyObject.OnReceive(routePacket);
                Remove(msgSeq);
            }
            else
            {
                _log.Error(() =>
                    $"request is not exist - [packetInfo:{routePacket.RouteHeader}]");
            }
        }
        catch (Exception ex)
        {
            _log.Error(() => $"{ex}");
        }
    }

    // 타임아웃 체크 (별도 스레드에서 1초마다 실행)
    private void CheckExpire()
    {
        if (timeout > 0)
        {
            List<int> keysToDelete = new();

            foreach (var item in _cache)
            {
                if (item.Value.IsExpired(timeout))
                {
                    var replyObject = item.Value;
                    replyObject.Throw((ushort)BaseErrorCode.RequestTimeout);
                    keysToDelete.Add(item.Key);
                }
            }

            foreach (var key in keysToDelete)
            {
                Remove(key);
            }
        }
    }

    public void Start()
    {
        var thread = new Thread(() =>
        {
            while (_isRunning)
            {
                CheckExpire();
                Thread.Sleep(1000);
            }
        });
        thread.Start();
    }
}
```

**Request-Response 매칭 프로세스**:
1. Request 전송 시: `GetSequence()`로 MsgSeq 생성 → `Put(seq, callback)` 등록
2. Response 수신 시: `OnReply(packet)` → MsgSeq로 callback 찾아서 호출
3. 타임아웃 체크: 1초마다 만료된 요청 확인 및 제거

#### 📄 ServerAddressResolver.cs (핵심 부분만 발췌)

**파일**: `Runtime/ServerAddressResolver.cs` (100 lines)

```csharp
internal class ServerAddressResolver(
    string bindEndpoint,
    XServerInfoCenter serverInfoCenter,
    XClientCommunicator communicateClient,
    IService service,
    ISystemController system)
{
    private readonly LOG<ServerAddressResolver> _log = new();
    private CancellationTokenSource? _cts;
    private PeriodicTimer? _periodicTimer;

    public void Start()
    {
        _log.Info(() => $"Server address resolver start");

        _cts = new CancellationTokenSource();
        _periodicTimer = new PeriodicTimer(
            TimeSpan.FromMilliseconds(ConstOption.AddressResolverPeriodMs));

        Task.Run(async () => await RunPeriodicTaskAsync(_cts.Token));
    }

    private async Task RunPeriodicTaskAsync(CancellationToken cancellationToken)
    {
        while (await _periodicTimer!.WaitForNextTickAsync(cancellationToken))
        {
            await TimerCallbackAsync();
        }
    }

    private async Task TimerCallbackAsync()
    {
        try
        {
            // 1. 내 서버 정보 생성
            var myServerInfo = new XServerInfo(
                bindEndpoint,
                service.ServiceId,
                service.ServerId,
                service.Nid,
                service.GetServiceType(),
                service.GetServerState(),
                service.GetActorCount(),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            // 2. 전체 서버 목록 조회 (시스템 컨트롤러에서)
            var serverInfoList = await system.UpdateServerInfoAsync(myServerInfo);

            // 3. 새로운 서버 발견 시 Connect
            var updateList = serverInfoCenter.Update(
                serverInfoList.Select(XServerInfo.Of).ToList());

            foreach (var serverInfo in updateList)
            {
                switch (serverInfo.GetState())
                {
                    case ServerState.RUNNING:
                        communicateClient.Connect(
                            serverInfo.GetNid(),
                            serverInfo.GetBindEndpoint());
                        break;

                    case ServerState.DISABLE:
                        communicateClient.Disconnect(
                            serverInfo.GetNid(),
                            serverInfo.GetBindEndpoint());
                        break;
                }
            }
        }
        catch (Exception e)
        {
            _log.Error(() => $"Error in TimerCallbackAsync: {e}");
        }
    }
}
```

**서버 디스커버리 프로세스**:
1. 주기적으로 (기본 3초) 자신의 ServerInfo를 시스템 컨트롤러에 전송
2. 전체 서버 목록을 수신
3. 새로운 서버 발견 시 자동으로 `Connect()` 호출
4. DISABLE 상태 서버는 `Disconnect()` 호출

---

### 2.5 ISystemController 구현 가이드 (컨텐츠 개발자용)

`ISystemController`는 **컨텐츠 개발자가 구현해야 하는 인터페이스**입니다. 프레임워크의 `ServerAddressResolver`가 이 인터페이스를 통해 서버 목록을 조회하고, ZeroMQ Full-Mesh 연결을 자동으로 구축합니다.

#### 인터페이스 정의

**파일**: `Abstractions/Shared/ISystemController.cs`

```csharp
public delegate Task SystemHandler(IPacket packet, ISystemPanel panel, ISender sender);

public interface ISystemHandlerRegister
{
    void Add(string msgId, SystemHandler handler);
}

public interface ISystemController
{
    /// <summary>
    /// 시스템 메시지 핸들러 등록
    /// </summary>
    void Handles(ISystemHandlerRegister handlerRegister);

    /// <summary>
    /// 내 서버 정보를 등록하고, 전체 서버 목록을 반환
    /// ServerAddressResolver가 주기적으로 호출 (기본 3초)
    /// </summary>
    Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo);
}
```

#### IServerInfo 인터페이스

```csharp
public enum ServerState
{
    RUNNING,   // 정상 동작 중 → Connect
    PAUSE,     // 일시 정지
    DISABLE    // 비활성화 → Disconnect
}

public interface IServerInfo
{
    string GetBindEndpoint();    // "tcp://192.168.1.10:8001"
    string GetNid();             // "1000:1" (ServiceId:ServerId)
    int GetServerId();           // 1, 2, 3...
    ServiceType GetServiceType(); // API, Play
    ushort GetServiceId();       // 1000, 2000, 3000
    ServerState GetState();      // RUNNING, PAUSE, DISABLE
    long GetLastUpdate();        // Unix timestamp (밀리초)
    int GetActorCount();         // 현재 Actor 수
}
```

#### 동작 흐름

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    서버 디스커버리 Full-Mesh 구축                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────────────┐                                               │
│  │  ServerAddressResolver│  (프레임워크)                                 │
│  │  - 매 3초마다 호출     │                                               │
│  └──────────┬───────────┘                                               │
│             │                                                            │
│             │ 1. UpdateServerInfoAsync(myServerInfo)                    │
│             ▼                                                            │
│  ┌──────────────────────┐      2. 서버 정보 저장/조회                    │
│  │   ISystemController  │ ◄─────────────────────────►  [외부 저장소]    │
│  │   (컨텐츠 구현)       │                               Redis/Consul/   │
│  └──────────┬───────────┘                               etcd/DB         │
│             │                                                            │
│             │ 3. return IReadOnlyList<IServerInfo>                      │
│             ▼                                                            │
│  ┌──────────────────────┐                                               │
│  │  ServerAddressResolver│                                               │
│  │  - 새 서버 발견       │                                               │
│  └──────────┬───────────┘                                               │
│             │                                                            │
│             │ 4. 상태에 따른 처리                                        │
│             ▼                                                            │
│  ┌──────────────────────────────────────────────┐                       │
│  │  XClientCommunicator                          │                       │
│  │  - RUNNING → Connect(nid, endpoint)          │                       │
│  │  - DISABLE → Disconnect(nid, endpoint)       │                       │
│  └──────────────────────────────────────────────┘                       │
│             │                                                            │
│             ▼                                                            │
│       [ZeroMQ Full-Mesh 연결 완성]                                       │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

#### 구현 예시 1: Redis 기반

```csharp
public class RedisSystemController : ISystemController
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _serverTtl = TimeSpan.FromSeconds(10);

    public RedisSystemController(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public void Handles(ISystemHandlerRegister handlerRegister)
    {
        // 커스텀 시스템 메시지 핸들러 등록 (필요 시)
        // handlerRegister.Add("MySystemMsg", HandleMySystemMsg);
    }

    public async Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
    {
        var db = _redis.GetDatabase();
        var nid = serverInfo.GetNid();

        // 1. 내 서버 정보 저장 (TTL 설정으로 자동 만료)
        var serverData = JsonSerializer.Serialize(new ServerInfoDto(serverInfo));
        await db.StringSetAsync($"server:{nid}", serverData, _serverTtl);

        // 2. 모든 서버 목록 조회
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: "server:*").ToList();

        var result = new List<IServerInfo>();
        foreach (var key in keys)
        {
            var data = await db.StringGetAsync(key);
            if (data.HasValue)
            {
                var dto = JsonSerializer.Deserialize<ServerInfoDto>(data!);
                result.Add(dto!.ToServerInfo());
            }
        }

        return result;
    }
}
```

#### 구현 예시 2: Consul 기반

```csharp
public class ConsulSystemController : ISystemController
{
    private readonly IConsulClient _consul;

    public ConsulSystemController(IConsulClient consul)
    {
        _consul = consul;
    }

    public void Handles(ISystemHandlerRegister handlerRegister)
    {
        // 커스텀 시스템 메시지 핸들러 등록 (필요 시)
    }

    public async Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
    {
        var nid = serverInfo.GetNid();

        // 1. 서비스 등록 (Health Check TTL 방식)
        var registration = new AgentServiceRegistration
        {
            ID = nid,
            Name = $"playhouse-{serverInfo.GetServiceType()}",
            Address = ExtractHost(serverInfo.GetBindEndpoint()),
            Port = ExtractPort(serverInfo.GetBindEndpoint()),
            Tags = new[] { serverInfo.GetServiceId().ToString() },
            Check = new AgentServiceCheck
            {
                TTL = TimeSpan.FromSeconds(10),
                DeregisterCriticalServiceAfter = TimeSpan.FromSeconds(30)
            }
        };
        await _consul.Agent.ServiceRegister(registration);
        await _consul.Agent.PassTTL($"service:{nid}", "alive");

        // 2. 모든 서비스 조회
        var services = await _consul.Agent.Services();
        var result = new List<IServerInfo>();

        foreach (var (_, service) in services.Response)
        {
            if (service.Service.StartsWith("playhouse-"))
            {
                result.Add(MapToServerInfo(service));
            }
        }

        return result;
    }
}
```

#### 구현 예시 3: 단순 메모리 기반 (개발/테스트용)

```csharp
public class InMemorySystemController : ISystemController
{
    // 정적으로 공유되는 서버 목록 (단일 프로세스 테스트용)
    private static readonly ConcurrentDictionary<string, ServerInfoEntry> _servers = new();
    private static readonly TimeSpan _expireTime = TimeSpan.FromSeconds(10);

    public void Handles(ISystemHandlerRegister handlerRegister)
    {
        // 테스트용 시스템 메시지 핸들러
        handlerRegister.Add("PingMsg", HandlePing);
    }

    private async Task HandlePing(IPacket packet, ISystemPanel panel, ISender sender)
    {
        sender.Reply(new PongMsg { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        await Task.CompletedTask;
    }

    public Task<IReadOnlyList<IServerInfo>> UpdateServerInfoAsync(IServerInfo serverInfo)
    {
        var nid = serverInfo.GetNid();
        var now = DateTimeOffset.UtcNow;

        // 1. 내 서버 정보 갱신
        _servers[nid] = new ServerInfoEntry(serverInfo, now);

        // 2. 만료된 서버 제거
        var expiredKeys = _servers
            .Where(kv => now - kv.Value.LastUpdate > _expireTime)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _servers.TryRemove(key, out _);
        }

        // 3. 활성 서버 목록 반환
        var result = _servers.Values
            .Select(e => e.ServerInfo)
            .ToList();

        return Task.FromResult<IReadOnlyList<IServerInfo>>(result);
    }

    private record ServerInfoEntry(IServerInfo ServerInfo, DateTimeOffset LastUpdate);
}
```

#### 부트스트랩 등록

```csharp
// Program.cs 또는 Startup.cs

// 1. DI 컨테이너에 ISystemController 구현체 등록
services.AddSingleton<ISystemController, RedisSystemController>();
// 또는
services.AddSingleton<ISystemController, ConsulSystemController>();
// 또는 (개발용)
services.AddSingleton<ISystemController, InMemorySystemController>();

// 2. 프레임워크가 자동으로 해석
// Communicator.cs 내부:
// var systemController = _option.ServiceProvider.GetRequiredService<ISystemController>();
```

#### 핵심 구현 포인트

| 항목 | 설명 |
|------|------|
| **UpdateServerInfoAsync 주기** | 프레임워크가 약 3초마다 호출 (`ConstOption.AddressResolverPeriodMs`) |
| **서버 TTL** | 10초 이상 권장 (3초 주기 × 3회 이상 여유) |
| **ServerState 활용** | `RUNNING`: 연결, `DISABLE`: 연결 해제, `PAUSE`: 연결 유지 |
| **Handles 메서드** | 서버 간 커스텀 시스템 메시지 처리 (선택적) |
| **외부 저장소** | Redis, Consul, etcd, DB 등 분산 환경에 적합한 저장소 사용 |

#### 시스템 메시지 핸들러 (Handles 메서드)

`Handles` 메서드를 통해 서버 간 커스텀 시스템 메시지를 처리할 수 있습니다:

```csharp
public void Handles(ISystemHandlerRegister handlerRegister)
{
    // 서버 간 시스템 메시지 핸들러 등록
    handlerRegister.Add("ClusterJoinMsg", HandleClusterJoin);
    handlerRegister.Add("ClusterLeaveMsg", HandleClusterLeave);
    handlerRegister.Add("ServerStatusQueryMsg", HandleServerStatusQuery);
}

private async Task HandleClusterJoin(IPacket packet, ISystemPanel panel, ISender sender)
{
    var msg = ClusterJoinMsg.Parser.ParseFrom(packet.Payload.DataSpan);
    _log.Info(() => $"Server joined: {msg.Nid}");

    // 클러스터 가입 처리 로직
    await Task.CompletedTask;
}

private async Task HandleServerStatusQuery(IPacket packet, ISystemPanel panel, ISender sender)
{
    // 서버 상태 조회 응답
    var response = new ServerStatusResponseMsg
    {
        State = panel.GetServerState().ToString(),
        ActorCount = panel.GetServerInfo().GetActorCount()
    };

    sender.Reply(response);
    await Task.CompletedTask;
}
```

**ISystemPanel 인터페이스** (핸들러에서 사용 가능):

```csharp
public interface ISystemPanel
{
    IServerInfo GetServerInfo();           // 현재 서버 정보
    IServerInfo GetServerInfoBy(ushort serviceId);  // 특정 서비스의 서버
    IServerInfo GetServerInfoByNid(string nid);     // NID로 서버 조회
    IList<IServerInfo> GetServers();       // 전체 서버 목록
    ServerState GetServerState();          // 현재 서버 상태
    void Pause();                          // 서버 일시 정지
    void Resume();                         // 서버 재개
    Task ShutdownASync();                  // 서버 종료
}
```

## 3. 메시지 구조 상세

### 3.1 ZMQ 3-Frame 구조

```
┌─────────────────────────────────────────────────────────┐
│                     ZMQ Message                       │
├─────────────────────────────────────────────────────────┤
│ Frame 0: Target NID (UTF-8 string)                      │
│          예: "1000:1" (Service 1000, Server 1)          │
├─────────────────────────────────────────────────────────┤
│ Frame 1: RouteHeader (Protobuf 직렬화)                  │
│          - Header (ServiceId, MsgId, MsgSeq, etc.)      │
│          - Sid, AccountId, StageId                      │
│          - IsSystem, IsBase, IsBackend, IsReply         │
├─────────────────────────────────────────────────────────┤
│ Frame 2: Payload (바이트 배열)                          │
│          - Protobuf 메시지 직렬화                       │
│          - 또는 기타 바이너리 데이터                    │
└─────────────────────────────────────────────────────────┘
```

**송신 시**: 애플리케이션이 3-Frame을 직접 구성
**수신 시**: Router 소켓이 Frame 0에 송신자 NID를 자동 추가 (Identity)

### 3.2 RouteHeader Protobuf 정의

**파일**: `Playhouse.Protocol/route.proto`

```protobuf
message HeaderMsg {
  int32 service_id = 1;      // 서비스 ID
  string msg_id = 2;         // 메시지 타입 (Protobuf 타입명)
  int32 msg_seq = 3;         // Request-Response 시퀀스 번호
  int32 error_code = 4;      // 에러 코드 (Response 시)
  int64 stageId = 5;         // Stage ID
}

message RouteHeaderMsg {
  HeaderMsg header_msg = 1;  // 기본 헤더
  int64 sid = 2;             // Session ID
  bool is_system = 3;        // 시스템 메시지 여부
  bool is_reply = 4;         // Reply 메시지 여부
  bool is_base = 5;          // Base 프레임워크 메시지 여부
  bool is_backend = 6;       // 백엔드 간 통신 여부
  int64 stage_id = 7;        // Stage ID
  int64 account_id = 8;      // Account ID
}
```

**필드 설명**:
- `service_id`: 목적지 서비스 타입 (1000=API, 2000=Session, 3000=Play)
- `msg_id`: 메시지 타입 식별자 (Protobuf 메시지 이름)
- `msg_seq`: Request-Response 매칭을 위한 시퀀스 번호 (0 = Notification)
- `error_code`: 응답 메시지의 에러 코드
- `sid`: Session ID (클라이언트 연결 식별)
- `account_id`: 계정 ID (Stage의 소유자)
- `stage_id`: Stage ID (Actor 인스턴스 식별)
- `is_system`: 시스템 메시지 여부 (ServerInfo, Heartbeat 등)
- `is_base`: PlayHouse 프레임워크 메시지 여부
- `is_backend`: 서버 간 통신 여부 (true) vs 클라이언트 통신 (false)
- `is_reply`: 응답 메시지 여부

### 3.3 NID (Node ID) 구조

**형식**: `"{serviceId}:{serverId}"`

```csharp
public static string MakeNid(ushort serviceId, int serverId)
{
    return $"{serviceId}:{serverId}";
}

// 예시:
// API 서버 1번: "1000:1"
// API 서버 2번: "1000:2"
// Session 서버 1번: "2000:1"
// Play 서버 3번: "3000:3"
```

**용도**:
- Router 소켓의 Identity로 사용
- 메시지 라우팅의 핵심 식별자
- ServerInfo 교환 시 서버 식별

## 4. 서버 연결 프로세스

### 4.1 서버 시작 시퀀스 다이어그램

```
┌──────────────┐
│ Application  │
└──────┬───────┘
       │
       │ 1. Communicator.Start()
       ↓
┌─────────────────────────────────────────────┐
│              Communicator                   │
├─────────────────────────────────────────────┤
│                                             │
│  ┌─────────────────────────────────────┐   │
│  │ 1. XServerCommunicator.Bind()       │   │
│  │    - playSocket.Bind(bindEndpoint)  │   │
│  │    - "tcp://0.0.0.0:8001"           │   │
│  └─────────────────────────────────────┘   │
│                                             │
│  ┌─────────────────────────────────────┐   │
│  │ 2. MessageLoop.Start()              │   │
│  │    - ServerThread 시작 (수신)       │   │
│  │    - ClientThread 시작 (송신)       │   │
│  └─────────────────────────────────────┘   │
│                                             │
│  ┌─────────────────────────────────────┐   │
│  │ 3. XClientCommunicator.Connect()    │   │
│  │    - 자기 자신에게 Connect          │   │
│  │    - Connect(myNid, myEndpoint)     │   │
│  └─────────────────────────────────────┘   │
│                                             │
│  ┌─────────────────────────────────────┐   │
│  │ 4. ServerAddressResolver.Start()    │   │
│  │    - 주기적 ServerInfo 브로드캐스트 │   │
│  │    - 새 서버 발견 시 Connect        │   │
│  └─────────────────────────────────────┘   │
└─────────────────────────────────────────────┘
```

### 4.2 서버 간 Full-Mesh 연결

```
Server A (1000:1)              Server B (2000:1)              Server C (3000:1)
  Bind(*:8001)                   Bind(*:9001)                   Bind(*:10001)
      │                              │                              │
      │ Connect(localhost:8001)      │ Connect(localhost:9001)      │ Connect(localhost:10001)
      ├──────────────────────────────┼──────────────────────────────┤
      │                              │                              │
      │ ServerAddressResolver        │ ServerAddressResolver        │ ServerAddressResolver
      │ (3초마다)                    │ (3초마다)                    │ (3초마다)
      │                              │                              │
      │ UpdateServerInfoAsync()      │ UpdateServerInfoAsync()      │ UpdateServerInfoAsync()
      │ ↓ ServerInfo List            │ ↓ ServerInfo List            │ ↓ ServerInfo List
      │                              │                              │
      │ 새 서버 발견:                │ 새 서버 발견:                │ 새 서버 발견:
      │ - Connect(2000:1, 9001)      │ - Connect(1000:1, 8001)      │ - Connect(1000:1, 8001)
      │ - Connect(3000:1, 10001)     │ - Connect(3000:1, 10001)     │ - Connect(2000:1, 9001)
      │                              │                              │
      └──────────── Full-Mesh Topology ─────────────────────────────┘
```

**특징**:
1. 각 서버는 자기 자신을 포함한 모든 서버에 Connect
2. ServerAddressResolver가 주기적으로 서버 목록 갱신
3. 새 서버 추가 시 자동으로 Full-Mesh 연결 형성
4. 서버 제거 시 자동으로 Disconnect

### 4.3 자기 자신에게 Connect하는 이유

```csharp
// Communicator.Start()에서
_clientCommunicator.Connect(nid, bindEndpoint);  // 자기 자신에게 Connect
```

**이유**:
1. **Router 소켓의 동작 방식**: Router 소켓은 Identity 기반 라우팅을 사용합니다.
2. **Identity 등록**: Connect()를 호출해야 자신의 Identity가 라우팅 테이블에 등록됩니다.
3. **로컬 메시지 처리**: 자기 자신에게 메시지를 보낼 수 있어야 합니다 (예: Timer 콜백).
4. **일관된 처리**: 로컬/원격 메시지를 동일한 방식으로 처리할 수 있습니다.

## 5. 메시지 송수신 흐름

### 5.1 Request 송신 흐름

```
┌────────────────┐
│ Application    │
│ (Stage/Actor)  │
└────────┬───────┘
         │
         │ sender.Request(targetNid, packet, callback)
         ↓
┌─────────────────────────────────────────────┐
│             XSender                         │
├─────────────────────────────────────────────┤
│ 1. msgSeq = requestCache.GetSequence()     │
│ 2. routeHeader.Header.MsgSeq = msgSeq      │
│ 3. requestCache.Put(msgSeq, callback)      │
│ 4. clientCommunicator.Send(nid, packet)    │
└─────────────────────────────────────────────┘
         │
         │ Queue.Add(() => playSocket.Send(...))
         ↓
┌─────────────────────────────────────────────┐
│       XClientCommunicator                   │
│       (ClientThread)                        │
├─────────────────────────────────────────────┤
│ foreach (action in queue.GetConsumingEnum) │
│ {                                           │
│     action.Invoke();  // Send 실행          │
│ }                                           │
└─────────────────────────────────────────────┘
         │
         │ playSocket.Send(nid, routePacket)
         ↓
┌─────────────────────────────────────────────┐
│       NetMqPlaySocket                       │
├─────────────────────────────────────────────┤
│ 1. ZMQMessage 생성                        │
│ 2. Frame 0: Target NID                      │
│ 3. Frame 1: RouteHeader (Protobuf)          │
│ 4. Frame 2: Payload                         │
│ 5. socket.TrySendMultipartMessage()         │
└─────────────────────────────────────────────┘
         │
         ↓
    [Network]
```

### 5.2 Response 수신 흐름

```
    [Network]
         │
         ↓
┌─────────────────────────────────────────────┐
│       NetMqPlaySocket                       │
├─────────────────────────────────────────────┤
│ 1. socket.TryReceiveMultipartMessage()      │
│ 2. Frame 0 → From NID                       │
│ 3. Frame 1 → RouteHeader (Protobuf 파싱)    │
│ 4. Frame 2 → FramePayload (Zero-Copy)       │
└─────────────────────────────────────────────┘
         │
         ↓
┌─────────────────────────────────────────────┐
│       XServerCommunicator                   │
│       (ServerThread)                        │
├─────────────────────────────────────────────┤
│ while (running) {                           │
│   packet = playSocket.Receive();            │
│   if (packet != null) {                     │
│     listener.OnReceive(packet);             │
│   }                                         │
│ }                                           │
└─────────────────────────────────────────────┘
         │
         │ listener.OnReceive(routePacket)
         ↓
┌─────────────────────────────────────────────┐
│       Communicator                          │
├─────────────────────────────────────────────┤
│ Dispatch(routePacket) {                     │
│   if (IsBackend && IsReply) {               │
│     requestCache.OnReply(packet);           │
│   }                                         │
│ }                                           │
└─────────────────────────────────────────────┘
         │
         │ requestCache.OnReply(packet)
         ↓
┌─────────────────────────────────────────────┐
│       RequestCache                          │
├─────────────────────────────────────────────┤
│ 1. msgSeq = packet.Header.MsgSeq            │
│ 2. replyObject = cache.Get(msgSeq)          │
│ 3. replyObject.OnReceive(packet)            │
│ 4. cache.Remove(msgSeq)                     │
└─────────────────────────────────────────────┘
         │
         │ callback.Invoke(errorCode, packet)
         ↓
┌────────────────┐
│ Application    │
│ (Callback)     │
└────────────────┘
```

### 5.3 Request-Response 매칭 과정

```
Request 전송:
  msgSeq = 1
  ↓
  RequestCache.Put(1, callback)
  ↓
  Send(nid, packet with msgSeq=1)

Response 수신:
  Receive(packet with msgSeq=1, IsReply=true)
  ↓
  RequestCache.OnReply(packet)
  ↓
  replyObject = cache.Get(1)
  ↓
  replyObject.OnReceive(packet)
  ↓
  callback.Invoke(errorCode, packet)
  ↓
  cache.Remove(1)
```

## 6. 통합 가이드

### 6.1 그대로 복사할 파일 (수정 불필요)

```
PlaySocket 계층 (100% 재사용):
├── Runtime/PlaySocket/IPlaySocket.cs
├── Runtime/PlaySocket/ZMQPlaySocket.cs
├── Runtime/PlaySocket/PlaySocketConfig.cs
├── Runtime/PlaySocket/SocketConfig.cs
└── Runtime/PlaySocket/PlaySocketFactory.cs

Message 계층 (100% 재사용):
├── Runtime/Message/Payload.cs
├── Runtime/Message/RoutePacket.cs (일부 메서드는 프로젝트에 맞게 조정)
└── Runtime/Message/IBasePacket.cs

Communicator 계층 (95% 재사용):
├── Runtime/XServerCommunicator.cs
├── Runtime/XClientCommunicator.cs
├── Runtime/MessageLoop.cs
├── Runtime/RequestCache.cs
└── Runtime/ServerAddressResolver.cs
```

### 6.2 수정 필요한 파일

#### Communicator.cs

**수정 사항**:
1. `SystemDispatcher` → PlayHouse-NET의 시스템 메시지 처리기로 교체
2. `IService` → PlayHouse-NET의 서비스 인터페이스로 교체
3. `XSystemPanel` → PlayHouse-NET의 시스템 패널로 교체

**수정 예시**:

```csharp
// 기존 (참조 시스템)
private readonly SystemDispatcher _systemDispatcher;
private readonly XSystemPanel _systemPanel;

// 수정 (PlayHouse-NET)
private readonly ISystemMessageHandler _systemHandler;
private readonly IServerInfoManager _serverInfoManager;
```

#### RoutePacket.cs

**수정 사항**:
1. 팩토리 메서드 중 PlayHouse-NET에 없는 메서드 제거
2. `TimerOf`, `AsyncBlockPacket` 등 프로젝트별 특화 메서드는 선택적 구현

### 6.3 새로 작성할 파일

#### XSender.cs

Request/Reply를 위한 송신 헬퍼 클래스:

```csharp
internal class XSender
{
    private readonly ushort _serviceId;
    private readonly XClientCommunicator _clientCommunicator;
    private readonly RequestCache _requestCache;
    private RouteHeader? _currentHeader;

    public XSender(
        ushort serviceId,
        XClientCommunicator clientCommunicator,
        RequestCache requestCache)
    {
        _serviceId = serviceId;
        _clientCommunicator = clientCommunicator;
        _requestCache = requestCache;
    }

    // Request 전송 (callback 방식)
    public void Request(string nid, IPacket packet, ReplyCallback callback)
    {
        var msgSeq = _requestCache.GetSequence();
        var routePacket = RoutePacket.Of(packet);
        routePacket.SetMsgSeq(msgSeq);
        routePacket.RouteHeader.IsBackend = true;

        var replyObject = new ReplyObject(callback);
        _requestCache.Put(msgSeq, replyObject);

        _clientCommunicator.Send(nid, routePacket);
    }

    // Request 전송 (async/await 방식)
    public async Task<CPacket> RequestAsync(string nid, IPacket packet)
    {
        var tcs = new TaskCompletionSource<RoutePacket>();
        var msgSeq = _requestCache.GetSequence();
        var routePacket = RoutePacket.Of(packet);
        routePacket.SetMsgSeq(msgSeq);
        routePacket.RouteHeader.IsBackend = true;

        var replyObject = new ReplyObject(taskCompletionSource: tcs);
        _requestCache.Put(msgSeq, replyObject);

        _clientCommunicator.Send(nid, routePacket);

        var replyPacket = await tcs.Task;
        return CPacket.Of(replyPacket);
    }

    // Reply 전송
    public void Reply(ushort errorCode, IPacket? replyPacket = null)
    {
        if (_currentHeader == null)
        {
            throw new Exception("No current packet header");
        }

        var routePacket = RoutePacket.ReplyOf(
            _serviceId,
            _currentHeader,
            errorCode,
            replyPacket);

        _clientCommunicator.Send(_currentHeader.From, routePacket);
    }

    // 현재 처리 중인 패킷 헤더 설정 (Reply를 위해)
    public void SetCurrentPacketHeader(RouteHeader header)
    {
        _currentHeader = header;
    }
}
```

#### ISystemController.cs

서버 정보 관리 인터페이스:

```csharp
public interface ISystemController
{
    // 서버 정보 업데이트 및 전체 서버 목록 반환
    Task<List<ServerInfo>> UpdateServerInfoAsync(XServerInfo myServerInfo);
}
```

## 7. 통합 예제 코드

### 7.1 서버 초기화 예제

```csharp
// 1. SocketConfig 생성
var nid = ISystemPanel.MakeNid(serviceId: 1000, serverId: 1);
var bindEndpoint = "tcp://0.0.0.0:8001";
var playSocketConfig = new PlaySocketConfig
{
    BackLog = 1000,
    Linger = 0,
    SendBufferSize = 2 * 1024 * 1024,
    ReceiveBufferSize = 2 * 1024 * 1024,
    SendHighWatermark = 1000000,
    ReceiveHighWatermark = 1000000
};

var socketConfig = new SocketConfig(nid, bindEndpoint, playSocketConfig);

// 2. PlaySocket 생성
var playSocket = PlaySocketFactory.CreatePlaySocket(socketConfig);

// 3. Communicator 생성
var requestCache = new RequestCache(timeout: 30000);  // 30초 타임아웃
var serverInfoCenter = new XServerInfoCenter();
var clientCommunicator = new XClientCommunicator(playSocket);

var communicatorOption = new CommunicatorOption.Builder()
    .SetIp("127.0.0.1")
    .SetPort(8001)
    .SetServiceId(1000)
    .SetServerId(1)
    .SetServiceProvider(serviceProvider)
    .SetPacketProducer(packetProducer)
    .Build();

var communicator = new Communicator(
    communicatorOption,
    playSocketConfig,
    requestCache,
    serverInfoCenter,
    service,
    clientCommunicator
);

// 4. 서버 시작
communicator.Start();

// 5. 종료 시
await communicator.StopAsync();
communicator.AwaitTermination();
```

### 7.2 메시지 송신 예제

```csharp
// Request 송신 (callback 방식)
var sender = new XSender(serviceId, clientCommunicator, requestCache);
var request = new LoginReq { UserId = "user123", Password = "pass" };

sender.Request("2000:1", request, (errorCode, reply) =>
{
    if (errorCode == 0)
    {
        var loginRes = reply.Parse<LoginRes>();
        Console.WriteLine($"Login success: {loginRes.SessionId}");
    }
    else
    {
        Console.WriteLine($"Login failed: {errorCode}");
    }
});

// Request 송신 (async/await 방식)
try
{
    var reply = await sender.RequestAsync("2000:1", request);
    var loginRes = reply.Parse<LoginRes>();
    Console.WriteLine($"Login success: {loginRes.SessionId}");
}
catch (PlayHouseException ex)
{
    Console.WriteLine($"Login failed: {ex.ErrorCode}");
}
```

### 7.3 메시지 수신 및 Reply 예제

```csharp
// Communicator에서 메시지 수신 시
public void OnReceive(RoutePacket routePacket)
{
    var sender = new XSender(_serviceId, _clientCommunicator, _requestCache);
    sender.SetCurrentPacketHeader(routePacket.RouteHeader);

    try
    {
        // 메시지 처리
        var loginReq = PacketProducer.CreatePacket(
            routePacket.MsgId,
            routePacket.Payload,
            routePacket.MsgSeq);

        // 비즈니스 로직 처리
        var loginRes = ProcessLogin(loginReq);

        // Reply 전송
        sender.Reply(errorCode: 0, loginRes);
    }
    catch (Exception ex)
    {
        // 에러 응답
        sender.Reply(errorCode: 500);
    }
}
```

### 7.4 서버 간 연결 예제

```csharp
// ServerAddressResolver에서 새 서버 발견 시
private async Task TimerCallbackAsync()
{
    // 1. 내 서버 정보 생성
    var myServerInfo = new XServerInfo(
        bindEndpoint: "tcp://10.0.1.100:8001",
        serviceId: 1000,
        serverId: 1,
        nid: "1000:1",
        serviceType: "API",
        serverState: ServerState.RUNNING,
        actorCount: 100,
        timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    );

    // 2. 전체 서버 목록 조회
    var serverInfoList = await _systemController.UpdateServerInfoAsync(myServerInfo);

    // 3. 새로운 서버에 Connect
    var updateList = _serverInfoCenter.Update(
        serverInfoList.Select(XServerInfo.Of).ToList());

    foreach (var serverInfo in updateList)
    {
        if (serverInfo.GetState() == ServerState.RUNNING)
        {
            // 새 서버에 Connect
            _clientCommunicator.Connect(
                serverInfo.GetNid(),      // "2000:1"
                serverInfo.GetBindEndpoint()  // "tcp://10.0.1.200:9001"
            );
        }
    }
}
```

## 8. 성능 최적화 및 주의사항

### 8.1 Zero-Copy 최적화

```csharp
// 수신 시 FramePayload 사용
public RoutePacket? Receive()
{
    // Frame을 직접 래핑 (복사 없음)
    var payload = new FramePayload(message[2]);
    return RoutePacket.Of(new RouteHeader(header), payload);
}

// 송신 시 FramePayload 재사용
public void Send(string nid, RoutePacket routePacket)
{
    if (payload is FramePayload framePayload)
    {
        // Frame을 그대로 재사용 (복사 없음)
        frame = framePayload.Frame;
    }
}
```

### 8.2 버퍼 설정 권장값

```csharp
var playSocketConfig = new PlaySocketConfig
{
    // 연결 대기 큐: 동시 접속 예상치의 2배
    BackLog = 1000,

    // OS 레벨 버퍼: 메시지 크기와 처리량에 따라 조정
    SendBufferSize = 2 * 1024 * 1024,      // 2MB
    ReceiveBufferSize = 2 * 1024 * 1024,   // 2MB

    // 내부 큐: 초당 메시지 수 * 예상 지연 시간(초)
    SendHighWatermark = 1000000,
    ReceiveHighWatermark = 1000000,

    // Linger: 0 (즉시 종료)
    Linger = 0
};
```

### 8.3 스레드 모델 주의사항

```
❌ 잘못된 사용:
- ServerThread에서 Send() 호출
- ClientThread에서 Receive() 호출
- Communicate() 메서드 내에서 블로킹 작업

✅ 올바른 사용:
- ServerThread: Receive()만 호출
- ClientThread: Send(), Connect()만 호출
- 비즈니스 로직은 별도 스레드 또는 Task에서 처리
```

### 8.4 Request-Response 타임아웃

```csharp
// RequestCache 생성 시 타임아웃 설정
var requestCache = new RequestCache(timeout: 30000);  // 30초

// 타임아웃 발생 시 자동으로 예외 발생
try
{
    var reply = await sender.RequestAsync(nid, request);
}
catch (PlayHouseException ex)
{
    if (ex.ErrorCode == BaseErrorCode.RequestTimeout)
    {
        // 타임아웃 처리
    }
}
```

### 8.5 에러 처리 패턴

```csharp
// 송신 실패 감지
if (!_socket.TrySendMultipartMessage(message))
{
    // RouterMandatory = true이므로 미연결 대상은 즉시 실패
    _log.Error(() => $"Send failed to {nid}");
}

// 수신 타임아웃 처리
var packet = playSocket.Receive();  // 1초 타임아웃
if (packet == null)
{
    // 타임아웃 발생 (정상 동작)
    continue;
}

// Request 타임아웃 처리
requestCache.CheckExpire();  // 1초마다 실행
```

## 9. 요약

### 9.1 핵심 특징

1. **Router-Router 패턴**: 하나의 소켓으로 Bind와 Connect 동시 사용
2. **NID 기반 라우팅**: `serviceId:serverId` 형식의 고유 식별자
3. **3-Frame 메시지**: [Target NID | RouteHeader | Payload]
4. **Zero-Copy 최적화**: FramePayload로 메모리 복사 최소화
5. **분리된 송수신 스레드**: ServerThread (수신) + ClientThread (송신)
6. **Request-Response 패턴**: MsgSeq 기반 요청-응답 매칭
7. **Full-Mesh 연결**: ServerAddressResolver를 통한 자동 연결 관리

### 9.2 통합 체크리스트

- [ ] PlaySocket 계층 파일 복사
- [ ] Message 계층 파일 복사
- [ ] Communicator 계층 파일 복사
- [ ] XSender 클래스 작성
- [ ] ISystemController 인터페이스 구현
- [ ] Protobuf 메시지 정의 (route.proto)
- [ ] ZMQ NuGet 패키지 설치
- [ ] 서버 초기화 코드 작성
- [ ] 메시지 송수신 테스트
- [ ] Request-Response 패턴 테스트
- [ ] 서버 간 연결 테스트
- [ ] 성능 및 안정성 테스트

### 9.3 참조 파일 위치 요약

```
D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouse\Runtime\
├── PlaySocket/
│   ├── IPlaySocket.cs (18 lines)
│   ├── ZMQPlaySocket.cs (163 lines) ⭐ 핵심
│   ├── PlaySocketConfig.cs (11 lines)
│   ├── SocketConfig.cs (8 lines)
│   └── PlaySocketFactory.cs
├── Message/
│   ├── Payload.cs (76 lines) ⭐ 핵심
│   ├── RoutePacket.cs (484 lines) ⭐ 핵심
│   └── IBasePacket.cs
├── Communicator.cs (319 lines) ⭐ 핵심
├── XServerCommunicator.cs (50 lines)
├── XClientCommunicator.cs (139 lines)
├── MessageLoop.cs (55 lines)
├── RequestCache.cs (150 lines)
└── ServerAddressResolver.cs (100 lines)
```

이 문서를 따라 ZMQ Runtime을 통합하면 PlayHouse-NET 프로젝트에서 안정적이고 고성능의 서버 간 통신 시스템을 구축할 수 있습니다.
