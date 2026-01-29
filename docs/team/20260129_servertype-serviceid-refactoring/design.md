# ServerType/ServiceId 분리 리팩토링 상세 설계

## 1. 목적 및 범위
- **목적:** 기존 `ServiceId`가 `Play=1`, `Api=2`로 서버 타입을 구분하던 구조를 분리하여, 동일 `ServerType` 내에서 다중 서비스 그룹을 지원한다.
- **범위:** Core 타입, ServerInfo/Discovery, Sender 인터페이스 및 구현, Bootstrap/Dispatcher, Runtime/Communicator, Protocol, 테스트.

## 2. 용어 정의
- **ServerType:** 서버 종류 구분. `Play=1`, `Api=2`.
- **ServiceId:** 같은 `ServerType` 내의 서비스 그룹 구분 값. 기본값 `1`.
- **ServerId:** 서버 인스턴스 식별자(문자열). 예: `play-1-1`.
- **NID:** 네트워크 식별자(기존 포맷 유지). 예: `serviceId:serverId`.

## 3. 설계 결정 및 전제
### 3.1 확정 사항
- `ServerType` 값은 기존 `ServiceType`의 값 유지 (`Play=1`, `Api=2`).
- `ServiceId` 기본값은 `1`.
- `route_header.proto`에 `server_type` 필드 추가.

### 3.2 확정 사항 (Gemini 리뷰 반영)
- **Breaking change로 처리**: Mixed version 호환 미지원. 모든 서버가 동시 업데이트 필요.
- `server_type=0` 또는 필드 없음 → **Error/Drop 처리** (호환 로직 없음)
- `route_header.proto`에 `server_type` 필드는 **tag 11** 사용.
- NID 포맷은 기존 유지. 필요 시 확장 가능성만 열어둠.
- 기존 `ServiceType` enum은 `ServerType`으로 **Rename** (공존 금지).

## 4. 클래스 구조
### 4.1 Core Types
```
ServerType (enum)
└── Play = 1
└── Api  = 2
```

```
IServerInfo
├── ServerType { get; }
├── ServiceId { get; }
├── ServerId { get; }
├── Address { get; }
├── State { get; }
└── Weight { get; }

XServerInfo : IServerInfo
└── (ServerType, ServiceId, ServerId, Address, State, Weight)
```

```
ServerConfig
├── ServerType
├── ServiceId
├── ServerId
├── BindAddress
└── Transport Options
```

### 4.2 Server Info / Discovery
```
IServerInfoCenter
├── Update(IEnumerable<IServerInfo>)
├── GetServerByService(ServerType, ushort, policy)
├── GetServerListByService(ServerType, ushort)
└── GetServer(string serverId)

XServerInfoCenter : IServerInfoCenter
├── _servers : ConcurrentDictionary<string, XServerInfo>
├── _roundRobinIndex : ConcurrentDictionary<(ServerType, ushort), int>
└── SelectionPolicy (RoundRobin / Weighted)
```

### 4.3 Sender 계층
```
ISender
├── ServerType { get; }
├── ServiceId { get; }
├── SendToService(ServerType, ServiceId, ...)
├── RequestToService(ServerType, ServiceId, ...)
└── Reply(...)

XSender (abstract)
├── ServerType
├── ServiceId
├── ServerInfoCenter
├── SendToService/RequestToService 구현
└── RoutePacket 생성

XStageSender : XSender
ApiSender   : XSender
XActorSender: ISender (위임)
```

### 4.4 Bootstrap / Dispatcher / Communicator
```
PlayServerOption / ApiServerOption
└── ServerType, ServiceId, ServerId, BindEndpoint, Transport Options

PlayServer / ApiServer
├── ServerConfig 생성
├── XServerInfo 생성
└── Dispatcher, Communicator 구성

PlayDispatcher / ApiDispatcher
├── ServerType, ServiceId 유지
└── Sender 생성 및 전달

CommunicatorOption
└── ServerType, ServiceId, ServerId 포함
```

### 4.5 Protocol / Runtime
```
RouteHeader (proto)
├── service_id
├── server_type
└── 기타 필드 (msg_seq, msg_id, from, stage_id, ...)

RoutePacket
└── Header(server_type/service_id 포함) + Payload

ZmqPlaySocket
└── HeaderPool 초기화 시 ServerType 반영
```

## 5. 인터페이스 변경
### 5.1 ISender 시그니처 변경
- 기존 `SendToService(ushort serviceId, ...)` → `SendToService(ServerType serverType, ushort serviceId, ...)`
- `RequestToService` 계열도 동일 변경.

