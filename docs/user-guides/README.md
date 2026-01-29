# PlayHouse-NET 사용자 가이드

PlayHouse를 이용한 게임 서버 개발 가이드입니다.

## 학습 경로

```
                    ┌─────────────────┐
                    │   Quick Start   │  ← 여기서 시작 (5분)
                    │  첫 서버 실행   │
                    └────────┬────────┘
                             │
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
      ┌───────────┐  ┌───────────┐  ┌───────────┐
      │  채팅방   │  │   로비    │  │ 실시간    │  ← 튜토리얼 (하나 선택)
      │  만들기   │  │ +매칭시스템│  │  게임     │
      └─────┬─────┘  └─────┬─────┘  └─────┬─────┘
            │              │              │
            └──────────────┼──────────────┘
                           ▼
                  ┌─────────────────┐
                  │   상세 가이드   │  ← 필요한 것만 참조
                  └─────────────────┘
```

---

## 빠른 시작

### [Quick Start](01-quick-start.md) ⏱️ 5분
PlayHouse 서버를 처음 실행해봅니다.

```
✓ 프로젝트 설정
✓ Stage/Actor 최소 구현
✓ 서버 실행 및 클라이언트 연결
```

---

## 튜토리얼

실제 게임 서버를 만들어보며 배웁니다. **하나를 선택해서 따라해보세요.**

### [채팅방 만들기](tutorials/chat-room.md) ⏱️ 30분
> 가장 기본적인 멀티플레이어 예제

```
배우는 것:
✓ 방 입장/퇴장
✓ 메시지 브로드캐스트
✓ 연결 상태 처리
```

### [로비 + 매칭 시스템](tutorials/lobby-matching.md) ⏱️ 1시간
> 실제 게임 구조에 가까운 예제

```
배우는 것:
✓ 로비에서 대기
✓ 매칭 로직 구현
✓ 게임룸으로 이동
✓ API 서버 연동
```

### [실시간 게임 (GameLoop)](tutorials/realtime-game.md) ⏱️ 1-2시간
> 60fps 액션 게임 서버

```
배우는 것:
✓ 고정 타임스텝 GameLoop
✓ 입력 처리 및 상태 동기화
✓ 지연 보상 기법
```

---

## 상세 가이드

특정 기능이 필요할 때 참조하세요.

| 문서 | 설명 | 핵심 API |
|------|------|----------|
| [연결 및 인증](02-connection-auth.md) | TCP/WebSocket 연결, 인증 처리 | `ConnectAsync`, `OnAuthenticate` |
| [메시지 송수신](03-messaging.md) | Send, Request, Push 패턴 | `Send`, `Request`, `Reply` |
| [Stage 구현](04-stage-implementation.md) | Stage 생명주기와 메시지 처리 | `OnCreate`, `OnDispatch` |
| [Actor 구현](05-actor-implementation.md) | Actor 인증과 세션 관리 | `OnAuthenticate`, `AccountId` |
| [타이머 & 게임루프](06-timer-gameloop.md) | 시간 기반 로직 | `AddRepeatTimer`, `StartGameLoop` |
| [서버 간 통신](07-server-communication.md) | API/Play 서버 연동 | `SendToApi`, `RequestToStage` |
| [API 컨트롤러](08-api-controller.md) | API 서버 구현 | `IApiController` |
| [비동기 작업](09-async-operations.md) | 외부 I/O, DB 호출 | `AsyncCompute`, `AsyncIO` |
| [설정](10-configuration.md) | 서버 옵션 설정 | `PlayServerOption` |

---

## 문제 해결

| 문서 | 설명 |
|------|------|
| [트러블슈팅](troubleshooting.md) | 자주 발생하는 문제와 해결책 |
| [모범 사례](best-practices.md) | 프로덕션 수준의 권장 패턴 |

---

## 핵심 개념 미리보기

### Stage와 Actor

```
┌──────────────────── Stage (게임룸) ────────────────────┐
│                                                        │
│   ┌─────────┐   ┌─────────┐   ┌─────────┐            │
│   │ Actor 1 │   │ Actor 2 │   │ Actor 3 │            │
│   │ (플레이어)│   │ (플레이어)│   │ (플레이어)│            │
│   └─────────┘   └─────────┘   └─────────┘            │
│                                                        │
│   게임 상태, 타이머, 로직 등                           │
└────────────────────────────────────────────────────────┘
```

- **Stage**: 게임룸, 로비, 매치 등 여러 플레이어가 상호작용하는 공간
- **Actor**: 개별 플레이어 (클라이언트 연결)

### 메시지 패턴

| 패턴 | 용도 | 예시 |
|------|------|------|
| **Send** | 단방향 전송 | 채팅 메시지, 이동 입력 |
| **Request/Reply** | 요청-응답 | 아이템 구매, 스킬 사용 |
| **Push** | 서버→클라이언트 | 상태 변경 알림, 브로드캐스트 |

---

## 대상 독자

- PlayHouse 프레임워크로 게임 서버를 개발하는 개발자
- C#/.NET에 익숙한 개발자
- 멀티플레이어 게임 서버 개발에 관심 있는 개발자

> 프레임워크 내부 구현에 관심이 있다면 [specifications/](../specifications/) 문서를 참조하세요.

---

## 버전 정보

- PlayHouse-NET 1.0
- 최종 업데이트: 2026-01-29
