#!/bin/bash
# Build script for PlayHouse C++ Connector

set -e

BUILD_TYPE=${1:-Release}
BUILD_TESTS=${2:-OFF}

echo "Building PlayHouse C++ Connector..."
echo "  Build Type: $BUILD_TYPE"
echo "  Tests: $BUILD_TESTS"

# Create build directory
mkdir -p build
cd build

# Configure
cmake .. \
    -DCMAKE_BUILD_TYPE=$BUILD_TYPE \
    -DBUILD_TESTING=$BUILD_TESTS

# Build
cmake --build . --config $BUILD_TYPE

echo "Build complete!"

if [ "$BUILD_TESTS" = "ON" ]; then
    echo "Running tests..."
    ctest --output-on-failure -C $BUILD_TYPE
fi
