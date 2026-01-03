#!/bin/bash

# ============================================================================
# PlayHouse Echo Benchmark Script
# ============================================================================
# 사용법: ./run_benchmark.sh [옵션]
#   --connections   클라이언트 수 (기본값: 1000)
#   --duration      테스트 시간(초) (기본값: 10)
#   --payloads      메시지 크기 목록 (기본값: "8,64,256,1024,65536")
# ============================================================================

set -e

# 기본값 설정
CONNECTIONS=${CONNECTIONS:-1000}
DURATION=${DURATION:-10}
PAYLOADS=${PAYLOADS:-"8,64,256,1024,65536"}
BASE_PORT=30000
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
RESULT_DIR="$PROJECT_ROOT/benchmark-results"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
REPORT_FILE="$RESULT_DIR/benchmark_report_${TIMESTAMP}.txt"
JSON_REPORT="$RESULT_DIR/benchmark_report_${TIMESTAMP}.json"

# 색상 정의
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# 파라미터 파싱
while [[ $# -gt 0 ]]; do
    case $1 in
        --connections)
            CONNECTIONS="$2"
            shift 2
            ;;
        --duration)
            DURATION="$2"
            shift 2
            ;;
        --payloads)
            PAYLOADS="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# 결과 저장용 배열
declare -A RESULTS_ASYNC
declare -A RESULTS_CALLBACK

# 유틸리티 함수
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

log_header() {
    echo ""
    echo -e "${CYAN}════════════════════════════════════════════════════════════════════════════════${NC}"
    echo -e "${CYAN}  $1${NC}"
    echo -e "${CYAN}════════════════════════════════════════════════════════════════════════════════${NC}"
}

# 서버 프로세스 종료
cleanup_servers() {
    log_info "Cleaning up existing server processes..."
    pkill -9 -f "PlayHouse.Benchmark.Echo.Server" 2>/dev/null || true
    sleep 2
}

# 서버 시작 및 대기
start_server() {
    local tcp_port=$1
    local zmq_port=$2
    local http_port=$3

    log_info "Starting server (TCP: $tcp_port, ZMQ: $zmq_port, HTTP: $http_port)..."

    dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.Echo.Server/PlayHouse.Benchmark.Echo.Server.csproj" \
        -c Release -- \
        --tcp-port $tcp_port \
        --zmq-port $zmq_port \
        --http-port $http_port \
        > /dev/null 2>&1 &

    SERVER_PID=$!

    # 서버 준비 대기
    local max_wait=30
    local waited=0
    while ! curl -s "http://localhost:$http_port/benchmark/stats" > /dev/null 2>&1; do
        sleep 1
        waited=$((waited + 1))
        if [ $waited -ge $max_wait ]; then
            log_error "Server failed to start within ${max_wait}s"
            kill -9 $SERVER_PID 2>/dev/null || true
            return 1
        fi
    done

    log_success "Server started (PID: $SERVER_PID)"
    return 0
}

# 서버 종료
stop_server() {
    if [ -n "$SERVER_PID" ]; then
        log_info "Stopping server (PID: $SERVER_PID)..."
        kill -9 $SERVER_PID 2>/dev/null || true
        wait $SERVER_PID 2>/dev/null || true
        sleep 2
    fi
}

