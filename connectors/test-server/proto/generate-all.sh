#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONNECTORS_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

echo "=========================================="
echo "Generating proto files for all languages"
echo "=========================================="

# Check if protoc is available
if ! command -v protoc &> /dev/null; then
    echo "Error: protoc is not installed or not in PATH"
    echo "Please install protobuf compiler:"
    echo "  - Ubuntu/Debian: sudo apt-get install protobuf-compiler"
    echo "  - macOS: brew install protobuf"
    echo "  - Windows: Download from https://github.com/protocolbuffers/protobuf/releases"
    exit 1
fi

# ==========================================
# C# (PlayHouse.TestServer)
# ==========================================
echo ""
echo "1. Generating C# proto files..."
OUT_CSHARP="$SCRIPT_DIR/../src/PlayHouse.TestServer/Shared/Proto"
mkdir -p "$OUT_CSHARP"

protoc --proto_path="$SCRIPT_DIR" \
  --csharp_out="$OUT_CSHARP" \
  "$SCRIPT_DIR/test_messages.proto"

echo "   ✓ C# files generated in: $OUT_CSHARP"

# ==========================================
# JavaScript/TypeScript (Node.js connector)
# ==========================================
echo ""
echo "2. Generating JavaScript/TypeScript proto files..."
OUT_JS="$CONNECTORS_ROOT/javascript/src/proto"
mkdir -p "$OUT_JS"

# Check if protoc plugins are available
if command -v protoc-gen-js &> /dev/null && command -v protoc-gen-ts &> /dev/null; then
    protoc --proto_path="$SCRIPT_DIR" \
      --js_out=import_style=commonjs,binary:"$OUT_JS" \
      --ts_out="$OUT_JS" \
      "$SCRIPT_DIR/test_messages.proto"
    echo "   ✓ JavaScript/TypeScript files generated in: $OUT_JS"
else
    echo "   ⚠ Skipping JavaScript/TypeScript (protoc plugins not found)"
    echo "   Install with: npm install -g protoc-gen-js protoc-gen-ts"
fi

# ==========================================
# Java (Android/Java connector)
# ==========================================
echo ""
echo "3. Generating Java proto files..."
OUT_JAVA="$CONNECTORS_ROOT/java/src/main/java"
mkdir -p "$OUT_JAVA"

protoc --proto_path="$SCRIPT_DIR" \
  --java_out="$OUT_JAVA" \
  "$SCRIPT_DIR/test_messages.proto"

echo "   ✓ Java files generated in: $OUT_JAVA"

# ==========================================
# C++ (Unreal/Native connector)
# ==========================================
echo ""
echo "4. Generating C++ proto files..."
OUT_CPP="$CONNECTORS_ROOT/cpp/src/proto"
mkdir -p "$OUT_CPP"

protoc --proto_path="$SCRIPT_DIR" \
  --cpp_out="$OUT_CPP" \
  "$SCRIPT_DIR/test_messages.proto"

echo "   ✓ C++ files generated in: $OUT_CPP"

# ==========================================
# Summary
# ==========================================
echo ""
echo "=========================================="
echo "✓ All proto files generated successfully"
echo "=========================================="
echo ""
echo "Generated files:"
echo "  - C#: $OUT_CSHARP"
echo "  - JavaScript/TypeScript: $OUT_JS"
echo "  - Java: $OUT_JAVA"
echo "  - C++: $OUT_CPP"
echo ""
echo "Note: Copy generated files to each connector's source directory as needed."
