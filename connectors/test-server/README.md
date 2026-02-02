# PlayHouse Test Server

PlayHouse 테스트 서버는 각종 커넥터(C#, JavaScript, C++, Unreal, Unity 등)의 통합 테스트를 위한 참조 서버 구현입니다.

## 목차

- [개요](#개요)
- [지원 프로토콜](#지원-프로토콜)
- [포트 구성](#포트-구성)
- [빌드 방법](#빌드-방법)
- [실행 방법](#실행-방법)
- [환경 변수](#환경-변수)
- [TLS 설정](#tls-설정)
- [테스트 시나리오](#테스트-시나리오)
- [커넥터별 테스트](#커넥터별-테스트)

## 개요

PlayHouse Test Server는 다음을 제공합니다.

- **다중 전송 프로토콜**: WebSocket, TCP, TCP+TLS
- **RESTful API**: HTTP/HTTPS 엔드포인트
- **게임 서버 기능**: Stage, Actor, Session 관리
- **E2E 테스트 지원**: 각 커넥터의 전체 워크플로우 검증

## 지원 프로토콜

| 프로토콜 | 포트 | TLS | 용도 |
|---------|------|-----|------|
| HTTP | 8080 | No | REST API, Health Check |
| HTTPS | 8443 | Yes | REST API (Secure) |
| WebSocket | 8080/ws | No | 실시간 양방향 통신 |
| WSS | 8443/ws | Yes | 실시간 양방향 통신 (Secure) |
| TCP | 34001 | No | Play Server 통신 |
| TCP+TLS | 34002 | Yes | Play Server 통신 (Secure) |

## 포트 구성

| 포트 | 프로토콜 | 설명 |
|------|----------|------|
| 8080 | HTTP + WebSocket | API Server + WebSocket 엔드포인트 (/ws) |
| 8443 | HTTPS + WSS | API Server + WSS 엔드포인트 (TLS) |
| 34001 | TCP | Play Server (비암호화) |
| 34002 | TCP+TLS | Play Server (암호화) |
| 15000 | ZMQ | Play 내부 통신 (선택사항) |
| 15300 | ZMQ | API 내부 통신 (선택사항) |

## 빌드 방법

### Docker 빌드

```bash
# 테스트 서버 디렉토리로 이동
cd connectors/test-server

# TLS 인증서
# ENABLE_TLS=true일 때 런타임에 자체 서명 인증서를 자동 생성합니다.

# Docker Compose로 빌드 및 실행
docker-compose up --build
```

### 로컬 빌드 (.NET SDK 필요)

```bash
# playhouse-net 루트로 이동
cd /path/to/playhouse-net

# 빌드
dotnet build connectors/test-server/src/PlayHouse.TestServer/PlayHouse.TestServer.csproj

# 실행
dotnet run --project connectors/test-server/src/PlayHouse.TestServer/PlayHouse.TestServer.csproj
```

## 실행 방법

### Docker Compose 사용 (권장)

```bash
# 백그라운드 실행
docker-compose up -d

# 로그 확인
docker-compose logs -f

# 중지
docker-compose down

# 완전 정리 (볼륨 포함)
docker-compose down -v
```

### 로컬 실행

```bash
# 환경 변수 설정 (선택사항)
export ASPNETCORE_URLS="http://+:8080;https://+:8443"
export ENABLE_TLS=true
export TCP_PORT=34001

# 실행
dotnet run --project connectors/test-server/src/PlayHouse.TestServer/PlayHouse.TestServer.csproj
```

### Health Check

```bash
# HTTP
curl http://localhost:8080/health

# HTTPS (자체 서명 인증서이므로 -k 옵션 사용)
curl -k https://localhost:8443/health
```

## 환경 변수

### ASP.NET Core 설정

| 변수 | 기본값 | 설명 |
|------|--------|------|
| `ASPNETCORE_URLS` | `http://+:8080` | 수신 대기 URL (옵션) |
| `ASPNETCORE_ENVIRONMENT` | `Development` | 환경 설정 (Development/Production) |
| `Logging__LogLevel__Default` | `Information` | 기본 로그 레벨 |

### PlayHouse 설정

| 변수 | 기본값 | 설명 |
|------|--------|------|
| `ENABLE_TLS` | `false` | TLS 활성화 여부 |
| `ENABLE_WEBSOCKET` | `true` | WebSocket 활성화 여부 |
| `TCP_PORT` | `34001` | TCP Play Server 포트 |
| `TCP_TLS_PORT` | `34002` | TCP+TLS Play Server 포트 |
| `HTTP_PORT` | `8080` | HTTP API 포트 |
| `HTTPS_PORT` | `8443` | HTTPS/WSS 포트 |
| `ZMQ_PLAY_PORT` | `15000` | ZMQ Play 내부 포트 |
| `ZMQ_API_PORT` | `15300` | ZMQ API 내부 포트 |

### TLS 인증서 설정

테스트 서버는 `ENABLE_TLS=true`일 때 **런타임에 자체 서명 인증서를 자동 생성**합니다.
커스텀 인증서를 사용하려면 Kestrel 기본 인증서 환경 변수를 지정하세요.

| 변수 | 기본값 | 설명 |
|------|--------|------|
| `ASPNETCORE_Kestrel__Certificates__Default__Path` | (없음) | PFX 인증서 경로 |
| `ASPNETCORE_Kestrel__Certificates__Default__Password` | (없음) | PFX 비밀번호 |

### 인증서 신뢰 설정 (개발 환경)

**Linux:**
```bash
sudo cp certs/server.crt /usr/local/share/ca-certificates/playhouse-test.crt
sudo update-ca-certificates
```

**macOS:**
```bash
sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain certs/server.crt
```

**Windows:**
```powershell
Import-Certificate -FilePath certs\server.crt -CertStoreLocation Cert:\LocalMachine\Root
```

> **주의**: 자체 서명 인증서는 개발/테스트 목적으로만 사용하세요. 프로덕션 환경에서는 공인된 CA에서 발급한 인증서를 사용해야 합니다.

## 테스트 시나리오

### 1. Echo 테스트
클라이언트가 보낸 메시지를 그대로 반환합니다.

**요청:**
```json
{
  "msgId": "EchoRequest",
  "content": "Hello PlayHouse",
  "sequence": 1
}
```

**응답:**
```json
{
  "msgId": "EchoReply",
  "content": "Hello PlayHouse",
  "sequence": 1
}
```

### 2. Session 테스트
클라이언트 세션 정보를 반환합니다.

**요청:**
```json
{
  "msgId": "GetSessionInfoRequest"
}
```

**응답:**
```json
{
  "msgId": "SessionInfo",
  "sessionId": "session-12345",
  "accountId": "user@example.com",
  "connectedAt": "2026-02-02T10:00:00Z"
}
```

### 3. Stage Join 테스트
게임 스테이지에 참여합니다.

**요청:**
```json
{
  "msgId": "JoinStageRequest",
  "stageType": "GameStage",
  "stageId": "stage-001"
}
```

**응답:**
```json
{
  "msgId": "JoinStageReply",
  "success": true,
  "stageId": "stage-001",
  "playerCount": 1
}
```

### 4. Actor 메시지 테스트
Actor에게 메시지를 전송합니다.

**요청:**
```json
{
  "msgId": "ActorMessage",
  "actorId": "player-001",
  "messageType": "Move",
  "data": {
    "x": 100,
    "y": 200
  }
}
```

**응답 (Callback):**
```json
{
  "msgId": "ActorMessageReply",
  "actorId": "player-001",
  "processed": true
}
```

## 커넥터별 테스트

### C# Connector (.NET)

```bash
# 테스트 실행
cd connectors/dotnet/tests/PlayHouse.Connector.Tests
dotnet test

# 특정 테스트만 실행
dotnet test --filter "FullyQualifiedName~EchoTest"
```

**샘플 코드:**
```csharp
var connector = new PlayHouseConnector("ws://localhost:8080/ws");
await connector.ConnectAsync();

var echoRequest = new EchoRequest { Content = "Hello", Sequence = 1 };
using var packet = new Packet(echoRequest);
var response = await connector.RequestAsync(packet);

response.MsgId.Should().Be("EchoReply");
```

### JavaScript Connector (Node.js/Browser)

```bash
# 테스트 실행
cd connectors/javascript
npm test

# WebSocket 연결 테스트
npm run test:websocket
```

**샘플 코드:**
```javascript
import { PlayHouseConnector } from '@playhouse/connector';

const connector = new PlayHouseConnector('ws://localhost:8080/ws');
await connector.connect();

const response = await connector.request({
  msgId: 'EchoRequest',
  content: 'Hello',
  sequence: 1
});

console.log(response); // { msgId: 'EchoReply', content: 'Hello', sequence: 1 }
```

### C++ Connector

```bash
# 빌드 및 테스트
cd connectors/cpp
mkdir build && cd build
cmake ..
make
ctest
```

**샘플 코드:**
```cpp
#include <playhouse/connector.hpp>

PlayHouseConnector connector("localhost", 34001);
connector.Connect();

EchoRequest request;
request.set_content("Hello");
request.set_sequence(1);

auto response = connector.Request(request);
std::cout << response.content() << std::endl;
```

### Unity Connector (C#)

```csharp
using PlayHouse.Connector;

public class GameClient : MonoBehaviour
{
    private PlayHouseConnector connector;

    async void Start()
    {
        connector = new PlayHouseConnector("ws://localhost:8080/ws");
        await connector.ConnectAsync();

        var echoRequest = new EchoRequest { Content = "Hello from Unity" };
        var response = await connector.RequestAsync(echoRequest);

        Debug.Log($"Received: {response.Content}");
    }
}
```

### Unreal Engine Connector (C++)

```cpp
#include "PlayHouseConnector.h"

void AGameMode::BeginPlay()
{
    Super::BeginPlay();

    UPlayHouseConnector* Connector = NewObject<UPlayHouseConnector>();
    Connector->Connect(TEXT("localhost"), 34001);

    FEchoRequest Request;
    Request.Content = TEXT("Hello from Unreal");
    Request.Sequence = 1;

    Connector->Request(Request, [](const FEchoReply& Response)
    {
        UE_LOG(LogTemp, Log, TEXT("Received: %s"), *Response.Content);
    });
}
```

## 트러블슈팅

### 포트 충돌

```bash
# 포트 사용 확인
sudo lsof -i :8080
sudo lsof -i :34001

# Docker Compose에서 포트 변경
# docker-compose.yml 파일 수정
ports:
  - "9080:8080"  # 호스트:컨테이너
```

### TLS 인증서 오류

```bash
# 인증서 재생성
cd certs
rm -f server.*
./generate-certs.sh

# Docker 컨테이너 재시작
docker-compose restart
```

### 연결 타임아웃

```bash
# 방화벽 확인 (Linux)
sudo ufw status
sudo ufw allow 8080/tcp
sudo ufw allow 34001/tcp

# Docker 네트워크 확인
docker network ls
docker network inspect playhouse-network
```

### 로그 확인

```bash
# 실시간 로그
docker-compose logs -f test-server

# 최근 로그 100줄
docker-compose logs --tail=100 test-server

# 로그 파일 (볼륨 마운트된 경우)
tail -f ./logs/playhouse-test-server.log
```

## 참고 자료

- [PlayHouse-NET 메인 문서](../../README.md)
- [프로토콜 정의](./proto/)
- [서버 구현](./src/PlayHouse.TestServer/)
- [개발 가이드](../../AGENTS.md)

## 라이선스

MIT License - 자세한 내용은 [LICENSE](../../LICENSE) 파일을 참고하세요.

## Docker TLS Notes (2026-02-02)

When using `connectors/test-server/docker-compose.yml`, TLS/WSS is enabled by default.
Make sure certificates exist before starting:

```
cd connectors/test-server/certs
./generate-certs.sh
```

Ports:
- HTTP/WS: 8080
- HTTPS/WSS: 8443
- TCP: 34001
- TCP+TLS: 34002

If ports conflict on Windows, override the host ports in `docker-compose.yml`.
