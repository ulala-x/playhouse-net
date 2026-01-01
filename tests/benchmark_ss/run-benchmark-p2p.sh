#!/bin/bash

# PlayHouse SS Benchmark 실행 스크립트 (Play-to-Stage 모드)
# 두 개의 PlayServer 인스턴스 실행
# 사용법: ./run-benchmark-p2p.sh [connections] [messages] [response-sizes]
#   response-sizes: 콤마로 구분된 응답 크기 (예: "256,65536")

# 스크립트 위치 기준으로 프로젝트 루트 찾기
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$PROJECT_ROOT"

# 기본값
CONNECTIONS=${1:-1}
MESSAGES=${2:-10000}
RESPONSE_SIZES=${3:-"256,512"}

# PlayServer 1 포트 (클라이언트 연결)
PLAY1_TCP_PORT=16110
PLAY1_ZMQ_PORT=16100
PLAY1_HTTP_PORT=5080

# PlayServer 2 포트 (타겟)
PLAY2_TCP_PORT=16120
PLAY2_ZMQ_PORT=16200
PLAY2_HTTP_PORT=5081

echo "================================================================================"
echo "PlayHouse SS Benchmark Execution Script (Play-to-Stage Mode)"
echo "================================================================================"
echo "Configuration:"
echo "  Connections: $CONNECTIONS"
echo "  Messages per connection: $MESSAGES"
echo "  Response sizes: $RESPONSE_SIZES bytes"
echo "  Mode: play-to-stage"
echo "  PlayServer 1 - TCP: $PLAY1_TCP_PORT, ZMQ: $PLAY1_ZMQ_PORT, HTTP: $PLAY1_HTTP_PORT"
echo "  PlayServer 2 - TCP: $PLAY2_TCP_PORT, ZMQ: $PLAY2_ZMQ_PORT, HTTP: $PLAY2_HTTP_PORT"
echo "================================================================================"
echo ""

# 프로젝트 빌드
echo "[1/5] Building projects..."
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.Shared --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.PlayServer --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.Client --configuration Release > /dev/null 2>&1

if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi

echo "[1/5] Build completed"

# 기존 서버 프로세스 정리
echo "[2/5] Cleaning up existing server processes..."
echo "  Checking for existing servers..."
curl -s -X POST http://localhost:$PLAY1_HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 && echo "  Sent shutdown to PlayServer 1 port $PLAY1_HTTP_PORT" || true
curl -s -X POST http://localhost:$PLAY2_HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 && echo "  Sent shutdown to PlayServer 2 port $PLAY2_HTTP_PORT" || true
sleep 2

pkill -f "PlayHouse.Benchmark.SS.PlayServer" 2>/dev/null
sleep 1

# 포트가 사용 중인지 확인 및 강제 종료
for PORT in $PLAY1_TCP_PORT $PLAY1_ZMQ_PORT $PLAY1_HTTP_PORT $PLAY2_TCP_PORT $PLAY2_ZMQ_PORT $PLAY2_HTTP_PORT; do
    if lsof -i :$PORT -t >/dev/null 2>&1; then
        echo "      Port $PORT still in use, force killing..."
        lsof -i :$PORT -t | xargs kill -9 2>/dev/null
        sleep 1
    fi
done

echo "[2/5] Cleanup completed"

# PlayServer 1 시작 (클라이언트 연결용)
echo "[3/5] Starting PlayServer 1 (TCP port $PLAY1_TCP_PORT, HTTP API port $PLAY1_HTTP_PORT)..."
dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.PlayServer --configuration Release -- \
    --tcp-port $PLAY1_TCP_PORT \
    --zmq-port $PLAY1_ZMQ_PORT \
    --http-port $PLAY1_HTTP_PORT \
    --server-id play-1 \
    --peers "play-1=tcp://127.0.0.1:$PLAY1_ZMQ_PORT,play-2=tcp://127.0.0.1:$PLAY2_ZMQ_PORT" > /tmp/benchmark-ss-playserver1.log 2>&1 &

PLAY_SERVER1_PID=$!
echo "      PlayServer 1 PID: $PLAY_SERVER1_PID"

# PlayServer 1 시작 대기
sleep 3

