#!/bin/bash

# PlayHouse Benchmark - Profiling Script
#
# 목적: 벤치마크 서버의 성능 병목을 식별하기 위한 프로파일링을 수행합니다.
#       CPU 핫스팟, 런타임 메트릭, Context switching 등을 측정합니다.
#
# 사용법: ./profile-benchmark.sh [connections] [duration]
#
# 파라미터:
#   connections - 동시 연결 수 (선택, 기본: 100)
#   duration    - 벤치마크 시간(초) (선택, 기본: 60)
#
# 예시:
#   ./profile-benchmark.sh
#   ./profile-benchmark.sh 100 60
#
# 결과: profiling-results/<timestamp>/ 디렉토리에 저장됩니다.
#       - cpu-profile.nettrace: CPU 프로파일 (speedscope.app에서 열기)
#       - runtime-counters.txt: 런타임 메트릭 (GC, ThreadPool, etc)
#       - perf-stat.txt: Context switching 통계 (Linux only)
#       - test-summary.txt: 테스트 설정 및 요약

set -e

# 스크립트 디렉토리 기준 경로 설정
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# 파라미터
CONNECTIONS=${1:-100}
BENCHMARK_DURATION=${2:-60}
SERVER_PORT=16110
HTTP_PORT=5080

# 프로파일링 설정
WARMUP_TIME=10              # 워밍업 시간 (초)
PROFILE_DURATION=30         # CPU 프로파일 수집 시간 (초)
COUNTERS_DURATION=40        # 카운터 모니터링 시간 (초)
MESSAGE_SIZE=1024           # 메시지 크기
MAX_INFLIGHT=1000          # 최대 동시 요청 수
MODE="send"                 # 테스트 모드

# 결과 저장 디렉토리
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RESULTS_DIR="$SCRIPT_DIR/profiling-results/$TIMESTAMP"

# 도구 경로
DOTNET_TRACE="${HOME}/.dotnet/tools/dotnet-trace"
DOTNET_COUNTERS="${HOME}/.dotnet/tools/dotnet-counters"

# 색상 코드
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 에러 핸들링
trap cleanup EXIT INT TERM

cleanup() {
    echo ""
    echo -e "${YELLOW}[CLEANUP] Stopping all processes...${NC}"

    # 클라이언트 종료
    if [ -n "$CLIENT_PID" ] && kill -0 "$CLIENT_PID" 2>/dev/null; then
        kill "$CLIENT_PID" 2>/dev/null || true
    fi

    # 카운터 모니터링 종료
    if [ -n "$COUNTER_PID" ] && kill -0 "$COUNTER_PID" 2>/dev/null; then
        kill "$COUNTER_PID" 2>/dev/null || true
    fi

    # perf 종료
    if [ -n "$PERF_PID" ] && kill -0 "$PERF_PID" 2>/dev/null; then
        kill "$PERF_PID" 2>/dev/null || true
    fi

    # 서버 종료
    curl -s -m 2 -X POST "http://localhost:$HTTP_PORT/benchmark/shutdown" > /dev/null 2>&1 || true
    pkill -9 -f "PlayHouse.Benchmark.Server" 2>/dev/null || true
    pkill -9 -f "PlayHouse.Benchmark.Client" 2>/dev/null || true

    sleep 1
    echo -e "${GREEN}[CLEANUP] Cleanup completed${NC}"
}

echo "================================================================================"
echo "PlayHouse Benchmark - Profiling"
echo "================================================================================"
echo "Configuration:"
echo "  Mode: $MODE (fire-and-forget with callback)"
echo "  Connections: $CONNECTIONS"
echo "  Message size: $MESSAGE_SIZE bytes"
echo "  Max in-flight: $MAX_INFLIGHT"
echo "  Benchmark duration: ${BENCHMARK_DURATION}s"
echo ""
echo "Profiling settings:"
echo "  Warmup time: ${WARMUP_TIME}s"
echo "  CPU profile duration: ${PROFILE_DURATION}s"
echo "  Counters monitoring: ${COUNTERS_DURATION}s"
echo ""
echo "Results will be saved to:"
echo "  $RESULTS_DIR"
echo "================================================================================"
echo ""

# 도구 확인
echo -e "${BLUE}[CHECK] Verifying profiling tools...${NC}"
if [ ! -f "$DOTNET_TRACE" ]; then
    echo -e "${RED}Error: dotnet-trace not found at $DOTNET_TRACE${NC}"
    echo "Install with: dotnet tool install -g dotnet-trace"
    exit 1
