#!/bin/bash

# PlayHouse S2S Benchmark - Duration-based Test
# 사용법: ./run_duration_benchmark.sh [connections] [duration]
# 각 모드별로 duration 동안 실행하며 모든 메시지 사이즈를 테스트합니다.

set -e

# 스크립트 디렉토리 기준 경로 설정
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
RESULT_DIR="$PROJECT_ROOT/benchmark-results"

# 기본값
CONNECTIONS=${1:-10000}
DURATION=${2:-10}
TCP_PORT=16110
ZMQ_PORT=16100
HTTP_PORT=5080
API_HTTP_PORT=5081
API_ZMQ_PORT=16201

# Echo 페이로드 크기 (배열)
PAYLOAD_SIZES=(64 256 1024 65536 131072)

# 통신 모드 배열
COMM_MODES=("request-async" "request-callback" "send")

echo "================================================================================"
echo "PlayHouse S2S Benchmark - Duration-based Test"
echo "================================================================================"
echo "Configuration:"
echo "  Connections: $CONNECTIONS"
echo "  Duration: ${DURATION}s per mode"
echo "  Modes: ${COMM_MODES[*]}"
echo "  Payload sizes: ${PAYLOAD_SIZES[*]} bytes (Echo: request=response)"
echo "================================================================================"
echo ""

# 프로젝트 빌드
echo "[1/4] Building projects..."
dotnet build "$SCRIPT_DIR/PlayHouse.Benchmark.SS.PlayServer/PlayHouse.Benchmark.SS.PlayServer.csproj" -c Release --verbosity quiet
dotnet build "$SCRIPT_DIR/PlayHouse.Benchmark.SS.ApiServer/PlayHouse.Benchmark.SS.ApiServer.csproj" -c Release --verbosity quiet
dotnet build "$SCRIPT_DIR/PlayHouse.Benchmark.SS.Client/PlayHouse.Benchmark.SS.Client.csproj" -c Release --verbosity quiet
echo "[1/4] Build completed"

# 기존 서버 프로세스 정리
echo "[2/4] Cleaning up existing servers..."
pkill -9 -f "PlayHouse.Benchmark.SS" 2>/dev/null || true
sleep 2

# 서버 시작
echo "[3/4] Starting S2S benchmark servers..."

# PlayServer 시작
echo "  Starting PlayServer (TCP: $TCP_PORT, ZMQ: $ZMQ_PORT, HTTP: $HTTP_PORT)..."
dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.SS.PlayServer/PlayHouse.Benchmark.SS.PlayServer.csproj" \
    -c Release -- --peers "api-1=tcp://127.0.0.1:$API_ZMQ_PORT" > /tmp/ss-playserver.log 2>&1 &
PLAY_PID=$!

sleep 3

# ApiServer 시작
echo "  Starting ApiServer (ZMQ: $API_ZMQ_PORT, HTTP: $API_HTTP_PORT)..."
dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.SS.ApiServer/PlayHouse.Benchmark.SS.ApiServer.csproj" \
    -c Release -- --peers "play-1=tcp://127.0.0.1:$ZMQ_PORT" > /tmp/ss-apiserver.log 2>&1 &
API_PID=$!

# 서버 시작 대기
max_wait=30
waited=0
while ! curl -s "http://localhost:$HTTP_PORT/benchmark/stats" > /dev/null 2>&1; do
    sleep 1
    waited=$((waited + 1))
    if [ $waited -ge $max_wait ]; then
        echo "Servers failed to start within ${max_wait}s"
        echo "PlayServer log:"
        cat /tmp/ss-playserver.log
        echo "ApiServer log:"
        cat /tmp/ss-apiserver.log
        exit 1
    fi
done

echo "[3/4] Servers started (PlayServer PID: $PLAY_PID, ApiServer PID: $API_PID)"

# 결과 디렉토리 생성
mkdir -p "$RESULT_DIR"

# 클라이언트 실행 - 각 모드별로 순차 실행
echo "[4/4] Running S2S benchmarks (duration-based)..."
echo ""

for COMM_MODE in "${COMM_MODES[@]}"; do
    echo "----------------------------------------"
    echo "Testing mode: $COMM_MODE"
    echo "----------------------------------------"

    for SIZE in "${PAYLOAD_SIZES[@]}"; do
        echo ""
        echo ">>> Echo test: ${SIZE} bytes (request=${SIZE}, response=${SIZE}) <<<"

        dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.SS.Client/PlayHouse.Benchmark.SS.Client.csproj" -c Release -- \
            --server 127.0.0.1:$TCP_PORT \
            --connections $CONNECTIONS \
            --mode ss-echo \
            --comm-mode $COMM_MODE \
            --duration $DURATION \
            --request-size $SIZE \
            --response-size $SIZE \
            --http-port $HTTP_PORT \
            --api-http-port $API_HTTP_PORT \
            --output-dir "$RESULT_DIR"

        # 테스트 간 간격
        sleep 1
    done

    echo ""

    # 서버 간 메트릭 정리를 위한 대기
    sleep 2
done

# 정리
echo ""
echo "Cleaning up..."
curl -s -m 2 -X POST http://localhost:$HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 || true
curl -s -m 2 -X POST http://localhost:$API_HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 || true
pkill -9 -f "PlayHouse.Benchmark.SS" 2>/dev/null || true
sleep 1

echo ""
echo "================================================================================"
echo "S2S Duration-based Benchmark completed"
echo "Results saved to: $RESULT_DIR"
echo "================================================================================"
