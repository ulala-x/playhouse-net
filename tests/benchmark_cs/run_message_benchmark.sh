#!/bin/bash

# PlayHouse Benchmark - Message Count Based Test
# 사용법: ./run_message_benchmark.sh [connections] [messages]
# 각 모드(RequestAsync, RequestCallback, Send)를 순차적으로 실행하여 message-count 기반으로 테스트합니다.

set -e

# 스크립트 디렉토리 기준 경로 설정
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# 기본값
CONNECTIONS=${1:-10000}
MESSAGES=${2:-1000}
SERVER_PORT=16110
HTTP_PORT=5080

# Echo 페이로드 크기 (배열)
PAYLOAD_SIZES=(64 256 1024 65536 131072)

# 테스트 모드
MODES=("request-async" "request-callback" "send")

echo "================================================================================"
echo "PlayHouse Benchmark - Message Count Based Test"
echo "================================================================================"
echo "Configuration:"
echo "  Connections: $CONNECTIONS"
echo "  Messages per mode: $MESSAGES"
echo "  Modes: ${MODES[@]}"
echo "  Payload sizes: ${PAYLOAD_SIZES[*]} bytes (Echo: request=response)"
echo "================================================================================"
echo ""

# 프로젝트 빌드
echo "[1/4] Building projects..."
dotnet build "$SCRIPT_DIR/PlayHouse.Benchmark.Server/PlayHouse.Benchmark.Server.csproj" -c Release --verbosity quiet
dotnet build "$SCRIPT_DIR/PlayHouse.Benchmark.Client/PlayHouse.Benchmark.Client.csproj" -c Release --verbosity quiet
echo "[1/4] Build completed"

# 기존 서버 프로세스 정리
echo "[2/4] Cleaning up existing servers..."
curl -s -m 2 -X POST http://localhost:$HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 || true
pkill -9 -f "PlayHouse.Benchmark.Server" 2>/dev/null || true
pkill -9 -f "PlayHouse.Benchmark.Client" 2>/dev/null || true
sleep 1

# 서버 시작
echo "[3/4] Starting benchmark server (port $SERVER_PORT, HTTP API port $HTTP_PORT)..."
dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.Server/PlayHouse.Benchmark.Server.csproj" -c Release -- \
    --tcp-port $SERVER_PORT \
    --http-port $HTTP_PORT > /tmp/benchmark-server.log 2>&1 &

SERVER_PID=$!

# 서버 시작 대기
max_wait=30
waited=0
while ! curl -s "http://localhost:$HTTP_PORT/benchmark/stats" > /dev/null 2>&1; do
    sleep 1
    waited=$((waited + 1))
    if [ $waited -ge $max_wait ]; then
        echo "Server failed to start within ${max_wait}s"
        cat /tmp/benchmark-server.log
        exit 1
    fi
done

echo "[3/4] Server started (PID: $SERVER_PID)"

# 각 모드별로 벤치마크 실행
echo "[4/4] Running benchmarks..."
echo ""

for MODE in "${MODES[@]}"; do
    echo "================================================================================"
    echo "Running benchmark: mode=$MODE"
    echo "================================================================================"

    for SIZE in "${PAYLOAD_SIZES[@]}"; do
        echo ""
        echo ">>> Echo test: ${SIZE} bytes (request=${SIZE}, response=${SIZE}) <<<"

        dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.Client/PlayHouse.Benchmark.Client.csproj" -c Release -- \
            --server 127.0.0.1:$SERVER_PORT \
            --connections $CONNECTIONS \
            --mode $MODE \
            --messages $MESSAGES \
            --request-size $SIZE \
            --response-size $SIZE \
            --http-port $HTTP_PORT

        # 테스트 간 간격
        sleep 1
    done

    echo ""
    echo "Completed: $MODE"
    echo ""

    # 모드 간 간격 (서버 안정화)
    if [ "$MODE" != "${MODES[-1]}" ]; then
        echo "Waiting for server stabilization..."
        sleep 2
    fi
done

# 정리
echo ""
echo "Cleaning up..."
curl -s -m 2 -X POST http://localhost:$HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 || true
pkill -9 -f "PlayHouse.Benchmark.Server" 2>/dev/null || true
sleep 1

echo ""
echo "================================================================================"
echo "Message-count-based benchmark completed"
echo "================================================================================"
