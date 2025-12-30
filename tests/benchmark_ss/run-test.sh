#!/bin/bash

# PlayHouse SS Benchmark 통합 테스트 스크립트
# 여러 시나리오로 순차 테스트 실행
# 사용법: ./run-test.sh

# 기본 설정
CCU=${1:-1}
MSG_PER_CONN=${2:-1000}

# 테스트할 응답 크기들
RESPONSE_SIZES=(256 65536)

echo "================================================================================"
echo "PlayHouse SS Benchmark Test Suite"
echo "================================================================================"
echo "Configuration:"
echo "  Concurrent Users (CCU): $CCU"
echo "  Messages per connection: $MSG_PER_CONN"
echo "  Response sizes: ${RESPONSE_SIZES[*]} bytes"
echo "================================================================================"
echo ""

# 프로젝트 빌드
echo "[1/4] Building projects..."
cd /home/ulalax/project/ulalax/playhouse/playhouse-net

dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.Shared --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.PlayServer --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.ApiServer --configuration Release > /dev/null 2>&1
dotnet build tests/benchmark_ss/PlayHouse.Benchmark.SS.Client --configuration Release > /dev/null 2>&1

if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi

echo "[1/4] Build completed"
echo ""

# 기존 서버 프로세스 정리
echo "[2/4] Cleaning up existing server processes..."
echo "  Checking for existing servers..."
curl -s -X POST http://localhost:5080/benchmark/shutdown > /dev/null 2>&1 && echo "  Sent shutdown to port 5080" || true
curl -s -X POST http://localhost:5081/benchmark/shutdown > /dev/null 2>&1 && echo "  Sent shutdown to port 5081" || true
sleep 2

pkill -f "PlayHouse.Benchmark.SS.PlayServer" 2>/dev/null
pkill -f "PlayHouse.Benchmark.SS.ApiServer" 2>/dev/null
sleep 1

# 포트가 사용 중인지 확인 (PlayServer: 16110, 16100, 5080, 16120, 16200, 5081, ApiServer: 16201)
ALL_PORTS=(16110 16100 5080 16120 16200 5081 16201)
for PORT in "${ALL_PORTS[@]}"; do
    if lsof -i :$PORT -t >/dev/null 2>&1; then
        echo "      Port $PORT still in use, force killing..."
        lsof -i :$PORT -t | xargs kill -9 2>/dev/null
        sleep 1
    fi
done

echo "[2/4] Cleanup completed"
echo ""

# 결과 저장을 위한 배열
declare -a RESULTS
TEST_INDEX=0

# Play-to-Api 테스트
echo "[3/4] Running benchmark tests..."
echo ""
echo "================================================================================"
echo "PART 1: Play-to-Api Tests"
echo "================================================================================"

for RESPONSE_SIZE in "${RESPONSE_SIZES[@]}"; do
    TEST_INDEX=$((TEST_INDEX + 1))
    echo ""
    echo "--------------------------------------------------------------------------------"
    echo "Test $TEST_INDEX: Play-to-Api with response size = $RESPONSE_SIZE bytes"
    echo "--------------------------------------------------------------------------------"

    ./tests/benchmark_ss/run-benchmark.sh $CCU $MSG_PER_CONN $RESPONSE_SIZE play-to-api

    CLIENT_EXIT_CODE=$?

    if [ $CLIENT_EXIT_CODE -ne 0 ]; then
        RESULTS[$TEST_INDEX]="Play-to-Api ($RESPONSE_SIZE bytes): FAILED"
    else
        RESULTS[$TEST_INDEX]="Play-to-Api ($RESPONSE_SIZE bytes): SUCCESS"
    fi

    # 다음 테스트 전 대기
    sleep 2
done

# 포트 정리 (클라이언트가 종료 처리했으므로 간단한 대기만)
echo ""
echo "Waiting for servers shutdown before Play-to-Stage tests..."
sleep 3

# 혹시 남아있는 프로세스 정리
pkill -f "PlayHouse.Benchmark.SS.PlayServer" 2>/dev/null || true
pkill -f "PlayHouse.Benchmark.SS.ApiServer" 2>/dev/null || true
sleep 1

for PORT in "${ALL_PORTS[@]}"; do
    if lsof -i :$PORT -t >/dev/null 2>&1; then
        lsof -i :$PORT -t | xargs kill -9 2>/dev/null
        sleep 1
    fi
done

# Play-to-Stage 테스트
echo ""
echo "================================================================================"
echo "PART 2: Play-to-Stage Tests"
echo "================================================================================"

for RESPONSE_SIZE in "${RESPONSE_SIZES[@]}"; do
    TEST_INDEX=$((TEST_INDEX + 1))
    echo ""
    echo "--------------------------------------------------------------------------------"
    echo "Test $TEST_INDEX: Play-to-Stage with response size = $RESPONSE_SIZE bytes"
    echo "--------------------------------------------------------------------------------"

    ./tests/benchmark_ss/run-benchmark-p2p.sh $CCU $MSG_PER_CONN $RESPONSE_SIZE

    CLIENT_EXIT_CODE=$?

    if [ $CLIENT_EXIT_CODE -ne 0 ]; then
        RESULTS[$TEST_INDEX]="Play-to-Stage ($RESPONSE_SIZE bytes): FAILED"
    else
        RESULTS[$TEST_INDEX]="Play-to-Stage ($RESPONSE_SIZE bytes): SUCCESS"
    fi

    # 다음 테스트 전 대기
    sleep 2
done

echo ""
echo "[3/4] All tests completed"
echo ""

# 최종 정리 (클라이언트가 종료 처리했으므로 간단한 대기만)
echo "[4/4] Final cleanup..."
sleep 3

# 혹시 남아있는 프로세스 정리
pkill -f "PlayHouse.Benchmark.SS.PlayServer" 2>/dev/null || true
pkill -f "PlayHouse.Benchmark.SS.ApiServer" 2>/dev/null || true

for PORT in "${ALL_PORTS[@]}"; do
    if lsof -i :$PORT -t >/dev/null 2>&1; then
        lsof -i :$PORT -t | xargs kill -9 2>/dev/null
    fi
done

echo "[4/4] Cleanup completed"
echo ""

# 결과 요약
echo "================================================================================"
echo "Test Results Summary"
echo "================================================================================"
echo "Configuration:"
echo "  CCU: $CCU"
echo "  Messages per connection: $MSG_PER_CONN"
echo ""
echo "Results:"
for i in "${!RESULTS[@]}"; do
    echo "  $i. ${RESULTS[$i]}"
done
echo ""
echo "Log files:"
echo "  /tmp/benchmark-ss-playserver.log   (Play-to-Api PlayServer)"
echo "  /tmp/benchmark-ss-apiserver.log    (Play-to-Api ApiServer)"
echo "  /tmp/benchmark-ss-playserver1.log  (Play-to-Stage PlayServer 1)"
echo "  /tmp/benchmark-ss-playserver2.log  (Play-to-Stage PlayServer 2)"
echo "================================================================================"

exit 0
