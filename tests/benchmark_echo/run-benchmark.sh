c#!/bin/bash

# PlayHouse Echo Benchmark 실행 스크립트
# 사용법: ./run-benchmark.sh [connections] [duration] [payload-size] [mode]

# 기본값
CONNECTIONS=${1:-10000}
DURATION=${2:-10}
PAYLOAD_SIZE=${3:-8}
MODE=${4:-request-async}
SERVER_PORT=16110
HTTP_PORT=5080

echo "================================================================================"
echo "PlayHouse Echo Benchmark Execution Script"
echo "================================================================================"
echo "Configuration:"
echo "  Connections: $CONNECTIONS"
echo "  Duration: $DURATION seconds"
echo "  Payload size: $PAYLOAD_SIZE bytes"
echo "  Mode: $MODE"
echo "================================================================================"
echo ""

# 1. 빌드
echo "[1/4] Cleaning and building projects..."
dotnet clean tests/benchmark_echo/PlayHouse.Benchmark.Echo.Shared --configuration Release > /dev/null 2>&1
dotnet clean tests/benchmark_echo/PlayHouse.Benchmark.Echo.Server --configuration Release > /dev/null 2>&1
dotnet clean tests/benchmark_echo/PlayHouse.Benchmark.Echo.Client --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_echo/PlayHouse.Benchmark.Echo.Shared --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_echo/PlayHouse.Benchmark.Echo.Server --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_echo/PlayHouse.Benchmark.Echo.Client --configuration Release > /dev/null 2>&1

if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi

echo "[1/4] Build completed"

# 2. 기존 서버 정리
echo "[2/4] Cleaning up existing servers..."
curl -s -X POST http://localhost:$HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 || true
sleep 2
pkill -f "PlayHouse.Benchmark.Echo.Server" 2>/dev/null
sleep 1
ZMQ_PORT=16100
for PORT in $SERVER_PORT $ZMQ_PORT; do
    if lsof -i :$PORT -t >/dev/null 2>&1; then
        lsof -i :$PORT -t | xargs kill -9 2>/dev/null
        sleep 1
    fi
done

# 3. 서버 시작
echo "[3/4] Starting echo benchmark server (port $SERVER_PORT, HTTP API port $HTTP_PORT)..."
dotnet run --project tests/benchmark_echo/PlayHouse.Benchmark.Echo.Server --configuration Release -- \
    --tcp-port $SERVER_PORT \
    --http-port $HTTP_PORT > /tmp/echo-benchmark-server.log 2>&1 &

SERVER_PID=$!
echo "      Server PID: $SERVER_PID"

sleep 3

if ! ps -p $SERVER_PID > /dev/null; then
    echo "      Server failed to start! Check /tmp/echo-benchmark-server.log"
    exit 1
fi

echo "[3/4] Server started successfully"

# 4. 클라이언트 실행
echo "[4/4] Running echo benchmark client..."
dotnet run --project tests/benchmark_echo/PlayHouse.Benchmark.Echo.Client --configuration Release -- \
    --server 127.0.0.1:$SERVER_PORT \
    --connections $CONNECTIONS \
    --duration $DURATION \
    --payload-size $PAYLOAD_SIZE \
    --mode $MODE \
    --http-port $HTTP_PORT

CLIENT_EXIT_CODE=$?

echo ""
echo "Waiting for server shutdown..."
sleep 3

pkill -f "PlayHouse.Benchmark.Echo.Server" 2>/dev/null || true

echo ""
echo "================================================================================"
echo "Echo Benchmark completed"
echo "Server log: /tmp/echo-benchmark-server.log"
echo "================================================================================"

exit $CLIENT_EXIT_CODE