fi

if [ ! -f "$DOTNET_COUNTERS" ]; then
    echo -e "${RED}Error: dotnet-counters not found at $DOTNET_COUNTERS${NC}"
    echo "Install with: dotnet tool install -g dotnet-counters"
    exit 1
fi

# perf는 선택사항
PERF_AVAILABLE=false
if command -v perf &> /dev/null; then
    PERF_AVAILABLE=true
    echo -e "${GREEN}[CHECK] Found: dotnet-trace, dotnet-counters, perf${NC}"
else
    echo -e "${YELLOW}[CHECK] Found: dotnet-trace, dotnet-counters${NC}"
    echo -e "${YELLOW}[CHECK] Warning: 'perf' not found (context switching stats will be skipped)${NC}"
fi
echo ""

# 결과 디렉토리 생성
mkdir -p "$RESULTS_DIR"

# 테스트 설정 저장
cat > "$RESULTS_DIR/test-summary.txt" <<EOF
PlayHouse Benchmark Profiling Results
=====================================

Timestamp: $TIMESTAMP
Test Mode: $MODE
Connections: $CONNECTIONS
Message Size: $MESSAGE_SIZE bytes
Max In-Flight: $MAX_INFLIGHT
Benchmark Duration: ${BENCHMARK_DURATION}s
Warmup Time: ${WARMUP_TIME}s
CPU Profile Duration: ${PROFILE_DURATION}s
Counters Duration: ${COUNTERS_DURATION}s

System Information:
- OS: $(uname -s) $(uname -r)
- CPU: $(nproc) cores
- .NET: $(dotnet --version)

EOF

# 프로젝트 빌드
echo -e "${BLUE}[1/7] Building projects...${NC}"
dotnet build "$SCRIPT_DIR/PlayHouse.Benchmark.Server/PlayHouse.Benchmark.Server.csproj" -c Release --verbosity quiet
dotnet build "$SCRIPT_DIR/PlayHouse.Benchmark.Client/PlayHouse.Benchmark.Client.csproj" -c Release --verbosity quiet
echo -e "${GREEN}[1/7] Build completed${NC}"
echo ""

# 기존 프로세스 정리
echo -e "${BLUE}[2/7] Cleaning up existing processes...${NC}"
curl -s -m 2 -X POST "http://localhost:$HTTP_PORT/benchmark/shutdown" > /dev/null 2>&1 || true
pkill -9 -f "PlayHouse.Benchmark.Server" 2>/dev/null || true
pkill -9 -f "PlayHouse.Benchmark.Client" 2>/dev/null || true
sleep 1
echo -e "${GREEN}[2/7] Cleanup completed${NC}"
echo ""

# 서버 시작
echo -e "${BLUE}[3/7] Starting benchmark server...${NC}"
dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.Server/PlayHouse.Benchmark.Server.csproj" -c Release -- \
    --tcp-port $SERVER_PORT \
    --http-port $HTTP_PORT > "$RESULTS_DIR/server.log" 2>&1 &

SERVER_PID=$!

# 서버 시작 대기
max_wait=30
waited=0
while ! curl -s "http://localhost:$HTTP_PORT/benchmark/stats" > /dev/null 2>&1; do
    sleep 1
    waited=$((waited + 1))
    if [ $waited -ge $max_wait ]; then
        echo -e "${RED}[3/7] Error: Server failed to start within ${max_wait}s${NC}"
        cat "$RESULTS_DIR/server.log"
        exit 1
    fi
done

echo -e "${GREEN}[3/7] Server started (PID: $SERVER_PID)${NC}"
echo "      Server log: $RESULTS_DIR/server.log"
echo ""

# 벤치마크 클라이언트 시작 (백그라운드)
echo -e "${BLUE}[4/7] Starting benchmark client...${NC}"
dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.Client/PlayHouse.Benchmark.Client.csproj" -c Release -- \
    --server 127.0.0.1:$SERVER_PORT \
    --connections $CONNECTIONS \
    --mode $MODE \
    --duration $BENCHMARK_DURATION \
    --message-size $MESSAGE_SIZE \
    --response-size $MESSAGE_SIZE \
    --http-port $HTTP_PORT \
    --max-inflight $MAX_INFLIGHT \
    --warmup-duration 3 > "$RESULTS_DIR/client.log" 2>&1 &

CLIENT_PID=$!
echo -e "${GREEN}[4/7] Client started (PID: $CLIENT_PID)${NC}"
echo "      Client log: $RESULTS_DIR/client.log"
echo ""

