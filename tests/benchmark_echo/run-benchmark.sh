#!/bin/bash

# PlayHouse Echo Benchmark 실행 스크립트
# 사용법: ./run-benchmark.sh [connections] [duration]
# 기본값: 1000 connections, 10초, 모든 모드/페이로드 테스트

set -e

# 기본값
CONNECTIONS=${1:-1000}
DURATION=${2:-10}
SERVER_PORT=16110
HTTP_PORT=5080
ZMQ_PORT=16100

# 테스트 설정
PAYLOAD_SIZES="8,64,256,1024,65536"
MODES="send request-async request-callback"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

echo "================================================================================"
echo "PlayHouse Echo Benchmark - Full Test Suite"
echo "================================================================================"
echo "Configuration:"
echo "  Connections: $CONNECTIONS"
echo "  Duration: $DURATION seconds"
echo "  Payload sizes: $PAYLOAD_SIZES"
echo "  Modes: $MODES"
echo "================================================================================"
echo ""

cd "$PROJECT_ROOT"

# 1. 빌드
echo "[1/4] Building projects..."
dotnet build tests/benchmark_echo/PlayHouse.Benchmark.Echo.Server -c Release --no-incremental -v q
dotnet build tests/benchmark_echo/PlayHouse.Benchmark.Echo.Client -c Release --no-incremental -v q

if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi
echo "[1/4] Build completed"

# 2. 기존 서버 정리
cleanup_server() {
    echo "Cleaning up existing servers..."
    curl -s -X POST http://localhost:$HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 || true
    sleep 1
    pkill -9 -f "PlayHouse.Benchmark.Echo.Server" 2>/dev/null || true
    sleep 2
}

# 3. 서버 시작
start_server() {
    echo "[2/4] Starting echo benchmark server..."
    dotnet run --project tests/benchmark_echo/PlayHouse.Benchmark.Echo.Server -c Release --no-build -- \
        --tcp-port $SERVER_PORT \
        --zmq-port $ZMQ_PORT \
        --http-port $HTTP_PORT > /tmp/echo-benchmark-server.log 2>&1 &

    SERVER_PID=$!
    echo "      Server PID: $SERVER_PID"

    # 서버 시작 대기
    for i in {1..10}; do
        if curl -s http://localhost:$HTTP_PORT/benchmark/stats > /dev/null 2>&1; then
            echo "[2/4] Server started successfully"
            return 0
        fi
        sleep 1
    done

    echo "Server failed to start! Check /tmp/echo-benchmark-server.log"
    exit 1
}

# 결과 저장 디렉토리
RESULTS_DIR="$SCRIPT_DIR/benchmark-results/$(date +%Y%m%d_%H%M%S)"
mkdir -p "$RESULTS_DIR"

# 전체 결과 요약 파일
SUMMARY_FILE="$RESULTS_DIR/summary.txt"

echo "Results will be saved to: $RESULTS_DIR"
echo ""

# 헤더 출력
{
    echo "================================================================================"
    echo "PlayHouse Echo Benchmark Results"
    echo "Date: $(date)"
    echo "Connections: $CONNECTIONS, Duration: ${DURATION}s"
    echo "================================================================================"
    echo ""
    printf "%-8s | %-16s | %12s | %8s | %10s | %15s\n" "Payload" "Mode" "Server TPS" "P99" "Memory" "GC (Gen0/1/2)"
    printf "%-8s-+-%-16s-+-%12s-+-%8s-+-%10s-+-%15s\n" "--------" "----------------" "------------" "--------" "----------" "---------------"
} | tee "$SUMMARY_FILE"

# 서버 정리 및 시작
cleanup_server
start_server

# 4. 각 모드별 테스트 실행
for MODE in $MODES; do
    echo ""
    echo "========================================"
    echo "Testing Mode: $MODE"
    echo "========================================"

    # 테스트 실행
    OUTPUT=$(dotnet run --project tests/benchmark_echo/PlayHouse.Benchmark.Echo.Client -c Release --no-build -- \
        --server 127.0.0.1:$SERVER_PORT \
        --connections $CONNECTIONS \
        --duration $DURATION \
        --payload-size $PAYLOAD_SIZES \
        --mode $MODE \
        --http-port $HTTP_PORT \
        --times 200 2>&1)

    echo "$OUTPUT"

    # 결과 파싱 및 요약 추가
    echo "$OUTPUT" | grep -E "^\[.*\]\s+[0-9]+B\s+\|" | while read line; do
        PAYLOAD=$(echo "$line" | sed 's/.*\] *//' | awk -F'|' '{print $1}' | tr -d ' ')
        SRV_TPS=$(echo "$line" | awk -F'|' '{print $3}' | tr -d ' ')
        SRV_P99=$(echo "$line" | awk -F'|' '{print $4}' | tr -d ' ')
        SRV_MEM=$(echo "$line" | awk -F'|' '{print $5}' | tr -d ' ')
        SRV_GC=$(echo "$line" | awk -F'|' '{print $6}' | tr -d ' ')

        printf "%-8s | %-16s | %12s | %8s | %10s | %15s\n" "$PAYLOAD" "$MODE" "$SRV_TPS" "$SRV_P99" "$SRV_MEM" "$SRV_GC" >> "$SUMMARY_FILE"
    done

    # 다음 테스트 전 대기
    sleep 3
done

# 서버 종료
echo ""
echo "Shutting down server..."
curl -s -X POST http://localhost:$HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 || true
sleep 2

echo ""
echo "================================================================================"
echo "Benchmark Complete!"
echo "================================================================================"
echo "Results saved to: $RESULTS_DIR"
echo ""
cat "$SUMMARY_FILE"
echo "================================================================================"
