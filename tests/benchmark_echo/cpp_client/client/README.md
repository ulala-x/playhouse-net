# PlayHouse Socket Implementation

PlayHouse 프로토콜을 처리하는 소켓 클래스 구현입니다.

## 파일 구조

```
client/
├── playhouse_socket.h    # PlayHouse 소켓 헤더
├── playhouse_socket.cpp  # PlayHouse 소켓 구현
└── README.md             # 이 문서
```

## 주요 클래스: PlayHouseSocket

`PlayHouseSocket`은 CGDK10의 `asio::Nsocket_tcp`를 확장하여 PlayHouse 프로토콜을 처리합니다.

### 상속 구조

```
asio::Isocket_tcp (CGDK10 인터페이스)
    └── asio::Nsocket_tcp (CGDK10 구현)
            └── playhouse::PlayHouseSocket (PlayHouse 프로토콜)
```

### 주요 기능

#### 1. 연결 관리

```cpp
PlayHouseSocket socket;

// 연결 이벤트 콜백 설정
socket.on_connect_callback = []() {
    std::cout << "Connected!" << std::endl;
};

socket.on_disconnect_callback = []() {
    std::cout << "Disconnected!" << std::endl;
};
```

#### 2. 메시지 수신

```cpp
socket.on_message_callback = [](
    const std::string& msg_id,
    uint16_t msg_seq,
    int64_t stage_id,
    uint16_t error_code,
    const std::vector<uint8_t>& payload)
{
    std::cout << "Received: " << msg_id
              << " seq=" << msg_seq
              << " error=" << error_code << std::endl;

    // payload는 Protobuf 직렬화된 데이터
    // 필요시 ParseFromArray()로 역직렬화
};
```

#### 3. 인증

```cpp
// 인증 요청 전송
socket.send_authenticate("1.0.0");

// 인증 상태 확인
if (socket.is_authenticated()) {
    std::cout << "Authenticated!" << std::endl;
}
```

#### 4. Stage 설정

```cpp
// Stage ID 설정 (Join 후)
socket.set_stage_id(12345);

// 모든 메시지는 설정된 Stage로 전송됨
```

#### 5. Echo 요청

```cpp
// Echo 요청 전송
std::string content = "Hello, World!";
int64_t timestamp = PlayHouseSocket::get_current_timestamp_ms();
socket.send_echo_request(content, timestamp);
```

#### 6. 범용 메시지 전송

```cpp
// Protobuf 메시지 직렬화
MyMessage msg;
msg.set_field("value");

std::vector<uint8_t> payload(msg.ByteSizeLong());
msg.SerializeToArray(payload.data(), payload.size());

// 메시지 전송
socket.send_message("MyMessage", payload);
```

### Relay Echo 모드

벤치마크를 위한 특수 모드입니다. EchoReply를 받으면 즉시 EchoRequest를 재전송합니다.

```cpp
// 전역 설정 (모든 소켓에 적용)
PlayHouseSocket::s_enable_relay_echo = true;

// 최초 Echo 요청 전송
socket.send_echo_request("test", timestamp);

// 이후 EchoReply → EchoRequest → EchoReply → ... 자동 반복
```

## 프로토콜 처리

### 패킷 포맷

**Request (Client → Server):**
```
[Length: 4B LE] - 메시지 본문 크기 (이 필드 제외)
[MsgIdLen: 1B] - MsgId의 UTF-8 바이트 길이
[MsgId: N bytes] - UTF-8 문자열
[MsgSeq: 2B LE] - 시퀀스 번호
[StageId: 8B LE] - Stage ID
[Payload: variable] - Protobuf 직렬화 데이터
```

**Response (Server → Client):**
```
[Length: 4B LE]
[MsgIdLen: 1B]
[MsgId: N bytes]
[MsgSeq: 2B LE]
[StageId: 8B LE]
[ErrorCode: 2B LE] - 0 = 성공
[OriginalSize: 4B LE] - 0 = 압축 안 됨
[Payload: variable]
```

### 패킷 수신 처리 흐름

```
CGDK10 asio::Nsocket_tcp
  │
  ├─ process_receive_async()
  │    └─ Length-prefix 프레이밍 (4바이트 읽기)
  │
  └─ on_message(const_buffer& msg)
       │
       └─ PlayHouseSocket::on_message()
            │
            ├─ Length prefix 검증
            ├─ Message body 추출
            └─ handle_packet()
                 │
                 ├─ Codec::decode_response()
                 ├─ Authentication 처리
                 ├─ Relay Echo 처리
                 └─ on_message_callback 호출
```

