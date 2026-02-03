#!/usr/bin/env bash
set -euo pipefail

# This script runs the C++ connector tests inside a containerized toolchain.
# It expects a C++ build image with cmake, ninja/make, and a compiler.

if [[ -z "${CPP_TEST_IMAGE:-}" ]]; then
  echo "CPP_TEST_IMAGE is required (e.g., ubuntu:24.04 with build tools)" >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CPP_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
ROOT_DIR="$(cd "$CPP_DIR/../.." && pwd)"

pushd "$CPP_DIR" >/dev/null
  docker compose -f docker-compose.test.yml up -d --build
popd >/dev/null

# Run tests inside container with repo mounted

docker run --rm \
  -v "$ROOT_DIR":/repo \
  -w /repo/connectors/cpp \
  "$CPP_TEST_IMAGE" \
  /bin/bash -lc "command -v git >/dev/null || { echo 'git is required in CPP_TEST_IMAGE to init submodules' >&2; exit 1; } \
  && git -C /repo submodule update --init --recursive \
  && mkdir -p build \
  && cd build \
  && cmake .. -DBUILD_TESTING=ON \
  && cmake --build . --parallel \
  && ctest --output-on-failure"

pushd "$CPP_DIR" >/dev/null
  docker compose -f docker-compose.test.yml down -v
popd >/dev/null
