#!/bin/bash

# PlayHouse Benchmark 실행 스크립트
# 사용법: ./run-benchmark.sh [connections] [messages] [response-size] [mode]

# 기본값
CONNECTIONS=${1:-1}
MESSAGES=${2:-10000}
RESPONSE_SIZE=${3:-256}
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
dotnet build tests/benchmark/PlayHouse.Benchmark.Shared --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark/PlayHouse.Benchmark.Server --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark/PlayHouse.Benchmark.Client --configuration Release > /dev/null 2>&1

if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi

echo "[1/4] Build completed"

# 서버 시작
echo "[2/4] Starting benchmark server (port $SERVER_PORT, HTTP API port $HTTP_PORT)..."
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

echo "[2/4] Server started successfully"

# 클라이언트 실행
echo "[3/4] Running benchmark client..."
dotnet run --project tests/benchmark/PlayHouse.Benchmark.Client --configuration Release -- \
    --server 127.0.0.1:$SERVER_PORT \
    --connections $CONNECTIONS \
    --messages $MESSAGES \
    --response-size $RESPONSE_SIZE \
    --mode $MODE \
    --http-port $HTTP_PORT

CLIENT_EXIT_CODE=$?

# 서버 종료
echo ""
echo "[4/4] Stopping benchmark server..."
kill $SERVER_PID 2>/dev/null
wait $SERVER_PID 2>/dev/null

echo "[4/4] Server stopped"
echo ""
echo "================================================================================"
echo "Benchmark completed"
echo "Server log: /tmp/benchmark-server.log"
echo "================================================================================"

exit $CLIENT_EXIT_CODE
