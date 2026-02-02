# Docker E2E Workflow (WSL2 + Windows 11)

This document covers the recommended two-path workflow:

- Path A: Docker server + local UE tests (fast)
- Path B: Docker server + UE-in-Docker tests (reproducible)

## Prerequisites

- Windows 11 with WSL2
- Ubuntu 24.04 in WSL2
- Docker Desktop with WSL integration enabled
- Unreal Engine 5.3+ installed on Windows
- Epic GitHub access if using UE container images

## Path A (Recommended): Docker Server + Local UE Tests

1) Start the PlayHouse test server (Docker)

```bash
cd /home/ulalax/project/ulalax/playhouse/playhouse-net/connectors/test-server
./certs/generate-certs.sh
docker compose up -d
```

2) Run UE automation tests locally (Windows UE Editor)

```bash
UE_EDITOR_CMD="/mnt/c/Program Files/Epic Games/UE_5.3/Engine/Binaries/Win64/UnrealEditor-Cmd.exe" \
UPROJECT_PATH="/mnt/c/Work/MyGame/MyGame.uproject" \
./connectors/unreal/scripts/run-local-e2e.sh
```

3) Stop the test server

```bash
cd /home/ulalax/project/ulalax/playhouse/playhouse-net/connectors/test-server
docker compose down
```

## Path B (Optional): Docker Server + UE-in-Docker Tests

This path is slower but provides a consistent CI-like environment.

1) Ensure Epic UE container access

- Link your Epic account to GitHub.
- Authenticate to GHCR and pull the UE image.

2) Start the PlayHouse test server

```bash
cd /home/ulalax/project/ulalax/playhouse/playhouse-net/connectors/test-server
./certs/generate-certs.sh
docker compose up -d
```

3) Run UE automation tests inside the container

```bash
UE_DOCKER_IMAGE="<your-ue-image-tag>" \
UPROJECT_PATH="/mnt/c/Work/MyGame/MyGame.uproject" \
./connectors/unreal/scripts/run-docker-e2e.sh
```

4) Stop the test server

```bash
cd /home/ulalax/project/ulalax/playhouse/playhouse-net/connectors/test-server
docker compose down
```

## Notes

- The UE container image tag varies by Epic release; set `UE_DOCKER_IMAGE` accordingly.
- If Docker cannot access the project path, check WSL file permissions and Docker Desktop file sharing.
- Reports are exported to `_ue/automation-reports` by default.
- For transport-specific tests (TCP/TLS/WS/WSS), add UE automation categories like:
  - `PlayHouse.Tcp`
  - `PlayHouse.Tls`
  - `PlayHouse.Ws`
  - `PlayHouse.Wss`

## Environment Variables

UE E2E tests read these variables (defaults shown):

- `TEST_SERVER_HOST` (localhost)
- `TEST_SERVER_TCP_PORT` (34001)
- `TEST_SERVER_TCP_TLS_PORT` (34002)
- `TEST_SERVER_HTTP_PORT` (8080)
- `TEST_SERVER_HTTPS_PORT` (8443)

## Scripts

- `connectors/unreal/scripts/run-local-e2e.sh`
- `connectors/unreal/scripts/run-docker-e2e.sh`
