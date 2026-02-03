#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Java connector ports
HTTP_PORT=28080
HTTPS_PORT=28443
TCP_PORT=28001
TCP_TLS_PORT=28002
CONTAINER_NAME="playhouse-test-java"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}[Java Connector Test]${NC} Starting..."

cleanup() {
    echo -e "${YELLOW}[Java Connector Test]${NC} Cleaning up..."
    curl -sf -X POST "http://localhost:$HTTP_PORT/api/shutdown" > /dev/null 2>&1 || true
    docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" down -v 2>/dev/null || true
}
trap cleanup EXIT

# Clean up existing container
cleanup

# Start test server
echo -e "${YELLOW}[Java Connector Test]${NC} Starting test server on HTTP:$HTTP_PORT, HTTPS:$HTTPS_PORT, TCP:$TCP_PORT, TCP+TLS:$TCP_TLS_PORT..."
docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" up -d --build

# Wait for health check
echo -e "${YELLOW}[Java Connector Test]${NC} Waiting for server to be ready..."
for i in {1..30}; do
    if curl -sf "http://localhost:$HTTP_PORT/api/health" > /dev/null 2>&1; then
        echo -e "${GREEN}[Java Connector Test]${NC} Server is ready!"
        break
    fi
    if [ $i -eq 30 ]; then
        echo -e "${RED}[Java Connector Test]${NC} Server failed to start"
        docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" logs
        exit 1
    fi
    echo -n "."
    sleep 1
done

# Run unit tests
echo -e "${YELLOW}[Java Connector Test]${NC} Running unit tests..."
set +e
"$SCRIPT_DIR/gradlew" -p "$SCRIPT_DIR" test --info
UNIT_STATUS=$?
set -e
if [ $UNIT_STATUS -ne 0 ]; then
    echo -e "${RED}[Java Connector Test]${NC} Unit tests failed. Dumping server logs..."
    docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" logs
    exit $UNIT_STATUS
fi

# Run integration tests
echo -e "${YELLOW}[Java Connector Test]${NC} Running integration tests..."
set +e
TEST_SERVER_HOST=127.0.0.1 \
TEST_SERVER_HTTP_PORT=$HTTP_PORT \
TEST_SERVER_HTTPS_PORT=$HTTPS_PORT \
TEST_SERVER_TCP_PORT=$TCP_PORT \
TEST_SERVER_TCP_TLS_PORT=$TCP_TLS_PORT \
"$SCRIPT_DIR/gradlew" -p "$SCRIPT_DIR" integrationTest --info
INTEGRATION_STATUS=$?
set -e
if [ $INTEGRATION_STATUS -ne 0 ]; then
    echo -e "${RED}[Java Connector Test]${NC} Integration tests failed. Dumping server logs..."
    docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" logs
    exit $INTEGRATION_STATUS
fi

echo -e "${GREEN}[Java Connector Test]${NC} All tests completed successfully!"
