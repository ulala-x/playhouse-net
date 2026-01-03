#!/bin/bash
# PlayHouse C++ Echo 자동화 벤치마크
# 다양한 설정으로 벤치마크를 실행하고 결과를 파일로 저장

set -e

# 설정
SERVER_DIR="../PlayHouse.Benchmark.Echo.Server"
CLIENT_BIN="./build/echo_benchmark"
TCP_PORT=16110
HTTP_PORT=5080
SERVER_HOST="127.0.0.1"
RESULTS_DIR="./benchmark_results"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
RESULTS_FILE="$RESULTS_DIR/benchmark_${TIMESTAMP}.txt"

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
        echo -e "${YELLOW}Stopping server (PID: $SERVER_PID)...${NC}" | tee -a $RESULTS_FILE
        kill $SERVER_PID 2>/dev/null || true
        wait $SERVER_PID 2>/dev/null || true
    fi
}

trap cleanup EXIT INT TERM

# 결과 디렉토리 생성
mkdir -p $RESULTS_DIR

# 서버 시작 함수
start_server() {
    echo -e "${BLUE}Starting PlayHouse Echo Server...${NC}" | tee -a $RESULTS_FILE
    cd $SERVER_DIR
    dotnet run -c Release -- --tcp-port $TCP_PORT --http-port $HTTP_PORT > /dev/null 2>&1 &
    SERVER_PID=$!
    cd - > /dev/null

    echo -e "${BLUE}Waiting for server to start (PID: $SERVER_PID)...${NC}" | tee -a $RESULTS_FILE
    sleep 5

    if ! curl -s "http://localhost:$HTTP_PORT/benchmark/stats" > /dev/null 2>&1; then
        echo -e "${RED}Failed to start server!${NC}" | tee -a $RESULTS_FILE
        exit 1
    fi

    echo -e "${GREEN}Server started successfully${NC}" | tee -a $RESULTS_FILE
}

# Stage 생성
create_stages() {
    local count=$1
    echo -e "${BLUE}Creating $count stages...${NC}" | tee -a $RESULTS_FILE

    local response=$(curl -s -X POST "http://localhost:$HTTP_PORT/benchmark/stages" \
        -H "Content-Type: application/json" \
        -d "{\"count\": $count, \"baseStageId\": 10000}")

    echo -e "${GREEN}Stages created: $response${NC}" | tee -a $RESULTS_FILE
}

# 메트릭 리셋
reset_metrics() {
    curl -s -X POST "http://localhost:$HTTP_PORT/benchmark/reset" > /dev/null
}

# 메트릭 조회
get_metrics() {
    curl -s "http://localhost:$HTTP_PORT/benchmark/stats"
}

# 테스트 실행
run_test() {
    local connections=$1
    local msg_size=$2
    local times=$3
    local duration=$4
    local test_name=$5

    echo "" | tee -a $RESULTS_FILE
    echo "======================================" | tee -a $RESULTS_FILE
    echo "Test: $test_name" | tee -a $RESULTS_FILE
    echo "  Connections: $connections" | tee -a $RESULTS_FILE
    echo "  Message Size: $msg_size bytes" | tee -a $RESULTS_FILE
    echo "  Times per connection: $times" | tee -a $RESULTS_FILE
    echo "  Duration: $duration seconds" | tee -a $RESULTS_FILE
    echo "======================================" | tee -a $RESULTS_FILE

    # 서버 메트릭 리셋
    reset_metrics
    sleep 1

    # 클라이언트 실행
    echo "Running C++ client..." | tee -a $RESULTS_FILE
    $CLIENT_BIN $SERVER_HOST $TCP_PORT $connections $msg_size $times $duration | tee -a $RESULTS_FILE

    # 잠시 대기
    sleep 2

    # 서버 메트릭 조회
    echo "Server Metrics:" | tee -a $RESULTS_FILE
    get_metrics | jq '.' | tee -a $RESULTS_FILE || get_metrics | tee -a $RESULTS_FILE

    echo "" | tee -a $RESULTS_FILE
}

# 메인 함수
main() {
    echo "========================================" | tee $RESULTS_FILE
    echo "PlayHouse C++ Echo Automatic Benchmark" | tee -a $RESULTS_FILE
    echo "Timestamp: $(date)" | tee -a $RESULTS_FILE
    echo "========================================" | tee -a $RESULTS_FILE
    echo "" | tee -a $RESULTS_FILE

    # 시스템 정보
    echo "System Information:" | tee -a $RESULTS_FILE
    echo "  OS: $(uname -s)" | tee -a $RESULTS_FILE
    echo "  Kernel: $(uname -r)" | tee -a $RESULTS_FILE
    echo "  CPU: $(lscpu | grep 'Model name' | sed 's/Model name: *//')" | tee -a $RESULTS_FILE
    echo "  CPU Cores: $(nproc)" | tee -a $RESULTS_FILE
    echo "  Memory: $(free -h | grep Mem | awk '{print $2}')" | tee -a $RESULTS_FILE
    echo "" | tee -a $RESULTS_FILE

    # 클라이언트 확인
    if [ ! -f "$CLIENT_BIN" ]; then
        echo "Error: Client binary not found: $CLIENT_BIN" | tee -a $RESULTS_FILE
        exit 1
    fi

    # 서버 시작
    start_server

    # Stage 생성
    create_stages 1000

    # 초기 메트릭
    echo "Initial server metrics:" | tee -a $RESULTS_FILE
    get_metrics | jq '.' | tee -a $RESULTS_FILE || get_metrics | tee -a $RESULTS_FILE
    sleep 2

    # 테스트 시나리오
    echo "" | tee -a $RESULTS_FILE
    echo "Starting benchmark tests..." | tee -a $RESULTS_FILE

    # 다양한 메시지 크기 테스트
    run_test 1000 8 200 10 "Small Messages (8B)"
    run_test 1000 64 200 10 "Medium Messages (64B)"
    run_test 1000 256 200 10 "Large Messages (256B)"
    run_test 1000 1024 200 10 "Extra Large Messages (1KB)"

    # 다양한 연결 수 테스트
    echo "" | tee -a $RESULTS_FILE
    echo "Testing different connection counts..." | tee -a $RESULTS_FILE
    run_test 100 64 200 10 "100 Connections (64B)"
    run_test 500 64 200 10 "500 Connections (64B)"
    run_test 1000 64 200 10 "1000 Connections (64B)"

    # 완료
    echo "" | tee -a $RESULTS_FILE
    echo "========================================" | tee -a $RESULTS_FILE
    echo "Benchmark completed!" | tee -a $RESULTS_FILE
    echo "Results saved to: $RESULTS_FILE" | tee -a $RESULTS_FILE
    echo "========================================" | tee -a $RESULTS_FILE
}

main
