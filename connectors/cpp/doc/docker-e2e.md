# C++ Connector E2E (Local + Docker + CI)

This guide standardizes E2E testing for the C++ connector using the PlayHouse test server.

## Targets

- E2E coverage using the shared test server.
- Local + Docker workflow for consistency.
- CI-ready commands.

## Local (Recommended)

```bash
# Ensure Boost headers are available
git submodule update --init --recursive

# Runs Docker test server + C++ tests
./connectors/cpp/scripts/run-local-e2e.sh
```

This uses `connectors/cpp/run-tests.sh`, which:
- starts the test server via Docker Compose
- builds the C++ connector (if needed)
- runs `ctest` with server ports injected

## Docker (CI-style)

```bash
CPP_TEST_IMAGE="ubuntu:24.04" \
./connectors/cpp/scripts/run-docker-e2e.sh
```

Notes:
- The image must include build tools (cmake, compiler, make/ninja).
- The script mounts the repo and runs build + tests in-container.

## Transport Coverage

The test server exposes:
- TCP: 34001
- TCP+TLS: 34002
- WS: 8080/ws
- WSS: 8443/ws

The C++ connector supports TCP, TCP+TLS, WS, and WSS.

## CI Integration (Example)

```bash
# Start server
cd connectors/cpp
docker compose -f docker-compose.test.yml up -d --build

# Build & test
mkdir -p build
cd build
cmake .. -DBUILD_TESTING=ON
cmake --build . --parallel
ctest --output-on-failure

# Cleanup
cd ..
docker compose -f docker-compose.test.yml down -v
```

## Environment Variables

The tests read these environment variables:
- `TEST_SERVER_HOST` (default: localhost)
- `TEST_SERVER_HTTP_PORT` (default: 8080)
- `TEST_SERVER_HTTPS_PORT` (default: 8443)
- `TEST_SERVER_TCP_PORT` (default: 34001)
- `TEST_SERVER_TCP_TLS_PORT` (default: 34002)
- `TEST_SERVER_WS_PORT` (default: 8080)
- `TEST_SERVER_WSS_PORT` (default: 8443)
