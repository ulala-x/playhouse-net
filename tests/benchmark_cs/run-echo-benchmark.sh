#!/bin/bash

# PlayHouse Echo Benchmark (Zero-Copy, Time-based)
# 사용법: ./run-echo-benchmark.sh [connections] [duration]

set -e

# 스크립트 디렉토리 기준 경로 설정
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# 기본값
CONNECTIONS=${1:-10}
DURATION=${2:-10}
SERVER_PORT=16110
HTTP_PORT=5080

# 페이로드 크기 배열
PAYLOAD_SIZES=(64 256 1024 65536)

echo "================================================================================"
echo "PlayHouse Echo Benchmark (Zero-Copy, Time-based)"
echo "================================================================================"
echo "Configuration:"
echo "  Connections: $CONNECTIONS"
echo "  Duration: ${DURATION}s per test"
echo "  Payload sizes: ${PAYLOAD_SIZES[@]} bytes"
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

# 클라이언트 실행 - 각 페이로드 크기별로 실행
echo "[4/4] Running Echo benchmarks..."
echo ""

for SIZE in "${PAYLOAD_SIZES[@]}"; do
    echo "================================================================================"
    echo ">>> Payload size: ${SIZE} bytes <<<"
    echo "================================================================================"

    dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.Client/PlayHouse.Benchmark.Client.csproj" -c Release -- \
        --server 127.0.0.1:$SERVER_PORT \
        --connections $CONNECTIONS \
        --mode echo \
        --duration $DURATION \
        --request-size $SIZE \
        --http-port $HTTP_PORT

    # 서버 안정화 대기 (마지막 테스트가 아닐 때만)
    if [ $SIZE != ${PAYLOAD_SIZES[-1]} ]; then
        echo ""
        echo "Waiting for server stabilization..."
        sleep 2
        echo ""
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
echo "Echo Benchmark completed"
echo "================================================================================"
