#!/bin/bash

# PlayHouse Benchmark - All Modes Comparison (전체 테스트)
#
# 목적: 모든 통신 모드 x 모든 페이로드 사이즈를 테스트하고 결과를 비교합니다.
#       성능 비교 및 회귀 테스트에 사용합니다.
#
# 사용법: ./run-benchmark.sh [mode] [sizes] [connections] [duration] [max-inflight]
#
# 파라미터 (공백으로 구분):
#   mode         - 테스트 모드 (기본: all): request-async, request-callback, send, all
#   sizes        - 페이로드 크기 리스트 (기본: 64,256,1024,65536)
#   connections  - 동시 연결 수 (기본: 10)
#   duration     - 테스트 시간(초) (기본: 10)
#   max-inflight - 최대 동시 요청 수 (기본: 200)
#
# 예시:
#   ./run-benchmark.sh all 1024 100 10 500
#   ./run-benchmark.sh send 64,256 50 20 100
#
# 참고: 특정 모드/사이즈 하나만 빠르게 테스트하려면 run-single.sh를 사용하세요.

set -e

# 스크립트 디렉토리 기준 경로 설정
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# 기본값
MODE=${1:-all}
PAYLOAD_SIZES=${2:-"64,256,1024,65536"}
CONNECTIONS=${3:-10}
DURATION=${4:-10}
MAX_INFLIGHT=${5:-200}

# 콤마 사용 여부 체크 (사용자가 실수로 콤마로 인자를 구분한 경우 경고)
if [[ "$1" == *","* ]] && [ -z "$2" ]; then
    echo "Warning: Detected comma in the first argument. Did you mean to use spaces?"
    echo "Correct usage: ./run-benchmark.sh mode size connections duration inflight"
    echo "Example: ./run-benchmark.sh all 1024 100 10 200"
    echo ""
fi
SERVER_PORT=16110
HTTP_PORT=5080

echo "================================================================================"
echo "PlayHouse Benchmark - All Modes Comparison"
echo "================================================================================"
echo "Configuration:"
echo "  Mode: $MODE"
echo "  Payload sizes: $PAYLOAD_SIZES bytes"
echo "  Connections: $CONNECTIONS"
echo "  Duration: ${DURATION}s per mode"
echo "  Max in-flight: $MAX_INFLIGHT"
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
    --http-port $HTTP_PORT > /tmp/benchmark-server.log 2>&1 &

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

# 클라이언트 실행 - all 모드로 모든 테스트 수행
echo "[4/4] Running benchmarks (all modes x all sizes)..."
echo ""

dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.Client/PlayHouse.Benchmark.Client.csproj" -c Release -- \
    --server 127.0.0.1:$SERVER_PORT \
    --connections $CONNECTIONS \
    --mode $MODE \
    --duration $DURATION \
    --message-size 64 \
    --response-size $PAYLOAD_SIZES \
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
