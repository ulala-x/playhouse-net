#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$ROOT_DIR"

echo "[1/6] connectors/run-all-tests.sh"
bash connectors/run-all-tests.sh

echo "[2/6] connectors/cpp/run-tests.sh"
bash connectors/cpp/run-tests.sh

echo "[3/6] connectors/cpp/scripts/run-local-e2e.sh"
bash connectors/cpp/scripts/run-local-e2e.sh

echo "[4/6] connectors/cpp/scripts/run-docker-e2e.sh"
bash connectors/cpp/scripts/run-docker-e2e.sh

echo "[5/6] benchmark_cs/run-single.sh"
tests/benchmark/benchmark_cs/run-single.sh --mode send --size 64 --duration 2 --warmup 1 --ccu 2

echo "[6/6] benchmark_ss/run-single.sh"
tests/benchmark/benchmark_ss/run-single.sh --mode send --size 64 --duration 2 --warmup 1 --ccu 2

echo "All checks completed."
