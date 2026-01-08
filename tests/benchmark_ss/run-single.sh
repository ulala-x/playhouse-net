#!/bin/bash

# PlayHouse S2S Benchmark - Single Test (단일 테스트)
#
# 목적: 특정 통신 모드와 페이로드 사이즈를 지정하여 빠르게 테스트합니다.
#       개발 중 빠른 검증이나 특정 조건 테스트에 사용합니다.
#
# 사용법: ./run-single.sh <comm-mode> <size> [connections] [duration] [max-inflight] [min-pool-size] [max-pool-size] [diag-level]
#
# 파라미터:
#   comm-mode      - 통신 모드 (필수): request-async, request-callback, send
#   size           - 페이로드 크기 (필수, bytes): 64, 1024, 65536 등
#   connections    - 동시 연결 수 (선택, 기본: 10)
#   duration       - 테스트 시간(초) (선택, 기본: 10)
#   max-inflight   - 최대 동시 요청 수 (선택, 기본: 200)
#   min-pool-size  - 최소 워커 수 (선택, 기본: 100)
#   max-pool-size  - 최대 워커 수 (선택, 기본: 1000)
#   diag-level     - 진단 레벨 (선택, 기본: -1, 0: Raw Echo, 1: Header Echo)
#
# 예시:
#   ./run-single.sh request-async 1024
#   ./run-single.sh send 65536 100 30 500 100 500
#
# 참고: 모든 모드/사이즈를 비교 테스트하려면 run-benchmark.sh를 사용하세요.

set -e

# 스크립트 디렉토리 기준 경로 설정
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
RESULT_DIR="$PROJECT_ROOT/benchmark-results"

# 파라미터 검증
if [ -z "$1" ] || [ -z "$2" ]; then
    echo "사용법: $0 <comm-mode> <size> [connections] [duration] [max-inflight] [min-pool-size] [max-pool-size]"
    echo ""
    echo "파라미터:"
    echo "  comm-mode      - 통신 모드 (필수): request-async, request-callback, send"
    echo "  size           - 페이로드 크기 (필수, bytes)"
    echo "  connections    - 동시 연결 수 (선택, 기본: 10)"
    echo "  duration       - 테스트 시간(초) (선택, 기본: 10)"
    echo "  max-inflight   - 최대 동시 요청 수 (선택, 기본: 200)"
    echo "  min-pool-size  - 최소 워커 수 (선택, 기본: 100)"
    echo "  max-pool-size  - 최대 워커 수 (선택, 기본: 1000)"
    echo ""
    echo "예시:"
    echo "  $0 request-async 1024"
    echo "  $0 send 65536 100 30 500 100 500"
    exit 1
fi

COMM_MODE=$1
SIZE=$2
CONNECTIONS=${3:-10}
DURATION=${4:-10}
MAX_INFLIGHT=${5:-200}
MIN_POOL_SIZE=${6:-100}
MAX_POOL_SIZE=${7:-1000}
DIAG_LEVEL=${8:--1}
TCP_PORT=16110
ZMQ_PORT=16100
HTTP_PORT=5080
API_HTTP_PORT=5081
API_ZMQ_PORT=16201

# Comm-mode 검증
case "$COMM_MODE" in
    request-async|request-callback|send)
        ;;
    *)
        echo "Error: Invalid comm-mode '$COMM_MODE'"
        echo "Valid modes: request-async, request-callback, send"
        exit 1
        ;;
esac

echo "================================================================================"
echo "PlayHouse S2S Benchmark - Single Test"
echo "================================================================================"
echo "Configuration:"
echo "  Comm-mode: $COMM_MODE"
echo "  Payload size: $SIZE bytes (Echo: request=response)"
echo "  Connections: $CONNECTIONS"
echo "  Duration: ${DURATION}s"
echo "  Max in-flight: $MAX_INFLIGHT"
echo "  Pool size: $MIN_POOL_SIZE ~ $MAX_POOL_SIZE"
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
    -c Release -- --tcp-port $TCP_PORT --http-port $HTTP_PORT --zmq-port $ZMQ_PORT \
    --min-pool-size $MIN_POOL_SIZE --max-pool-size $MAX_POOL_SIZE \
    --peers "api-1=tcp://127.0.0.1:$API_ZMQ_PORT" > /tmp/ss-playserver.log 2>&1 &
PLAY_PID=$!

sleep 3

# ApiServer 시작
echo "  Starting ApiServer (ZMQ: $API_ZMQ_PORT, HTTP: $API_HTTP_PORT)..."
dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.SS.ApiServer/PlayHouse.Benchmark.SS.ApiServer.csproj" \
    -c Release -- --zmq-port $API_ZMQ_PORT --http-port $API_HTTP_PORT \
    --min-pool-size $MIN_POOL_SIZE --max-pool-size $MAX_POOL_SIZE \
    --diagnostic-level $DIAG_LEVEL \
    --peers "play-1=tcp://127.0.0.1:$ZMQ_PORT" > /tmp/ss-apiserver.log 2>&1 &
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

# 벤치마크 실행
echo "[4/4] Running S2S benchmark..."
echo ""

mkdir -p "$RESULT_DIR"

dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.SS.Client/PlayHouse.Benchmark.SS.Client.csproj" -c Release -- \
    --server 127.0.0.1:$TCP_PORT \
    --connections $CONNECTIONS \
    --mode ss-echo \
    --comm-mode $COMM_MODE \
    --duration $DURATION \
    --message-size $SIZE \
    --response-size $SIZE \
    --http-port $HTTP_PORT \
    --api-http-port $API_HTTP_PORT \
    --output-dir "$RESULT_DIR" \
    --max-inflight $MAX_INFLIGHT

# 정리
echo ""
echo "Cleaning up..."
curl -s -m 2 -X POST http://localhost:$HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 || true
curl -s -m 2 -X POST http://localhost:$API_HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 || true
pkill -9 -f "PlayHouse.Benchmark.SS" 2>/dev/null || true
sleep 1

echo ""
echo "================================================================================"
echo "S2S Benchmark completed"
echo "================================================================================"
