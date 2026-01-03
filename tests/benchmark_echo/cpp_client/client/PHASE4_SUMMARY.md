# Phase 4: Echo Client Implementation - Summary

## Overview

CGDK10의 test_tcp_echo_client 아키텍처를 기반으로 PlayHouse Echo 벤치마크 클라이언트를 구현했습니다.

## Files Created

### 1. client/echo_client.h
- **EchoSocket**: PlayHouseSocket을 상속받아 EchoClient와 통합
  - `on_connect()`: 연결 시 EchoClient 알림
  - `on_disconnect()`: 연결 종료 시 EchoClient 알림
  - `on_message()`: 메시지 수신 시 파싱 및 EchoClient 알림

- **EchoClient**: 메인 클라이언트 클래스
  - 연결 테스트 모드: min~max 범위 내 연결 유지
  - 트래픽 테스트 모드: times만큼 에코 메시지 전송
  - Relay Echo 모드: EchoReply 수신 즉시 재전송
  - 통계 추적: 메시지 송수신 개수, 초당 처리량

### 2. client/echo_client.cpp
- EchoSocket 구현
  - PlayHouseSocket의 virtual 메서드 오버라이드
  - 메시지 파싱 및 relay echo 처리

- EchoClient 구현
  - 커넥터 관리 (EchoConnector inner class)
  - 연결 테스트 로직 (process_connect_test)
  - 트래픽 테스트 로직 (process_traffic_test)
  - 백그라운드 처리 스레드 (process_loop)
  - 통계 계산

### 3. client/main.cpp
- 키보드 입력 처리 (Linux/Windows 호환)
  - KeyboardInit 클래스: termios 설정
  - _kbhit(), _getch() 구현

- 화면 출력
  - print_title(): 타이틀 및 명령어 가이드
  - print_endpoint(): 서버 엔드포인트 표시
  - print_statistics_info(): 연결/송신/수신 통계
  - print_setting_info(): 테스트 모드 설정

- 키 매핑 (CGDK10과 동일)
  - '1'~'5': 연결 생성 (1, 10, 100, 1000, 10000)
  - '6', '7': 연결 종료
  - 'q': 연결 테스트 토글
  - 'w', 'e': min range 조정
  - 'r', 't': max range 조정
  - Space: 트래픽 테스트 토글
  - 'a'~'h': times 증가
  - 'z'~'n': times 감소
  - 'j', 'm': 메시지 크기 변경
  - 'u'~'p': 즉시 전송
  - '/': Relay Echo 토글
  - ESC: 종료

### 4. playhouse_socket.h 수정
- protected 섹션 추가
  - m_stage_id, m_msg_seq, m_authenticated를 protected로 변경
  - m_last_echo_content를 protected로 변경
  - 파생 클래스(EchoSocket)에서 접근 가능하도록

### 5. CMakeLists.txt 업데이트
- CLIENT_SOURCES 추가
- echo_client 실행 파일 타겟 추가
- Boost를 optional로 변경 (헤더만 사용)

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                         Main Loop                            │
│  - 키보드 입력 처리                                           │
│  - 통계 출력 (1초마다)                                        │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       │ 제어
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                       EchoClient                             │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Processing Thread (100ms interval)                    │ │
│  │   - process_connect_test()                             │ │
│  │   - process_traffic_test()                             │ │
│  │   - 통계 업데이트                                       │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Connection Management                                  │ │
│  │   - request_connect(count)                             │ │
│  │   - request_disconnect(count)                          │ │
│  │   - request_send_immediately(count)                    │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Socket Event Handlers                                  │ │
│  │   - on_socket_connect(socket)                          │ │
│  │   - on_socket_disconnect(socket)                       │ │
│  │   - on_socket_message(socket, ...)                     │ │
│  └────────────────────────────────────────────────────────┘ │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       │ 소켓 생성
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                    EchoConnector                             │
│  - process_create_socket() → EchoSocket 생성                │
│  - 각 EchoSocket에 EchoClient 포인터 설정                   │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       │ 생성
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                      EchoSocket                              │
│  (extends PlayHouseSocket)                                   │
│                                                              │
│  - on_connect() → EchoClient::on_socket_connect()           │
│  - on_disconnect() → EchoClient::on_socket_disconnect()     │
│  - on_message() → 파싱 → EchoClient::on_socket_message()    │
│  - Relay Echo 처리 (s_enable_relay_echo)                    │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       │ 사용
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                  PlayHouseSocket                             │
│  - send_authenticate()                                       │
│  - send_echo_request()                                       │
│  - Codec 사용하여 프로토콜 인코딩/디코딩                      │
└─────────────────────────────────────────────────────────────┘
```

## Key Design Decisions

### 1. EchoSocket 상속 구조
- PlayHouseSocket을 상속받아 EchoSocket 생성
- CGDK10의 패턴과 일치 (socket_tcp를 상속받는 방식)
- virtual 메서드 오버라이드로 이벤트 처리
- EchoClient와 느슨한 결합 (포인터로 연결)

### 2. EchoConnector Inner Class
- EchoClient::start()에서 로컬 클래스로 정의
- process_create_socket()를 오버라이드하여 EchoSocket 생성
- 각 소켓에 EchoClient 포인터 자동 설정
- CGDK10 connector 패턴 활용

### 3. 통계 추적
- atomic 변수로 멀티스레드 안전성 보장
- 백그라운드 스레드에서 100ms마다 기준값 업데이트
- 메인 스레드에서 1초마다 rate 계산 및 출력
- CGDK10과 유사한 통계 출력 형식

### 4. Message Type Configuration
- MESSAGE_TYPES 배열: 7가지 메시지 크기 정의 (8B ~ 64KB)
- CGDK10과 동일한 크기 및 pre-allocation count
- 메시지 크기 인덱스로 선택

## Statistics Output Format

```
[PlayHouse Echo Client - C++]

