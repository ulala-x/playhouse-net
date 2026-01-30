# 비동기 작업 (AsyncCompute / AsyncIO)

PlayHouse의 Stage는 단일 스레드 이벤트 루프에서 동작합니다. 외부 I/O나 무거운 연산을 수행할 때는 이벤트 루프를 블록하지 않도록 `AsyncCompute`와 `AsyncIO`를 사용해야 합니다.

## 개요

Stage 내에서 다음과 같은 작업을 수행할 때 AsyncBlock을 사용합니다.

- 데이터베이스 쿼리
- HTTP API 호출
- 파일 I/O
- CPU 집약적인 계산 (암호화, 압축, AI 연산 등)

AsyncBlock은 두 단계로 구성됩니다.

1. **PreCallback**: 백그라운드 스레드에서 실행 (외부 작업 수행)
2. **PostCallback**: Stage 이벤트 루프에서 실행 (Stage 상태 안전하게 접근)

## 1. AsyncIO - I/O 바운드 작업

I/O 작업(데이터베이스, HTTP, 파일 등)에 사용합니다.

### 1.1 기본 사용법

```csharp
public class GameStage : IStage
{
    private readonly IUserRepository _userRepository;

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "LoadUserDataRequest")
        {
            var request = LoadUserDataRequest.Parser.ParseFrom(packet.Payload.DataSpan);

            // 즉시 수락 응답
            actor.ActorLink.Reply(CPacket.Empty("LoadUserDataAccepted"));

            // AsyncIO로 DB 조회
            StageLink.AsyncIO(
                preCallback: async () =>
                {
                    // I/O 스레드 풀에서 실행
                    // Stage 상태 접근 금지!
                    var userData = await _userRepository.GetUserDataAsync(request.UserId);
                    return userData;
                },
                postCallback: async (result) =>
                {
                    // Stage 이벤트 루프에서 실행
                    // Stage 상태 안전하게 접근 가능
                    var userData = (UserData)result!;

                    // 로드된 데이터를 클라이언트로 전송
                    actor.ActorLink.SendToClient(CPacket.Of(new LoadUserDataNotify
                    {
                        UserId = userData.Id,
                        Level = userData.Level,
                        Gold = userData.Gold
                    }));

                    return Task.CompletedTask;
                }
            );
        }
    }
}
```

### 1.2 PostCallback 없이 사용

PostCallback은 선택적입니다. 결과를 Stage로 돌려보낼 필요가 없으면 생략할 수 있습니다.

```csharp
// 로그만 기록하는 경우
StageLink.AsyncIO(
    async () =>
    {
        await _logger.WriteLogAsync("Player joined", actor.ActorLink.AccountId);
        return null;
    }
    // postCallback 생략
);
```

### 1.3 여러 I/O 작업 조합

```csharp
StageLink.AsyncIO(
    async () =>
    {
        // 여러 I/O 작업을 병렬로 수행
        var userTask = _userRepository.GetUserAsync(userId);
        var inventoryTask = _inventoryRepository.GetInventoryAsync(userId);
        var friendsTask = _friendRepository.GetFriendsAsync(userId);

        await Task.WhenAll(userTask, inventoryTask, friendsTask);

        return new
        {
            User = await userTask,
            Inventory = await inventoryTask,
            Friends = await friendsTask
        };
    },
    async (result) =>
    {
        var data = (dynamic)result!;

        // 모든 데이터를 한 번에 전송
        actor.ActorLink.SendToClient(CPacket.Of(new InitialDataNotify
        {
            UserData = ConvertToProto(data.User),
            InventoryData = ConvertToProto(data.Inventory),
            FriendsData = ConvertToProto(data.Friends)
        }));

        return Task.CompletedTask;
    }
);
```

## 2. AsyncCompute - CPU 바운드 작업

CPU 집약적인 계산에 사용합니다. ComputeTaskPool은 CPU 코어 수만큼의 제한된 동시성을 가집니다.

### 2.1 기본 사용법

```csharp
public async Task OnDispatch(IActor actor, IPacket packet)
{
    if (packet.MsgId == "EncryptDataRequest")
    {
        var request = EncryptDataRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        actor.ActorLink.Reply(CPacket.Empty("EncryptDataAccepted"));

        StageLink.AsyncCompute(
            preCallback: async () =>
            {
                // CPU 집약적인 암호화 작업
                // ComputeTaskPool에서 실행 (CPU 코어 수만큼 제한)
                var encryptedData = await EncryptData(request.Data, request.Key);
                return encryptedData;
            },
            postCallback: async (result) =>
            {
                var encrypted = (byte[])result!;

                actor.ActorLink.SendToClient(CPacket.Of(new EncryptDataNotify
                {
                    EncryptedData = Google.Protobuf.ByteString.CopyFrom(encrypted)
                }));

                return Task.CompletedTask;
            }
        );
    }
}
```

