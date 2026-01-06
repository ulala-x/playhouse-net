# PlayHouse-NET

## Project Overview

PlayHouse-NET is a .NET-based real-time game server framework designed for simplicity, performance, and scalability. It is inspired by the Java-based PlayHouse framework but has been re-architected to be a more streamlined and .NET-native solution.

The framework is built around a single "Room Server" concept, which simplifies the overall architecture. It utilizes a Stage/Actor model to handle concurrency and scaling, with a focus on lock-free message processing for high performance.

**Key Technologies:**

*   **.NET 8.0/9.0/10.0:** The project targets modern .NET versions.
*   **C#:** The primary language of the project.
*   **ASP.NET Core:** Used for the built-in HTTP API.
*   **System.Net.Sockets and System.Net.WebSockets:** For TCP and WebSocket communication.
*   **Microsoft.Extensions:** The project leverages standard .NET Core extension libraries for dependency injection, configuration, and logging.

**Architecture:**

The architecture is layered and consists of:

*   **Application Layer:** Where the user's custom logic (Stages, Actors, HTTP Controllers) resides.
*   **Abstractions Layer:** Defines the public interfaces for the framework (e.g., `IStage`, `IActor`).
*   **Core Engine Layer:** The heart of the framework, containing the message dispatcher, Stage/Actor manager, and timer system.
*   **Infrastructure Layer:** Handles the low-level concerns like socket transport, the HTTP server, and packet serialization.

## Building and Running

### Building the Project

To build the project, you can use the standard .NET CLI command from the root directory:

```bash
dotnet build
```

### Running the Project

The project is an ASP.NET Core application, so you can run it using the `dotnet run` command. You will need to specify the project to run, for example:

```bash
# Example of running the main server project (assuming the project is in src/PlayHouse)
dotnet run --project src/PlayHouse/PlayHouse.csproj
```

### Running Tests

To run the tests, you can use the `dotnet test` command from the root directory:

```bash
dotnet test
```

## Development Conventions

*   **Message-Based Communication:** All interactions between actors are done through asynchronous messages.
*   **Actor Isolation:** Actors maintain their own state and are isolated from each other.
*   **Asynchronous-First:** The framework is designed to be fully asynchronous, using `async/await` and `ValueTask` extensively.
*   **.NET Core Integration:** The framework is designed to integrate seamlessly with the .NET Core ecosystem, using `Microsoft.Extensions` for dependency injection, configuration, and logging.
*   **Configuration:** The project uses `appsettings.json` for configuration, following the standard ASP.NET Core pattern.
