# [최종 계획서] S2S(Server-to-Server) 고성능 메시징 최적화

## 1. 개요 (Overview)
사용자님의 `net-zmq` 실험 결론인 **"Managed Pool + byte[] API > Native Zero-copy"** 전략을 PlayHouse-NET의 S2S 통신에 전면 적용함. 인터옵 오버헤드를 유발하는 ZMQ Native 객체 대신, 관리형 버퍼 풀을 활용한 표준 byte[] 통신으로 성능을 극대화함.

## 2. 핵심 최적화 지침 (Core Strategy)

### A. MessagePool 기반 byte[] 통신
- **결과:** ZMQ byte[] API와 연동하여 인터옵 오버헤드 최소화 완료.

### B. Managed 영역의 객체 할당 제거 (Zero-Object)
- **Header:** `ZmqPlaySocket`에서 `ThreadLocal` 헤더 버퍼와 `ObjectPool<RouteHeader>` 도입.
- **Packet:** `RoutePacket` 객체 풀링 도입.
- **ApiServer:** `ApiWorkItem` 및 `ApiSender` 풀링/재사용 적용.
- **성과:** S2S 통신 중 Managed Heap 할당을 0에 가깝게 감축 (Srv GC 2/2/2 달성).

### C. 컨텐션 및 스케줄링 최적화
- **RequestCache:** 실험 결과 샤딩 오버헤드가 단일 딕셔너리 효율을 상쇄함을 확인하여 단일 큐로 원복 후 객체 풀링만 유지.
- **Task Pool:** ApiServer에 GlobalTaskPool을 도입하여 상주 Task 제어.

---

## 3. 구현 성과 (Execution Status)

1. **[완료] ZmqPlaySocket 최적화:** `ThreadLocal` 및 `MergeFrom` 기반 헤더 처리.
2. **[완료] RoutePacket & ApiWorkItem 풀링:** 매 요청당 할당 제거.
3. **[완료] ApiServer Task Pool 연동:** 시스템 전체 Task 개수 고정.
4. **[진행 중] S2S 성능 임계점 돌파:** 10,000 CCU 상황에서 TPS 및 Latency 튜닝.

## 4. 기대 효과
- **실용적 고성능:** 사용자님의 실험 결과가 증명하듯, 가장 낮은 오버헤드로 가장 높은 처리량을 얻을 수 있음.
- **안정성:** 수백만 번의 S2S 통신 중에도 Managed Heap의 할당 속도를 극도로 낮게 유지.