### 2.2 CPU 집약적 계산 예제

```csharp
// AI 경로 탐색
StageLink.AsyncCompute(
    async () =>
    {
        // A* 알고리즘 같은 무거운 경로 탐색
        var path = await _pathfinder.FindPathAsync(
            start: currentPosition,
            goal: targetPosition,
            map: _mapData
        );
        return path;
    },
    async (result) =>
    {
        var path = (List<Vector3>)result!;

        // 경로를 NPC에게 설정
        _npc.SetPath(path);

        return Task.CompletedTask;
    }
);

// 물리 시뮬레이션
StageLink.AsyncCompute(
    async () =>
    {
        // 충돌 감지 및 물리 계산
        var collisions = await _physics.SimulateAsync(_entities, deltaTime);
        return collisions;
    },
    async (result) =>
    {
        var collisions = (List<Collision>)result!;

        // Stage 상태 업데이트
        foreach (var collision in collisions)
        {
            ApplyCollision(collision);
        }

        return Task.CompletedTask;
    }
);
```

## 3. AsyncIO vs AsyncCompute 선택 가이드

| 작업 유형 | 사용할 메서드 | 이유 |
|-----------|--------------|------|
| 데이터베이스 쿼리 | `AsyncIO` | I/O 대기 시간이 많음 |
| HTTP API 호출 | `AsyncIO` | 네트워크 I/O |
| 파일 읽기/쓰기 | `AsyncIO` | 디스크 I/O |
| Redis/캐시 조회 | `AsyncIO` | 네트워크 I/O |
| 암호화/복호화 | `AsyncCompute` | CPU 집약적 |
| 이미지 처리 | `AsyncCompute` | CPU 집약적 |
| AI 경로 탐색 | `AsyncCompute` | CPU 집약적 |
| 압축/해제 | `AsyncCompute` | CPU 집약적 |
| 수학 계산 | `AsyncCompute` | CPU 집약적 |

**간단한 규칙**

- 대부분의 시간을 "기다림"에 소비하면 → `AsyncIO`
- 대부분의 시간을 "계산"에 소비하면 → `AsyncCompute`

## 4. 실전 예제

### 예제 1: 데이터베이스 저장 및 랭킹 업데이트

```csharp
public class RankedGameStage : IStage
{
    private readonly IGameRepository _gameRepository;
    private readonly ILeaderboardService _leaderboard;
    private Dictionary<string, int> _playerScores = new();

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "GameFinished")
        {
            var finalScore = _playerScores[actor.ActorLink.AccountId];

            actor.ActorLink.Reply(CPacket.Empty("GameFinishedAccepted"));

            // AsyncIO로 DB 저장 및 랭킹 업데이트
            StageLink.AsyncIO(
                async () =>
                {
                    // DB에 게임 결과 저장
                    await _gameRepository.SaveGameResultAsync(new GameResult
                    {
                        PlayerId = actor.ActorLink.AccountId,
                        StageId = StageLink.StageId,
                        Score = finalScore,
                        Timestamp = DateTimeOffset.UtcNow
                    });

                    // 리더보드 업데이트
                    var newRank = await _leaderboard.UpdateScoreAsync(
                        actor.ActorLink.AccountId,
                        finalScore
                    );

                    return newRank;
                },
                async (result) =>
                {
                    var newRank = (int)result!;

                    // 최종 결과를 클라이언트에 전송
                    actor.ActorLink.SendToClient(CPacket.Of(new GameResultNotify
                    {
                        FinalScore = finalScore,
                        NewRank = newRank,
                        Saved = true
                    }));

                    return Task.CompletedTask;
                }
            );
        }
    }
}
```

### 예제 2: 외부 API 호출 (결제 검증)

