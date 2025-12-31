#!/bin/bash

# PlayHouse Benchmark 통합 테스트 스크립트
# 여러 응답 크기로 순차 테스트 실행 (각 테스트마다 서버 재시작)

# 기본 설정
CCU=${1:-10}
MSG_PER_CONN=${2:-1000}
REQUEST_SIZE=${3:-64}
SERVER_PORT=16110
HTTP_PORT=5080

# 테스트할 응답 크기들 (공백으로 구분)
RESPONSE_SIZES="256 1500 65536"

# 테스트할 모드들
MODES="request-async request-callback"

echo "================================================================================"
echo "PlayHouse Benchmark Test Suite"
echo "================================================================================"
echo "Configuration:"
echo "  Concurrent Users (CCU): $CCU"
echo "  Messages per connection: $MSG_PER_CONN"
echo "  Request size: $REQUEST_SIZE bytes"
echo "  Response sizes: $RESPONSE_SIZES bytes"
echo "  Modes: $MODES"
echo "================================================================================"
echo ""

# 프로젝트 빌드
echo "[1/4] Building projects..."
cd /home/ulalax/project/ulalax/playhouse/playhouse-net

dotnet build tests/benchmark_cs/PlayHouse.Benchmark.Shared --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_cs/PlayHouse.Benchmark.Server --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_cs/PlayHouse.Benchmark.Client --configuration Release > /dev/null 2>&1

if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi

echo "[1/4] Build completed"
echo ""

# 기존 서버 프로세스 정리 함수
cleanup_server() {
    echo "  Cleaning up server processes..."
    curl -s -X POST http://localhost:$HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 || true
    sleep 2
    pkill -f "PlayHouse.Benchmark.Server" 2>/dev/null || true
    sleep 1

    # 포트가 사용 중인지 확인
    ZMQ_PORT=16100
    for PORT in $SERVER_PORT $ZMQ_PORT $HTTP_PORT; do
        if lsof -i :$PORT -t >/dev/null 2>&1; then
            echo "      Force killing process on port $PORT..."
            lsof -i :$PORT -t | xargs kill -9 2>/dev/null || true
            sleep 1
        fi
    done
}

# 서버 시작 함수
start_server() {
    echo "  Starting server (TCP: $SERVER_PORT, HTTP: $HTTP_PORT)..."
    dotnet run --project tests/benchmark_cs/PlayHouse.Benchmark.Server --configuration Release -- \
        --tcp-port $SERVER_PORT \
        --http-port $HTTP_PORT > /tmp/benchmark-server.log 2>&1 &

    SERVER_PID=$!
    echo "      Server PID: $SERVER_PID"

    # 서버 시작 대기
    sleep 3

    # 서버가 정상적으로 시작되었는지 확인
    if ! ps -p $SERVER_PID > /dev/null; then
        echo "      Server failed to start! Check /tmp/benchmark-server.log"
        return 1
    fi

    echo "      Server started successfully"
    return 0
}

# 초기 정리
echo "[2/4] Initial cleanup..."
cleanup_server
echo "[2/4] Cleanup completed"
echo ""

# 각 모드/응답 크기별로 테스트 실행
echo "[3/4] Running benchmark tests..."
echo ""

TEST_NUM=1
NUM_MODES=$(echo $MODES | wc -w)
NUM_SIZES=$(echo $RESPONSE_SIZES | wc -w)
TOTAL_TESTS=$((NUM_MODES * NUM_SIZES))
OVERALL_EXIT_CODE=0

for MODE in $MODES; do
    for RESP_SIZE in $RESPONSE_SIZES; do
        echo "================================================================================
"
        echo "Test $TEST_NUM/$TOTAL_TESTS: Mode = $MODE, Response Size = $RESP_SIZE bytes"
        echo "================================================================================"

        # 서버 시작
        start_server
        if [ $? -ne 0 ]; then
            echo "ERROR: Failed to start server for test $TEST_NUM"
            OVERALL_EXIT_CODE=1
            break 2
        fi

        # 테스트 실행
        dotnet run --project tests/benchmark_cs/PlayHouse.Benchmark.Client --configuration Release -- \
            --server 127.0.0.1:$SERVER_PORT \
            --connections $CCU \
            --messages $MSG_PER_CONN \
            --request-size $REQUEST_SIZE \
            --response-size $RESP_SIZE \
            --mode $MODE \
            --http-port $HTTP_PORT \
            --label "${MODE}_resp${RESP_SIZE}"

        CLIENT_EXIT_CODE=$?
        if [ $CLIENT_EXIT_CODE -ne 0 ]; then
            echo "ERROR: Test failed for mode $MODE, response size $RESP_SIZE"
            OVERALL_EXIT_CODE=1
        fi

        # 서버 종료
        cleanup_server

        # 다음 테스트 전 대기
        if [ $TEST_NUM -lt $TOTAL_TESTS ]; then
            echo "Waiting before next test..."
            sleep 2
            echo ""
        fi

        TEST_NUM=$((TEST_NUM + 1))
    done
done

echo ""
echo "[3/4] All tests completed"
echo ""

# 최종 정리
echo "[4/4] Final cleanup..."
cleanup_server
echo "[4/4] Cleanup completed"
echo ""

# 결과 요약
echo "================================================================================"
echo "Test Results Summary"
echo "================================================================================"
echo "Configuration:"
echo "  CCU: $CCU"
echo "  Messages per connection: $MSG_PER_CONN"
echo "  Request size: $REQUEST_SIZE bytes"
echo "  Response sizes tested: $RESPONSE_SIZES bytes"
echo "  Modes tested: $MODES"
echo "  Total tests: $TOTAL_TESTS"
echo ""
echo "Server log: /tmp/benchmark-server.log"
echo "Client logs: tests/benchmark_cs/PlayHouse.Benchmark.Client/benchmark-results/"
echo "================================================================================"

exit $OVERALL_EXIT_CODE
