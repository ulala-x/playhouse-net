#!/bin/bash

# PlayHouse Benchmark 통합 테스트 스크립트
# 여러 응답 크기로 순차 테스트 실행

# 기본 설정
CCU=${1:-10}
MSG_PER_CONN=${2:-1000}
REQUEST_SIZE=${3:-64}
SERVER_PORT=16110
HTTP_PORT=5080

# 테스트할 응답 크기들
RESPONSE_SIZES=(256 1500 65536)

echo "================================================================================"
echo "PlayHouse Benchmark Test Suite"
echo "================================================================================"
echo "Configuration:"
echo "  Concurrent Users (CCU): $CCU"
echo "  Messages per connection: $MSG_PER_CONN"
echo "  Request size: $REQUEST_SIZE bytes"
echo "  Response sizes: ${RESPONSE_SIZES[*]} bytes"
echo "================================================================================"
echo ""

# 프로젝트 빌드
echo "[1/5] Building projects..."
cd /home/ulalax/project/ulalax/playhouse/playhouse-net

dotnet build tests/benchmark/PlayHouse.Benchmark.Shared --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark/PlayHouse.Benchmark.Server --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark/PlayHouse.Benchmark.Client --configuration Release > /dev/null 2>&1

if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi

echo "[1/5] Build completed"
echo ""

# 기존 서버 프로세스 정리
echo "[2/5] Cleaning up existing server processes..."
pkill -f "PlayHouse.Benchmark.Server" 2>/dev/null
sleep 2

# 포트가 사용 중인지 확인 (TCP: 16110, ZMQ: 16100)
ZMQ_PORT=16100
for PORT in $SERVER_PORT $ZMQ_PORT; do
    if lsof -i :$PORT -t >/dev/null 2>&1; then
        echo "      Port $PORT still in use, force killing..."
        lsof -i :$PORT -t | xargs kill -9 2>/dev/null
        sleep 1
    fi
done

# 서버 시작
echo "[2/5] Starting benchmark server (port $SERVER_PORT, HTTP API port $HTTP_PORT)..."
dotnet run --project tests/benchmark/PlayHouse.Benchmark.Server --configuration Release -- \
    --tcp-port $SERVER_PORT \
    --http-port $HTTP_PORT > /tmp/benchmark-server.log 2>&1 &

SERVER_PID=$!
echo "      Server PID: $SERVER_PID"

# 서버 시작 대기
sleep 3

# 서버가 정상적으로 시작되었는지 확인
if ! ps -p $SERVER_PID > /dev/null; then
    echo "      Server failed to start! Check /tmp/benchmark-server.log"
    exit 1
fi

echo "[2/5] Server started successfully"
echo ""

# 결과 저장을 위한 배열
declare -a RESULTS

# 각 응답 크기별로 테스트 실행
echo "[3/5] Running benchmark tests..."
TEST_COUNT=0
TOTAL_TESTS=${#RESPONSE_SIZES[@]}

for RESPONSE_SIZE in "${RESPONSE_SIZES[@]}"; do
    TEST_COUNT=$((TEST_COUNT + 1))
    echo ""
    echo "--------------------------------------------------------------------------------"
    echo "Test $TEST_COUNT/$TOTAL_TESTS: Response size = $RESPONSE_SIZE bytes"
    echo "--------------------------------------------------------------------------------"

    # 테스트 실행
    OUTPUT=$(dotnet run --project tests/benchmark/PlayHouse.Benchmark.Client --configuration Release -- \
        --server 127.0.0.1:$SERVER_PORT \
        --connections $CCU \
        --messages $MSG_PER_CONN \
        --request-size $REQUEST_SIZE \
        --response-size $RESPONSE_SIZE \
        --mode request-async \
        --http-port $HTTP_PORT 2>&1)

    CLIENT_EXIT_CODE=$?

    if [ $CLIENT_EXIT_CODE -ne 0 ]; then
        echo "Client test failed for response size $RESPONSE_SIZE bytes"
        RESULTS[$TEST_COUNT]="$RESPONSE_SIZE bytes: FAILED"
    else
        # 결과 파싱 (TPS 추출)
        TPS=$(echo "$OUTPUT" | grep -oP 'TPS:\s+\K[\d,]+' | tail -1)
        LATENCY=$(echo "$OUTPUT" | grep -oP 'Average latency:\s+\K[\d.]+' | tail -1)

        if [ -n "$TPS" ] && [ -n "$LATENCY" ]; then
            RESULTS[$TEST_COUNT]="$RESPONSE_SIZE bytes: $TPS TPS, ${LATENCY}ms avg latency"
        else
            RESULTS[$TEST_COUNT]="$RESPONSE_SIZE bytes: COMPLETED (metrics unavailable)"
        fi

        echo "$OUTPUT"
    fi

    # 다음 테스트 전 메트릭 리셋 (마지막 테스트가 아닌 경우)
    if [ $TEST_COUNT -lt $TOTAL_TESTS ]; then
        echo ""
        echo "Resetting metrics..."
        curl -s -X POST http://localhost:$HTTP_PORT/benchmark/reset > /dev/null 2>&1
        sleep 1
    fi
done

echo ""
echo "[3/5] All tests completed"
echo ""

# 서버 종료
echo "[4/5] Stopping benchmark server..."
kill $SERVER_PID 2>/dev/null
sleep 2

# 서버가 아직 실행 중이면 강제 종료
if ps -p $SERVER_PID > /dev/null 2>&1; then
    echo "      Server still running, force killing..."
    kill -9 $SERVER_PID 2>/dev/null
fi

# 좀비 프로세스 정리
pkill -f "PlayHouse.Benchmark.Server" 2>/dev/null

echo "[4/5] Server stopped"
echo ""

# 결과 요약
echo "[5/5] Test Results Summary"
echo "================================================================================"
echo "Configuration:"
echo "  CCU: $CCU"
echo "  Messages per connection: $MSG_PER_CONN"
echo "  Request size: $REQUEST_SIZE bytes"
echo ""
echo "Results:"
for i in "${!RESULTS[@]}"; do
    echo "  $i. ${RESULTS[$i]}"
done
echo ""
echo "Server log: /tmp/benchmark-server.log"
echo "================================================================================"

exit 0
