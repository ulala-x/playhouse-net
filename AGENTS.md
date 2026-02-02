# PlayHouse Project Guidelines

## Project Overview

PlayHouse is a multi-language real-time game server framework designed for simplicity, performance, and scalability. The framework supports multiple server implementations (.NET, Java, C++, Node.js) and client connectors (Unity, Unreal, JavaScript).

## Repository Structure

```
playhouse/
├── servers/                    # Server implementations
│   ├── dotnet/                 # .NET server (active)
│   │   ├── src/PlayHouse/      # Core library
│   │   ├── extensions/         # Serialization extensions
│   │   └── tests/              # Unit, E2E, benchmark tests
│   ├── java/                   # Java server (planned)
│   ├── cpp/                    # C++ server (planned)
│   └── nodejs/                 # Node.js server (planned)
├── connectors/                 # Client connectors
│   ├── csharp/                 # C# connector (Unity & E2E)
│   ├── cpp/                    # C++ connector (Unreal & E2E)
│   ├── java/                   # Java connector (E2E only)
│   └── javascript/             # JavaScript connector (Web)
├── protocol/                   # Shared protocol definitions
├── docs/                       # Documentation
├── examples/                   # Example projects
└── tools/                      # Development tools
```

## Build & Test Commands

### .NET Server
```bash
# Build (from repository root)
dotnet build PlayHouse.sln

# Run all tests
dotnet test PlayHouse.sln

# Run specific test projects
dotnet test servers/dotnet/tests/unit/PlayHouse.Unit/
dotnet test servers/dotnet/tests/e2e/PlayHouse.E2E/

# Run E2E tests via script
./servers/dotnet/tests/e2e/run.sh

# Run benchmarks
./servers/dotnet/tests/benchmark/benchmark_cs/run-single.sh --mode request-async --size 1024
./servers/dotnet/tests/benchmark/benchmark_ss/run-single.sh --mode request-async --size 1024
```

## Coding Conventions

### C# (.NET)
- Nullable reference types enabled
- File-scoped namespaces
- PascalCase for types/methods/properties
- camelCase for locals/parameters
- `_camelCase` for private fields
- XML documentation for public APIs

### Testing
- Frameworks: xUnit, FluentAssertions, NSubstitute
- Given-When-Then structure preferred
- Naming: `{Target}_{Condition}_{ExpectedResult}`
- DisplayName in Korean (per project convention)

## E2E Test Principles

### No Test Code in Production
- Never add test-specific handlers, mocks, or stubs to production code (`src/`)
- E2E tests must validate the complete system flow
- Bad example: `if (msgId.Contains("Echo"))` in PlayServer

### E2E vs Integration Tests
- **E2E Tests**: Validate via client public API
  - Connector → PlayServer → Stage → Actor → Response
  - Response content, callbacks, state changes
- **Integration Tests**: Validate internal server state
  - SessionManager.SessionCount
  - Internal timer behavior
  - AsyncBlock internals

### Server Callback E2E Verification
| Callback | E2E Verification Method |
|----------|------------------------|
| IActor.OnAuthenticate | IsAuthenticated() state + response packet |
| IStage.OnDispatch | Response packet content |
| IStage.OnJoinStage | Response + subsequent message handling |
| IStageSender.SendToClient | OnReceive callback (Push) |
| IActorSender.Reply | RequestAsync response |

### Response + Callback Verification
```csharp
// 1. Response verification (client API)
var response = await _connector.RequestAsync(packet);
response.MsgId.Should().Be("EchoReply");

// 2. Callback verification (test implementation)
testStage.ReceivedMsgIds.Should().Contain("EchoRequest");
testActor.OnAuthenticateCalled.Should().BeTrue();
```

## Connector Callback Rules

### ImmediateSynchronizationContext (Benchmarks)
```csharp
// Use ImmediateSynchronizationContext for benchmarks
SynchronizationContext.SetSynchronizationContext(
    new ImmediateSynchronizationContext());

var connector = new ClientConnector();
connector.Init(new ConnectorConfig());
// MainThreadAction() not needed - callbacks execute immediately
```

### MainThreadAction() (Normal Tests)
```csharp
// Without SynchronizationContext
connector.Request(packet, response => { /* callback */ });

// Must call MainThreadAction() periodically
connector.MainThreadAction();
```

### Message Patterns
| Pattern | Response | Description |
|---------|----------|-------------|
| Send | None | Fire-and-forget |
| Request | Reply | Request-response |
| Push | - | Server→Client notification (SendToClient) |

## Message Definition Rules

### Use Proto Messages
```csharp
// Bad
using var packet = Packet.Empty("EchoRequest");

// Good
var echoRequest = new EchoRequest { Content = "Hello", Sequence = 1 };
using var packet = new Packet(echoRequest);
```

## Commit Guidelines

- Follow Conventional Commits: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`
- Include concise summary and test results
- For performance changes, include benchmark results

## License

Apache 2.0 with Commons Clause - Free to use, SaaS requires separate license.
