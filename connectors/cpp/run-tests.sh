#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# C++ connector ports
HTTP_PORT=48080
TCP_PORT=48001
CONTAINER_NAME="playhouse-test-cpp"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}[C++ Connector Test]${NC} Starting..."

cleanup() {
    echo -e "${YELLOW}[C++ Connector Test]${NC} Cleaning up..."
    docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" down -v 2>/dev/null || true
}
trap cleanup EXIT

# Clean up existing container
cleanup

# Start test server
echo -e "${YELLOW}[C++ Connector Test]${NC} Starting test server on HTTP:$HTTP_PORT, TCP:$TCP_PORT..."
docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" up -d --build

# Wait for health check
echo -e "${YELLOW}[C++ Connector Test]${NC} Waiting for server to be ready..."
for i in {1..30}; do
    if curl -sf "http://localhost:$HTTP_PORT/api/health" > /dev/null 2>&1; then
        echo -e "${GREEN}[C++ Connector Test]${NC} Server is ready!"
        break
    fi
    if [ $i -eq 30 ]; then
        echo -e "${RED}[C++ Connector Test]${NC} Server failed to start"
        docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" logs
        exit 1
    fi
    echo -n "."
    sleep 1
done

# Build if needed
BUILD_DIR="$SCRIPT_DIR/build"
if [ ! -d "$BUILD_DIR" ]; then
    echo -e "${YELLOW}[C++ Connector Test]${NC} Building project..."
    mkdir -p "$BUILD_DIR"
    cd "$BUILD_DIR"
    cmake .. -DBUILD_TESTING=ON
    cmake --build . --parallel
    cd "$SCRIPT_DIR"
fi

# Run tests
echo -e "${YELLOW}[C++ Connector Test]${NC} Running tests..."
TEST_SERVER_HOST=localhost \
TEST_SERVER_HTTP_PORT=$HTTP_PORT \
TEST_SERVER_TCP_PORT=$TCP_PORT \
ctest --test-dir "$BUILD_DIR" --output-on-failure

echo -e "${GREEN}[C++ Connector Test]${NC} Tests completed successfully!"
