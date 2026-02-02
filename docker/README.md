# Docker

Docker configurations for PlayHouse servers.

## Structure

```
docker/
├── dotnet-server/     # .NET server Dockerfile
├── java-server/       # Java server Dockerfile
├── cpp-server/        # C++ server Dockerfile
├── nodejs-server/     # Node.js server Dockerfile
└── docker-compose.yml # Local development setup
```

## Quick Start

Run all servers locally:

```bash
docker-compose up -d
```

## Individual Servers

```bash
# .NET Server
docker build -t playhouse-dotnet -f dotnet-server/Dockerfile ../servers/dotnet
docker run -p 5000:5000 playhouse-dotnet
```
