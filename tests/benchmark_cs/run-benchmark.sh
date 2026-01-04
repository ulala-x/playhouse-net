#!/bin/bash

# PlayHouse Benchmark 실행 스크립트
# 사용법: ./run-benchmark.sh [connections] [messages] [response-size] [mode]

set -e

# 스크립트 디렉토리 기준 경로 설정
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# 기본값
CONNECTIONS=${1:-100}
MESSAGES=${2:-100}
RESPONSE_SIZE=${3:-1024}
MODE=${4:-request-async}
SERVER_PORT=16110
HTTP_PORT=5080

echo "================================================================================"
echo "PlayHouse Benchmark Execution Script"
echo "================================================================================"
echo "Configuration:"
echo "  Connections: $CONNECTIONS"
echo "  Messages per connection: $MESSAGES"
echo "  Response size: $RESPONSE_SIZE bytes"
echo "  Mode: $MODE"
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

# 클라이언트 실행
echo "[4/4] Running benchmark client..."
dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.Client/PlayHouse.Benchmark.Client.csproj" -c Release -- \
    --server 127.0.0.1:$SERVER_PORT \
    --connections $CONNECTIONS \
    --messages $MESSAGES \
    --response-size $RESPONSE_SIZE \
    --mode $MODE \
    --http-port $HTTP_PORT

# 정리
echo ""
echo "Cleaning up..."
pkill -9 -f "PlayHouse.Benchmark.Server" 2>/dev/null || true
sleep 1

echo ""
echo "================================================================================"
echo "Benchmark completed"
echo "================================================================================"
