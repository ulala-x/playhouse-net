# C++ Echo 클라이언트 연결 문제 보고

## 문제 요약

C++ Echo 클라이언트(`echo_benchmark`)가 PlayHouse 서버에 연결하지 못하는 문제가 발생했습니다.

## 증상

```
Connecting 1000 sessions...
Waiting for connections to establish...
  Connected: 0 / 1000
Connected sessions: 0

Error: Failed to establish connections
```

- `request_connect()`를 호출해도 연결이 하나도 성립되지 않음
- `get_connection_count()`가 계속 0을 반환
- 서버는 정상 작동 중 (C# 클라이언트는 연결 성공)

## 조사 내용

### 1. 서버 상태 확인
- PlayHouse Echo 서버는 정상 작동 중 ✓
- 포트 16110 리스닝 중 ✓
- HTTP API (포트 5080) 정상 응답 ✓
- C# 클라이언트는 정상 연결 및 통신 ✓

### 2. C++ 클라이언트 코드 분석

#### 연결 흐름
```cpp
// 1. echo_client.cpp
EchoClient::start()
  → connector->start()  // asio::system 초기화
  → m_process_thread 시작

// 2. request_connect()
boost::asio::ip::address::from_string(m_host)  // "localhost" → 실패!
  → endpoint 생성 실패
  → connector->request_connect() 호출 안됨
```

#### 발견된 문제
1. **"localhost" 문자열 처리 실패**:
   - `boost::asio::ip::address::from_string("localhost")`는 실패
   - IP 주소 형식("127.0.0.1")만 지원
   - DNS 리졸버 필요

2. **asio::system 초기화 이슈**:
   - `asio::system::instance()`는 lazy initialization 지원
   - `asio::system::init_instance()`가 백그라운드 IO 스레드 시작
   - `benchmark_main.cpp`에서 초기화 추가했으나 효과 없음

3. **연결 확인 로직 부재**:
   - `on_socket_connect()` 콜백이 호출되는지 확인 불가
   - 에러 메시지 출력 없음
   - 디버깅 로그 부재

## 시도한 해결 방법

### 1. 호스트 이름을 IP로 변경
```bash
# run_benchmark.sh
SERVER_HOST="127.0.0.1"  # localhost → 127.0.0.1
```
**결과**: 여전히 연결 실패

### 2. ASIO 시스템 명시적 초기화
```cpp
class AsioSystemGuard {
public:
    AsioSystemGuard() {
        asio::system::init_instance();
    }
    ~AsioSystemGuard() {
        asio::system::destroy_instance();
    }
};
```
**결과**: 여전히 연결 실패

### 3. 연결 대기 시간 증가
```cpp
for (int i = 0; i < 20; ++i) {  // 10초 대기
    std::this_thread::sleep_for(500ms);
    // ...
}
```
**결과**: 여전히 연결 실패

## 현재 상태

- ✗ C++ 클라이언트 연결 불가
- ✓ C# 클라이언트 정상 작동
- ✓ 인터랙티브 클라이언트(`echo_client`) 실행 가능 (연결은 미확인)

## 추가 조사 필요

1. **인터랙티브 클라이언트 연결 테스트**:
   - `echo_client`로 실제 연결 시도
   - 키 입력으로 연결 요청 ('4' 키 - 1000개 연결)
   - 연결 성공 여부 확인

2. **echo_client.cpp 상세 분석**:
   - `EchoSocket::on_connect()` 호출 여부 확인
   - `PlayHouseSocket::on_connect()` 구현 확인
   - ASIO connector 동작 확인

3. **네트워크 패킷 분석**:
   - tcpdump/Wireshark로 TCP 연결 시도 확인
   - SYN 패킷 전송 여부 확인
   - 서버 응답 확인

4. **에러 로깅 추가**:
   - `request_connect()` 에러 처리 개선
   - ASIO 에러 코드 출력
   - 연결 실패 원인 로깅

## 권장 해결 방법

### 단기 (벤치마크 완료용)
- C# 클라이언트 사용 (현재 정상 작동)
- C++ 클라이언트는 별도 이슈로 트래킹

### 중기 (디버깅)
1. 인터랙티브 클라이언트로 연결 테스트
2. 디버그 빌드로 상세 로그 확인
3. Wireshark로 네트워크 패킷 분석
4. ASIO 예제 코드와 비교

### 장기 (근본 해결)
1. DNS 리졸버 추가
2. 에러 처리 개선
3. 연결 상태 모니터링 강화
4. 단위 테스트 추가

## 파일 위치

- **벤치마크 실행 파일**: `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_echo/cpp_client/build/echo_benchmark`
- **소스 코드**: `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_echo/cpp_client/client/`
  - `benchmark_main.cpp`: 벤치마크 메인
  - `echo_client.cpp`: 클라이언트 구현
  - `main.cpp`: 인터랙티브 클라이언트
- **벤치마크 스크립트**: `/home/ulalax/project/ulalax/playhouse/playhouse-net/tests/benchmark_echo/cpp_client/run_benchmark.sh`

## 관련 코드

### echo_client.cpp:242-259
```cpp
void EchoClient::request_connect(int64_t count)
{
    if (!m_connector) {
        return;
    }

    // Prepare endpoint
    boost::asio::ip::tcp::endpoint endpoint;
    try {
        auto address = boost::asio::ip::address::from_string(m_host);
        endpoint = boost::asio::ip::tcp::endpoint(address, static_cast<unsigned short>(m_port));
    } catch (...) {
        std::cerr << "Invalid endpoint: " << m_host << ":" << m_port << std::endl;
        return;  // 여기서 리턴하면 연결 시도 안 함!
    }

    // Create connections
    for (int64_t i = 0; i < count; ++i) {
        m_connector->request_connect(endpoint);
    }
}
```

## 결론

C++ 클라이언트는 현재 연결 문제로 벤치마크를 수행할 수 없습니다. 근본 원인은 아직 불명확하며, 추가 조사가 필요합니다. 당분간 C# 클라이언트를 사용하여 벤치마크를 수행하는 것을 권장합니다.
