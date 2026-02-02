# Repository Guidelines

## Project Structure & Module Organization
- `src/PlayHouse/` contains the core PlayHouse-NET library.
  - `Abstractions/` public interfaces and contracts.
  - `Core/` core runtime logic (Play, Api, Messaging).
  - `Runtime/` transport and server-mesh implementations.
  - `Extensions/` DI and hosting helpers.
  - `Infrastructure/` utilities (e.g., memory pools).
  - `Proto/` protobuf definitions.
- `tests/` contains all test and benchmark projects.
  - `PlayHouse.Tests.Unit/`, `PlayHouse.Tests.Integration/`, `PlayHouse.Tests.Performance/`.
  - `benchmark_cs/` and `benchmark_ss/` hold benchmark clients/servers and scripts.
- `doc/` and `docs/` hold specifications and architecture notes.

## Build, Test, and Development Commands
- `dotnet build playhouse-net.sln` builds the full solution.
- `dotnet test` runs all tests.
- `dotnet test tests/PlayHouse.Tests.Unit` runs unit tests only.
- `dotnet test --filter "DisplayName~Stage"` runs tests by DisplayName filter.
- `tests/benchmark_cs/run-single.sh` runs a single C# benchmark scenario.

## Coding Style & Naming Conventions
- C# with nullable reference types enabled and implicit usings enabled (see `Directory.Build.props`).
- Use file-scoped namespaces and keep public APIs documented with XML comments.
- Naming: PascalCase for types/methods/properties, camelCase for locals/parameters, `_camelCase` for private fields.
- Keep warning level at 5 and treat warnings as errors for Release builds.

## Testing Guidelines
- Frameworks: xUnit, FluentAssertions, NSubstitute.
- Prefer Given-When-Then structure and state verification over implementation verification.
- Use fakes over mocks when possible.
- Naming: `{Target}_{Condition}_{ExpectedResult}` for test methods.
- DisplayName should be clear and in Korean (per `tests/README.md`).
- Coverage targets are defined in `tests/README.md`; run coverage with:
  `dotnet test --collect:"XPlat Code Coverage"`.

## Commit & Pull Request Guidelines
- Commit messages follow Conventional Commits style (e.g., `feat: ...`, `refactor: ...`, `docs: ...`).
- PRs should include a concise summary, the tests run (or a rationale if skipped), and links to related issues/specs.
- If you touch benchmarks or performance-critical paths, include updated benchmark results or notes.

## Configuration & Docs
- Solution file: `playhouse-net.sln`.
- Key references: `tests/README.md`, `doc/` specs, and `docs/specifications/`.

## Communication
- Address the user as "팀장님" in responses.
