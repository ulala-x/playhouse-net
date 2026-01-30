# PlayHouse-NET Documentation

PlayHouse-NET 실시간 게임 서버 프레임워크 문서입니다.

## 문서 구조

```
docs/
├── getting-started/    # 시작하기
├── concepts/           # 핵심 개념
├── guides/             # 실전 가이드
├── tutorials/          # 튜토리얼
├── internals/          # 내부 구현
└── reference/          # API 레퍼런스
```

## 읽기 순서

### 처음 시작하는 경우

1. **[Getting Started](./getting-started/)** - 설치 및 첫 서버 구동
2. **[Concepts](./concepts/)** - Stage/Actor, 메시징 등 핵심 개념 이해
3. **[Guides](./guides/)** - 실전 구현 가이드
4. **[Tutorials](./tutorials/)** - 단계별 예제 프로젝트

### 내부 동작을 이해하고 싶은 경우

**[Internals](./internals/)** - 이벤트 루프, 패킷 구조, 소켓 전송 등 내부 구현

### API 레퍼런스

**[Reference](./reference/)** - HTTP API, 클라이언트 프로토콜, 메트릭

---

## Getting Started

| 문서 | 설명 |
|------|------|
| [Quick Start](./getting-started/quick-start.md) | 빠른 시작 가이드 |

## Concepts

핵심 개념을 이해합니다. **읽기 순서: Overview → Stage/Actor → 나머지**

| 문서 | 설명 |
|------|------|
| [Overview](./concepts/overview.md) | **시작점** - Play/API 서버, Link 통신 |
| [Stage/Actor](./concepts/stage-actor.md) | **핵심 모델** - 게임방과 참가자 |
| [Connection Lifecycle](./concepts/connection-lifecycle.md) | 연결 및 인증 생명주기 |
| [Messaging](./concepts/messaging.md) | 메시지 패턴 (Request-Reply, Push, Link) |
| [Timer & GameLoop](./concepts/timer-gameloop.md) | 타이머 및 게임루프 개념 |

## Guides

실전 구현 가이드입니다.

| 문서 | 설명 |
|------|------|
| [Stage Implementation](./guides/stage-implementation.md) | Stage 구현 방법 |
| [Actor Implementation](./guides/actor-implementation.md) | Actor 구현 방법 |
| [Server Communication](./guides/server-communication.md) | 서버 간 통신 |
| [API Controller](./guides/api-controller.md) | HTTP API 컨트롤러 |
| [Async Operations](./guides/async-operations.md) | 비동기 작업 처리 |
| [Configuration](./guides/configuration.md) | 설정 및 옵션 |
| [Best Practices](./guides/best-practices.md) | 모범 사례 |
| [Troubleshooting](./guides/troubleshooting.md) | 문제 해결 |

## Tutorials

단계별 예제 프로젝트입니다.

| 문서 | 설명 |
|------|------|
| [Chat Room](./tutorials/chat-room.md) | 채팅방 구현 |
| [Realtime Game](./tutorials/realtime-game.md) | 실시간 게임 구현 |
| [Lobby & Matching](./tutorials/lobby-matching.md) | 로비 및 매칭 시스템 |

## Internals

내부 구현을 이해합니다. 프레임워크 동작 원리를 알고 싶을 때 참고하세요.

| 문서 | 설명 |
|------|------|
| [Overview](./internals/overview.md) | 프레임워크 개요 |
| [Architecture](./internals/architecture.md) | 시스템 아키텍처 |
| [Event Loop](./internals/event-loop.md) | 이벤트 루프 구현 |
| [Stage/Actor Model](./internals/stage-actor.md) | Stage/Actor 내부 구현 |
| [Timer System](./internals/timer-system.md) | 타이머 시스템 내부 |
| [Packet Structure](./internals/packet-structure.md) | 패킷 바이너리 구조 |
| [Socket Transport](./internals/socket-transport.md) | 소켓 전송 계층 |
| [Bootstrap](./internals/bootstrap.md) | 부트스트랩 시스템 |
| [Memory Pool](./internals/memory-pool.md) | 메모리 풀링 |
| [Connector](./internals/connector.md) | 테스트 커넥터 |
| [Testing](./internals/testing.md) | 테스트 전략 |

## Reference

API 및 프로토콜 레퍼런스입니다.

| 문서 | 설명 |
|------|------|
| [HTTP API](./reference/http-api.md) | REST API 스펙 |
| [Client Protocol](./reference/client-protocol.md) | 클라이언트 프로토콜 |
| [Metrics](./reference/metrics.md) | 메트릭 및 관측성 |

---

## 개발 원칙

프로젝트 개발 철학 및 아키텍처 원칙은 [ARCHITECTURE.md](../ARCHITECTURE.md)를 참고하세요.
