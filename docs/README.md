# Documentation

PlayHouse framework documentation.

## Structure

```
docs/
├── architecture/          # Overall architecture and design
│   └── overview.md        # Architecture overview
├── servers/               # Server implementation guides
│   └── dotnet/            # .NET server documentation
├── connectors/            # Connector usage guides
│   ├── unity-guide.md     # Unity connector guide
│   ├── unreal-guide.md    # Unreal connector guide
│   └── javascript-guide.md # JavaScript connector guide
└── api/                   # API reference (auto-generated)
```

## Quick Links

### Getting Started

- [Architecture Overview](architecture/overview.md)
- [.NET Server Guide](servers/dotnet/)

### Server Guides

| Language | Documentation | Status |
|----------|---------------|--------|
| .NET | [servers/dotnet/](servers/dotnet/) | Active |
| Java | servers/java/ | Planned |
| C++ | servers/cpp/ | Planned |
| Node.js | servers/nodejs/ | Planned |

### Connector Guides

| Connector | Target Platform | Guide |
|-----------|-----------------|-------|
| Unity | Unity Engine | [unity-guide.md](connectors/unity-guide.md) |
| Unreal | Unreal Engine | [unreal-guide.md](connectors/unreal-guide.md) |
| JavaScript | Web/Node.js | [javascript-guide.md](connectors/javascript-guide.md) |

## Contributing to Docs

See [CONTRIBUTING.md](../CONTRIBUTING.md) for documentation contribution guidelines.
