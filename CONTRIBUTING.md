# Contributing to PlayHouse

Thank you for your interest in contributing to PlayHouse! This document provides guidelines for contributing.

## Code of Conduct

Please read and follow our [Code of Conduct](CODE_OF_CONDUCT.md).

## How to Contribute

### Reporting Issues

- Use GitHub Issues for bug reports and feature requests
- Search existing issues before creating a new one
- Provide detailed reproduction steps for bugs
- Include version information and environment details

### Pull Requests

1. **Fork the repository** and create your branch from `main`
2. **Follow the coding style** of the project
3. **Add tests** for new functionality
4. **Update documentation** as needed
5. **Write clear commit messages**

### Commit Message Format

```
type(scope): subject

body (optional)
```

Types:
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation only
- `style`: Code style (formatting, missing semi colons, etc)
- `refactor`: Code refactoring
- `test`: Adding tests
- `chore`: Maintenance tasks

Example:
```
feat(connector): add WebSocket reconnection support

- Add exponential backoff for reconnection
- Add configurable retry limit
```

### Development Setup

#### .NET Server

```bash
cd servers/dotnet
dotnet restore
dotnet build
dotnet test
```

#### C# Connector

```bash
cd connectors/csharp
dotnet restore
dotnet build
```

### Testing

- Write unit tests for new functionality
- Ensure all existing tests pass
- Run E2E tests when applicable

```bash
# Run all tests
cd servers/dotnet
dotnet test
```

## Project Structure

```
playhouse/
├── protocol/           # Shared protocol definitions
├── servers/            # Server implementations
│   ├── dotnet/
│   ├── java/
│   ├── cpp/
│   └── nodejs/
├── connectors/         # Client connectors
│   ├── csharp/
│   ├── cpp/
│   ├── java/
│   ├── javascript/
│   ├── unity/
│   └── unreal/
├── docs/
├── examples/
└── tools/
```

## Questions?

Feel free to open an issue for questions or join our community discussions.

Thank you for contributing!
