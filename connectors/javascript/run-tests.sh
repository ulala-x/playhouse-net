#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# JavaScript connector ports
HTTP_PORT=38080
TCP_PORT=38001
WS_PORT=38080
CONTAINER_NAME="playhouse-test-js"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}[JavaScript Connector Test]${NC} Starting..."

cleanup() {
    echo -e "${YELLOW}[JavaScript Connector Test]${NC} Cleaning up..."
    docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" down -v 2>/dev/null || true
}
trap cleanup EXIT

# Clean up existing container
cleanup

# Start test server
echo -e "${YELLOW}[JavaScript Connector Test]${NC} Starting test server on HTTP:$HTTP_PORT, WS:$WS_PORT..."
docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" up -d --build

# Wait for health check
echo -e "${YELLOW}[JavaScript Connector Test]${NC} Waiting for server to be ready..."
for i in {1..30}; do
    if curl -sf "http://localhost:$HTTP_PORT/api/health" > /dev/null 2>&1; then
        echo -e "${GREEN}[JavaScript Connector Test]${NC} Server is ready!"
        break
    fi
    if [ $i -eq 30 ]; then
        echo -e "${RED}[JavaScript Connector Test]${NC} Server failed to start"
        docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" logs
        exit 1
    fi
    echo -n "."
    sleep 1
done

# Install dependencies if needed
if [ ! -d "$SCRIPT_DIR/node_modules" ]; then
    echo -e "${YELLOW}[JavaScript Connector Test]${NC} Installing dependencies..."
    npm --prefix "$SCRIPT_DIR" ci
fi

# Run tests
echo -e "${YELLOW}[JavaScript Connector Test]${NC} Running tests..."
TEST_SERVER_HOST=localhost \
TEST_SERVER_HTTP_PORT=$HTTP_PORT \
TEST_SERVER_WS_PORT=$WS_PORT \
npm --prefix "$SCRIPT_DIR" test

echo -e "${GREEN}[JavaScript Connector Test]${NC} Tests completed successfully!"
