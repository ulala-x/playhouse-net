# PlayHouse Test Server - API Server 구현

## 개요
PlayHouse 커넥터 E2E 테스트를 위한 API Server 구현입니다. HTTP 엔드포인트를 통해 Stage 생성/조회 API를 제공합니다.

## 디렉토리 구조

```
src/PlayHouse.TestServer/
├── Api/
│   └── StageApiController.cs      # HTTP API 엔드포인트
├── Play/
│   ├── TestActor.cs                # Actor 구현 (인증 처리)
│   └── TestStageActor.cs           # Stage 구현 (메시지 처리)
├── Shared/
│   └── TestSystemController.cs     # 서버 디스커버리 구현
├── Program.cs                      # 서버 진입점
├── appsettings.json                # 설정 파일
└── PlayHouse.TestServer.csproj    # 프로젝트 파일
```

## 주요 컴포넌트

### 1. StageApiController (`Api/StageApiController.cs`)

HTTP API 컨트롤러로 다음 엔드포인트를 제공합니다:

- **GET /api/health** - 헬스 체크
  ```json
  Response: { "status": "healthy", "serverId": "play-1" }
  ```

- **POST /api/stages** - Stage 생성
  ```json
  Request: { "stageType": "TestStage", "stageId": 1001 }
  Response: { "success": true, "stageId": 1001, "replyPayloadId": "TestStage:10" }
  ```

- **POST /api/stages/get-or-create** - Stage 조회 또는 생성
  ```json
  Request: { "stageType": "TestStage", "stageId": 1001 }
  Response: { "success": true, "isCreated": false, "stageId": 1001, "replyPayloadId": null }
  ```

### 2. Program.cs

API Server와 Play Server를 동시에 기동하는 진입점입니다.

**주요 기능:**
- API Server + Play Server 동시 구동
- HTTP API 서버 (ASP.NET Core)
- 환경 변수를 통한 설정
- 서버 간 연결 안정화 대기

**환경 변수:**
```bash
PLAY_SERVER_ID=play-1       # PlayServer ID (기본값: play-1)
API_SERVER_ID=api-1         # ApiServer ID (기본값: api-1)
TCP_PORT=34001              # TCP 포트 (기본값: 34001)
HTTP_PORT=8080              # HTTP 포트 (기본값: 8080)
```

### 3. TestSystemController (`Shared/TestSystemController.cs`)

서버 디스커버리를 위한 SystemController 구현입니다.

- InMemorySystemController를 사용한 간단한 서버 디스커버리
- 서버 정보 TTL: 10초

## 빌드 및 실행

### 빌드
```bash
cd src/PlayHouse.TestServer
dotnet build
```

### 실행 (기본 포트)
```bash
cd src/PlayHouse.TestServer
dotnet run
```

서버가 다음 포트에서 실행됩니다:
- TCP: 34001
- HTTP: 8080
- ZMQ Play: 15000
- ZMQ API: 15300

### 실행 (사용자 정의 포트)
```bash
export TCP_PORT=35001
export HTTP_PORT=9090
dotnet run
```

## API 테스트

### 헬스 체크
```bash
curl http://localhost:8080/api/health
```

### Stage 생성
```bash
curl -X POST http://localhost:8080/api/stages \
  -H "Content-Type: application/json" \
  -d '{"stageType":"TestStage","stageId":1001}'
```

### Stage 조회 또는 생성
```bash
curl -X POST http://localhost:8080/api/stages/get-or-create \
  -H "Content-Type: application/json" \
  -d '{"stageType":"TestStage","stageId":1001}'
```

## 아키텍처

```
┌─────────────────────────────────────────┐
│         PlayHouse Test Server           │
├─────────────────────────────────────────┤
│                                         │
│  ┌──────────────┐    ┌──────────────┐  │
│  │   API Server │    │  Play Server │  │
│  │   (ZMQ)      │◄──►│   (ZMQ+TCP)  │  │
│  └──────────────┘    └──────────────┘  │
│         ▲                    ▲          │
│         │                    │          │
│  ┌──────┴──────┐      ┌──────┴──────┐  │
│  │ HTTP API    │      │ Connectors  │  │
│  │ (ASP.NET)   │      │ (TCP/WS)    │  │
│  └─────────────┘      └─────────────┘  │
└─────────────────────────────────────────┘
```

## Proto 메시지

Proto 파일은 `../../proto/test_messages.proto`에 정의되어 있으며, 빌드 시 자동으로 C# 코드가 생성됩니다.

주요 메시지:
- `CreateStagePayload` / `CreateStageReply`
- `AuthenticateRequest` / `AuthenticateReply`
- `EchoRequest` / `EchoReply`

## 참고 사항

1. **E2E 테스트 패턴**: `servers/dotnet/tests/e2e/PlayHouse.E2E/Program.cs` 참고
2. **HTTP API 패턴**: `servers/dotnet/tests/e2e/PlayHouse.E2E.Shared/Infrastructure/TestHttpApiController.cs` 참고
3. **서버 생성 패턴**: `servers/dotnet/tests/e2e/PlayHouse.E2E.Shared/Utils/ServerFactory.cs` 참고

## 다음 단계

- [ ] Play 서버 핸들러 구현 (다른 에이전트 담당)
- [ ] Dockerfile 작성 (다른 에이전트 담당)
- [ ] 추가 E2E 테스트 시나리오 구현
