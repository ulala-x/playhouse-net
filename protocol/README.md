# Protocol

Shared protocol definitions using Protocol Buffers.

## Structure

```
protocol/
├── messages.proto      # Core message definitions
└── README.md
```

## Generating Code

Use the scripts in `tools/proto-gen/` to generate code for each language:

```bash
# Generate all
./tools/proto-gen/generate-all.sh

# Generate specific language
./tools/proto-gen/generate-csharp.sh
./tools/proto-gen/generate-cpp.sh
./tools/proto-gen/generate-java.sh
./tools/proto-gen/generate-ts.sh
```
