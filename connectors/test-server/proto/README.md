# PlayHouse Test Server - Protocol Buffers

이 디렉토리는 PlayHouse 커넥터 테스트 서버용 Protocol Buffers 메시지 정의 및 코드 생성 스크립트를 포함합니다.

## 📁 파일 구조

```
proto/
├── test_messages.proto      # 테스트 메시지 정의
├── generate-csharp.sh        # C# 코드 생성 스크립트
├── generate-all.sh           # 모든 언어용 코드 생성 스크립트
└── README.md                 # 이 문서
```

## 📦 메시지 정의

### 인증 관련
- `AuthenticateRequest` / `AuthenticateReply`: 클라이언트 인증

### Stage 생성
- `CreateStagePayload` / `CreateStageReply`: Stage OnCreate 검증

### 기본 테스트
- `EchoRequest` / `EchoReply`: 기본 Request/Reply 테스트
- `StatusRequest` / `StatusReply`: 서버 상태 조회

### 에러 테스트
- `FailRequest` / `FailReply`: 에러 처리 검증
- `NoResponseRequest`: 타임아웃 테스트

### 페이로드 테스트
- `LargePayloadRequest` / `LargePayloadReply`: 큰 페이로드 및 압축 검증

### 브로드캐스트 테스트
- `BroadcastRequest` / `BroadcastNotify`: Push 메시지 검증

### Stage 관리
- `CloseStageRequest` / `CloseStageReply`: Stage 종료
- `ActorLeftNotify`: Actor 퇴장 알림
- `ConnectionChangedNotify`: 연결 상태 변경

### API Server
- `ApiEchoRequest` / `ApiEchoReply`: API 서버 통신 검증

### IActorSender 테스트
- `GetAccountIdRequest` / `GetAccountIdReply`: AccountId 조회
- `LeaveStageRequest` / `LeaveStageReply`: Stage 퇴장

### Timer 테스트
- `StartRepeatTimerRequest`: 반복 타이머
- `StartCountTimerRequest`: 카운트 타이머
- `TimerTickNotify`: 타이머 콜백
- `StartTimerReply`: 타이머 시작 응답

### Benchmark
- `BenchmarkRequest` / `BenchmarkReply`: 성능 측정

## 🚀 사용 방법

### 사전 요구사항

Protocol Buffers 컴파일러(`protoc`)가 설치되어 있어야 합니다.

#### Ubuntu/Debian
```bash
sudo apt-get update
sudo apt-get install -y protobuf-compiler
```

#### macOS
```bash
brew install protobuf
```

#### Windows
[Protocol Buffers Releases](https://github.com/protocolbuffers/protobuf/releases)에서 다운로드

### C# 코드 생성

```bash
cd connectors/test-server/proto
./generate-csharp.sh
```

생성 위치: `../src/PlayHouse.TestServer/Shared/Proto/`

### 모든 언어용 코드 생성

```bash
cd connectors/test-server/proto
./generate-all.sh
```

생성 위치:
- **C#**: `../src/PlayHouse.TestServer/Shared/Proto/`
- **JavaScript/TypeScript**: `../../javascript/src/proto/`
- **Java**: `../../java/src/main/java/`
- **C++**: `../../cpp/src/proto/`

## 📝 메시지 사용 예제

### C# (테스트 서버)

```csharp
using PlayHouse.TestServer.Proto;
using PlayHouse.TestServer.Shared;

// 메시지 생성
var echoRequest = new EchoRequest
{
    Content = "Hello, PlayHouse!",
    Sequence = 1
};

// Packet 생성
using var packet = new Packet(echoRequest);

// 메시지 ID 확인
if (packet.MsgId == TestMessageIds.EchoRequest)
{
    var request = packet.Parse<EchoRequest>();
    Console.WriteLine($"Content: {request.Content}, Sequence: {request.Sequence}");
}
```

### JavaScript (커넥터)

```javascript
import { EchoRequest } from './proto/test_messages_pb';

// 메시지 생성
const echoRequest = new EchoRequest();
echoRequest.setContent("Hello, PlayHouse!");
echoRequest.setSequence(1);

// Packet으로 전송
const packet = new Packet(echoRequest);
await connector.sendAsync(packet);
```

### Java (Android 커넥터)

```java
import com.playhouse.test.proto.TestMessagesProto.EchoRequest;

// 메시지 생성
EchoRequest echoRequest = EchoRequest.newBuilder()
    .setContent("Hello, PlayHouse!")
    .setSequence(1)
    .build();

// Packet으로 전송
Packet packet = new Packet(echoRequest);
connector.sendAsync(packet);
```

### C++ (Unreal/Native 커넥터)

```cpp
#include "proto/test_messages.pb.h"

// 메시지 생성
playhouse::test::EchoRequest echoRequest;
echoRequest.set_content("Hello, PlayHouse!");
echoRequest.set_sequence(1);

// Packet으로 전송
auto packet = std::make_shared<Packet>(echoRequest);
connector->SendAsync(packet);
```

## 🔗 관련 파일

- **Proto 정의**: `test_messages.proto`
- **C# 상수 클래스**: `../src/PlayHouse.TestServer/Shared/TestMessages.cs`
- **기존 E2E 테스트**: `../../../../servers/dotnet/tests/e2e/PlayHouse.E2E.Shared/Proto/test_messages.proto`

## ⚠️ 주의사항

1. **Proto 파일 수정 후 반드시 코드 재생성**
   - proto 파일 수정 후 해당 언어의 생성 스크립트를 실행하세요.

2. **메시지 ID 일관성**
   - `TestMessages.cs`의 상수와 proto 메시지 이름이 일치해야 합니다.

3. **호환성 유지**
   - 기존 E2E 테스트와 메시지 호환성을 유지해야 합니다.
   - 필드 번호를 변경하지 마세요 (breaking change).

4. **네임스페이스**
   - C#: `PlayHouse.TestServer.Proto`
   - Java: `com.playhouse.test.proto`
   - Package: `playhouse.test`

## 🔧 트러블슈팅

### protoc not found
```bash
# Ubuntu/Debian
sudo apt-get install protobuf-compiler

# macOS
brew install protobuf

# 설치 확인
protoc --version
```

### 생성된 파일이 없음
- `generate-csharp.sh`에 실행 권한이 있는지 확인: `chmod +x generate-csharp.sh`
- 출력 디렉토리 경로가 올바른지 확인

### JavaScript/TypeScript 플러그인 없음
```bash
npm install -g protoc-gen-js protoc-gen-ts
```

## 📚 참고 문서

- [Protocol Buffers Language Guide](https://protobuf.dev/programming-guides/proto3/)
- [Protocol Buffers C# Tutorial](https://protobuf.dev/getting-started/csharptutorial/)
- [PlayHouse E2E Tests](../../../../servers/dotnet/tests/e2e/)
