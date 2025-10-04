# Aspire RabbitMQ Demo

This repository contains a minimal [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) AppHost that provisions a RabbitMQ container with the management plugin enabled. It is a convenient starting point for experimenting with Aspire's distributed application builder and for integrating RabbitMQ into multi-service solutions.

## Features
- RabbitMQ container managed through the Aspire AppHost lifecycle
- Parameterized credentials (`RabbitMQUser`, `RabbitMQPassword`) with sensible defaults
- RabbitMQ management dashboard enabled out of the box

## Prerequisites
- [.NET SDK 9.0](https://dotnet.microsoft.com/download)
- Docker Desktop or another container runtime compatible with .NET Aspire resources
- (Optional) Visual Studio 2022 17.10+ or VS Code with the C# Dev Kit for richer tooling

## Getting Started
1. Restore dependencies:
   ```bash
   dotnet restore
   ```
2. Launch the distributed application (from the repository root):
   ```bash
   dotnet run --project AspireRabbitMQDemo/AppHost1.csproj
   ```
3. Wait for Docker to pull and start the RabbitMQ container. The console output shows the allocated ports.

### Connect to RabbitMQ
- **AMQP:** The broker listens on port `5672` by default. Check the Aspire output if the port is mapped differently on your machine.
- **Management UI:** Navigate to `http://localhost:15672` (or the mapped port) and sign in with the configured credentials.

## Configure Credentials
The AppHost defines two parameters that control the broker credentials:
- `RabbitMQUser` (default: `guest`)
- `RabbitMQPassword` (default: `guest`)

Override them with [.NET user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets):
```bash
cd AspireRabbitMQDemo
dotnet user-secrets set RabbitMQUser myuser
dotnet user-secrets set RabbitMQPassword mypassword
```

You can also export environment variables before launching:
```bash
export RabbitMQUser=myuser
export RabbitMQPassword=mypassword
cd ..
dotnet run --project AspireRabbitMQDemo/AppHost1.csproj
```

## Project Structure
- `AspireRabbitMQDemo/Program.cs` - configures the distributed application and RabbitMQ resource
- `AspireRabbitMQDemo/AppHost1.csproj` - AppHost project with Aspire package references
- `AspireRabbitMQDemo/appsettings*.json` - logging configuration applied to the host

## Next Steps
- Extend the AppHost to include additional services that depend on RabbitMQ.
- Wire up health checks, dashboards, or custom lifecycle hooks using Aspire's eventing model.
- Containerize application services that consume RabbitMQ and declare the dependency through Aspire resources.
