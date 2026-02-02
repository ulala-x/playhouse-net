#!/usr/bin/env bash
set -euo pipefail

# Example usage:
# UE_DOCKER_IMAGE="ghcr.io/epicgames/unreal-engine:5.3" \
# UPROJECT_PATH="/mnt/c/Work/MyGame/MyGame.uproject" \
# ./connectors/unreal/scripts/run-docker-e2e.sh

if [[ -z "${UE_DOCKER_IMAGE:-}" ]]; then
  echo "UE_DOCKER_IMAGE is required (Epic UE container image)" >&2
  exit 1
fi

if [[ -z "${UPROJECT_PATH:-}" ]]; then
  echo "UPROJECT_PATH is required (path to .uproject)" >&2
  exit 1
fi

ROOT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
TEST_SERVER_DIR="$ROOT_DIR/test-server"

if [[ ! -f "$TEST_SERVER_DIR/docker-compose.yml" ]]; then
  echo "test-server docker-compose.yml not found at $TEST_SERVER_DIR" >&2
  exit 1
fi

pushd "$TEST_SERVER_DIR" >/dev/null
  docker compose up -d
popd >/dev/null

PROJECT_DIR=$(cd "$(dirname "$UPROJECT_PATH")" && pwd)
UPROJECT_BASENAME=$(basename "$UPROJECT_PATH")

# NOTE: The UE container image must be pulled with proper Epic/GitHub authorization.
# This command runs a headless UE automation test inside the container.

docker run --rm \
  -v "$PROJECT_DIR":/project \
  -w /project \
  "$UE_DOCKER_IMAGE" \
  /bin/bash -lc \
  "/home/ue4/Engine/Binaries/Linux/UnrealEditor-Cmd /project/$UPROJECT_BASENAME \
    -ExecCmds='Automation RunTests PlayHouse.*;Quit' \
    -unattended -nopause -nosplash"

pushd "$TEST_SERVER_DIR" >/dev/null
  docker compose down
popd >/dev/null