## 구현 세부사항

### CGDK10 소켓과의 통합

CGDK10의 `asio::Nsocket_tcp`는 이미 Length-prefix 기반 프레이밍을 처리합니다:

- `process_receive_async()`: 4바이트 길이 필드를 읽고 완전한 메시지를 버퍼링
- `on_message()`: 완전한 메시지가 도착하면 호출 (Length 포함)

PlayHouseSocket은 이를 활용하여:
1. `on_message()`에서 Length prefix를 검증
2. Message body를 `Codec::decode_response()`로 파싱
3. 콜백 호출 또는 Relay Echo 처리

### 시퀀스 번호 관리

```cpp
std::atomic<uint16_t> m_msg_seq{0};

uint16_t next_msg_seq() {
    return m_msg_seq.fetch_add(1);
}
```

- 원자적 증가로 스레드 안전성 보장
- 요청/응답 매칭에 사용

### Protobuf 직렬화

현재 구현은 수동 Protobuf 인코딩을 사용합니다 (`.proto` 파일 생성 전):

```cpp
// EchoRequest 수동 인코딩
std::vector<uint8_t> payload;
payload.push_back(0x0A);  // Field 1, wire type 2 (string)
payload.push_back(content.size());
payload.insert(payload.end(), content.begin(), content.end());
// ... timestamp 인코딩
```

**TODO:** Protobuf 컴파일러로 생성된 코드로 교체:
```cpp
#include "echo.pb.h"

EchoRequest request;
request.set_content(content);
request.set_client_timestamp(timestamp);

std::vector<uint8_t> payload(request.ByteSizeLong());
request.SerializeToArray(payload.data(), payload.size());
```

## 사용 예제

### 기본 Echo 클라이언트

```cpp
#include "client/playhouse_socket.h"
#include <iostream>

int main() {
    playhouse::PlayHouseSocket socket;

    // 콜백 설정
    socket.on_connect_callback = [&socket]() {
        std::cout << "Connected, authenticating..." << std::endl;
        socket.send_authenticate("1.0.0");
    };

    socket.on_message_callback = [&socket](
        const std::string& msg_id,
        uint16_t msg_seq,
        int64_t stage_id,
        uint16_t error_code,
        const std::vector<uint8_t>& payload)
    {
        if (msg_id == "AuthenticateReply") {
            std::cout << "Authenticated!" << std::endl;
            // Echo 요청 전송
            socket.send_echo_request("Hello",
                PlayHouseSocket::get_current_timestamp_ms());
        }
        else if (msg_id == "EchoReply") {
            std::cout << "Echo received, seq=" << msg_seq << std::endl;
        }
    };

    // 연결 (CGDK10 connector 사용)
    // ... connector 설정 및 연결

    return 0;
}
```

### Relay Echo 벤치마크

```cpp
// Relay Echo 모드 활성화
playhouse::PlayHouseSocket::s_enable_relay_echo = true;

// 여러 소켓 생성 및 연결
std::vector<std::shared_ptr<playhouse::PlayHouseSocket>> sockets;
for (int i = 0; i < 100; ++i) {
    auto socket = std::make_shared<playhouse::PlayHouseSocket>();
    // ... 설정 및 연결
    sockets.push_back(socket);
}

// 각 소켓에서 최초 Echo 전송
for (auto& socket : sockets) {
    socket->send_echo_request("benchmark",
        PlayHouseSocket::get_current_timestamp_ms());
}

// 이후 자동으로 Echo 반복
// EchoReply → EchoRequest → EchoReply → ...
```

## 컴파일

```bash
cd /home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_echo/cpp_client
mkdir -p build && cd build
cmake ..
make
```

CMakeLists.txt에 다음 추가 필요:
```cmake
add_library(playhouse_client
    client/playhouse_socket.cpp
)

target_link_libraries(playhouse_client
    playhouse_protocol
    asio
    Boost::system
)
```

## 향후 개선 사항

1. **Protobuf 통합**
   - `.proto` 컴파일 및 생성된 코드 사용
   - 수동 인코딩 제거

2. **에러 처리 강화**
   - 타임아웃 처리
   - 재연결 로직
   - 에러 코드별 핸들링

3. **성능 최적화**
   - 제로카피 버퍼 처리
   - 메모리 풀 사용

4. **로깅**
   - 디버그 로깅 추가
   - 통계 정보 수집

5. **테스트**
   - 단위 테스트 추가
   - 통합 테스트 작성