# 워밍업 대기
echo -e "${BLUE}[5/7] Warming up for ${WARMUP_TIME}s...${NC}"
sleep "$WARMUP_TIME"
echo -e "${GREEN}[5/7] Warmup completed${NC}"
echo ""

# 프로파일링 시작
echo -e "${BLUE}[6/7] Collecting profiling data...${NC}"
echo ""

# 6.1 CPU 프로파일 수집
echo -e "${YELLOW}[6/7-1] Collecting CPU profile (${PROFILE_DURATION}s)...${NC}"
$DOTNET_TRACE collect --process-id $SERVER_PID --duration 00:00:$PROFILE_DURATION \
    --providers Microsoft-DotNETCore-SampleProfiler \
    --output "$RESULTS_DIR/cpu-profile.nettrace" > /dev/null 2>&1 &
TRACE_PID=$!

# 6.2 런타임 카운터 모니터링
echo -e "${YELLOW}[6/7-2] Monitoring runtime counters (${COUNTERS_DURATION}s)...${NC}"
$DOTNET_COUNTERS monitor --process-id $SERVER_PID \
    --counters System.Runtime \
    --refresh-interval 1 > "$RESULTS_DIR/runtime-counters.txt" 2>&1 &
COUNTER_PID=$!

# 6.3 Context switching 측정 (Linux only)
if [ "$PERF_AVAILABLE" = true ]; then
    echo -e "${YELLOW}[6/7-3] Measuring context switches (${COUNTERS_DURATION}s)...${NC}"
    perf stat -e context-switches,cpu-migrations,page-faults -p $SERVER_PID \
        sleep $COUNTERS_DURATION > "$RESULTS_DIR/perf-stat.txt" 2>&1 &
    PERF_PID=$!
fi

# CPU 프로파일 수집 대기
wait $TRACE_PID 2>/dev/null || true
echo -e "${GREEN}[6/7-1] CPU profile collected: cpu-profile.nettrace${NC}"

# 카운터 모니터링 종료
sleep $((COUNTERS_DURATION - PROFILE_DURATION))
kill $COUNTER_PID 2>/dev/null || true
echo -e "${GREEN}[6/7-2] Runtime counters saved: runtime-counters.txt${NC}"

# perf 완료 대기
if [ "$PERF_AVAILABLE" = true ]; then
    wait $PERF_PID 2>/dev/null || true
    echo -e "${GREEN}[6/7-3] Context switching stats saved: perf-stat.txt${NC}"
fi

echo ""
echo -e "${GREEN}[6/7] Profiling data collection completed${NC}"
echo ""

# 벤치마크 완료 대기
echo -e "${BLUE}[7/7] Waiting for benchmark to complete...${NC}"
wait $CLIENT_PID 2>/dev/null || true
echo -e "${GREEN}[7/7] Benchmark completed${NC}"
echo ""

# 클라이언트 로그에서 결과 추출
if [ -f "$RESULTS_DIR/client.log" ]; then
    echo "Extracting benchmark results..."
    grep -A 20 "Final Results" "$RESULTS_DIR/client.log" >> "$RESULTS_DIR/test-summary.txt" 2>/dev/null || true
fi

# 최종 요약
echo "================================================================================"
echo "Profiling completed successfully!"
echo "================================================================================"
echo ""
echo "Results saved to: $RESULTS_DIR"
echo ""
echo "Files generated:"
echo "  - cpu-profile.nettrace      : CPU hotspot analysis (open with speedscope.app)"
echo "  - runtime-counters.txt      : GC, ThreadPool, allocation metrics"
if [ "$PERF_AVAILABLE" = true ]; then
echo "  - perf-stat.txt             : Context switching statistics"
fi
echo "  - test-summary.txt          : Test configuration and results"
echo "  - client.log                : Client output"
echo "  - server.log                : Server output"
echo ""
echo "Next steps:"
echo "  1. Open cpu-profile.nettrace in speedscope.app:"
echo "     https://www.speedscope.app/"
echo "  2. Check runtime-counters.txt for:"
echo "     - cpu-usage: CPU utilization (%)"
echo "     - threadpool-queue-length: ThreadPool saturation"
echo "     - alloc-rate: Memory allocation rate (MB/s)"
echo "     - gen-0-gc-count, gen-1-gc-count: GC frequency"
if [ "$PERF_AVAILABLE" = true ]; then
echo "  3. Check perf-stat.txt for context-switches/sec"
fi
echo ""
echo "See PROFILING.md for detailed analysis instructions."
echo "================================================================================"
