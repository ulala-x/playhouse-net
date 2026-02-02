# PlayHouse Test Server Proto - 구현 완료 보고서

## 작업 개요

PlayHouse 커넥터 테스트 서버용 Protocol Buffers 메시지 정의 및 코드 생성 인프라를 구축했습니다.

**작업 일시**: 2026-02-02
**담당**: Claude Agent (Proto & Shared 전담)
**위치**: `/connectors/test-server/proto/`

## 생성된 파일 목록

### 1. Proto 디렉토리 (`/connectors/test-server/proto/`)

| 파일명 | 라인 수 | 설명 |
|--------|---------|------|
| `test_messages.proto` | 258 | 테스트 메시지 정의 (proto3) |
| `generate-csharp.sh` | 30 | C# 코드 생성 스크립트 |
| `generate-all.sh` | 97 | 모든 언어용 코드 생성 스크립트 |
| `README.md` | 222 | Proto 디렉토리 사용 가이드 |
| `USAGE.md` | 532 | 메시지 사용법 및 커넥터별 예제 |
| **합계** | **1,139** | |

### 2. Shared 디렉토리 (`/connectors/test-server/src/PlayHouse.TestServer/Shared/`)

| 파일명 | 라인 수 | 설명 |
|--------|---------|------|
| `TestMessages.cs` | 161 | 메시지 ID 및 에러 코드 상수 |
| `Proto/` (디렉토리) | - | protoc 생성 파일 저장 위치 (생성 대기) |

## 메시지 정의 상세

### 메시지 카테고리 (총 36개 메시지)

| 카테고리 | 메시지 수 | 설명 |
|----------|-----------|------|
| **인증** | 2 | AuthenticateRequest/Reply |
| **Stage 생성** | 2 | CreateStagePayload/Reply |
| **Echo 테스트** | 2 | EchoRequest/Reply |
| **에러 테스트** | 3 | FailRequest/Reply, NoResponseRequest |
| **페이로드 테스트** | 2 | LargePayloadRequest/Reply |
| **브로드캐스트** | 2 | BroadcastRequest, BroadcastNotify |
| **상태 조회** | 3 | StatusRequest/Reply, ChatMessage |
| **Stage 관리** | 4 | CloseStageRequest/Reply, ActorLeftNotify, ConnectionChangedNotify |
| **API Server** | 2 | ApiEchoRequest/Reply |
| **IActorSender** | 4 | GetAccountIdRequest/Reply, LeaveStageRequest/Reply |
| **Timer** | 4 | StartRepeatTimerRequest, StartCountTimerRequest, TimerTickNotify, StartTimerReply |
| **Benchmark** | 2 | BenchmarkRequest/Reply |
| **합계** | **36** | |

### 네임스페이스 설정

```protobuf
package playhouse.test;

option csharp_namespace = "PlayHouse.TestServer.Proto";
option java_package = "com.playhouse.test.proto";
option java_outer_classname = "TestMessagesProto";
```

## 코드 생성 스크립트

### 1. `generate-csharp.sh`

C# 전용 코드 생성 스크립트

**기능:**
- `test_messages.proto` → C# 클래스 생성
- 출력 경로: `../src/PlayHouse.TestServer/Shared/Proto/`
- protoc 설치 검증
- 에러 처리 및 사용자 친화적 메시지

**실행:**
```bash
cd connectors/test-server/proto
./generate-csharp.sh
```

### 2. `generate-all.sh`

다중 언어 지원 코드 생성 스크립트

**지원 언어:**
- C# (PlayHouse.TestServer)
- JavaScript/TypeScript (Node.js 커넥터)
- Java (Android/Java 커넥터)
- C++ (Unreal/Native 커넥터)

**출력 경로:**
- C#: `../src/PlayHouse.TestServer/Shared/Proto/`
- JavaScript/TypeScript: `../../javascript/src/proto/`
- Java: `../../java/src/main/java/`
- C++: `../../cpp/src/proto/`

**실행:**
```bash
cd connectors/test-server/proto
./generate-all.sh
```

