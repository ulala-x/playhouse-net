#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}========================================${NC}"
echo -e "${BLUE}  PlayHouse Connector Tests (All)${NC}"
echo -e "${BLUE}========================================${NC}"
echo ""
echo -e "Port allocation:"
echo -e "  ${YELLOW}C#${NC}:         HTTP 18080, HTTPS 18443, TCP 18001, TLS 18002"
echo -e "  ${YELLOW}Java${NC}:       HTTP 28080, HTTPS 28443, TCP 28001, TLS 28002"
echo -e "  ${YELLOW}JavaScript${NC}: HTTP 38080, HTTPS 38443, WS  38080, WSS 38443"
echo -e "  ${YELLOW}C++${NC}:        HTTP 48080, HTTPS 48443, TCP 48001, TLS 48002"
echo ""

# Parse arguments
PARALLEL=false
CONNECTORS=()

while [[ $# -gt 0 ]]; do
    case $1 in
        --parallel|-p)
            PARALLEL=true
            shift
            ;;
        csharp|java|javascript|cpp)
            CONNECTORS+=("$1")
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [--parallel|-p] [connector...]"
            echo ""
            echo "Options:"
            echo "  --parallel, -p    Run tests in parallel"
            echo ""
            echo "Connectors: csharp, java, javascript, cpp"
            echo "If no connector specified, all will be run."
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Default to all connectors
if [ ${#CONNECTORS[@]} -eq 0 ]; then
    CONNECTORS=(csharp java javascript cpp)
fi

# Track results
declare -A RESULTS

run_test() {
    local connector=$1
    local script="$SCRIPT_DIR/$connector/run-tests.sh"

    if [ ! -f "$script" ]; then
        echo -e "${RED}[ERROR]${NC} Script not found: $script"
        RESULTS[$connector]="SKIP"
        return 1
    fi

    echo -e "${YELLOW}[Starting]${NC} $connector connector tests..."

    if bash "$script"; then
        RESULTS[$connector]="PASS"
        return 0
    else
        RESULTS[$connector]="FAIL"
        return 1
    fi
}

if [ "$PARALLEL" = true ]; then
    echo -e "${BLUE}Running tests in parallel...${NC}"
    echo ""

    PIDS=()
    for connector in "${CONNECTORS[@]}"; do
        run_test "$connector" &
        PIDS+=($!)
    done

    # Wait for all processes
    FAILED=0
    for i in "${!PIDS[@]}"; do
        if ! wait ${PIDS[$i]}; then
            FAILED=$((FAILED + 1))
        fi
    done
else
    echo -e "${BLUE}Running tests sequentially...${NC}"
    echo ""

    FAILED=0
    for connector in "${CONNECTORS[@]}"; do
        if ! run_test "$connector"; then
            FAILED=$((FAILED + 1))
        fi
        echo ""
    done
fi

# Print summary
echo ""
echo -e "${BLUE}========================================${NC}"
echo -e "${BLUE}  Test Results Summary${NC}"
echo -e "${BLUE}========================================${NC}"

for connector in "${CONNECTORS[@]}"; do
    result="${RESULTS[$connector]:-UNKNOWN}"
    case $result in
        PASS)
            echo -e "  ${GREEN}[PASS]${NC} $connector"
            ;;
        FAIL)
            echo -e "  ${RED}[FAIL]${NC} $connector"
            ;;
        SKIP)
            echo -e "  ${YELLOW}[SKIP]${NC} $connector"
            ;;
        *)
            echo -e "  ${YELLOW}[????]${NC} $connector"
            ;;
    esac
done

echo ""

if [ $FAILED -eq 0 ]; then
    echo -e "${GREEN}All tests passed!${NC}"
    exit 0
else
    echo -e "${RED}$FAILED test suite(s) failed.${NC}"
    exit 1
fi
