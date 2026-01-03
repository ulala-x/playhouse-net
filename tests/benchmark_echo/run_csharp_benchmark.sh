#!/bin/bash
# PlayHouse C# Echo 벤치마크
# 사용법: ./run_csharp_benchmark.sh

set -e

# 설정
SERVER_DIR="./PlayHouse.Benchmark.Echo.Server"
CLIENT_DIR="./PlayHouse.Benchmark.Echo.Client"
TCP_PORT=16110
ZMQ_PORT=16100
HTTP_PORT=5080

# 색상 정의
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# 서버 PID
SERVER_PID=""

# 정리 함수
cleanup() {
    if [ -n "$SERVER_PID" ]; then
        echo -e "${YELLOW}Stopping server (PID: $SERVER_PID)...${NC}"
        kill $SERVER_PID 2>/dev/null || true
        wait $SERVER_PID 2>/dev/null || true
    fi

    # 포트 정리
    fuser -k $ZMQ_PORT/tcp 2>/dev/null || true
    fuser -k $TCP_PORT/tcp 2>/dev/null || true
    fuser -k $HTTP_PORT/tcp 2>/dev/null || true
}

trap cleanup EXIT INT TERM

# 서버 시작 함수
start_server() {
    echo -e "${BLUE}Starting PlayHouse Echo Server...${NC}"
    cd $SERVER_DIR
    dotnet run -c Release -- --tcp-port $TCP_PORT --zmq-port $ZMQ_PORT --http-port $HTTP_PORT > server.log 2>&1 &
    SERVER_PID=$!
    cd - > /dev/null

    echo -e "${BLUE}Waiting for server to start (PID: $SERVER_PID)...${NC}"
    sleep 5

    # 서버 헬스 체크
    if ! curl -s "http://localhost:$HTTP_PORT/benchmark/stats" > /dev/null 2>&1; then
        echo -e "${RED}Failed to start server!${NC}"
        cat $SERVER_DIR/server.log
        exit 1
    fi

    echo -e "${GREEN}Server started successfully${NC}"
}

# Stage 생성
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
    curl -s -X POST "http://localhost:$HTTP_PORT/benchmark/reset" > /dev/null
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

    # Stage 생성
    create_stages $connections

    # 클라이언트 실행
    echo -e "${YELLOW}Running C# client...${NC}"
    cd $CLIENT_DIR

    # 클라이언트를 직접 빌드하고 실행 (dotnet run은 종료 시 서버를 shutdown하므로)
    dotnet build -c Release > /dev/null 2>&1

    # 별도 프로세스로 실행
    timeout $((duration + 10)) dotnet bin/Release/net8.0/PlayHouse.Benchmark.Echo.Client.dll \
        --connections $connections \
        --duration $duration \
        --payload-size $msg_size \
        --times $times \
        --http-port $HTTP_PORT \
        --base-stage-id 10000 \
        --mode request-async \
        --output-dir benchmark-results \
        --label "$test_name" 2>&1 | grep -v "shutdown\|Sending shutdown"

    cd - > /dev/null

    echo ""
    sleep 3
}

# 메인 함수
main() {
    echo -e "${GREEN}========================================${NC}"
    echo -e "${GREEN}PlayHouse C# Echo Benchmark${NC}"
    echo -e "${GREEN}========================================${NC}"

    # 포트 정리
    fuser -k $ZMQ_PORT/tcp 2>/dev/null || true
    fuser -k $TCP_PORT/tcp 2>/dev/null || true
    fuser -k $HTTP_PORT/tcp 2>/dev/null || true
    sleep 2

    # 서버 시작
    start_server

    # Stage 생성 (1000개)
    create_stages 1000

    # 초기 메트릭 확인
    echo -e "${YELLOW}Initial server metrics:${NC}"
    get_metrics
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
    echo -e "${GREEN}Results saved in: $CLIENT_DIR/benchmark-results/${NC}"
}

main
