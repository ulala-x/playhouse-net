#!/bin/bash

# PlayHouse SS Benchmark 실행 스크립트 (ALL 모드)
# 4개 서버 (PlayServer 2개 + ApiServer 2개)로 모든 벤치마크 모드 테스트
# 사용법: ./run-benchmark-all.sh [connections] [messages] [response-sizes]
#   response-sizes: 콤마로 구분된 응답 크기 (예: "256,65536")

# 스크립트 위치 기준으로 프로젝트 루트 찾기
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$PROJECT_ROOT"

# 기본값
CONNECTIONS=${1:-1000}
MESSAGES=${2:-10000}
RESPONSE_SIZES=${3:-"1500,65536"}

# 포트 설정
PLAY1_TCP_PORT=16110
PLAY1_ZMQ_PORT=16100
PLAY1_HTTP_PORT=5080

PLAY2_TCP_PORT=16120
PLAY2_ZMQ_PORT=16200
PLAY2_HTTP_PORT=5082

API1_ZMQ_PORT=16201
API1_HTTP_PORT=5081

API2_ZMQ_PORT=16301
API2_HTTP_PORT=5083

echo "================================================================================"
echo "PlayHouse SS Benchmark Execution Script (ALL Mode)"
echo "================================================================================"
echo "Configuration:"
echo "  Connections: $CONNECTIONS"
echo "  Messages per connection: $MESSAGES"
echo "  Response sizes: $RESPONSE_SIZES bytes"
echo "  Mode: all (play-to-stage, play-to-api, api-to-api)"
echo ""
echo "Server Configuration:"
echo "  PlayServer 1 - TCP: $PLAY1_TCP_PORT, ZMQ: $PLAY1_ZMQ_PORT, HTTP: $PLAY1_HTTP_PORT"
echo "  PlayServer 2 - TCP: $PLAY2_TCP_PORT, ZMQ: $PLAY2_ZMQ_PORT, HTTP: $PLAY2_HTTP_PORT"
echo "  ApiServer  1 - ZMQ: $API1_ZMQ_PORT, HTTP: $API1_HTTP_PORT"
echo "  ApiServer  2 - ZMQ: $API2_ZMQ_PORT, HTTP: $API2_HTTP_PORT"
echo "================================================================================"
echo ""

# 프로젝트 빌드
echo "[1/7] Building projects..."
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.Shared --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.PlayServer --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.ApiServer --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.Client --configuration Release > /dev/null 2>&1

if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi

echo "[1/7] Build completed"

# 기존 서버 프로세스 정리
echo "[2/7] Cleaning up existing server processes..."
echo "  Checking for existing servers..."
curl -s -X POST http://localhost:$PLAY1_HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 && echo "  Sent shutdown to PlayServer 1 port $PLAY1_HTTP_PORT" || true
curl -s -X POST http://localhost:$PLAY2_HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 && echo "  Sent shutdown to PlayServer 2 port $PLAY2_HTTP_PORT" || true
curl -s -X POST http://localhost:$API1_HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 && echo "  Sent shutdown to ApiServer 1 port $API1_HTTP_PORT" || true
curl -s -X POST http://localhost:$API2_HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 && echo "  Sent shutdown to ApiServer 2 port $API2_HTTP_PORT" || true
sleep 2

pkill -f "PlayHouse.Benchmark.SS.PlayServer" 2>/dev/null
pkill -f "PlayHouse.Benchmark.SS.ApiServer" 2>/dev/null
sleep 1

# 포트가 사용 중인지 확인 및 강제 종료
for PORT in $PLAY1_TCP_PORT $PLAY1_ZMQ_PORT $PLAY1_HTTP_PORT $PLAY2_TCP_PORT $PLAY2_ZMQ_PORT $PLAY2_HTTP_PORT $API1_ZMQ_PORT $API1_HTTP_PORT $API2_ZMQ_PORT $API2_HTTP_PORT; do
    if lsof -i :$PORT -t >/dev/null 2>&1; then
        echo "      Port $PORT still in use, force killing..."
        lsof -i :$PORT -t | xargs kill -9 2>/dev/null
        sleep 1
    fi
done

echo "[2/7] Cleanup completed"

# 전체 서버 목록 (자기 자신 포함하여 같은 서버 내 Stage 간 통신 가능)
ALL_PEERS="play-1=tcp://127.0.0.1:$PLAY1_ZMQ_PORT,play-2=tcp://127.0.0.1:$PLAY2_ZMQ_PORT,api-1=tcp://127.0.0.1:$API1_ZMQ_PORT,api-2=tcp://127.0.0.1:$API2_ZMQ_PORT"

# PlayServer 1 시작
echo "[3/7] Starting PlayServer 1 (TCP port $PLAY1_TCP_PORT, HTTP API port $PLAY1_HTTP_PORT)..."
dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.PlayServer --configuration Release -- \
    --tcp-port $PLAY1_TCP_PORT \
    --zmq-port $PLAY1_ZMQ_PORT \
    --http-port $PLAY1_HTTP_PORT \
    --server-id play-1 \
    --peers "$ALL_PEERS" > /tmp/benchmark-ss-playserver1.log 2>&1 &

PLAY_SERVER1_PID=$!
echo "      PlayServer 1 PID: $PLAY_SERVER1_PID"

# PlayServer 1 시작 대기
sleep 3

# PlayServer 1이 정상적으로 시작되었는지 확인
if ! ps -p $PLAY_SERVER1_PID > /dev/null; then
    echo "      PlayServer 1 failed to start! Check /tmp/benchmark-ss-playserver1.log"
    exit 1
fi

echo "[3/7] PlayServer 1 started successfully"

