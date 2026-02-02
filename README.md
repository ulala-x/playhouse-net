# PlayHouse

[![Build Status](https://github.com/user/playhouse/workflows/CI/badge.svg)](https://github.com/user/playhouse/actions)
[![License](https://img.shields.io/badge/License-Apache%202.0%20with%20Commons%20Clause-blue.svg)](LICENSE)

**PlayHouse** is a high-performance, multi-language game server framework designed for real-time multiplayer games.

## Features

- **Multi-Language Support**: Server implementations in .NET, Java, C++, and Node.js
- **Cross-Platform Connectors**: Unity, Unreal Engine, and JavaScript clients
- **Actor Model**: Stage-based actor architecture for game logic
- **Protocol Buffers**: Efficient binary serialization
- **High Performance**: Optimized for low-latency real-time communication

## Project Structure

```
playhouse/
├── protocol/           # Shared protocol definitions (protobuf)
├── servers/            # Server implementations
│   ├── dotnet/         # .NET server
│   ├── java/           # Java server
│   ├── cpp/            # C++ server
│   └── nodejs/         # Node.js server
├── connectors/         # Client connectors
│   ├── csharp/         # C# connector (Unity, .NET)
│   ├── cpp/            # C++ connector (Unreal)
│   ├── java/           # Java connector
│   ├── javascript/     # JavaScript connector
│   ├── unity/          # Unity package
│   └── unreal/         # Unreal plugin
├── docs/               # Documentation
├── examples/           # Sample projects
└── tools/              # Development tools
```

## Quick Start

### .NET Server

```bash
cd servers/dotnet
dotnet build
dotnet run --project src/PlayHouse
```

### Unity Client

Add the package via git URL:
```
https://github.com/user/playhouse.git?path=connectors/unity
```

## Documentation

- [Architecture Overview](docs/architecture/overview.md)
- [Getting Started](docs/servers/dotnet/getting-started/)
- [API Reference](docs/api/)

## License

This project is licensed under the Apache License 2.0 with Commons Clause. See [LICENSE](LICENSE) for details.

**Summary**:
- Free for internal use, modification, and redistribution
- Commercial SaaS/hosting requires a separate license
- Contact us for commercial licensing inquiries

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## Security

To report security vulnerabilities, please see [SECURITY.md](SECURITY.md).