[remote endpoint]
address : 127.0.0.1
port    : 16110

[connection] now 1000

[send]       messages    123456   messages/s    12500.00   bytes/s 3200000

[receive]    messages    123456   messages/s    12500.00   bytes/s 3200000

[con test]   on  range min 500 ~ max 800
[echo test]  on  message size 256B,  times 200
[relay echo] off
```

## Building

### Dependencies Required
```bash
# Protobuf (development headers)
sudo apt-get install libprotobuf-dev protobuf-compiler

# Boost (optional, headers only)
sudo apt-get install libboost-dev
```

### Build Commands
```bash
cd /home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_echo/cpp_client
mkdir -p build
cd build
cmake ..
make -j$(nproc)
```

### Run
```bash
./echo_client
```

## Testing Scenarios

### 1. Connection Test
- 'q' 키로 연결 테스트 활성화
- min=500, max=800 범위 내 연결 유지
- 100ms마다 랜덤하게 연결/해제

### 2. Traffic Test
- Space 키로 트래픽 테스트 활성화
- 모든 연결에 대해 times=200개 메시지 전송
- 100ms 간격으로 반복

### 3. Relay Echo Test
- '/' 키로 활성화
- EchoReply 수신 즉시 동일한 내용으로 재전송
- 최대 처리량 측정에 유용

### 4. Combined Test
- 연결 테스트 + 트래픽 테스트 동시 실행
- 동적 연결 변화 속에서 안정적인 메시지 처리 검증

## Integration with PlayHouse Server

1. **서버 시작**
   ```bash
   cd /home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_echo
   dotnet run
   ```

2. **클라이언트 시작**
   ```bash
   cd cpp_client/build
   ./echo_client
   ```

3. **테스트 시퀀스**
   - '1' 키로 1개 연결 생성
   - 서버 로그에서 인증 및 Stage 생성 확인
   - 'u' 키로 1개 메시지 전송
   - 통계에서 send/receive 카운트 확인
   - Space 키로 트래픽 테스트 활성화
   - 1초 단위 처리량 모니터링

## Performance Expectations

- **연결**: 10,000+ connections
- **처리량**: 100,000+ msg/s (8B messages)
- **레이턴시**: < 1ms (로컬호스트)

## Known Issues

1. **Protobuf 의존성**
   - 현재 수동으로 protobuf 메시지 인코딩 (playhouse_socket.cpp)
   - 추후 proto 파일 기반 자동 생성으로 개선 필요

2. **통계 정확도**
   - Rate 계산이 1초 간격이라 짧은 버스트에서 부정확할 수 있음
   - CGDK10의 Nstatistics 사용 시 개선 가능

3. **에러 처리**
   - 네트워크 에러 처리 최소화
   - 프로덕션 환경에서는 재연결 로직 필요

## Next Steps

1. **빌드 및 테스트**
   - 의존성 설치
   - 컴파일 및 링크 에러 수정
   - PlayHouse 서버와 통합 테스트

2. **벤치마크 실행**
   - 다양한 메시지 크기 테스트
   - 연결 개수별 성능 측정
   - C# 클라이언트와 성능 비교

3. **문서화**
   - 성능 측정 결과 기록
   - 최적화 포인트 분석
   - 튜닝 가이드 작성
