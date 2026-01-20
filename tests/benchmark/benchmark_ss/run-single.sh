#!/bin/bash

# PlayHouse S2S Benchmark - Single Test (단일 테스트)
#
# 목적: 특정 통신 모드와 페이로드 사이즈를 지정하여 빠르게 테스트합니다.
#       개발 중 빠른 검증이나 특정 조건 테스트에 사용합니다.
#
# 사용법: ./run-single.sh --mode <mode> --size <size> [options]
#
# 필수 파라미터:
#   --mode           - 통신 모드: request-async, request-callback, send
#   --size           - 페이로드 크기 (bytes): 64, 1024, 65536 등
#
# 선택 파라미터:
#   --ccu            - 동시 연결 수 (기본: 10)
#   --duration       - 테스트 시간(초) (기본: 10)
#   --inflight       - 최대 동시 요청 수 (기본: 200)
#   --min-pool-size  - 최소 워커 수 (기본: 100)
#   --max-pool-size  - 최대 워커 수 (기본: 1000)
#   --diag-level     - 진단 레벨 (기본: -1, 0: Raw Echo, 1: Header Echo)
#   --warmup         - Warm-up 시간(초) (기본: 3)
#
# 예시:
#   ./run-single.sh --mode request-async --size 1024
#   ./run-single.sh --mode send --size 65536 --ccu 100 --duration 30
#
# 참고: 모든 모드/사이즈를 비교 테스트하려면 run-benchmark.sh를 사용하세요.

set -e

# 스크립트 디렉토리 기준 경로 설정
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
RESULT_DIR="$PROJECT_ROOT/benchmark-results"

# 기본값 설정
MODE=""
SIZE=""
CCU=10
DURATION=10
INFLIGHT=200
MIN_POOL_SIZE=100
MAX_POOL_SIZE=1000
DIAG_LEVEL=-1
WARMUP=3

# Named parameter 파싱
while [[ $# -gt 0 ]]; do
    case $1 in
        --mode)
            MODE="$2"
            shift 2
            ;;
        --size)
            SIZE="$2"
            shift 2
            ;;
        --ccu)
            CCU="$2"
            shift 2
            ;;
        --duration)
            DURATION="$2"
            shift 2
            ;;
        --inflight)
            INFLIGHT="$2"
            shift 2
            ;;
        --min-pool-size)
            MIN_POOL_SIZE="$2"
            shift 2
            ;;
        --max-pool-size)
            MAX_POOL_SIZE="$2"
            shift 2
            ;;
        --diag-level)
            DIAG_LEVEL="$2"
            shift 2
            ;;
        --warmup)
            WARMUP="$2"
            shift 2
            ;;
        -h|--help)
            echo "사용법: $0 --mode <mode> --size <size> [options]"
            echo ""
            echo "필수 파라미터:"
            echo "  --mode           - 통신 모드: request-async, request-callback, send"
            echo "  --size           - 페이로드 크기 (bytes)"
            echo ""
            echo "선택 파라미터:"
            echo "  --ccu            - 동시 연결 수 (기본: 10)"
            echo "  --duration       - 테스트 시간(초) (기본: 10)"
            echo "  --inflight       - 최대 동시 요청 수 (기본: 200)"
            echo "  --min-pool-size  - 최소 워커 수 (기본: 100)"
            echo "  --max-pool-size  - 최대 워커 수 (기본: 1000)"
            echo "  --diag-level     - 진단 레벨 (기본: -1)"
            echo "  --warmup         - Warm-up 시간(초) (기본: 3)"
            echo ""
            echo "예시:"
            echo "  $0 --mode request-async --size 1024"
            echo "  $0 --mode send --size 65536 --ccu 100 --duration 30"
            exit 0
            ;;
        *)
            echo "Unknown parameter: $1"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# 필수 파라미터 검증
if [ -z "$MODE" ] || [ -z "$SIZE" ]; then
    echo "Error: --mode and --size are required"
    echo ""
    echo "사용법: $0 --mode <mode> --size <size> [options]"
    echo "자세한 도움말: $0 --help"
    exit 1
fi
TCP_PORT=16110
ZMQ_PORT=16100
HTTP_PORT=5080
API_HTTP_PORT=5081
API_ZMQ_PORT=16201

# Comm-mode 검증
case "$MODE" in
    request-async|request-callback|send)
        ;;
    *)
        echo "Error: Invalid comm-mode '$MODE'"
        echo "Valid modes: request-async, request-callback, send"
        exit 1
        ;;
esac

echo "================================================================================"
echo "PlayHouse S2S Benchmark - Single Test"
echo "================================================================================"
echo "Configuration:"
echo "  Comm-mode: $MODE"
echo "  Payload size: $SIZE bytes (Echo: request=response)"
echo "  Connections: $CCU"
echo "  Duration: ${DURATION}s"
echo "  Warmup: ${WARMUP}s"
echo "  Max in-flight: $INFLIGHT"
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
    --ccu $CCU \
    --test-mode ss-echo \
    --mode $MODE \
    --duration $DURATION \
    --message-size $SIZE \
    --response-size $SIZE \
    --http-port $HTTP_PORT \
    --api-http-port $API_HTTP_PORT \
    --output-dir "$RESULT_DIR" \
    --inflight $INFLIGHT \
    --warmup $WARMUP

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