예시:
```csharp
// Before
sender.SendToService(serviceId: 2, packet); // 2 = Api

// After
sender.SendToService(ServerType.Api, serviceId: 1, packet);
```

### 5.2 편의성 메소드 유지 (Gemini 제안 반영)
기존 `SendToApi`, `SendToStage` 등의 헬퍼 메소드는 유지하여 코드 수정 범위를 최소화:
```csharp
// 기존 헬퍼 메소드 유지 (내부적으로 SendToService 호출)
void SendToApi(string apiServerId, IPacket packet);
void SendToStage(string playServerId, long stageId, IPacket packet);
void RequestToApi(string apiServerId, IPacket packet, ReplyCallback callback);
```

### 5.3 IServerInfo / ServerConfig 변경
- `ServerType` 속성 추가.
- 생성자 및 Create 메서드에 `ServerType` 매개변수 추가.

### 5.4 IServerInfoCenter 변경
- 조회 키를 `(ServerType, ServiceId)` 복합 키로 변경.
- Round-robin 인덱스 키도 동일 복합 키 사용.

### 5.5 Protocol 변경
- `route_header.proto`에 `server_type` 필드 추가.
- `RoutePacket` 생성 시 `ServerType`를 헤더에 반영.

## 6. 데이터 흐름
### 6.1 서버 부트스트랩 흐름
```
PlayServerOption/ApiServerOption
   └─(ServerType, ServiceId, ServerId)
        ↓
PlayServer/ApiServer
   ├─ ServerConfig 생성
   ├─ XServerInfo 생성
   └─ Communicator/Dispatcher 구성
        ↓
XServerInfoCenter.Update()
   └─ (ServerType, ServiceId) 기준 등록
```

### 6.2 라우팅/전송 흐름
```
Sender.SendToService(ServerType, ServiceId, packet)
   ↓
XServerInfoCenter.GetServerByService(ServerType, ServiceId, policy)
   ↓
RoutePacket 생성
   ├─ RouteHeader.server_type = ServerType
   └─ RouteHeader.service_id  = ServiceId
   ↓
Communicator.Send
   ↓
ZmqPlaySocket 송신
   ↓
수신측 Dispatcher
   └─ server_type/service_id 기반 라우팅
```

### 6.3 응답 흐름
```
수신측 처리
   ↓
Reply(RoutePacket)
   └─ 기존 msg_seq 매칭 유지
   └─ server_type/service_id는 라우팅 컨텍스트 보존
```

## 7. 의존성 체인
```
PlayServerOption / ApiServerOption
  └─ ServerType, ServiceId
        ↓
PlayServer / ApiServer
  ├─ ServerConfig (ServerType, ServiceId)
  ├─ XServerInfo (ServerType, ServiceId)
  └─ Dispatcher/Communicator 구성
        ↓
PlayDispatcher / ApiDispatcher
  └─ Sender 생성 시 ServerType, ServiceId 전달
        ↓
XSender (abstract)
  └─ SendToService/RequestToService에서 ServerInfoCenter 사용
        ↓
XServerInfoCenter
  └─ (ServerType, ServiceId) 조합으로 서버 선택
        ↓
RoutePacket / RouteHeader
  └─ server_type, service_id 포함
```

## 8. 호환성 및 마이그레이션 포인트
- **Breaking change:** 모든 서버/클라이언트가 동시에 업데이트되어야 함.
- 구성 파일 및 옵션에 `ServerType` 필드 추가 필요.
- **`server_type=0` 또는 필드 없음 → Error/Drop 처리** (호환 로직 없음, Gemini 리뷰 반영)

예시 (설정):
```json
{
  "PlayServer": {
    "ServerType": "Play",
    "ServiceId": 1,
    "ServerId": "play-1-1"
  }
}
```

## 9. 테스트 관점 요약
- `(ServerType, ServiceId)` 조합별 Round-robin 분리 동작 확인.
- Play ↔ Api 간 통신과 동일 타입 내 다중 ServiceId 그룹 라우팅 확인.
- `server_type` 필드 포함된 패킷의 직렬화/역직렬화 검증.
- `server_type=0` 수신 시 Error/Drop 처리 검증.

---

## 리뷰 결과 (Phase 2)

### Claude 검토
- **승인**: 설계 구조 적합, 데이터 흐름 명확

### Gemini 리뷰
- **승인** (보완 반영)
- 피드백 반영 사항:
  1. 프로토콜 호환성 전략 명확화 (Breaking change로 통일)
  2. Proto 필드 번호 tag 11 확정
  3. ISender 편의성 메소드 유지 권장