**특징:**
- 각 언어별 플러그인 가용성 검증
- 누락된 플러그인은 경고 출력 (에러 없이 계속 진행)
- 상세한 진행 상황 출력

## TestMessages.cs 구조

### 1. TestMessageIds (36개 상수)

모든 proto 메시지와 1:1 매핑되는 메시지 ID 상수 클래스

**사용 예:**
```csharp
if (packet.MsgId == TestMessageIds.EchoRequest)
{
    var request = packet.Parse<EchoRequest>();
}
```

### 2. TestErrorCodes (6개 상수)

테스트용 에러 코드 정의

| 코드 | 값 | 설명 |
|------|-----|------|
| GeneralError | 1000 | 일반 에러 |
| AuthenticationFailed | 1001 | 인증 실패 |
| PermissionDenied | 1002 | 권한 없음 |
| ResourceNotFound | 1003 | 리소스 없음 |
| Timeout | 1004 | 타임아웃 |
| InvalidRequest | 1005 | 잘못된 요청 |

### 3. StageTypes (4개 상수)

테스트용 Stage 타입 정의

| 타입 | 설명 |
|------|------|
| TestStage | 기본 테스트 Stage |
| EchoStage | Echo 테스트용 |
| BroadcastStage | 브로드캐스트 테스트용 |
| BenchmarkStage | 벤치마크 테스트용 |

## 문서화

### 1. README.md

**내용:**
- 파일 구조
- 메시지 정의 개요
- 사용 방법 (코드 생성)
- 사전 요구사항 (protoc 설치)
- 트러블슈팅 가이드

**대상 독자:**
- 새로운 개발자
- 테스트 서버 설정 담당자

### 2. USAGE.md