```csharp
public class ShopStage : IStage
{
    private readonly IPaymentService _paymentService;

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "VerifyPurchaseRequest")
        {
            var request = VerifyPurchaseRequest.Parser.ParseFrom(packet.Payload.DataSpan);

            actor.ActorLink.Reply(CPacket.Empty("VerifyPurchaseAccepted"));

            StageLink.AsyncIO(
                async () =>
                {
                    // 외부 결제 API 호출 (Google Play, App Store 등)
                    var verifyResult = await _paymentService.VerifyReceiptAsync(
                        request.Receipt,
                        request.Platform
                    );

                    return verifyResult;
                },
                async (result) =>
                {
                    var verifyResult = (PaymentVerifyResult)result!;

                    if (verifyResult.IsValid)
                    {
                        // 검증 성공: 아이템 지급
                        GiveItemToPlayer(actor, verifyResult.ItemId, verifyResult.Quantity);

                        actor.ActorLink.SendToClient(CPacket.Of(new PurchaseVerifiedNotify
                        {
                            Success = true,
                            ItemId = verifyResult.ItemId,
                            Quantity = verifyResult.Quantity
                        }));
                    }
                    else
                    {
                        // 검증 실패
                        actor.ActorLink.SendToClient(CPacket.Of(new PurchaseVerifiedNotify
                        {
                            Success = false,
                            ErrorMessage = verifyResult.ErrorMessage
                        }));
                    }

                    return Task.CompletedTask;
                }
            );
        }
    }
}
```

### 예제 3: AI 경로 탐색 (CPU 집약)

```csharp
public class DungeonStage : IStage
{
    private readonly IPathfinder _pathfinder;
    private Map _map;
    private Dictionary<int, NPC> _npcs = new();

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "MoveNPCRequest")
        {
            var request = MoveNPCRequest.Parser.ParseFrom(packet.Payload.DataSpan);
            var npc = _npcs[request.NpcId];

            actor.ActorLink.Reply(CPacket.Empty("MoveNPCAccepted"));

            StageLink.AsyncCompute(
                async () =>
                {
                    // CPU 집약적인 경로 탐색 (A* 알고리즘)
                    var path = await _pathfinder.FindPathAsync(
                        start: npc.Position,
                        goal: new Vector3(request.TargetX, request.TargetY, request.TargetZ),
                        map: _map,
                        options: new PathfindingOptions
                        {
                            MaxSearchDepth = 1000,
                            AllowDiagonal = true,
                            HeuristicWeight = 1.2f
                        }
                    );

                    return path;
                },
                async (result) =>
                {
                    var path = (List<Vector3>)result!;

                    if (path != null && path.Count > 0)
                    {
                        // NPC에게 경로 설정
                        npc.SetPath(path);

                        // 모든 플레이어에게 NPC 이동 브로드캐스트
                        BroadcastToAllPlayers(new NPCMoveNotify
                        {
                            NpcId = request.NpcId,
                            Path = { path.Select(v => new Vector3Proto
                            {
                                X = v.X,
                                Y = v.Y,
                                Z = v.Z
                            }) }
                        });
                    }

                    return Task.CompletedTask;
                }
            );
        }
    }
}
```

### 예제 4: AsyncBlock 내에서 서버간 통신

```csharp
public async Task OnDispatch(IActor actor, IPacket packet)
{
    if (packet.MsgId == "LoadExternalDataRequest")
    {
        var request = LoadExternalDataRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        actor.ActorLink.Reply(CPacket.Empty("LoadExternalDataAccepted"));

        StageLink.AsyncIO(
            async () =>
            {
                // PreCallback에서 API 서버로 요청
                // ConfigureAwait(false) 사용 권장
                using var response = await StageLink.RequestToApiService(
                    serviceId: 200,
                    packet: CPacket.Of(new GetExternalDataRequest
                    {
                        DataId = request.DataId
                    })
                ).ConfigureAwait(false);

                var externalData = GetExternalDataResponse.Parser.ParseFrom(
                    response.Payload.DataSpan
                );

                return externalData;
            },
            async (result) =>
            {
                var data = (GetExternalDataResponse)result!;

                // Stage 상태 업데이트
                UpdateStageWithExternalData(data);

                // 클라이언트에 알림
                actor.ActorLink.SendToClient(CPacket.Of(new ExternalDataLoadedNotify
                {
                    DataId = data.DataId,
                    Success = true
                }));

                return Task.CompletedTask;
            }
        );
    }
}
```

## 5. 주의사항

### 5.1 PreCallback에서 Stage 상태 접근 금지

PreCallback은 백그라운드 스레드에서 실행되므로 Stage 상태에 접근하면 안 됩니다.

```csharp
// 잘못된 예
StageLink.AsyncIO(
    async () =>
    {
        // ❌ 위험: Stage 필드 접근 (Race condition)
        var playerId = _players[0].AccountId;
        var data = await _repository.GetDataAsync(playerId);
        return data;
    },
    async (result) => Task.CompletedTask
);

// 올바른 예
var playerId = _players[0].AccountId; // Stage 이벤트 루프에서 읽기

StageLink.AsyncIO(
    async () =>
    {
        // ✅ 안전: 캡처된 지역 변수 사용
        var data = await _repository.GetDataAsync(playerId);
        return data;
    },
    async (result) =>
    {
        // ✅ 안전: PostCallback에서는 Stage 상태 접근 가능
        _playerData[playerId] = (PlayerData)result!;
        return Task.CompletedTask;
    }
);
```

