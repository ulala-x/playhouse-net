#!/bin/bash

# PlayHouse S2S Benchmark - All Modes Comparison (전체 테스트)
#
# 목적: 모든 통신 모드 x 모든 페이로드 사이즈를 테스트하고 결과를 비교합니다.
#       S2S 성능 비교 및 회귀 테스트에 사용합니다.
#
# 사용법: ./run-benchmark.sh [connections] [duration] [max-inflight]
#
# 파라미터:
#   connections  - 동시 연결 수 (기본: 10)
#   duration     - 테스트 시간(초) (기본: 10)
#   max-inflight - 최대 동시 요청 수 (기본: 200)
#
# 테스트 통신 모드: RequestAsync, RequestCallback, Send
# 페이로드 사이즈: 64, 256, 1024, 65536 bytes
#
# 참고: 특정 모드/사이즈만 빠르게 테스트하려면 run-single.sh를 사용하세요.

set -e

# 스크립트 디렉토리 기준 경로 설정
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
RESULT_DIR="$PROJECT_ROOT/benchmark-results"

# 기본값
CONNECTIONS=${1:-10}
DURATION=${2:-10}
MAX_INFLIGHT=${3:-200}
TCP_PORT=16110
ZMQ_PORT=16100
HTTP_PORT=5080
API_HTTP_PORT=5081
API_ZMQ_PORT=16201

# 페이로드 크기 (콤마 구분)
PAYLOAD_SIZES="64,256,1024,65536"

echo "================================================================================"
echo "PlayHouse S2S Benchmark - All Modes Comparison"
echo "================================================================================"
echo "Configuration:"
echo "  Connections: $CONNECTIONS"
echo "  Duration: ${DURATION}s per mode"
echo "  Max in-flight: $MAX_INFLIGHT"
echo "  Modes: RequestAsync, RequestCallback, Send"
echo "  Payload sizes: $PAYLOAD_SIZES bytes"
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

# 클라이언트 실행 - all 모드로 모든 테스트 수행
echo "[4/4] Running S2S benchmarks (all modes x all sizes)..."
echo ""

mkdir -p "$RESULT_DIR"

dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.SS.Client/PlayHouse.Benchmark.SS.Client.csproj" -c Release -- \
    --server 127.0.0.1:$TCP_PORT \
    --connections $CONNECTIONS \
    --mode ss-echo \
    --comm-mode all \
    --duration $DURATION \
    --message-size 64 \
    --response-size $PAYLOAD_SIZES \
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
