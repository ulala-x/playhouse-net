#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="$SCRIPT_DIR/../src/PlayHouse.TestServer/Shared/Proto"

echo "Generating C# proto files..."
echo "Proto file: $SCRIPT_DIR/test_messages.proto"
echo "Output directory: $OUT_DIR"

# Create output directory if it doesn't exist
mkdir -p "$OUT_DIR"

# Check if protoc is available
if ! command -v protoc &> /dev/null; then
    echo "Error: protoc is not installed or not in PATH"
    echo "Please install protobuf compiler:"
    echo "  - Ubuntu/Debian: sudo apt-get install protobuf-compiler"
    echo "  - macOS: brew install protobuf"
    echo "  - Windows: Download from https://github.com/protocolbuffers/protobuf/releases"
    exit 1
fi

# Generate C# code
protoc --proto_path="$SCRIPT_DIR" \
  --csharp_out="$OUT_DIR" \
  "$SCRIPT_DIR/test_messages.proto"

echo "✓ Generated C# proto files in $OUT_DIR"
ls -lh "$OUT_DIR"