**내용:**
- 메시지별 상세 사용법
- 커넥터별 구현 예제 (C#, JavaScript, Java, C++)
- 테스트 시나리오 예제
- 주의사항 및 베스트 프랙티스

**대상 독자:**
- 커넥터 개발자
- E2E 테스트 작성자

## 기존 E2E 테스트와의 호환성

### 참조한 파일
- `servers/dotnet/tests/e2e/PlayHouse.E2E.Shared/Proto/test_messages.proto`

### 호환 메시지 (기존 E2E와 동일)
- EchoRequest/EchoReply
- BroadcastNotify
- StatusRequest/StatusReply
- ChatMessage
- CreateStagePayload/CreateStageReply
- AuthenticateRequest/AuthenticateReply
- Timer 관련 메시지
- Benchmark 관련 메시지
- API 관련 메시지

### 커넥터 테스트용 추가/변경 사항
1. **AuthenticateRequest**: `metadata` 필드 추가 (map<string, string>)
2. **LargePayloadReply**: `compressed` 필드 추가 (압축 여부 표시)
3. **BroadcastNotify**: `sender_id` 필드 추가 (문자열 형태 발신자 ID)
4. **FailRequest/Reply**: 에러 테스트 명시화

## 다음 단계

### 1. Proto 코드 생성

protoc 설치 후 코드 생성:

```bash
# Ubuntu/Debian
sudo apt-get install -y protobuf-compiler

# macOS
brew install protobuf

# 코드 생성
cd connectors/test-server/proto
./generate-csharp.sh
```

**예상 생성 파일:**
- `../src/PlayHouse.TestServer/Shared/Proto/TestMessages.cs` (protoc 생성)

### 2. 다른 에이전트 작업과의 연계

**Api/ 담당 에이전트:**
- `PlayHouse.TestServer.Proto` 네임스페이스 사용
- `TestMessageIds`, `TestErrorCodes` 상수 참조
- ApiEchoRequest/Reply 핸들러 구현

**Play/ 담당 에이전트:**
- TestStage, TestActor 구현 시 proto 메시지 사용
- EchoRequest/Reply, BroadcastRequest/Notify 핸들러 구현
- Timer 테스트 메시지 핸들러 구현

**Dockerfile 담당 에이전트:**
- protoc 설치를 빌드 스크립트에 포함 (선택사항)
- 또는 사전 생성된 proto 파일 사용

### 3. 커넥터 개발자를 위한 안내

각 커넥터 개발자에게 제공할 사항:

1. **Proto 파일 복사**
   ```bash
   cp connectors/test-server/proto/test_messages.proto <connector-project>/proto/
   ```

2. **언어별 코드 생성**
   - C#: `generate-csharp.sh` 실행 또는 수동 protoc
   - JavaScript: `protoc --js_out --ts_out`
   - Java: `protoc --java_out`
   - C++: `protoc --cpp_out`

3. **USAGE.md 참조**
   - 언어별 사용 예제
   - 테스트 시나리오

## 검증 체크리스트

- [x] Proto 파일 작성 (test_messages.proto)
- [x] C# 코드 생성 스크립트 (generate-csharp.sh)
- [x] 다중 언어 코드 생성 스크립트 (generate-all.sh)
- [x] 스크립트 실행 권한 설정 (chmod +x)
- [x] 메시지 ID 상수 클래스 (TestMessages.cs)
- [x] 에러 코드 상수 정의
- [x] Stage 타입 상수 정의
- [x] README.md 작성
- [x] USAGE.md 작성 (커넥터별 예제 포함)
- [x] 기존 E2E 테스트와 호환성 확인
- [ ] protoc로 C# 코드 생성 (protoc 설치 필요)

## 기술 스택

- **Proto 버전**: proto3
- **C# 타겟**: .NET 8.0
- **Java 타겟**: Java 8+
- **JavaScript/TypeScript**: ES6+
- **C++**: C++17+
- **빌드 도구**: protoc (Protocol Buffers Compiler)

## 파일 위치 요약

```
connectors/test-server/
├── proto/
│   ├── test_messages.proto          (258줄, 36개 메시지)
│   ├── generate-csharp.sh           (30줄, 실행 가능)
│   ├── generate-all.sh              (97줄, 실행 가능)
│   ├── README.md                    (222줄)
│   ├── USAGE.md                     (532줄)
│   └── IMPLEMENTATION_SUMMARY.md    (이 문서)
└── src/PlayHouse.TestServer/
    └── Shared/
        ├── TestMessages.cs          (161줄, 46개 상수)
        └── Proto/                   (protoc 생성 파일 저장 위치)
```

## 주의사항 및 베스트 프랙티스

### Proto 파일 수정 시

1. **필드 번호 변경 금지**: Breaking change 발생
2. **필드 추가 시 번호 증가**: 기존 필드 번호 재사용 금지
3. **코드 재생성 필수**: proto 수정 후 반드시 `generate-*.sh` 실행
4. **TestMessages.cs 동기화**: 새 메시지 추가 시 상수도 추가

### 메시지 ID 사용

1. **타입 안정성**: `TestMessageIds` 상수 사용
2. **매직 스트링 금지**: 직접 문자열 사용 금지
   ```csharp
   // Good
   if (packet.MsgId == TestMessageIds.EchoRequest)

   // Bad
   if (packet.MsgId == "EchoRequest")
   ```

### E2E 테스트 작성 시

1. **Proto 메시지 사용**: `Packet.Empty()` 대신 proto 메시지 사용
   ```csharp
   // Good
   var echoRequest = new EchoRequest { Content = "Hello", Sequence = 1 };
   using var packet = new Packet(echoRequest);

   // Bad
   using var packet = Packet.Empty("EchoRequest");
   ```

2. **Response와 Callback 모두 검증**: E2E 테스트 원칙 준수
   ```csharp
   // Response 검증
   var response = await connector.RequestAsync(echoRequest);
   response.MsgId.Should().Be(TestMessageIds.EchoReply);

   // Callback 검증
   testActor.ReceivedMsgIds.Should().Contain(TestMessageIds.EchoRequest);
   ```

## 참고 문서

- [Protocol Buffers Language Guide (proto3)](https://protobuf.dev/programming-guides/proto3/)
- [PlayHouse E2E Tests](../../../../servers/dotnet/tests/e2e/)
- [AGENTS.md](../../../AGENTS.md) - PlayHouse 프로젝트 가이드라인

---

**작성자**: Claude Agent (Proto & Shared 전담)
**최종 업데이트**: 2026-02-02
**상태**: 구현 완료 (protoc 코드 생성 대기)
