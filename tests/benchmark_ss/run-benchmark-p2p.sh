#!/bin/bash

# PlayHouse SS Benchmark 실행 스크립트 (Play-to-Stage 모드)
# 두 개의 PlayServer 인스턴스 실행
# 사용법: ./run-benchmark-p2p.sh [connections] [messages] [response-size]

# 스크립트 위치 기준으로 프로젝트 루트 찾기
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$PROJECT_ROOT"

# 기본값
CONNECTIONS=${1:-1}
MESSAGES=${2:-10000}
RESPONSE_SIZE=${3:-1500}

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
echo "  Response size: $RESPONSE_SIZE bytes"
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
pkill -f "PlayHouse.Benchmark.SS.PlayServer" 2>/dev/null
sleep 2

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
    --target-nid play-2 \
    --target-port $PLAY2_ZMQ_PORT > /tmp/benchmark-ss-playserver1.log 2>&1 &

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
    --target-nid play-1 \
    --target-port $PLAY1_ZMQ_PORT > /tmp/benchmark-ss-playserver2.log 2>&1 &

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

# PlayServer 2에 타겟 Stage 생성 (더미 연결 - 백그라운드 유지)
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

# 클라이언트 실행 (play-to-stage 모드)
echo "[5/5] Running benchmark client (play-to-stage mode)..."
dotnet run --project tests/benchmark_ss/PlayHouse.Benchmark.SS.Client --configuration Release -- \
    --server 127.0.0.1:$PLAY1_TCP_PORT \
    --connections $CONNECTIONS \
    --messages $MESSAGES \
    --response-size $RESPONSE_SIZE \
    --mode play-to-stage \
    --http-port $PLAY1_HTTP_PORT

CLIENT_EXIT_CODE=$?

# 서버 종료
echo ""
echo "Stopping servers and clients..."
echo "  Stopping dummy client..."
kill $DUMMY_CLIENT_PID 2>/dev/null
sleep 1

echo "  Stopping PlayServer 1..."
kill $PLAY_SERVER1_PID 2>/dev/null
sleep 1
if ps -p $PLAY_SERVER1_PID > /dev/null 2>&1; then
    kill -9 $PLAY_SERVER1_PID 2>/dev/null
fi

echo "  Stopping PlayServer 2..."
kill $PLAY_SERVER2_PID 2>/dev/null
sleep 1
if ps -p $PLAY_SERVER2_PID > /dev/null 2>&1; then
    kill -9 $PLAY_SERVER2_PID 2>/dev/null
fi

# 좀비 프로세스 정리
pkill -f "PlayHouse.Benchmark.SS" 2>/dev/null

echo "All processes stopped"
echo ""
echo "================================================================================"
echo "Benchmark completed"
echo "PlayServer 1 log: /tmp/benchmark-ss-playserver1.log"
echo "PlayServer 2 log: /tmp/benchmark-ss-playserver2.log"
echo "================================================================================"

exit $CLIENT_EXIT_CODE
