#!/bin/bash

# PlayHouse SS Benchmark 실행 스크립트 (Play-to-Api 모드)
# 사용법: ./run-benchmark.sh [connections] [messages] [response-size] [mode]

# 스크립트 위치 기준으로 프로젝트 루트 찾기
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$PROJECT_ROOT"

# 기본값
CONNECTIONS=${1:-1000}
MESSAGES=${2:-10000}
RESPONSE_SIZE=${3:-1500}
MODE=${4:-play-to-api}

# 포트 설정
PLAY_TCP_PORT=16110
PLAY_ZMQ_PORT=16100
PLAY_HTTP_PORT=5080
API_ZMQ_PORT=16201

echo "================================================================================"
echo "PlayHouse SS Benchmark Execution Script (Play-to-Api Mode)"
echo "================================================================================"
echo "Configuration:"
echo "  Connections: $CONNECTIONS"
echo "  Messages per connection: $MESSAGES"
echo "  Response size: $RESPONSE_SIZE bytes"
echo "  Mode: $MODE"
echo "  PlayServer - TCP: $PLAY_TCP_PORT, ZMQ: $PLAY_ZMQ_PORT, HTTP: $PLAY_HTTP_PORT"
echo "  ApiServer  - ZMQ: $API_ZMQ_PORT"
echo "================================================================================"
echo ""

# 프로젝트 빌드
echo "[1/5] Building projects..."
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.Shared --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.PlayServer --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.ApiServer --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.Client --configuration Release > /dev/null 2>&1

if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi

echo "[1/5] Build completed"

# 기존 서버 프로세스 정리
echo "[2/5] Cleaning up existing server processes..."
echo "  Checking for existing servers..."
curl -s -X POST http://localhost:$PLAY_HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 && echo "  Sent shutdown to PlayServer port $PLAY_HTTP_PORT" || true
curl -s -X POST http://localhost:5081/benchmark/shutdown > /dev/null 2>&1 && echo "  Sent shutdown to ApiServer port 5081" || true
sleep 2

pkill -f "PlayHouse.Benchmark.SS.PlayServer" 2>/dev/null
pkill -f "PlayHouse.Benchmark.SS.ApiServer" 2>/dev/null
sleep 1

# 포트가 사용 중인지 확인 및 강제 종료
for PORT in $PLAY_TCP_PORT $PLAY_ZMQ_PORT $PLAY_HTTP_PORT $API_ZMQ_PORT; do
    if lsof -i :$PORT -t >/dev/null 2>&1; then
        echo "      Port $PORT still in use, force killing..."
        lsof -i :$PORT -t | xargs kill -9 2>/dev/null
        sleep 1
    fi
done

echo "[2/5] Cleanup completed"

# PlayServer 시작 (먼저 시작하여 ApiServer가 연결할 수 있도록)
echo "[3/5] Starting PlayServer (TCP port $PLAY_TCP_PORT, HTTP API port $PLAY_HTTP_PORT)..."
dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.PlayServer --configuration Release -- \
    --tcp-port $PLAY_TCP_PORT \
    --zmq-port $PLAY_ZMQ_PORT \
    --http-port $PLAY_HTTP_PORT > /tmp/benchmark-ss-playserver.log 2>&1 &

PLAY_SERVER_PID=$!
echo "      PlayServer PID: $PLAY_SERVER_PID"

# PlayServer 시작 대기
sleep 3

# PlayServer가 정상적으로 시작되었는지 확인
if ! ps -p $PLAY_SERVER_PID > /dev/null; then
    echo "      PlayServer failed to start! Check /tmp/benchmark-ss-playserver.log"
    exit 1
fi

echo "[3/5] PlayServer started successfully"

# ApiServer 시작
echo "[4/5] Starting ApiServer (ZMQ port $API_ZMQ_PORT)..."
dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.ApiServer --configuration Release -- \
    --zmq-port $API_ZMQ_PORT \
    --play-port $PLAY_ZMQ_PORT > /tmp/benchmark-ss-apiserver.log 2>&1 &

API_SERVER_PID=$!
echo "      ApiServer PID: $API_SERVER_PID"

# ApiServer 시작 대기
sleep 2

# ApiServer가 정상적으로 시작되었는지 확인
if ! ps -p $API_SERVER_PID > /dev/null; then
    echo "      ApiServer failed to start! Check /tmp/benchmark-ss-apiserver.log"
    echo "      Stopping PlayServer..."
    kill $PLAY_SERVER_PID 2>/dev/null
    exit 1
fi

echo "[4/5] ApiServer started successfully"

# 서버 간 연결 대기
echo "      Waiting for server connection (3 seconds)..."
sleep 3

# 클라이언트 실행
echo "[5/5] Running benchmark client..."
dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.Client --configuration Release -- \
    --server 127.0.0.1:$PLAY_TCP_PORT \
    --connections $CONNECTIONS \
    --messages $MESSAGES \
    --response-size $RESPONSE_SIZE \
    --mode $MODE \
    --http-port $PLAY_HTTP_PORT

CLIENT_EXIT_CODE=$?

# 클라이언트가 서버 종료를 처리하므로 여기서는 대기만 수행
echo ""
echo "Waiting for servers shutdown..."
sleep 3

# 혹시 남아있는 프로세스 정리
pkill -f "PlayHouse.Benchmark.SS.PlayServer" 2>/dev/null || true
pkill -f "PlayHouse.Benchmark.SS.ApiServer" 2>/dev/null || true

echo ""
echo "================================================================================"
echo "Benchmark completed"
echo "PlayServer log: /tmp/benchmark-ss-playserver.log"
echo "ApiServer log:  /tmp/benchmark-ss-apiserver.log"
echo "================================================================================"

exit $CLIENT_EXIT_CODE
