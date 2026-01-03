#!/bin/bash
# PlayHouse C# Echo 간단 벤치마크
# 각 테스트마다 서버를 재시작

set -e

# 설정
SERVER_DIR="./PlayHouse.Benchmark.Echo.Server"
CLIENT_DIR="./PlayHouse.Benchmark.Echo.Client"
TCP_PORT=16110
ZMQ_PORT=16100
HTTP_PORT=5080

# 색상
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m'

# 포트 정리
cleanup_ports() {
    fuser -k $ZMQ_PORT/tcp 2>/dev/null || true
    fuser -k $TCP_PORT/tcp 2>/dev/null || true
    fuser -k $HTTP_PORT/tcp 2>/dev/null || true
    sleep 2
}

# 테스트 실행
run_single_test() {
    local connections=$1
    local msg_size=$2
    local times=$3
    local duration=$4
    local test_name=$5

    echo -e "${GREEN}=====================================${NC}"
    echo -e "${GREEN}Test: $test_name${NC}"
    echo -e "${GREEN}  CCU: $connections, Size: $msg_size B, Duration: ${duration}s${NC}"
    echo -e "${GREEN}=====================================${NC}"

    # 클라이언트 실행 (서버 자동 시작/종료)
    cd $CLIENT_DIR
    dotnet run -c Release -- \
        --connections $connections \
        --duration $duration \
        --payload-size $msg_size \
        --times $times \
        --http-port $HTTP_PORT \
        --base-stage-id 10000 \
        --mode request-async \
        --output-dir benchmark-results \
        --label "$test_name"
    cd - > /dev/null

    # 서버 정리 및 대기
    cleanup_ports
    sleep 3
}

# 메인
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}PlayHouse Echo Benchmark${NC}"
echo -e "${GREEN}========================================${NC}"

cleanup_ports

# 테스트 실행
run_single_test 1000 8 200 10 "8B"
run_single_test 1000 64 200 10 "64B"
run_single_test 1000 256 200 10 "256B"
run_single_test 1000 1024 200 10 "1KB"

echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}All tests completed!${NC}"
echo -e "${GREEN}Results: $CLIENT_DIR/benchmark-results/${NC}"
echo -e "${GREEN}========================================${NC}"