### 5.2 ConfigureAwait(false) 사용

PreCallback 내에서 async/await를 사용할 때는 `ConfigureAwait(false)`를 사용하는 것이 좋습니다.

```csharp
StageLink.AsyncIO(
    async () =>
    {
        // ConfigureAwait(false) 권장
        var data = await _repository.GetDataAsync(userId).ConfigureAwait(false);
        var processed = await ProcessDataAsync(data).ConfigureAwait(false);
        return processed;
    },
    async (result) =>
    {
        // PostCallback에서는 ConfigureAwait 불필요
        UpdateStageState((ProcessedData)result!);
        return Task.CompletedTask;
    }
);
```

### 5.3 예외 처리

AsyncBlock 내에서 발생한 예외는 적절히 처리해야 합니다.

```csharp
StageLink.AsyncIO(
    async () =>
    {
        try
        {
            var data = await _repository.GetDataAsync(userId);
            return new { Success = true, Data = data };
        }
        catch (Exception ex)
        {
            // 로깅
            _logger.LogError(ex, "Failed to load data");
            return new { Success = false, Data = (object?)null };
        }
    },
    async (result) =>
    {
        var response = (dynamic)result!;

        if (response.Success)
        {
            actor.ActorLink.SendToClient(CPacket.Of(new DataLoadedNotify
            {
                Data = ConvertToProto(response.Data)
            }));
        }
        else
        {
            actor.ActorLink.SendToClient(CPacket.Of(new DataLoadFailedNotify
            {
                ErrorMessage = "Failed to load data"
            }));
        }

        return Task.CompletedTask;
    }
);
```

### 5.4 PostCallback은 선택적

결과를 Stage로 돌려보낼 필요가 없으면 PostCallback을 생략할 수 있습니다.

```csharp
// PostCallback 없이 사용 (fire-and-forget 로깅)
StageLink.AsyncIO(
    async () =>
    {
        await _analyticsService.LogEventAsync("player_joined", new
        {
            playerId = actor.ActorLink.AccountId,
            stageId = StageLink.StageId,
            timestamp = DateTimeOffset.UtcNow
        });
        return null;
    }
);
```

### 5.5 과도한 AsyncBlock 사용 주의

AsyncBlock은 스레드 전환 오버헤드가 있습니다. 간단한 작업은 직접 수행하세요.

```csharp
// 나쁜 예: 간단한 계산을 AsyncCompute로
StageLink.AsyncCompute(
    async () => playerScore + 100,  // 불필요한 AsyncBlock
    async (result) => { /* ... */ }
);

// 좋은 예: 직접 계산
var newScore = playerScore + 100;
actor.ActorLink.Reply(CPacket.Of(new ScoreUpdateNotify { Score = newScore }));
```

## 6. 스레드 풀 설정

AsyncBlock의 동시성은 서버 옵션에서 설정할 수 있습니다.

```csharp
// PlayServerOption에서 설정 (기본값)
builder.UsePlayHouse<PlayServer>(options =>
{
    options.MinTaskPoolSize = 100;  // 워커 풀 최소 크기
    options.MaxTaskPoolSize = 1000; // 워커 풀 최대 크기
});
```

- **IoTaskPool**: 기본 동시성 100 (높은 동시성)
- **ComputeTaskPool**: CPU 코어 수만큼 제한 (과도한 CPU 사용 방지)

## 7. 요약

| 메서드 | 용도 | 스레드 풀 | 동시성 |
|--------|------|-----------|--------|
| `AsyncIO` | I/O 바운드 작업 | IoTaskPool | 높음 (기본 100) |
| `AsyncCompute` | CPU 바운드 작업 | ComputeTaskPool | CPU 코어 수 |

**핵심 원칙**

1. Stage 이벤트 루프를 블록하는 작업은 AsyncBlock 사용
2. I/O 작업은 `AsyncIO`, CPU 작업은 `AsyncCompute`
3. PreCallback에서는 Stage 상태 접근 금지
4. PostCallback에서 Stage 상태를 안전하게 업데이트
5. 예외 처리를 반드시 구현
6. 과도한 사용은 오버헤드를 초래하므로 주의

AsyncBlock을 올바르게 사용하면 단일 스레드 이벤트 루프의 장점을 유지하면서도 외부 I/O와 무거운 연산을 효율적으로 처리할 수 있습니다.
