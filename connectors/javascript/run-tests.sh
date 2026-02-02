#!/bin/bash

# PlayHouse JavaScript Connector Integration Tests Runner
set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}=== PlayHouse JavaScript Connector Integration Tests ===${NC}"
echo ""

# Check if docker-compose is installed
if ! command -v docker-compose &> /dev/null; then
    echo -e "${RED}Error: docker-compose is not installed${NC}"
    exit 1
fi

# Check if npm is installed
if ! command -v npm &> /dev/null; then
    echo -e "${RED}Error: npm is not installed${NC}"
    exit 1
fi

# Function to cleanup on exit
cleanup() {
    echo ""
    echo -e "${YELLOW}Cleaning up...${NC}"
    docker-compose -f docker-compose.test.yml down -v
    echo -e "${GREEN}Cleanup complete${NC}"
}

# Trap EXIT to ensure cleanup runs
trap cleanup EXIT

# Start test server
echo -e "${YELLOW}Starting test server...${NC}"
docker-compose -f docker-compose.test.yml up -d

# Wait for test server to be healthy
echo -e "${YELLOW}Waiting for test server to be healthy...${NC}"
MAX_RETRIES=30
RETRY_COUNT=0

while [ $RETRY_COUNT -lt $MAX_RETRIES ]; do
    if curl -f http://localhost:38080/api/health > /dev/null 2>&1; then
        echo -e "${GREEN}Test server is healthy!${NC}"
        break
    fi

    RETRY_COUNT=$((RETRY_COUNT + 1))
    if [ $RETRY_COUNT -eq $MAX_RETRIES ]; then
        echo -e "${RED}Test server failed to become healthy${NC}"
        docker-compose -f docker-compose.test.yml logs
        exit 1
    fi

    echo -n "."
    sleep 1
done

echo ""
echo -e "${YELLOW}Running integration tests...${NC}"
echo ""

# Set environment variables for test server
export TEST_SERVER_HOST=localhost
export TEST_SERVER_HTTP_PORT=38080
export TEST_SERVER_WS_PORT=38001

# Run tests
if npm run test:integration; then
    echo ""
    echo -e "${GREEN}=== All tests passed! ===${NC}"
    exit 0
else
    echo ""
    echo -e "${RED}=== Tests failed ===${NC}"
    echo ""
    echo -e "${YELLOW}Test server logs:${NC}"
    docker-compose -f docker-compose.test.yml logs
    exit 1
fi
