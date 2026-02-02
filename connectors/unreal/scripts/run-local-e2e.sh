#!/usr/bin/env bash
set -euo pipefail

# Example usage:
# UE_EDITOR_CMD="/mnt/c/Program Files/Epic Games/UE_5.3/Engine/Binaries/Win64/UnrealEditor-Cmd.exe" \
# UPROJECT_PATH="/mnt/c/Work/MyGame/MyGame.uproject" \
# ./connectors/unreal/scripts/run-local-e2e.sh

if [[ -z "${UE_EDITOR_CMD:-}" ]]; then
  echo "UE_EDITOR_CMD is required (path to UnrealEditor-Cmd.exe)" >&2
  exit 1
fi

if [[ -z "${UPROJECT_PATH:-}" ]]; then
  echo "UPROJECT_PATH is required (path to .uproject)" >&2
  exit 1
fi

ROOT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
TEST_SERVER_DIR="$ROOT_DIR/test-server"
REPORT_DIR="$ROOT_DIR/../_ue/automation-reports"

mkdir -p "$REPORT_DIR"

if [[ ! -f "$TEST_SERVER_DIR/docker-compose.yml" ]]; then
  echo "test-server docker-compose.yml not found at $TEST_SERVER_DIR" >&2
  exit 1
fi

pushd "$TEST_SERVER_DIR" >/dev/null
  docker compose up -d
popd >/dev/null

"$UE_EDITOR_CMD" "$UPROJECT_PATH" \
  -ExecCmds="Automation RunTests PlayHouse.*" \
  -TestExit="Automation Test Queue Empty" \
  -ReportExportPath="$REPORT_DIR" \
  -unattended -nopause -nosplash

pushd "$TEST_SERVER_DIR" >/dev/null
  docker compose down
popd >/dev/null
