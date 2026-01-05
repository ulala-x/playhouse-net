#!/bin/bash

# PlayHouse Benchmark - All Modes Comparison (전체 테스트)
#
# 목적: 모든 통신 모드 x 모든 페이로드 사이즈를 테스트하고 결과를 비교합니다.
#       성능 비교 및 회귀 테스트에 사용합니다.
#
# 사용법: ./run-benchmark.sh [connections] [duration] [batch_size]
#
# 파라미터:
#   connections  - 동시 연결 수 (기본: 10)
#   duration     - 테스트 시간(초) (기본: 10)
#   batch_size   - 연결 배치 크기 (기본: 100, 대규모 연결시 50 권장)
#
# 테스트 모드: RequestAsync, RequestCallback, Send
# 페이로드 사이즈: 64, 256, 1024, 65536 bytes
#
# 참고: 특정 모드/사이즈만 빠르게 테스트하려면 run-single.sh를 사용하세요.

set -e

# 스크립트 디렉토리 기준 경로 설정
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# 기본값
CONNECTIONS=${1:-10}
DURATION=${2:-10}
BATCH_SIZE=${3:-100}
SERVER_PORT=16110
HTTP_PORT=5080

# 페이로드 크기 (콤마 구분)
PAYLOAD_SIZES="64,256,1024,65536"

echo "================================================================================"
echo "PlayHouse Benchmark - All Modes Comparison"
echo "================================================================================"
echo "Configuration:"
echo "  Connections: $CONNECTIONS"
echo "  Duration: ${DURATION}s per mode"
echo "  Batch size: $BATCH_SIZE (connections per batch)"
echo "  Modes: RequestAsync, RequestCallback, Send"
echo "  Payload sizes: $PAYLOAD_SIZES bytes"
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
    --mode all \
    --duration $DURATION \
    --request-size 64 \
    --response-size $PAYLOAD_SIZES \
    --http-port $HTTP_PORT \
    --batch-size $BATCH_SIZE

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