# PlayServer 1이 정상적으로 시작되었는지 확인
if ! ps -p $PLAY_SERVER1_PID > /dev/null; then
    echo "      PlayServer 1 failed to start! Check /tmp/benchmark-ss-playserver1.log"
    exit 1
fi

echo "[3/5] PlayServer 1 started successfully"

# PlayServer 2 시작 (타겟용)
echo "[4/5] Starting PlayServer 2 (TCP port $PLAY2_TCP_PORT, HTTP API port $PLAY2_HTTP_PORT)..."
dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.PlayServer --configuration Release -- \
    --tcp-port $PLAY2_TCP_PORT \
    --zmq-port $PLAY2_ZMQ_PORT \
    --http-port $PLAY2_HTTP_PORT \
    --server-id play-2 \
    --peers "play-1=tcp://127.0.0.1:$PLAY1_ZMQ_PORT,play-2=tcp://127.0.0.1:$PLAY2_ZMQ_PORT" > /tmp/benchmark-ss-playserver2.log 2>&1 &

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

echo "[4/5] PlayServer 2 started successfully"

# 서버 간 연결 대기
echo "      Waiting for server connection (3 seconds)..."
sleep 3

# Stage 생성을 위해 임시 API 서버 시작
echo "      Starting temporary ApiServer for stage creation..."
TMP_API_ZMQ_PORT=16300
TMP_API_HTTP_PORT=5082

dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.ApiServer --configuration Release -- \
    --zmq-port $TMP_API_ZMQ_PORT \
    --http-port $TMP_API_HTTP_PORT \
    --server-id tmp-api \
    --peers "play-1=tcp://127.0.0.1:$PLAY1_ZMQ_PORT,play-2=tcp://127.0.0.1:$PLAY2_ZMQ_PORT" > /tmp/benchmark-ss-tmpapi.log 2>&1 &

TMP_API_PID=$!
echo "      Temporary ApiServer PID: $TMP_API_PID"
sleep 3

# 임시 API 서버 시작 확인
if ! ps -p $TMP_API_PID > /dev/null; then
    echo "      Temporary ApiServer failed to start! Check /tmp/benchmark-ss-tmpapi.log"
    kill $PLAY_SERVER1_PID 2>/dev/null
    kill $PLAY_SERVER2_PID 2>/dev/null
    exit 1
fi

# Stage 생성 (API 서버를 통해)
echo "      Creating stages via temporary API server..."

# PlayServer 1용 Stage 생성 (첫 10개만)
CREATE_COUNT=$CONNECTIONS
if [ $CREATE_COUNT -gt 10 ]; then
    CREATE_COUNT=10
fi

for i in $(seq 0 $(($CREATE_COUNT - 1))); do
    STAGE_ID=$((1000 + i))
    curl -s -X POST http://localhost:$TMP_API_HTTP_PORT/benchmark/create-stage \
        -H "Content-Type: application/json" \
        -d "{\"playNid\":\"play-1\",\"stageType\":\"BenchmarkStage\",\"stageId\":$STAGE_ID}" > /dev/null 2>&1
done

# PlayServer 2용 타겟 Stage 생성 (ID: 2000)
curl -s -X POST http://localhost:$TMP_API_HTTP_PORT/benchmark/create-stage \
    -H "Content-Type: application/json" \
    -d "{\"playNid\":\"play-2\",\"stageType\":\"BenchmarkStage\",\"stageId\":2000}" > /dev/null 2>&1

echo "      Stages created successfully"
sleep 1

# 임시 API 서버 종료
echo "      Stopping temporary ApiServer..."
curl -s -X POST http://localhost:$TMP_API_HTTP_PORT/benchmark/shutdown > /dev/null 2>&1
sleep 2
pkill -f "tmp-api" 2>/dev/null || true
echo "      Temporary ApiServer stopped"

# 클라이언트 실행 (play-to-stage 모드, 모든 응답 크기를 한 번에 테스트)
echo "[5/5] Running benchmark client (play-to-stage mode)..."
dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.Client --configuration Release -- \
    --server 127.0.0.1:$PLAY1_TCP_PORT \
    --connections $CONNECTIONS \
    --messages $MESSAGES \
    --response-size $RESPONSE_SIZES \
    --mode play-to-stage \
    --target-nid play-2 \
    --target-stage-id 2000 \
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
echo "================================================================================"

exit $CLIENT_EXIT_CODE