# 단일 벤치마크 실행
run_single_benchmark() {
    local mode=$1
    local payload=$2
    local tcp_port=$3
    local http_port=$4

    log_info "Running benchmark: mode=$mode, payload=${payload}B, connections=$CONNECTIONS, duration=${DURATION}s" >&2

    # 메트릭 리셋
    curl -s -X POST "http://localhost:$http_port/benchmark/reset" > /dev/null

    # 기존 JSON 파일을 임시로 이동 (새 파일 감지를 위해)
    local backup_dir="$RESULT_DIR/.backup_$$"
    mkdir -p "$backup_dir"
    mv "$RESULT_DIR"/echo_benchmark_*.json "$backup_dir/" 2>/dev/null || true

    # 벤치마크 실행 (출력을 로그 파일로)
    local run_log="$RESULT_DIR/run_${mode}_${payload}.log"
    dotnet run --project "$SCRIPT_DIR/PlayHouse.Benchmark.Echo.Client/PlayHouse.Benchmark.Echo.Client.csproj" \
        -c Release -- \
        --server "127.0.0.1:$tcp_port" \
        --http-port $http_port \
        --connections $CONNECTIONS \
        --duration $DURATION \
        --payload-size $payload \
        --mode $mode \
        --output-dir "$RESULT_DIR" > "$run_log" 2>&1

    # 새로 생성된 JSON 파일 찾기
    sleep 1
    local json_file=$(ls -t "$RESULT_DIR"/echo_benchmark_*.json 2>/dev/null | head -1)

    # 백업 파일 복원
    mv "$backup_dir"/*.json "$RESULT_DIR/" 2>/dev/null || true
    rmdir "$backup_dir" 2>/dev/null || true

    if [ -z "$json_file" ] || [ ! -f "$json_file" ]; then
        log_error "Result JSON file not found" >&2
        echo "${payload}|0|0|0|0|0"
        return
    fi

    # JSON에서 결과 파싱 (Python 사용)
    local parsed=$(python3 -c "
import json
with open('$json_file') as f:
    data = json.load(f)
r = data['Results'][0]
srv = r['Server']
cli = r['Client']
print(f\"{int(srv['ThroughputMsgPerSec'])}|{srv['LatencyP99Ms']:.2f}|{int(cli['ThroughputMsgPerSec'])}|{cli['RttLatencyP99Ms']:.2f}|{srv['ProcessedMessages']}\")
" 2>/dev/null)

    local srv_tps=$(echo "$parsed" | cut -d'|' -f1)
    local srv_p99=$(echo "$parsed" | cut -d'|' -f2)
    local cli_tps=$(echo "$parsed" | cut -d'|' -f3)
    local cli_rtt=$(echo "$parsed" | cut -d'|' -f4)
    local messages=$(echo "$parsed" | cut -d'|' -f5)

    # 결과 저장
    echo "${payload}|${srv_tps:-0}|${srv_p99:-0}|${cli_tps:-0}|${cli_rtt:-0}|${messages:-0}"
}

# 모드별 전체 벤치마크 실행
run_mode_benchmarks() {
    local mode=$1
    local mode_name=$2
    local port_offset=$3

    log_header "Testing $mode_name Mode"

    local tcp_port=$((BASE_PORT + port_offset))
    local zmq_port=$((BASE_PORT + port_offset + 1))
    local http_port=$((BASE_PORT + port_offset + 2))

    # 서버 시작
    if ! start_server $tcp_port $zmq_port $http_port; then
        log_error "Failed to start server for $mode_name mode"
        return 1
    fi

    # 각 페이로드 크기별 테스트
    IFS=',' read -ra PAYLOAD_ARRAY <<< "$PAYLOADS"
    for payload in "${PAYLOAD_ARRAY[@]}"; do
        local result=$(run_single_benchmark "$mode" "$payload" "$tcp_port" "$http_port")

        if [ "$mode" == "request-async" ]; then
            RESULTS_ASYNC[$payload]="$result"
        else
            RESULTS_CALLBACK[$payload]="$result"
        fi

        log_success "Completed: ${payload}B"

        # 다음 테스트 전 잠시 대기
        sleep 2
    done

    # 서버 종료
    stop_server
}

# 결과 레포트 생성
generate_report() {
    log_header "Generating Report"

    mkdir -p "$RESULT_DIR"

    # 텍스트 레포트 생성
    {
        echo "================================================================================"
        echo "  PlayHouse Echo Benchmark Report"
        echo "================================================================================"
        echo ""
        echo "Test Configuration:"
        echo "  - Connections: $CONNECTIONS"
        echo "  - Duration: ${DURATION}s"
        echo "  - Payload Sizes: $PAYLOADS"
        echo "  - Timestamp: $(date)"
        echo ""
        echo "================================================================================"
        echo "  RequestAsync Mode Results"
        echo "================================================================================"
        echo ""
        printf "%-12s | %12s | %10s | %12s | %12s | %15s\n" \
            "Payload" "Server TPS" "Srv P99" "Client TPS" "Client RTT" "Messages"
        printf "%-12s-+-%12s-+-%10s-+-%12s-+-%12s-+-%15s\n" \
            "------------" "------------" "----------" "------------" "------------" "---------------"

        IFS=',' read -ra PAYLOAD_ARRAY <<< "$PAYLOADS"
        for payload in "${PAYLOAD_ARRAY[@]}"; do
            if [ -n "${RESULTS_ASYNC[$payload]}" ]; then
                IFS='|' read -ra parts <<< "${RESULTS_ASYNC[$payload]}"
                local size_str=$(format_size $payload)
                printf "%-12s | %12s | %8sms | %12s | %10sms | %15s\n" \
                    "$size_str" "${parts[1]}/s" "${parts[2]}" "${parts[3]}/s" "${parts[4]}" "${parts[5]}"
            fi
        done

        echo ""
        echo "================================================================================"
        echo "  RequestCallback Mode Results"
        echo "================================================================================"
        echo ""
        printf "%-12s | %12s | %10s | %12s | %12s | %15s\n" \
            "Payload" "Server TPS" "Srv P99" "Client TPS" "Client RTT" "Messages"
        printf "%-12s-+-%12s-+-%10s-+-%12s-+-%12s-+-%15s\n" \
            "------------" "------------" "----------" "------------" "------------" "---------------"

        for payload in "${PAYLOAD_ARRAY[@]}"; do
            if [ -n "${RESULTS_CALLBACK[$payload]}" ]; then
                IFS='|' read -ra parts <<< "${RESULTS_CALLBACK[$payload]}"
                local size_str=$(format_size $payload)
                printf "%-12s | %12s | %8sms | %12s | %10sms | %15s\n" \
                    "$size_str" "${parts[1]}/s" "${parts[2]}" "${parts[3]}/s" "${parts[4]}" "${parts[5]}"
            fi
        done

        echo ""
        echo "================================================================================"
        echo "  Mode Comparison (Server TPS)"
        echo "================================================================================"
        echo ""
        printf "%-12s | %15s | %15s | %10s\n" \
            "Payload" "RequestAsync" "RequestCallback" "Diff"
        printf "%-12s-+-%15s-+-%15s-+-%10s\n" \
            "------------" "---------------" "---------------" "----------"

        for payload in "${PAYLOAD_ARRAY[@]}"; do
            if [ -n "${RESULTS_ASYNC[$payload]}" ] && [ -n "${RESULTS_CALLBACK[$payload]}" ]; then
                IFS='|' read -ra async_parts <<< "${RESULTS_ASYNC[$payload]}"
                IFS='|' read -ra callback_parts <<< "${RESULTS_CALLBACK[$payload]}"

                local async_tps=${async_parts[1]:-0}
                local callback_tps=${callback_parts[1]:-0}
                local size_str=$(format_size $payload)

                if [ "$async_tps" -gt 0 ] && [ "$callback_tps" -gt 0 ]; then
                    local diff=$(echo "scale=1; (($callback_tps - $async_tps) / $async_tps) * 100" | bc 2>/dev/null || echo "N/A")
                    printf "%-12s | %13s/s | %13s/s | %9s%%\n" \
                        "$size_str" "$async_tps" "$callback_tps" "$diff"
                else
                    printf "%-12s | %13s/s | %13s/s | %10s\n" \
                        "$size_str" "$async_tps" "$callback_tps" "N/A"
                fi
            fi
        done

        echo ""
        echo "================================================================================"
        echo "  Report saved to: $REPORT_FILE"
        echo "================================================================================"

    } | tee "$REPORT_FILE"

    # JSON 레포트 생성
    {
        echo "{"
        echo "  \"timestamp\": \"$(date -Iseconds)\","
        echo "  \"config\": {"
        echo "    \"connections\": $CONNECTIONS,"
        echo "    \"duration\": $DURATION,"
        echo "    \"payloads\": \"$PAYLOADS\""
        echo "  },"
        echo "  \"results\": {"
        echo "    \"request_async\": {"

        local first=true
        IFS=',' read -ra PAYLOAD_ARRAY <<< "$PAYLOADS"
        for payload in "${PAYLOAD_ARRAY[@]}"; do
            if [ -n "${RESULTS_ASYNC[$payload]}" ]; then
                IFS='|' read -ra parts <<< "${RESULTS_ASYNC[$payload]}"
                [ "$first" = false ] && echo ","
                echo "      \"${payload}\": {"
                echo "        \"server_tps\": ${parts[1]:-0},"
                echo "        \"server_p99_ms\": ${parts[2]:-0},"
                echo "        \"client_tps\": ${parts[3]:-0},"
                echo "        \"client_rtt_p99_ms\": ${parts[4]:-0},"
                echo "        \"messages\": ${parts[5]:-0}"
                echo -n "      }"
                first=false
            fi
        done

        echo ""
        echo "    },"
        echo "    \"request_callback\": {"

        first=true
        for payload in "${PAYLOAD_ARRAY[@]}"; do
            if [ -n "${RESULTS_CALLBACK[$payload]}" ]; then
                IFS='|' read -ra parts <<< "${RESULTS_CALLBACK[$payload]}"
                [ "$first" = false ] && echo ","
                echo "      \"${payload}\": {"
                echo "        \"server_tps\": ${parts[1]:-0},"
                echo "        \"server_p99_ms\": ${parts[2]:-0},"
                echo "        \"client_tps\": ${parts[3]:-0},"
                echo "        \"client_rtt_p99_ms\": ${parts[4]:-0},"
                echo "        \"messages\": ${parts[5]:-0}"
                echo -n "      }"
                first=false
            fi
        done

        echo ""
        echo "    }"
        echo "  }"
        echo "}"
    } > "$JSON_REPORT"

    log_success "Reports saved:"
    log_info "  Text: $REPORT_FILE"
    log_info "  JSON: $JSON_REPORT"
}

# 사이즈 포맷팅
format_size() {
    local bytes=$1
    if [ $bytes -ge 65536 ]; then
        echo "64KB"
    elif [ $bytes -ge 1024 ]; then
        echo "$((bytes/1024))KB"
    else
        echo "${bytes}B"
    fi
}

# 메인 실행
main() {
    log_header "PlayHouse Echo Benchmark"

    echo ""
    log_info "Configuration:"
    log_info "  Connections: $CONNECTIONS"
    log_info "  Duration: ${DURATION}s"
    log_info "  Payloads: $PAYLOADS"
    echo ""

    # 빌드
    log_info "Building projects..."
    dotnet build "$SCRIPT_DIR/PlayHouse.Benchmark.Echo.Server/PlayHouse.Benchmark.Echo.Server.csproj" -c Release --verbosity quiet
    dotnet build "$SCRIPT_DIR/PlayHouse.Benchmark.Echo.Client/PlayHouse.Benchmark.Echo.Client.csproj" -c Release --verbosity quiet
    log_success "Build completed"

    # 기존 서버 정리
    cleanup_servers

    # RequestAsync 모드 테스트
    run_mode_benchmarks "request-async" "RequestAsync" 0

    # 포트 정리를 위한 대기
    sleep 3
    cleanup_servers

    # RequestCallback 모드 테스트
    run_mode_benchmarks "request-callback" "RequestCallback" 10

    # 정리
    cleanup_servers

    # 레포트 생성
    generate_report

    log_header "Benchmark Complete"
}

# 스크립트 실행
main "$@"
