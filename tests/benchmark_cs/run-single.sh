#!/bin/bash

# PlayHouse Benchmark - Single Test (단일 테스트)
#
# 목적: 특정 모드와 페이로드 사이즈를 지정하여 빠르게 테스트합니다.
#       개발 중 빠른 검증이나 특정 조건 테스트에 사용합니다.
#
# 사용법: ./run-single.sh <mode> <size> [connections] [duration] [max-inflight] [min-pool-size] [max-pool-size]
#
# 파라미터:
#   mode           - 테스트 모드 (필수): request-async, request-callback, send
#   size           - 페이로드 크기 (필수, bytes): 64, 1024, 65536 등
#   connections    - 동시 연결 수 (선택, 기본: 10)
#   duration       - 테스트 시간(초) (선택, 기본: 10)
#   max-inflight   - 최대 동시 요청 수 (선택, 기본: 200)
#   min-pool-size  - 최소 워커 수 (선택, 기본: 100)
#   max-pool-size  - 최대 워커 수 (선택, 기본: 1000)
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

# 파라미터 검증
if [ -z "$1" ] || [ -z "$2" ]; then
    echo "사용법: $0 <mode> <size> [connections] [duration] [max-inflight] [min-pool-size] [max-pool-size]"
    echo ""
    echo "파라미터:"
    echo "  mode           - 테스트 모드 (필수): request-async, request-callback, send"
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

MODE=$1
SIZE=$2
CONNECTIONS=${3:-10}
DURATION=${4:-10}
MAX_INFLIGHT=${5:-200}
MIN_POOL_SIZE=${6:-100}
MAX_POOL_SIZE=${7:-1000}
SERVER_PORT=16110
HTTP_PORT=5080

# Mode 검증
case "$MODE" in
    request-async|request-callback|send)
        ;;
    *)
        echo "Error: Invalid mode '$MODE'"
        echo "Valid modes: request-async, request-callback, send"
        exit 1
        ;;
esac

echo "================================================================================"
echo "PlayHouse Benchmark - Single Test"
echo "================================================================================"
echo "Configuration:"
echo "  Mode: $MODE"
echo "  Payload size: $SIZE bytes (Echo: request=response)"
echo "  Connections: $CONNECTIONS"
echo "  Duration: ${DURATION}s"
echo "  Max in-flight: $MAX_INFLIGHT"
echo "  Pool size: $MIN_POOL_SIZE ~ $MAX_POOL_SIZE"
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
    --http-port $HTTP_PORT \
    --min-pool-size $MIN_POOL_SIZE \
    --max-pool-size $MAX_POOL_SIZE > /tmp/benchmark-server.log 2>&1 &

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

# 벤치마크 실행
echo "[4/4] Running benchmark..."
echo ""

dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.Client/PlayHouse.Benchmark.Client.csproj" -c Release -- \
    --server 127.0.0.1:$SERVER_PORT \
    --connections $CONNECTIONS \
    --mode $MODE \
    --duration $DURATION \
    --message-size $SIZE \
    --response-size $SIZE \
    --http-port $HTTP_PORT \
    --max-inflight $MAX_INFLIGHT

# 정리
echo ""
echo "Cleaning up..."
curl -s -m 2 -X POST http://localhost:$HTTP_PORT/benchmark/shutdown > /dev/null 2>&1 || true
pkill -9 -f "PlayHouse.Benchmark.Server" 2>/dev/null || true
sleep 1

echo ""
echo "================================================================================"
echo "Benchmark completed"
echo "================================================================================"