# PlayServer 2 시작
echo "[4/7] Starting PlayServer 2 (TCP port $PLAY2_TCP_PORT, HTTP API port $PLAY2_HTTP_PORT)..."
dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.PlayServer --configuration Release -- \
    --tcp-port $PLAY2_TCP_PORT \
    --zmq-port $PLAY2_ZMQ_PORT \
    --http-port $PLAY2_HTTP_PORT \
    --server-id play-2 \
    --peers "$ALL_PEERS" > /tmp/benchmark-ss-playserver2.log 2>&1 &

PLAY_SERVER2_PID=$!
echo "      PlayServer 2 PID: $PLAY_SERVER2_PID"

# PlayServer 2 시작 대기
sleep 3

# PlayServer 2가 정상적으로 시작되었는지 확인
if ! ps -p $PLAY_SERVER2_PID > /dev/null; then
    echo "      PlayServer 2 failed to start! Check /tmp/benchmark-ss-playserver2.log"
    echo "      Stopping PlayServer 1..."
    kill $PLAY_SERVER1_PID 2>/dev/null
    exit 1
fi

echo "[4/7] PlayServer 2 started successfully"

# ApiServer 1 시작
echo "[5/7] Starting ApiServer 1 (ZMQ port $API1_ZMQ_PORT, HTTP port $API1_HTTP_PORT)..."
dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.ApiServer --configuration Release -- \
    --zmq-port $API1_ZMQ_PORT \
    --http-port $API1_HTTP_PORT \
    --server-id api-1 \
    --peers "$ALL_PEERS" > /tmp/benchmark-ss-apiserver1.log 2>&1 &

API_SERVER1_PID=$!
echo "      ApiServer 1 PID: $API_SERVER1_PID"

# ApiServer 1 시작 대기
sleep 2

# ApiServer 1이 정상적으로 시작되었는지 확인
if ! ps -p $API_SERVER1_PID > /dev/null; then
    echo "      ApiServer 1 failed to start! Check /tmp/benchmark-ss-apiserver1.log"
    echo "      Stopping PlayServer 1 and 2..."
    kill $PLAY_SERVER1_PID 2>/dev/null
    kill $PLAY_SERVER2_PID 2>/dev/null
    exit 1
fi

echo "[5/7] ApiServer 1 started successfully"

# ApiServer 2 시작
echo "[6/7] Starting ApiServer 2 (ZMQ port $API2_ZMQ_PORT, HTTP port $API2_HTTP_PORT)..."
dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.ApiServer --configuration Release -- \
    --zmq-port $API2_ZMQ_PORT \
    --http-port $API2_HTTP_PORT \
    --server-id api-2 \
    --peers "$ALL_PEERS" > /tmp/benchmark-ss-apiserver2.log 2>&1 &

API_SERVER2_PID=$!
echo "      ApiServer 2 PID: $API_SERVER2_PID"

# ApiServer 2 시작 대기
sleep 2

# ApiServer 2가 정상적으로 시작되었는지 확인
if ! ps -p $API_SERVER2_PID > /dev/null; then
    echo "      ApiServer 2 failed to start! Check /tmp/benchmark-ss-apiserver2.log"
    echo "      Stopping all servers..."
    kill $PLAY_SERVER1_PID 2>/dev/null
    kill $PLAY_SERVER2_PID 2>/dev/null
    kill $API_SERVER1_PID 2>/dev/null
    exit 1
fi

echo "[6/7] ApiServer 2 started successfully"

# 서버 간 연결 대기
echo "      Waiting for server connections (3 seconds)..."
sleep 3

# PlayServer 2에 타겟 Stage 생성 (더미 클라이언트 - 백그라운드 유지)
echo "      Creating and maintaining target Stage on PlayServer 2 (Stage ID: 2000)..."
# 연결을 유지하여 Stage가 살아있도록 함 (messages=0으로 연결만 유지)
dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.Client --configuration Release -- \
    --server 127.0.0.1:$PLAY2_TCP_PORT \
    --connections 1 \
    --messages 0 \
    --stage-id 2000 \
    --http-port $PLAY2_HTTP_PORT > /tmp/benchmark-ss-dummy-client.log 2>&1 &

DUMMY_CLIENT_PID=$!
echo "      Dummy client PID: $DUMMY_CLIENT_PID"

# Stage 생성 및 인증 완료 대기
sleep 5
echo "      Target Stage 2000 created and connection maintained"

# 클라이언트 실행 (all 모드로 모든 벤치마크 테스트)
echo "[7/7] Running benchmark client (all modes)..."
dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.Client --configuration Release -- \
    --server 127.0.0.1:$PLAY1_TCP_PORT \
    --connections $CONNECTIONS \
    --messages $MESSAGES \
    --response-size $RESPONSE_SIZES \
    --mode all \
    --http-port $PLAY1_HTTP_PORT

CLIENT_EXIT_CODE=$?

# 클라이언트가 서버 종료를 처리하므로 여기서는 대기만 수행
echo ""
echo "Stopping dummy client..."
kill $DUMMY_CLIENT_PID 2>/dev/null || true
sleep 1

echo "Waiting for servers shutdown..."
sleep 3

# 혹시 남아있는 프로세스 정리
pkill -f "PlayHouse.Benchmark.SS" 2>/dev/null || true

echo ""
echo "================================================================================"
echo "Benchmark completed"
echo "PlayServer 1 log: /tmp/benchmark-ss-playserver1.log"
echo "PlayServer 2 log: /tmp/benchmark-ss-playserver2.log"
echo "ApiServer 1 log:  /tmp/benchmark-ss-apiserver1.log"
echo "ApiServer 2 log:  /tmp/benchmark-ss-apiserver2.log"
echo "Dummy client log: /tmp/benchmark-ss-dummy-client.log"
echo "================================================================================"

exit $CLIENT_EXIT_CODE
