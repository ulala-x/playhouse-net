# 팀 리뷰 종합 및 플랜 보완 완료

## 리뷰 참여
- **Codex**: 기술적 정확성, Proto 매핑, 격리 전략 검토
- **Gemini**: 문서 품질, 실용성, 리스크 분석
- **Claude**: 리뷰 종합 및 플랜 반영

## 주요 보완 사항 (7개)

### 1. Proto 매핑 완성
**문제:** ErrorPacket, Disconnect 등이 48개 목록에 없음
**해결:** `### 에러 처리 (시스템 메시지)` 섹션 추가
- ErrorCode (Connector): RequestTimeout, NetworkError 등
- ErrorMessage (서버): MsgId "Error:" 시작

### 2. 테스트 격리 자동화
**문제:** 수동 ID 입력 시 복붙 실수 위험
**해결:** VerifierBase에 헬퍼 추가
```csharp
protected string GenerateUniqueUserId(string prefix);
protected long GenerateUniqueStageId(int baseOffset = 0);
```

### 3. 핸들러 해제 패턴
**문제:** OnReceive 핸들러 누적
**해결:** MessagingVerifier 예제 업데이트
```csharp
protected override async Task TeardownAsync()
{
    if (_receiveHandler != null)
    {
        Connector.OnReceive -= _receiveHandler;
        _receiveHandler = null;
    }
}
```

### 4. ErrorCode/ErrorMessage 검증
**문제:** 에러 검증 패턴 불명확
**해결:** 예제 코드 추가
- Connector: `ex.ErrorCode == ErrorCode.RequestTimeout`
- 서버: `response.MsgId.StartsWith("Error:")`

### 5. ServerLifecycle 전략
**문제:** Server Once Pattern과 서버 종료 테스트 충돌
**해결:** 임시 서버 인스턴스 생성 방식
```csharp
var tempServer = ServerFactory.CreatePlayServer(tcpPort: 0, zmqPort: 0);
// ... 테스트 후 종료
await tempServer.DisposeAsync();
```

### 6. ZMQ 포트 동적 할당
**문제:** CI 병렬 실행 시 포트 충돌
**해결:** 환경 변수 지원
```csharp
var zmqPortOffset = int.Parse(Environment.GetEnvironmentVariable("ZMQ_PORT_OFFSET") ?? "0");
var zmqPlayPort = 15000 + zmqPortOffset;
```

### 7. 구현 일정 조정
**문제:** 안정화 버퍼 부족
**해결:** Phase 3-4 일정 재조정
- Phase 3: Day 3-12 (Day 11-12 안정화 버퍼)
- Phase 4: Day 13-14 (CI 통합 + 문서화 분리)

## 구현 시 주의사항

1. **DIIntegration 3-4번**: proto 정의 추가 필요
2. **StageToApi "S2S 직접 라우팅"**: proto 정의 확정 필요
3. **콜백 검증 식별성**: 응답 필드에 콜백 증거 포함
4. **성능 테스트 격리**: `--category` 단독 실행 권장

## 최종 판정

| AI | 판정 | 조건 |
|----|------|------|
| Codex | 보완 필요 | 7개 액션 아이템 |
| Gemini | **강력 추천** | ZMQ 포트 + ID 헬퍼 |
| Claude | **구현 가능** | 플랜 보완 완료 |

## 다음 단계

**✅ Phase 1 구현 시작 가능**

플랜 파일: `/home/ulalax/.claude/plans/partitioned-drifting-lemur.md`
리뷰 파일:
- `docs/team/20260115_plan-review/codex-review.md`
- `docs/team/20260115_plan-review/gemini-review.md`
- `docs/team/20260115_plan-review/summary.md` (본 파일)
