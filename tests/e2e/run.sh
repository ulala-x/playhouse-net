#!/bin/bash
# Verification 테스트 실행 스크립트 (Linux/macOS)

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/PlayHouse.Verification"

echo "=========================================="
echo "PlayHouse Verification Tests"
echo "=========================================="

cd "$SCRIPT_DIR/../.."

echo "[1/2] Building..."
dotnet build "$PROJECT_DIR" --configuration Release --verbosity quiet

echo "[2/2] Running verification tests..."
dotnet run --project "$PROJECT_DIR" --configuration Release --no-build

echo "=========================================="
echo "Done"
echo "=========================================="
