#!/bin/bash
# PlayHouse C++ Echo 벤치마크
# 사용법: ./run_benchmark.sh

set -e

# 설정
SERVER_DIR="../PlayHouse.Benchmark.Echo.Server"
CLIENT_BIN="./build/echo_benchmark"
TCP_PORT=16110
HTTP_PORT=5080
SERVER_HOST="127.0.0.1"

# 색상 정의
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 서버 PID
SERVER_PID=""

# 정리 함수
cleanup() {
    if [ -n "$SERVER_PID" ]; then
        echo -e "${YELLOW}Stopping server (PID: $SERVER_PID)...${NC}"
        kill $SERVER_PID 2>/dev/null || true
        wait $SERVER_PID 2>/dev/null || true
    fi
}

# 종료 시그널 핸들러
trap cleanup EXIT INT TERM

# 서버 시작 함수
start_server() {
    echo -e "${BLUE}Starting PlayHouse Echo Server...${NC}"
    cd $SERVER_DIR
    dotnet run -c Release -- --tcp-port $TCP_PORT --http-port $HTTP_PORT > /dev/null 2>&1 &
    SERVER_PID=$!
    cd - > /dev/null

    echo -e "${BLUE}Waiting for server to start (PID: $SERVER_PID)...${NC}"
    sleep 5

    # 서버 헬스 체크
    if ! curl -s "http://localhost:$HTTP_PORT/benchmark/stats" > /dev/null 2>&1; then
        echo -e "${RED}Failed to start server!${NC}"
        exit 1
    fi

    echo -e "${GREEN}Server started successfully${NC}"
}

# 서버 종료 함수
stop_server() {
    if [ -n "$SERVER_PID" ]; then
        echo -e "${YELLOW}Stopping server...${NC}"
        kill $SERVER_PID 2>/dev/null || true
        wait $SERVER_PID 2>/dev/null || true
        SERVER_PID=""
    fi
}

# Stage 사전 생성
create_stages() {
    local count=$1
    echo -e "${BLUE}Creating $count stages...${NC}"

    local response=$(curl -s -X POST "http://localhost:$HTTP_PORT/benchmark/stages" \
        -H "Content-Type: application/json" \
        -d "{\"count\": $count, \"baseStageId\": 10000}")

    echo -e "${GREEN}Stages created: $response${NC}"
}

# 메트릭 조회
get_metrics() {
    curl -s "http://localhost:$HTTP_PORT/benchmark/stats"
}

# 메트릭 리셋
reset_metrics() {
    echo -e "${BLUE}Resetting server metrics...${NC}"
    curl -s -X POST "http://localhost:$HTTP_PORT/benchmark/reset" > /dev/null
    echo -e "${GREEN}Metrics reset${NC}"
}

# 테스트 실행
run_test() {
    local connections=$1
    local msg_size=$2
    local times=$3
    local duration=$4
    local test_name=$5

    echo ""
    echo -e "${BLUE}======================================${NC}"
    echo -e "${BLUE}Test: $test_name${NC}"
    echo -e "${BLUE}  Connections: $connections${NC}"
    echo -e "${BLUE}  Message Size: $msg_size bytes${NC}"
    echo -e "${BLUE}  Times per connection: $times${NC}"
    echo -e "${BLUE}  Duration: $duration seconds${NC}"
    echo -e "${BLUE}======================================${NC}"

    # 서버 메트릭 리셋
    reset_metrics
    sleep 1

    # 클라이언트 실행
    echo -e "${YELLOW}Running C++ client...${NC}"
    $CLIENT_BIN $SERVER_HOST $TCP_PORT $connections $msg_size $times $duration

    # 잠시 대기 (메트릭 수집 완료)
    sleep 2

    # 서버 메트릭 조회
    echo -e "${YELLOW}Server Metrics:${NC}"
    get_metrics | jq '.' || get_metrics

    echo ""
}

# 메인 함수
main() {
    echo -e "${GREEN}========================================${NC}"
    echo -e "${GREEN}PlayHouse C++ Echo Benchmark${NC}"
    echo -e "${GREEN}========================================${NC}"

    # 클라이언트 바이너리 확인
    if [ ! -f "$CLIENT_BIN" ]; then
        echo -e "${RED}Error: Client binary not found: $CLIENT_BIN${NC}"
        echo -e "${YELLOW}Please build the client first:${NC}"
        echo -e "  cd build && cmake .. && make"
        exit 1
    fi

    # 서버 시작
    start_server

    # Stage 생성 (1000개)
    create_stages 1000

    # 초기 메트릭 확인
    echo -e "${YELLOW}Initial server metrics:${NC}"
    get_metrics | jq '.' || get_metrics
    sleep 2

    # 테스트 시나리오 실행
    echo ""
    echo -e "${GREEN}Starting benchmark tests...${NC}"

    # 1. 1000 connections, 8 bytes, 200 times, 10 seconds
    run_test 1000 8 200 10 "Small Messages (8B)"

    # 2. 1000 connections, 64 bytes, 200 times, 10 seconds
    run_test 1000 64 200 10 "Medium Messages (64B)"

    # 3. 1000 connections, 256 bytes, 200 times, 10 seconds
    run_test 1000 256 200 10 "Large Messages (256B)"

    # 4. 1000 connections, 1024 bytes, 200 times, 10 seconds
    run_test 1000 1024 200 10 "Extra Large Messages (1KB)"

    # 완료
    echo ""
    echo -e "${GREEN}========================================${NC}"
    echo -e "${GREEN}Benchmark completed!${NC}"
    echo -e "${GREEN}========================================${NC}"
}

# 스크립트 실행
main
