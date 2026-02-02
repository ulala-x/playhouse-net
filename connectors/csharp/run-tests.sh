#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# C# connector ports
HTTP_PORT=18080
TCP_PORT=18001
CONTAINER_NAME="playhouse-test-csharp"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}[C# Connector Test]${NC} Starting..."

cleanup() {
    echo -e "${YELLOW}[C# Connector Test]${NC} Cleaning up..."
    curl -sf -X POST "http://localhost:$HTTP_PORT/api/shutdown" > /dev/null 2>&1 || true
    docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" down -v 2>/dev/null || true
}
trap cleanup EXIT

# Clean up existing container
cleanup

# Start test server
echo -e "${YELLOW}[C# Connector Test]${NC} Starting test server on HTTP:$HTTP_PORT, TCP:$TCP_PORT..."
docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" up -d --build

# Wait for health check
echo -e "${YELLOW}[C# Connector Test]${NC} Waiting for server to be ready..."
for i in {1..30}; do
    if curl -sf "http://localhost:$HTTP_PORT/api/health" > /dev/null 2>&1; then
        echo -e "${GREEN}[C# Connector Test]${NC} Server is ready!"
        break
    fi
    if [ $i -eq 30 ]; then
        echo -e "${RED}[C# Connector Test]${NC} Server failed to start"
        docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" logs
        exit 1
    fi
    echo -n "."
    sleep 1
done

# Run tests
echo -e "${YELLOW}[C# Connector Test]${NC} Running tests..."
set +e
TEST_SERVER_HOST=127.0.0.1 \
TEST_SERVER_HTTP_PORT=$HTTP_PORT \
TEST_SERVER_TCP_PORT=$TCP_PORT \
dotnet test "$SCRIPT_DIR/tests/PlayHouse.Connector.IntegrationTests" --logger "console;verbosity=normal"
TEST_STATUS=$?
set -e
if [ $TEST_STATUS -ne 0 ]; then
    echo -e "${RED}[C# Connector Test]${NC} Tests failed. Dumping server logs..."
    docker-compose -f "$SCRIPT_DIR/docker-compose.test.yml" logs
    exit $TEST_STATUS
fi

echo -e "${GREEN}[C# Connector Test]${NC} Tests completed successfully!"
