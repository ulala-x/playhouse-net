#!/bin/bash

# ============================================================================
# PlayHouse Server-to-Server (S2S) Benchmark Script
# ============================================================================
# 사용법: ./run_benchmark.sh [옵션]
#   --connections   클라이언트 수 (기본값: 100)
#   --messages      연결당 메시지 수 (기본값: 10000)
#   --request-size  요청 페이로드 크기 (기본값: 64)
# ============================================================================

set -e

# 기본값 설정
CONNECTIONS=${CONNECTIONS:-1000}
MESSAGES=${MESSAGES:-10000}
REQUEST_SIZE=${REQUEST_SIZE:-1024}
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
RESULT_DIR="$PROJECT_ROOT/benchmark-results"

# 색상 정의
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'

# 파라미터 파싱
while [[ $# -gt 0 ]]; do
    case $1 in
        --connections)
            CONNECTIONS="$2"
            shift 2
            ;;
        --messages)
            MESSAGES="$2"
            shift 2
            ;;
        --request-size)
            REQUEST_SIZE="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

log_info() { echo -e "${BLUE}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[SUCCESS]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }
log_header() {
    echo ""
    echo -e "${CYAN}════════════════════════════════════════════════════════════════════════════════${NC}"
    echo -e "${CYAN}  $1${NC}"
    echo -e "${CYAN}════════════════════════════════════════════════════════════════════════════════${NC}"
}

cleanup_servers() {
    log_info "Cleaning up existing server processes..."
    pkill -9 -f "PlayHouse.Benchmark.SS" 2>/dev/null || true
    sleep 2
}

start_servers() {
    log_info "Starting PlayServer (TCP: 16110, ZMQ: 16100)..."
    dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.SS.PlayServer/PlayHouse.Benchmark.SS.PlayServer.csproj" \
        -c Release -- --peers "api-1=tcp://127.0.0.1:16201" > /tmp/ss-playserver.log 2>&1 &
    PLAY_PID=$!

    sleep 3

    log_info "Starting ApiServer (ZMQ: 16201)..."
    dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.SS.ApiServer/PlayHouse.Benchmark.SS.ApiServer.csproj" \
        -c Release -- --peers "play-1=tcp://127.0.0.1:16100" > /tmp/ss-apiserver.log 2>&1 &
    API_PID=$!

    # 서버 준비 대기
    local max_wait=30
    local waited=0
    while ! curl -s "http://localhost:5080/benchmark/stats" > /dev/null 2>&1; do
        sleep 1
        waited=$((waited + 1))
        if [ $waited -ge $max_wait ]; then
            log_error "Servers failed to start within ${max_wait}s"
            cleanup_servers
            exit 1
        fi
    done

    log_success "Servers started (PlayServer PID: $PLAY_PID, ApiServer PID: $API_PID)"
}

run_benchmark() {
    log_info "Running S2S benchmark..."
    log_info "  Connections: $CONNECTIONS"
    log_info "  Messages per connection: $MESSAGES"
    log_info "  Request size: ${REQUEST_SIZE}B"
    echo ""

    dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.SS.Client/PlayHouse.Benchmark.SS.Client.csproj" \
        -c Release -- \
        --server 127.0.0.1:16110 \
        --connections $CONNECTIONS \
        --messages $MESSAGES \
        --request-size $REQUEST_SIZE \
        --mode all
}

main() {
    log_header "PlayHouse S2S Benchmark (Stage → API)"

    mkdir -p "$RESULT_DIR"

    # 빌드
    log_info "Building projects..."
    dotnet build "$SCRIPT_DIR/PlayHouse.Benchmark.SS.PlayServer/PlayHouse.Benchmark.SS.PlayServer.csproj" -c Release --verbosity quiet
    dotnet build "$SCRIPT_DIR/PlayHouse.Benchmark.SS.ApiServer/PlayHouse.Benchmark.SS.ApiServer.csproj" -c Release --verbosity quiet
    dotnet build "$SCRIPT_DIR/PlayHouse.Benchmark.SS.Client/PlayHouse.Benchmark.SS.Client.csproj" -c Release --verbosity quiet
    log_success "Build completed"

    # 기존 서버 정리
    cleanup_servers

    # 서버 시작
    start_servers

    # 벤치마크 실행
    run_benchmark

    # 정리
    cleanup_servers

    log_header "Benchmark Complete"
}

main "$@"
